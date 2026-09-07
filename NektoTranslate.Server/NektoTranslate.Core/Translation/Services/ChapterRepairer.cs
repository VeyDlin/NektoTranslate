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
// translated.
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
    EngineOptions engine,
    ILogger<ChapterRepairer> logger
) : IChapterRepairer {

    private readonly BatchingOptions batching = engine.batching;


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
        // mentions. That is exactly the set a "who is who" pass needs to compare a chapter's own
        // spellings against.
        IReadOnlyList<RepairTerm> names = terms.Where(term => RepairPrompt.IsName(term.category)).ToList();

        string previousTail = await PreviousChapterTailAsync(novel.id, language, chapter.index, cancellationToken);

        ApplicationSettings applicationSettings = await settings.GetAsync(cancellationToken);
        ChunkBudget budget = ChunkBudget.From(applicationSettings, batching);

        (List<string> pieces, List<int> owners) = SegmentChunker.Flatten(blocks, budget);
        List<IReadOnlyList<string>> requestBatches = SegmentChunker.Partition(pieces, budget);

        (IReadOnlyList<NameAlignment> alignments, double alignmentCost) = await AlignNamesAsync(
            chapter.id,
            language,
            applicationSettings.glossaryModel,
            names,
            requestBatches,
            cancellationToken
        );

        // One alignment pass plus one step per rewrite batch, so the bar inside this chapter has as
        // many stops as the repair actually takes.
        int stepCount = requestBatches.Count + 1;
        progress?.Report(new RunStep("aligning names", 1, stepCount, alignmentCost));

        List<string> repairedPieces = [];
        string carriedTail = string.Empty;
        double costUsd = alignmentCost;

        for (int index = 0; index < requestBatches.Count; index++) {
            IReadOnlyList<string> batch = requestBatches[index];

            (IReadOnlyList<string> result, double batchCost) = await RepairResilientAsync(
                language,
                novel.model,
                voice,
                terms,
                alignments,
                previousTail,
                carriedTail,
                batch,
                cancellationToken
            );

            repairedPieces.AddRange(result);
            costUsd += batchCost;

            progress?.Report(new RunStep(
                $"rewriting batch {index + 1} of {requestBatches.Count}",
                index + 2,
                stepCount,
                batchCost
            ));

            if (requestBatches.Count > 1) {
                carriedTail = string.Join("\n", result.TakeLast(batching.carryParagraphs));
            }
        }

        List<string> repairedBlocks = SegmentChunker.Rejoin(repairedPieces, owners, blocks.Count);

        ChapterTranslation repaired = new ChapterTranslation {
            chapterId = chapter.id,
            language = language,
            markdown = TranslationBlocks.Join(repairedBlocks),
            plainText = TranslationBlocks.PlainTextOf(repairedBlocks),
            origin = TranslationOrigin.Repaired,
            model = novel.model,
            costUsd = costUsd
        };

        database.chapterTranslations.Add(repaired);

        // A chapter can sit at Failed after an earlier attempt - a forced re-translation that broke,
        // or a previous repair - even though a perfectly usable rendering is still on file underneath.
        // A repair that lands successfully is a new usable rendering, so it heals that status back
        // rather than leaving a working chapter flagged as broken.
        chapter.translationState = ChapterTranslationState.Translated;

        await RecordAlignmentVariantsAsync(alignments, names, cancellationToken);

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


    // A batch the model could not answer in the required format is halved and each half retried,
    // mirroring ClaudeSegmentTranslator's own resilience - the failure mode is identical, and so is
    // the fix. Subdivision stops at a single segment: at that point the format is not the problem.
    private async Task<(IReadOnlyList<string> segments, double costUsd)> RepairResilientAsync(
        string language,
        string model,
        VoiceProfile? voice,
        IReadOnlyList<RepairTerm> terms,
        IReadOnlyList<NameAlignment> alignments,
        string previousTail,
        string carriedTail,
        IReadOnlyList<string> batch,
        CancellationToken cancellationToken
    ) {
        try {
            return await RepairBatchAsync(
                language,
                model,
                voice,
                terms,
                alignments,
                previousTail,
                carriedTail,
                batch,
                cancellationToken
            );
        } catch (SegmentProtocolException) when (batch.Count > 1) {
            int half = batch.Count / 2;

            (IReadOnlyList<string> first, double firstCost) = await RepairResilientAsync(
                language,
                model,
                voice,
                terms,
                alignments,
                previousTail,
                carriedTail,
                batch.Take(half).ToList(),
                cancellationToken
            );

            (IReadOnlyList<string> second, double secondCost) = await RepairResilientAsync(
                language,
                model,
                voice,
                terms,
                alignments,
                previousTail,
                carriedTail,
                batch.Skip(half).ToList(),
                cancellationToken
            );

            return (first.Concat(second).ToList(), firstCost + secondCost);
        }
    }


    private async Task<(IReadOnlyList<string> segments, double costUsd)> RepairBatchAsync(
        string language,
        string model,
        VoiceProfile? voice,
        IReadOnlyList<RepairTerm> terms,
        IReadOnlyList<NameAlignment> alignments,
        string previousTail,
        string carriedTail,
        IReadOnlyList<string> batch,
        CancellationToken cancellationToken
    ) {
        string prompt = SegmentProtocol.Format(batch);
        double costUsd = 0;
        SegmentProtocolException? lastFailure = null;

        for (int attempt = 1; attempt <= batching.maxAttempts; attempt++) {
            ClaudeCodeOptions options = new ClaudeCodeOptions {
                Model = model,
                SystemPrompt = RepairPrompt.BuildSystemPrompt(
                    language,
                    voice,
                    terms,
                    previousTail,
                    carriedTail,
                    batch.Count,
                    attempt,
                    alignments
                ),
                MaxTurns = 1,
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
                return (SegmentProtocol.Parse(reply.ToString(), batch.Count), costUsd);
            } catch (SegmentProtocolException failure) {
                lastFailure = failure;
            }
        }

        throw lastFailure!;
    }


    // Asks the glossary model, once per request batch, which of the book's established names this
    // chapter's own text spells differently - the "who is who" pass the rewrite loop below needs its
    // answer from before it can tell the model to fix a name the mention test never found.
    //
    // Run over the same requestBatches the repair loop itself will send, not the chapter as a whole,
    // so the passage this asks about is exactly the passage size the rest of the pipeline already
    // treats as one request; a name flagged in an earlier batch is unioned in so it still reaches the
    // system prompt of every later batch, including the ones it was not itself found in.
    //
    // A failure here - the model erroring, or a reply ParseNameAlignments cannot use - must not be
    // able to break a repair that would otherwise have succeeded: it is caught, logged, and the
    // repair proceeds with no alignments, exactly as it did before this pass existed.
    private async Task<(IReadOnlyList<NameAlignment> alignments, double costUsd)> AlignNamesAsync(
        long chapterId,
        string language,
        string model,
        IReadOnlyList<RepairTerm> names,
        IReadOnlyList<IReadOnlyList<string>> requestBatches,
        CancellationToken cancellationToken
    ) {
        if (names.Count == 0) {
            return ([], 0);
        }

        try {
            string systemPrompt = RepairPrompt.BuildNameAlignmentSystemPrompt(language, names);
            Dictionary<string, NameAlignment> unioned = new Dictionary<string, NameAlignment>(StringComparer.Ordinal);
            double costUsd = 0;

            foreach (IReadOnlyList<string> batch in requestBatches) {
                string passage = string.Join("\n", batch);

                (string reply, double batchCost) = await AskAsync(systemPrompt, passage, model, cancellationToken);
                costUsd += batchCost;

                foreach (NameAlignment alignment in RepairPrompt.ParseNameAlignments(
                    reply,
                    names,
                    passage,
                    engine.glossary.maxInflectionLength
                )) {
                    unioned[alignment.written] = alignment;
                }
            }

            return (unioned.Values.ToList(), costUsd);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception failure) {
            logger.LogWarning(failure, "Could not align established names for chapter {ChapterId}", chapterId);

            return ([], 0);
        }
    }


    // The next chapter that misspells the same name the same way is then caught by the ordinary
    // mention test without a second model call - a written form the alignment pass confirmed once
    // becomes part of the term's own recorded variants instead of being rediscovered, chapter after
    // chapter, at the glossary model's cost. Only an alignment traced back to a TranslationTerm
    // qualifies: a GlossaryEntry's aliases are source-language spellings, and a target-language
    // misspelling has no business among them.
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


    // The same one-shot, tool-less, single-turn call VoiceLearner.AskAsync makes for its own
    // mechanical reading-comprehension questions - listing names rather than writing prose is not
    // what the book's translation model is billed for.
    private static async Task<(string reply, double costUsd)> AskAsync(
        string systemPrompt,
        string prompt,
        string model,
        CancellationToken cancellationToken
    ) {
        ClaudeCodeOptions options = new ClaudeCodeOptions {
            Model = model,
            SystemPrompt = systemPrompt,
            MaxTurns = 1,
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


// One name the alignment pass found spelled differently in this chapter than the book has already
// settled on: written is exactly the spelling this chapter's own text uses, established is the
// RepairTerm.term it was matched to. Both are known to actually mean something by the time this is
// constructed - ParseNameAlignments has already dropped a pair whose established value is not one
// of the offered names, whose written value does not occur in the passage, or that is a no-op
// because the two sides already agree.
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
    // the name-alignment pass compares a chapter's own spellings against.
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
        IReadOnlyList<NameAlignment> alignments
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

        return prompt.ToString();
    }


    // The "who is who" pass's own system prompt: every established name the book has, so the model
    // can compare this chapter's spellings against a roster instead of guessing from the passage
    // alone. The passage itself is the user message, not part of this - the same split ChapterRepairer
    // already uses for the rewrite prompt above.
    public static string BuildNameAlignmentSystemPrompt(string language, IReadOnlyList<RepairTerm> names) {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine(
            $"You are reading one passage of an existing {language} translation of a light novel. "
            + "Below is the established rendering of every person, place and organisation in this "
            + "book."
        );
        prompt.AppendLine(
            "Read the passage and list every name in it that refers to one of these established "
            + "names but is spelled differently, one per line, exactly in this form: written in the "
            + "passage | established rendering"
        );
        prompt.AppendLine(
            "Skip a name that is already spelled correctly. List a name only when you are confident "
            + "it is the same person, place or organisation - never guess."
        );
        prompt.AppendLine("If no name in the passage needs correcting, answer NONE. Write nothing else.");

        prompt.AppendLine();
        prompt.AppendLine("ESTABLISHED NAMES");

        foreach (RepairTerm name in names) {
            prompt.Append("- ").AppendLine(name.term);
        }

        return prompt.ToString();
    }


    // What ParseNameAlignments trusts a line to mean is deliberately narrow, because this reply feeds
    // straight into rewriting the chapter: a pair the model half-invented would make the rewrite hunt
    // for a spelling that is not actually there. A line survives only when it has exactly one "|", both
    // sides are non-empty once trimmed, established is one of the names this call actually offered,
    // written is not simply established repeated back, and written can be found in the passage by the
    // same rule the rest of the pipeline uses to decide a term is present at all.
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
