using System.Text;
using System.Text.Json;
using ClaudeCodeSdk;
using ClaudeCodeSdk.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Models;
using NektoTranslate.Glossary.Entities;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Settings.Entities;
using NektoTranslate.Settings.Services;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Entities;
using NektoTranslate.Translation.Enums;


namespace NektoTranslate.Translation.Services;


// Rewrites a stretch of an existing translation that has no reliable original behind it, in the
// voice a VoiceProfile has already learned from the chapters of this book a human actually
// translated - the way a careful editor works: read the chapter once and write a memo before
// touching a paragraph, rewrite paragraph by paragraph with the couple of neighbours around each as
// context, read the finished chapter back once more, and write down what was decided so the next
// chapter does not decide again.
//
// This is a repair, not a translation, and the two are not the same operation with a different
// input. A translation pass can check its own output against the source it was given; this one
// cannot, because there is nothing on the other side to check against - the chapter's sourceMarkdown
// may be null, and this class never reads it even when it is not, because a chapter that still has
// an original belongs to a translate run, not a repair. The model may straighten names and
// terminology against the glossary, bring the register and sentence flow in line with the learned
// voice, and untangle a sentence a machine pass mangled - but whatever meaning that earlier pass
// lost or invented is gone for good, and no amount of rewriting recovers it. Every prompt this class
// sends says so, because it is exactly the kind of limit an interface can quietly imply away.
//
// Reuses the wire format the rest of the pipeline already speaks - SegmentProtocol's marked lines,
// SegmentChunker's paragraph-then-sentence cascade - so the repaired chapter keeps the same block
// count the block editor already knows how to address. It does not reuse ClaudeSegmentTranslator
// itself: that class's prompt is fixed to "translate this source into that language", and asking it
// to translate a language into itself would be exactly the bespoke, misleading prompt this class
// exists to avoid.
//
// Always writes a new ChapterTranslation and never touches the one it read. The machine text a bad
// pass produced stays on file as the previous version - the only way back if a repair reads worse
// than what it replaced.
public class ChapterRepairer(
    NektoDbContext database,
    ISettingsService settings,
    ITermUpserter termUpserter,
    EngineOptions engine,
    ILogger<ChapterRepairer> logger
) : IChapterRepairer {

    private readonly BatchingOptions batching = engine.batching;


    // Everything about this repair that stays the same across every batch it sends, the re-pass
    // included - bundled the same way ChapterTranslationRequest bundles a translate run's own
    // constants, so a method that needs one of them does not have to carry eight separate
    // parameters to get it.
    private sealed record RepairContext(
        string language,
        string model,
        VoiceProfile? voice,
        IReadOnlyList<RepairTerm> terms,
        CarefulPass.Memo memo,
        IReadOnlyList<string> examples,
        string previousTail,
        int thinkingTokens,
        int passSegments,
        int passContextBefore,
        int passContextAfter
    );


    public async Task<RepairedChapter> RepairAsync(
        long chapterId,
        IProgress<RunStep>? progress = null,
        CancellationToken cancellationToken = default
    ) {
        Chapter chapter = await database.chapters
            .Include(candidate => candidate.novel)
            .FirstAsync(candidate => candidate.id == chapterId, cancellationToken);

        Novel novel = chapter.novel!;
        string language = novel.targetLanguage;

        ChapterTranslation? current = await database.chapterTranslations
            .Where(translation => translation.chapterId == chapterId && translation.language == language)
            .OrderByDescending(translation => translation.createdAt)
            .ThenByDescending(translation => translation.id)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null) {
            throw new InvalidOperationException(
                $"Chapter {chapterId} has no {language} translation yet. Repair rewrites an existing "
                + "rendering; it does not create one."
            );
        }

        List<string> blocks = TranslationBlocks.Split(current.markdown);

        if (blocks.Count == 0) {
            throw new InvalidOperationException(
                $"Chapter {chapterId}'s {language} translation has no text to repair."
            );
        }

        VoiceProfile? voice = await database.voiceProfiles
            .AsNoTracking()
            .Where(profile => profile.novelId == novel.id && profile.language == language)
            .OrderByDescending(profile => profile.createdAt)
            .FirstOrDefaultAsync(cancellationToken);

        List<TranslationTerm> knownTerms = await database.translationTerms
            .AsNoTracking()
            .Where(term => term.novelId == novel.id && term.language == language)
            .ToListAsync(cancellationToken);

        // Renderings the source-term pipeline settled on for chapters that have their original
        // beside them - a name learned there belongs in a chapter that has no original at all just
        // as much as a term VoiceLearner read straight off the translation.
        List<GlossaryEntry> knownGlossaryEntries = await database.glossaryEntries
            .AsNoTracking()
            .Where(entry => entry.novelId == novel.id && entry.language == language)
            .ToListAsync(cancellationToken);

        // Only the terms this chapter's own text actually mentions go into the prompt, the same
        // discipline SelectForChapterAsync applies to the glossary - sending every term the book has
        // ever settled would grow every request with the book instead of with the chapter.
        IReadOnlyList<RepairTerm> terms = RepairPrompt.SelectRelevantTerms(
            RepairPrompt.MergeTerms(knownTerms, knownGlossaryEntries),
            current.plainText,
            engine.glossary.maxInflectionLength,
            engine.glossary.maxNamesPerRepair
        );

        // Every Person, Place and Organization SelectRelevantTerms offered - which, unlike the rest
        // of the glossary, is already the whole book's roster regardless of what this chapter
        // mentions. That is exactly the set the memo's "who is who" question needs to compare a
        // chapter's own spellings against.
        IReadOnlyList<RepairTerm> names = terms.Where(term => RepairPrompt.IsName(term.category)).ToList();

        string previousTail = await PreviousChapterTailAsync(novel.id, language, chapter.index, cancellationToken);

        ApplicationSettings applicationSettings = await settings.GetAsync(cancellationToken);
        // Null means the book's own model - the one novel.model already names for every other run.
        string model = applicationSettings.repairModel ?? novel.model;
        ChunkBudget budget = ChunkBudget.From(applicationSettings, batching);

        (List<string> pieces, List<int> owners) = SegmentChunker.Flatten(blocks, budget);
        List<SegmentChunker.ContextBatch> passBatches = SegmentChunker.WithContext(
            pieces,
            applicationSettings.passSegments,
            applicationSettings.passContextBefore,
            applicationSettings.passContextAfter
        );

        // One memo, one step per pass batch, one proofread if it is on, and one decisions step -
        // always, since a repair always leaves something for the next chapter to reuse. A needed
        // re-pass grows this by one more once the proofread has actually run.
        int stepCount = 1 + passBatches.Count + (applicationSettings.proofread ? 1 : 0) + 1;
        int stepIndex = 1;
        double costUsd = 0;

        (CarefulPass.Memo memo, double memoCostUsd) = await BuildMemoAsync(
            language,
            applicationSettings.glossaryModel,
            names,
            current.plainText,
            engine.glossary.maxInflectionLength,
            cancellationToken
        );
        costUsd += memoCostUsd;
        progress?.Report(new RunStep("reading the chapter", stepIndex, stepCount, memoCostUsd));

        IReadOnlyList<string> examples = await LoadExamplesAsync(novel.id, language, voice, cancellationToken);

        RepairContext context = new RepairContext(
            language,
            model,
            voice,
            terms,
            memo,
            examples,
            previousTail,
            applicationSettings.thinkingTokens,
            applicationSettings.passSegments,
            applicationSettings.passContextBefore,
            applicationSettings.passContextAfter
        );

        List<string> repairedPieces = [];
        string carriedTail = string.Empty;
        int pieceCursor = 0;

        foreach (SegmentChunker.ContextBatch batch in passBatches) {
            (IReadOnlyList<string> result, double batchCostUsd) = await RepairResilientAsync(
                context,
                carriedTail,
                batch,
                null,
                cancellationToken
            );

            repairedPieces.AddRange(result);
            pieceCursor += batch.body.Count;
            costUsd += batchCostUsd;
            stepIndex++;

            progress?.Report(new RunStep(
                CarefulPass.ParagraphRangeTitle("rewriting", pieceCursor - batch.body.Count + 1, pieceCursor, pieces.Count),
                stepIndex,
                stepCount,
                batchCostUsd
            ));

            if (passBatches.Count > 1) {
                carriedTail = string.Join("\n", result.TakeLast(batching.carryParagraphs));
            }
        }

        List<string> repairedBlocks = SegmentChunker.Rejoin(repairedPieces, owners, blocks.Count);

        if (applicationSettings.proofread) {
            // Empty, not carriedTail: a re-pass batch is not necessarily adjacent to wherever the
            // main loop's last batch happened to end, so that tail is not this batch's neighbour
            // and would only read as a non sequitur under CONTINUATION. Its own contextBefore,
            // drawn from its actual position, already covers what a reader needs for continuity;
            // context.previousTail - the previous chapter's own tail - is unaffected either way.
            (List<string> proofread, double proofreadCostUsd, int nextStepIndex, int nextStepCount) = await ProofreadAndRepassAsync(
                context,
                pieces,
                owners,
                repairedPieces,
                repairedBlocks,
                string.Empty,
                stepIndex,
                stepCount,
                progress,
                cancellationToken
            );

            repairedBlocks = proofread;
            costUsd += proofreadCostUsd;
            stepIndex = nextStepIndex;
            stepCount = nextStepCount;
        }

        ChapterTranslation repaired = new ChapterTranslation {
            chapterId = chapter.id,
            language = language,
            markdown = TranslationBlocks.Join(repairedBlocks),
            plainText = TranslationBlocks.PlainTextOf(repairedBlocks),
            origin = TranslationOrigin.Repaired,
            model = model,
            costUsd = costUsd
        };

        database.chapterTranslations.Add(repaired);

        // A chapter can sit at Failed after an earlier attempt - a forced re-translation that broke,
        // or a previous repair - even though a perfectly usable rendering is still on file underneath.
        // A repair that lands successfully is a new usable rendering, so it heals that status back
        // rather than leaving a working chapter flagged as broken.
        chapter.translationState = ChapterTranslationState.Translated;

        await RecordAlignmentVariantsAsync(memo.misspelled, names, cancellationToken);

        stepIndex++;

        double decisionsCostUsd = await RecordDecisionsAsync(
            novel.id,
            language,
            chapter.id,
            repairedBlocks,
            applicationSettings.glossaryModel,
            budget,
            cancellationToken
        );
        costUsd += decisionsCostUsd;
        repaired.costUsd = costUsd;

        progress?.Report(new RunStep("recording decisions", stepIndex, stepCount, decisionsCostUsd));

        await database.SaveChangesAsync(cancellationToken);

        return new RepairedChapter(repaired.id, repairedBlocks.Count, costUsd);
    }


    // The tail of the immediately preceding chapter's newest translation, whatever its origin -
    // imported, manual, machine or already repaired. It is there purely for continuity: a pronoun,
    // a scene the chapter opens mid-way through, the register the previous page left off in. Not a
    // multi-chapter voice window the way ChapterTranslator samples one - a repair already has the
    // book's learned voice from the VoiceProfile, and stacking a second source of "how this book
    // sounds" would only leave the model to arbitrate between two instructions that might disagree.
    private async Task<string> PreviousChapterTailAsync(
        long novelId,
        string language,
        int beforeIndex,
        CancellationToken cancellationToken
    ) {
        string? plainText = await database.chapterTranslations
            .AsNoTracking()
            .Where(translation => translation.language == language
                && translation.chapter!.novelId == novelId
                && translation.chapter.index < beforeIndex)
            .OrderByDescending(translation => translation.chapter!.index)
            .ThenByDescending(translation => translation.createdAt)
            .Select(translation => translation.plainText)
            .FirstOrDefaultAsync(cancellationToken);

        if (plainText is null) {
            return string.Empty;
        }

        return string.Join("\n", RepairPrompt.Tail(plainText, batching.carryParagraphs));
    }


    // Two or three paragraphs of this book's own good translation, for the pass to match exactly -
    // read from the newest translation of the chapter the voice profile itself was learned up to,
    // since that chapter is known to carry a human translation. No voice profile means no chapter to
    // point at, so there is nothing to show.
    private async Task<IReadOnlyList<string>> LoadExamplesAsync(
        long novelId,
        string language,
        VoiceProfile? voice,
        CancellationToken cancellationToken
    ) {
        if (voice is null) {
            return [];
        }

        string? plainText = await database.chapterTranslations
            .AsNoTracking()
            .Where(translation => translation.language == language
                && translation.chapter!.novelId == novelId
                && translation.chapter.index == voice.toChapterIndex)
            .OrderByDescending(translation => translation.createdAt)
            .ThenByDescending(translation => translation.id)
            .Select(translation => translation.plainText)
            .FirstOrDefaultAsync(cancellationToken);

        if (plainText is null) {
            return [];
        }

        return CarefulPass.SelectExampleParagraphs(VoicePrompt.SplitParagraphs(plainText));
    }


    // A batch the model could not answer in the required format is halved and each half retried,
    // mirroring ClaudeSegmentTranslator's own resilience - the failure mode is identical, and so is
    // the fix. Subdivision stops at a single segment: at that point the format is not the problem.
    // Both halves keep the whole batch's context, exactly as the translate side does.
    private async Task<(IReadOnlyList<string> segments, double costUsd)> RepairResilientAsync(
        RepairContext context,
        string carriedTail,
        SegmentChunker.ContextBatch batch,
        IReadOnlyList<string?>? critiques,
        CancellationToken cancellationToken
    ) {
        try {
            return await RepairBatchAsync(context, carriedTail, batch, critiques, cancellationToken);
        } catch (SegmentProtocolException) when (batch.body.Count > 1) {
            int half = batch.body.Count / 2;

            SegmentChunker.ContextBatch firstHalf = batch with { body = batch.body.Take(half).ToList() };
            SegmentChunker.ContextBatch secondHalf = batch with { body = batch.body.Skip(half).ToList() };

            (IReadOnlyList<string> first, double firstCost) = await RepairResilientAsync(
                context,
                carriedTail,
                firstHalf,
                critiques?.Take(half).ToList(),
                cancellationToken
            );

            (IReadOnlyList<string> second, double secondCost) = await RepairResilientAsync(
                context,
                carriedTail,
                secondHalf,
                critiques?.Skip(half).ToList(),
                cancellationToken
            );

            return (first.Concat(second).ToList(), firstCost + secondCost);
        }
    }


    private async Task<(IReadOnlyList<string> segments, double costUsd)> RepairBatchAsync(
        RepairContext context,
        string carriedTail,
        SegmentChunker.ContextBatch batch,
        IReadOnlyList<string?>? critiques,
        CancellationToken cancellationToken
    ) {
        string prompt = CarefulPass.BuildBatchPrompt(batch.contextBefore, batch.body, batch.contextAfter, critiques);
        double costUsd = 0;
        SegmentProtocolException? lastFailure = null;

        for (int attempt = 1; attempt <= batching.maxAttempts; attempt++) {
            ClaudeCodeOptions options = new ClaudeCodeOptions {
                Model = context.model,
                SystemPrompt = RepairPrompt.BuildSystemPrompt(
                    context.language,
                    context.voice,
                    context.terms,
                    context.previousTail,
                    carriedTail,
                    batch.body.Count,
                    attempt,
                    context.memo.misspelled,
                    context.memo,
                    context.examples
                ),
                MaxTurns = 1,
                EnvironmentVariables = ThinkingEnvironment.BuildEnvironmentVariables(context.thinkingTokens),
                ExtraArgs = new Dictionary<string, string?> {
                    { "safe-mode", null },
                    { "tools", "" },
                    { "no-session-persistence", null }
                }
            };

            StringBuilder reply = new StringBuilder();

            await foreach (IMessage message in ClaudeQuery.QueryAsync(prompt, options, null, cancellationToken)) {
                if (message is AssistantMessage assistant) {
                    foreach (TextBlock block in assistant.Content.OfType<TextBlock>()) {
                        reply.Append(block.Text);
                    }
                }

                if (message is ResultMessage result) {
                    costUsd += result.TotalCostUsd ?? 0;

                    if (result.IsError) {
                        throw new InvalidOperationException($"Claude Code returned an error: {result.Result}");
                    }
                }
            }

            try {
                return (SegmentProtocol.Parse(reply.ToString(), batch.body.Count), costUsd);
            } catch (SegmentProtocolException failure) {
                lastFailure = failure;
            }
        }

        throw lastFailure!;
    }


    // Reads the repaired chapter back once, the way a proofreader reads a finished page, and redoes
    // whatever it flags through the same careful pass, with its critique attached. At most one
    // re-pass: what comes back from it is final, whether or not it would still satisfy a second
    // reading.
    private async Task<(List<string> blocks, double costUsd, int stepIndex, int stepCount)> ProofreadAndRepassAsync(
        RepairContext context,
        List<string> pieces,
        List<int> owners,
        List<string> repairedPieces,
        List<string> repairedBlocks,
        string carriedTail,
        int stepIndex,
        int stepCount,
        IProgress<RunStep>? progress,
        CancellationToken cancellationToken
    ) {
        double costUsd = 0;

        string proofreadSystemPrompt = CarefulPass.BuildRepairProofreadSystemPrompt(
            context.language,
            repairedBlocks.Count,
            context.terms,
            context.voice?.summary
        );
        string passage = SegmentProtocol.Format(repairedBlocks);

        (string reply, double proofreadCostUsd) = await AskAsync(
            proofreadSystemPrompt,
            passage,
            context.model,
            context.thinkingTokens,
            cancellationToken
        );

        costUsd += proofreadCostUsd;
        stepIndex++;

        IReadOnlyList<CarefulPass.ProofreadFinding> findings = CarefulPass.ParseProofreadFindings(
            reply,
            repairedBlocks.Count
        );

        progress?.Report(new RunStep("proofreading", stepIndex, stepCount, proofreadCostUsd));

        if (findings.Count == 0) {
            return (repairedBlocks, costUsd, stepIndex, stepCount);
        }

        Dictionary<int, string> problemsByBlock = findings
            .GroupBy(finding => finding.segmentIndex)
            .ToDictionary(group => group.Key, group => string.Join("; ", group.Select(finding => finding.problem)));

        List<int> flaggedPieces = Enumerable.Range(0, pieces.Count)
            .Where(pieceIndex => problemsByBlock.ContainsKey(owners[pieceIndex]))
            .ToList();

        List<CarefulPass.RepassGroup> repassGroups = CarefulPass.GroupFlaggedForRepass(
            flaggedPieces,
            pieces,
            context.passSegments,
            context.passContextBefore,
            context.passContextAfter
        );

        double repassCostUsd = 0;

        foreach (CarefulPass.RepassGroup group in repassGroups) {
            List<string?> critiques = Enumerable.Range(0, group.batch.body.Count)
                .Select(offset => problemsByBlock.GetValueOrDefault(owners[group.firstPieceIndex + offset]))
                .ToList();

            (IReadOnlyList<string> redone, double batchCostUsd) = await RepairResilientAsync(
                context,
                carriedTail,
                group.batch,
                critiques,
                cancellationToken
            );

            repassCostUsd += batchCostUsd;

            for (int offset = 0; offset < redone.Count; offset++) {
                repairedPieces[group.firstPieceIndex + offset] = redone[offset];
            }
        }

        costUsd += repassCostUsd;
        stepCount++;
        stepIndex++;

        progress?.Report(new RunStep(
            CarefulPass.RedoneParagraphsTitle(problemsByBlock.Count),
            stepIndex,
            stepCount,
            repassCostUsd
        ));

        return (SegmentChunker.Rejoin(repairedPieces, owners, repairedBlocks.Count), costUsd, stepIndex, stepCount);
    }


    // Reads the whole chapter once, before any paragraph of it is touched - the same "which of
    // these established names does this text spell differently" question the old per-batch
    // alignment pass asked, now asked once for the whole chapter instead of once per request batch,
    // which is also what lets it catch a name spelled two different ways in two different batches
    // of the same chapter.
    private static async Task<(CarefulPass.Memo memo, double costUsd)> BuildMemoAsync(
        string language,
        string glossaryModel,
        IReadOnlyList<RepairTerm> names,
        string chapterPlainText,
        int maxInflectionLength,
        CancellationToken cancellationToken
    ) {
        string systemPrompt = CarefulPass.BuildRepairMemoPrompt(language, names);

        (string reply, double costUsd) = await AskAsync(
            systemPrompt,
            chapterPlainText,
            glossaryModel,
            0,
            cancellationToken
        );

        return (CarefulPass.ParseRepairMemo(reply, names, chapterPlainText, maxInflectionLength), costUsd);
    }


    // The next chapter that misspells the same name the same way is then caught by the ordinary
    // mention test without a second model call - a written form the memo confirmed once becomes
    // part of the term's own recorded variants instead of being rediscovered, chapter after chapter,
    // at the glossary model's cost. Only an alignment traced back to a TranslationTerm qualifies: a
    // GlossaryEntry's aliases are source-language spellings, and a target-language misspelling has
    // no business among them.
    private async Task RecordAlignmentVariantsAsync(
        IReadOnlyList<NameAlignment> alignments,
        IReadOnlyList<RepairTerm> names,
        CancellationToken cancellationToken
    ) {
        if (alignments.Count == 0) {
            return;
        }

        Dictionary<string, long> translationTermIdsByTerm = names
            .Where(name => name.translationTermId is not null)
            .ToDictionary(name => name.term, name => name.translationTermId!.Value, StringComparer.Ordinal);

        foreach (NameAlignment alignment in alignments) {
            if (!translationTermIdsByTerm.TryGetValue(alignment.established, out long translationTermId)) {
                continue;
            }

            TranslationTerm row = await database.translationTerms.FirstAsync(
                candidate => candidate.id == translationTermId,
                cancellationToken
            );

            HashSet<string> variants = VoicePrompt.DecodeVariants(row.variantsJson);
            variants.Add(alignment.written);
            row.variantsJson = JsonSerializer.Serialize(variants);
        }
    }


    // GitHub issue #10: what a repair decided is written down so the next chapter does not decide
    // again. Reads VoicePrompt's own chunk extraction over the just-repaired text - the same
    // mechanical, budget-sized chunking VoiceLearner uses for a whole sample, here over one
    // chapter - and keeps only its TERMS section; the VOICE half of the same reply is not this
    // step's concern. New names are upserted through the same merge voice learning itself uses,
    // via ITermUpserter, so a name already known only gains occurrences and variants rather than
    // being recorded twice.
    //
    // A failure here must not be able to undo a repair that otherwise succeeded - the chapter was
    // already rewritten and is worth keeping whether or not this bookkeeping step lands - so it is
    // caught, logged, and the repair still returns as though nothing went wrong here.
    private async Task<double> RecordDecisionsAsync(
        long novelId,
        string language,
        long chapterId,
        IReadOnlyList<string> repairedBlocks,
        string glossaryModel,
        ChunkBudget budget,
        CancellationToken cancellationToken
    ) {
        try {
            string plainText = TranslationBlocks.PlainTextOf(repairedBlocks);
            List<string> paragraphs = VoicePrompt.SplitParagraphs(plainText).ToList();

            if (paragraphs.Count == 0) {
                return 0;
            }

            (List<string> pieces, _) = SegmentChunker.Flatten(paragraphs, budget);
            List<IReadOnlyList<string>> requestBatches = SegmentChunker.Partition(pieces, budget);

            Dictionary<string, MergedTerm> merged = new(StringComparer.Ordinal);
            double costUsd = 0;

            foreach (IReadOnlyList<string> batch in requestBatches) {
                (string reply, double batchCost) = await AskAsync(
                    VoicePrompt.BuildChunkSystemPrompt(language),
                    string.Join("\n\n", batch),
                    glossaryModel,
                    0,
                    cancellationToken
                );

                costUsd += batchCost;

                ChunkExtraction extraction = VoicePrompt.Parse(reply);
                VoicePrompt.MergeTerms(merged, extraction.terms, chapterId);
            }

            foreach (MergedTerm term in merged.Values) {
                int occurrences = VoicePrompt.CountOccurrences(plainText, term.term, term.variants);

                await termUpserter.UpsertAsync(novelId, language, term, occurrences, cancellationToken);
            }

            return costUsd;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception failure) {
            logger.LogWarning(failure, "Could not record decisions for chapter {ChapterId}", chapterId);

            return 0;
        }
    }


    // The same one-shot, tool-less, single-turn call the memo, the decisions step and the proofread
    // all make. thinkingTokens is 0 for a mechanical reading (the memo, the decisions step) and the
    // configured budget for the proofread, which is a pass call like any other.
    private static async Task<(string reply, double costUsd)> AskAsync(
        string systemPrompt,
        string prompt,
        string model,
        int thinkingTokens,
        CancellationToken cancellationToken
    ) {
        ClaudeCodeOptions options = new ClaudeCodeOptions {
            Model = model,
            SystemPrompt = systemPrompt,
            MaxTurns = 1,
            EnvironmentVariables = ThinkingEnvironment.BuildEnvironmentVariables(thinkingTokens),
            ExtraArgs = new Dictionary<string, string?> {
                { "safe-mode", null },
                { "tools", "" },
                { "no-session-persistence", null }
            }
        };

        StringBuilder reply = new StringBuilder();
        double costUsd = 0;

        await foreach (IMessage message in ClaudeQuery.QueryAsync(prompt, options, null, cancellationToken)) {
            if (message is AssistantMessage assistant) {
                foreach (TextBlock block in assistant.Content.OfType<TextBlock>()) {
                    reply.Append(block.Text);
                }
            }

            if (message is ResultMessage result) {
                costUsd += result.TotalCostUsd ?? 0;

                if (result.IsError) {
                    throw new InvalidOperationException($"Claude Code returned an error: {result.Result}");
                }
            }
        }

        return (reply.ToString(), costUsd);
    }
}


// One term offered to a repair, whichever of the two tables it came from. A TranslationTerm is
// read straight off this book's own translated chapters and carries every inflection actually
// seen there; a GlossaryEntry only ever carries the single rendering the source-term pipeline
// settled on. Reducing both to this shape is what lets SelectRelevantTerms and BuildSystemPrompt
// treat them alike instead of learning two glossaries' worth of quirks.
//
// category and occurrences come from whichever source produced the term - a GlossaryEntry has no
// occurrence count of its own, so it carries 0, which only ever affects the order names are capped
// in, never whether a mentioned one is offered. translationTermId is null unless the row came from
// a TranslationTerm; it is what lets a later alignment write its confirmed spelling back as a
// variant of the right row, and what stops it from writing one onto a GlossaryEntry's
// source-language aliases instead.
public sealed record RepairTerm(
    string term,
    IReadOnlyList<string> variants,
    string? notes,
    GlossaryCategory category,
    int occurrences,
    long? translationTermId
);


// One name the memo found spelled differently in this chapter than the book has already settled
// on: written is exactly the spelling this chapter's own text uses, established is the
// RepairTerm.term it was matched to. Both are known to actually mean something by the time this is
// constructed - RepairPrompt.ParseNameAlignments has already dropped a pair whose established value
// is not one of the offered names, whose written value does not occur in the passage, or that is a
// no-op because the two sides already agree.
public sealed record NameAlignment(string written, string established);


// The parts of a repair that need neither the database nor the model, kept apart and public so they
// can be tested without either: which of the book's settled terms a chapter's own text actually
// mentions, how far a continuity tail reaches, and what the repair prompt tells the model. The
// prompt carries the one instruction this whole feature depends on - what it may fix and what it may
// not invent back - so it is worth being able to assert on directly rather than only by reading it.
public static class RepairPrompt {

    // Combines both of the book's sources of a settled rendering into one list, keyed by the
    // rendering itself. A TranslationTerm and a GlossaryEntry can describe the same name - one
    // read off a chapter with no original, the other settled from a chapter that had one - and
    // when they do, the TranslationTerm wins: it is read off this book's own prose and carries
    // the variants actually seen there, where a GlossaryEntry carries none.
    public static IReadOnlyList<RepairTerm> MergeTerms(
        IReadOnlyList<TranslationTerm> translationTerms,
        IReadOnlyList<GlossaryEntry> glossaryEntries
    ) {
        Dictionary<string, RepairTerm> merged = new Dictionary<string, RepairTerm>(StringComparer.Ordinal);

        foreach (GlossaryEntry entry in glossaryEntries) {
            merged[entry.targetTerm] = new RepairTerm(
                entry.targetTerm,
                [],
                entry.notes,
                entry.category,
                0,
                null
            );
        }

        foreach (TranslationTerm term in translationTerms) {
            merged[term.term] = new RepairTerm(
                term.term,
                VoicePrompt.DecodeVariants(term.variantsJson).ToList(),
                term.notes,
                term.category,
                term.occurrences,
                term.id
            );
        }

        return merged.Values.ToList();
    }


    // A misspelled name is precisely the thing the mention test below cannot find - the whole reason
    // this pass exists - so a Person, Place or Organization is offered whether or not this chapter's
    // own text mentions it. Everything else keeps the mention discipline: sending every technique and
    // item the book has ever settled would grow every request with the book instead of with the
    // chapter.
    //
    // The name roster is still capped, because an unmentioned name is an assumption that the chapter
    // needs it - true for a chapter that misspells it, wasted context for one that never touches it
    // at all. A name this chapter does mention must never be dropped for one it does not, so mentioned
    // names are kept in full and only the remaining budget, if any, goes to the rest by frequency.
    public static IReadOnlyList<RepairTerm> SelectRelevantTerms(
        IReadOnlyList<RepairTerm> terms,
        string plainText,
        int maxInflectionLength,
        int maxNamesPerRepair = 150
    ) {
        List<RepairTerm> names = terms.Where(term => IsName(term.category)).ToList();
        List<RepairTerm> others = terms.Where(term => !IsName(term.category)).ToList();

        List<RepairTerm> mentionedNames = names
            .Where(term => Mentions(term, plainText, maxInflectionLength))
            .OrderByDescending(term => term.occurrences)
            .ThenBy(term => term.term, StringComparer.Ordinal)
            .ToList();

        HashSet<string> mentionedTerms = mentionedNames
            .Select(term => term.term)
            .ToHashSet(StringComparer.Ordinal);

        List<RepairTerm> unmentionedNames = names
            .Where(term => !mentionedTerms.Contains(term.term))
            .OrderByDescending(term => term.occurrences)
            .ThenBy(term => term.term, StringComparer.Ordinal)
            .ToList();

        int remainingCap = Math.Max(0, maxNamesPerRepair - mentionedNames.Count);

        List<RepairTerm> selectedNames = mentionedNames.Concat(unmentionedNames.Take(remainingCap)).ToList();

        List<RepairTerm> selectedOthers = others
            .Where(term => Mentions(term, plainText, maxInflectionLength))
            .ToList();

        return selectedNames
            .Concat(selectedOthers)
            .OrderByDescending(term => term.term.Length)
            .ThenBy(term => term.term, StringComparer.Ordinal)
            .ToList();
    }


    // Person, Place and Organization are the categories a reader notices at a glance when they are
    // wrong; Technique, Item and Other are not. Shared between SelectRelevantTerms, which uses it to
    // decide what bypasses the mention test, and ChapterRepairer, which uses it to build the roster
    // the memo compares a chapter's own spellings against.
    public static bool IsName(GlossaryCategory category) {
        return category is GlossaryCategory.Person or GlossaryCategory.Place or GlossaryCategory.Organization;
    }


    public static string[] Tail(string plainText, int paragraphs) {
        return plainText
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Where(line => line.Trim().Length > 0)
            .TakeLast(paragraphs)
            .ToArray();
    }


    public static string BuildSystemPrompt(
        string language,
        VoiceProfile? voice,
        IReadOnlyList<RepairTerm> terms,
        string previousChapterTail,
        string carriedTail,
        int segmentCount,
        int attempt,
        IReadOnlyList<NameAlignment> alignments,
        CarefulPass.Memo? memo = null,
        IReadOnlyList<string>? examples = null
    ) {
        StringBuilder prompt = new StringBuilder();

        // The limit stated up front, in the model's own instructions, rather than only in a comment
        // a person reading this file might see. An interface that only says "repair this chapter"
        // reads as a promise to make it right; this is the sentence that keeps it from overreaching.
        prompt.AppendLine(
            $"You are repairing an existing {language} translation of a light novel chapter. There is "
            + "no reliable original for this chapter - it is missing, or not to be trusted - so you "
            + "cannot check your rewrite against a source. You may correct names and terminology "
            + "against the glossary below, bring the register and sentence flow in line with the "
            + "established voice, and untangle a sentence that reads as broken, literal, or "
            + "machine-translated. You must NOT invent, guess, or restore meaning this translation "
            + "may have lost - if a passage reads as unclear or incomplete, smooth its wording without "
            + "adding content that is not already there."
        );
        prompt.AppendLine("Never write a preamble, a heading, a note, a gloss, or a comment on the text.");

        prompt.AppendLine();
        prompt.AppendLine("FORMAT");
        prompt.AppendLine(
            $"The input is a numbered list of segments, one per line, each starting with a marker "
            + $"such as {Marker(0)}. Return exactly {segmentCount} segments, each on its own line, "
            + "each starting with its own unchanged marker, in the same order."
        );
        prompt.AppendLine(
            "Never merge, split, drop, reorder or add a segment, even when the rewrite would read "
            + "better that way. One input segment is one output segment."
        );
        prompt.AppendLine("Write nothing before the first marker and nothing after the last segment.");
        prompt.AppendLine(
            "Segment text is Markdown. Preserve its syntax: *emphasis*, **strong**, headings, "
            + "> quotes, list bullets, [links](url) and ![images](src) must come back in the same "
            + "form. A segment may contain inline HTML such as <ruby>; leave any such tag exactly as "
            + "it is."
        );

        if (attempt > 1) {
            prompt.AppendLine();
            prompt.AppendLine(
                "The previous reply did not follow this format. Emit only marked segment lines, one "
                + "per segment, and nothing else."
            );
        }

        if (voice is not null) {
            prompt.AppendLine();
            prompt.AppendLine("VOICE - how this book's human translator writes. Match it.");
            prompt.AppendLine(voice.summary);
        }

        if (terms.Count > 0) {
            prompt.AppendLine();
            prompt.AppendLine("GLOSSARY - these renderings are already established. Use them exactly.");

            foreach (RepairTerm term in terms) {
                prompt.Append("- ").Append(term.term);

                if (!string.IsNullOrWhiteSpace(term.notes)) {
                    prompt.Append(" (").Append(term.notes).Append(')');
                }

                prompt.AppendLine();
            }
        }

        if (alignments.Count > 0) {
            prompt.AppendLine();
            prompt.AppendLine(
                "NAMES IN THIS CHAPTER - this translation spells some established names differently. "
                + "Write each of these with its established rendering, in every form it takes:"
            );

            foreach (NameAlignment alignment in alignments) {
                prompt.Append("- \"").Append(alignment.written).Append("\" -> ").Append(alignment.established);
                prompt.AppendLine();
            }
        }

        if (previousChapterTail.Length > 0) {
            prompt.AppendLine();
            prompt.AppendLine(
                "CONTINUITY - the end of the previous chapter's translation, for voice and pronoun "
                + "continuity only. It is already finished work: do not repeat it, and do not include "
                + "it in your output."
            );
            prompt.AppendLine(previousChapterTail);
        }

        if (carriedTail.Length > 0) {
            prompt.AppendLine();
            prompt.AppendLine(
                "CONTINUATION - the end of this chapter's own previous batch, already repaired. Match "
                + "it; do not repeat it."
            );
            prompt.AppendLine(carriedTail);
        }

        // What the chapter's own memo already read, how to read each paragraph closely rather than
        // skimming a whole batch, and an example or two of this book's own good translation - the
        // three things a careful pass adds on top of everything above, which stays exactly what it
        // already was.
        if (memo is not null) {
            CarefulPass.AppendThisChapter(prompt, memo);
        }

        CarefulPass.AppendHowToReadForRepair(prompt, language);
        CarefulPass.AppendExamples(prompt, examples ?? []);

        return prompt.ToString();
    }


    // What ParseNameAlignments trusts a line to mean is deliberately narrow, because this reply feeds
    // straight into rewriting the chapter: a pair the model half-invented would make the rewrite hunt
    // for a spelling that is not actually there. A line survives only when it has exactly one "|", both
    // sides are non-empty once trimmed, established is one of the names this call actually offered,
    // written is not simply established repeated back, and written can be found in the passage by the
    // same rule the rest of the pipeline uses to decide a term is present at all.
    //
    // Shared with CarefulPass.ParseRepairMemo, which reads this exact reply shape out of a memo's
    // MISSPELLED section rather than a dedicated alignment call's whole reply.
    public static IReadOnlyList<NameAlignment> ParseNameAlignments(
        string reply,
        IReadOnlyList<RepairTerm> names,
        string passagePlainText,
        int maxInflectionLength
    ) {
        HashSet<string> established = names.Select(name => name.term).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, NameAlignment> byWritten = new Dictionary<string, NameAlignment>(StringComparer.Ordinal);

        foreach (string rawLine in reply.ReplaceLineEndings("\n").Split('\n')) {
            string line = rawLine.Trim();

            if (line.Length == 0 || line.Equals("NONE", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            string[] fields = line.Split('|');

            if (fields.Length != 2) {
                continue;
            }

            string written = fields[0].Trim();
            string establishedName = fields[1].Trim();

            if (written.Length == 0
                || written.Equals(establishedName, StringComparison.Ordinal)
                || !established.Contains(establishedName)
                || !TermMatching.Contains(passagePlainText, written, maxInflectionLength)) {
                continue;
            }

            byWritten[written] = new NameAlignment(written, establishedName);
        }

        return byWritten.Values.ToList();
    }


    private static string Marker(int index) {
        return $"⟦#{index}⟧";
    }


    private static bool Mentions(RepairTerm term, string plainText, int maxInflectionLength) {
        if (TermMatching.Contains(plainText, term.term, maxInflectionLength)) {
            return true;
        }

        foreach (string variant in term.variants) {
            if (TermMatching.Contains(plainText, variant, maxInflectionLength)) {
                return true;
            }
        }

        return false;
    }
}
