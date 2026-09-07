using System.Text;
using System.Text.Json;
using ClaudeCodeSdk;
using ClaudeCodeSdk.Types;
using NektoTranslate.Common.Models;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Settings.Services;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Services;


public interface ISegmentTranslator {

    Task<ChapterTranslationOutcome> TranslateAsync(
        ChapterTranslationRequest request,
        Func<string, Task>? onDelta = null,
        IBatchCache? cache = null,
        IProgress<RunStep>? progress = null,
        CancellationToken cancellationToken = default
    );
}


// Translates one chapter the way a careful editor works: read the whole chapter once and write a
// memo before touching a paragraph, translate paragraph by paragraph with the couple of neighbours
// around each as context, then read the finished chapter back once more and redo whatever still
// reads wrong.
//
// Three flags strip the coding agent down to a plain translation engine: --safe-mode drops
// CLAUDE.md, skills, plugins, hooks and MCP servers while leaving authentication working;
// --system-prompt replaces the built-in prompt instead of appending to it; --tools "" sends no
// tool definitions. Measured on a single sentence that is the difference between ~29k and ~358
// input tokens.
public class ClaudeSegmentTranslator(EngineOptions options) : ISegmentTranslator {

    private readonly BatchingOptions batching = options.batching;


    // A chapter is normally many small requests rather than one - passSegments paragraphs at a
    // time, each read with its neighbours as context, the way an editor works rather than the way a
    // single big batch skims. A paragraph too large for one request on its own is still split on
    // sentence or character boundaries first, exactly as before.
    //
    // Batches run in order and each is given the tail of the previous batch's translation, so the
    // voice does not reset mid-chapter. The glossary is in the system prompt and so reaches every
    // batch unchanged. The chapter is read back once as a whole after every batch has answered, and
    // whatever the proofread flags is redone once more with its critique attached.
    public async Task<ChapterTranslationOutcome> TranslateAsync(
        ChapterTranslationRequest request,
        Func<string, Task>? onDelta = null,
        IBatchCache? cache = null,
        IProgress<RunStep>? progress = null,
        CancellationToken cancellationToken = default
    ) {
        if (request.segments.Count == 0) {
            return new ChapterTranslationOutcome([], string.Empty, 0);
        }

        cache ??= NullBatchCache.instance;

        // Everything that reaches the model through the system prompt has to be in the cache key,
        // or an edited instruction would be served the translation made under the old one. The
        // glossary was already here; the style prompts and the quote convention steer the wording
        // just as directly.
        string promptFingerprint = BatchHash.FingerprintOf([
            ("glossary", string.Join(
                "|",
                request.glossary.Select(term => $"{term.sourceTerm}={term.targetTerm}")
            )),
            ("global-style", request.globalStyleGuide ?? string.Empty),
            ("book-style", request.styleGuide ?? string.Empty),
            ("quotes", request.normalizeQuotes ? "normalised" : "as-source")
        ]);

        // A paragraph longer than a whole batch is split on sentence boundaries first, so no single
        // request can be oversized no matter how the author writes. The pieces are rejoined
        // afterwards, which keeps the chapter's paragraph count exactly what it was.
        ChunkBudget budget = request.effectiveBudget;

        (List<string> pieces, List<int> owners) = SegmentChunker.Flatten(request.segments, budget);
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(
            pieces,
            request.passSegments,
            request.passContextBefore,
            request.passContextAfter
        );

        int stepCount = 1 + batches.Count + (request.proofread ? 1 : 0);
        int stepIndex = 1;
        double totalCostUsd = 0;

        (CarefulPass.Memo memo, double memoCostUsd) = await BuildMemoAsync(request, cancellationToken);
        totalCostUsd += memoCostUsd;
        progress?.Report(new RunStep("reading the chapter", stepIndex, stepCount, memoCostUsd));

        List<string> translatedPieces = [];
        List<string> carriedContext = request.recentContext.ToList();
        string sessionId = string.Empty;
        int pieceCursor = 0;

        for (int batchPosition = 0; batchPosition < batches.Count; batchPosition++) {
            SegmentChunker.ContextBatch batch = batches[batchPosition];

            string hash = BatchHash.Of(
                batch.contextBefore,
                batch.body,
                batch.contextAfter,
                request.targetLanguage,
                request.model,
                promptFingerprint
            );

            // A batch already translated in an earlier, interrupted run is reused rather than paid
            // for again. On a chapter that takes many requests, a failure on a late one otherwise
            // discards every one that succeeded before it - and the retry spends that money twice.
            string? cached = await cache.TryGetAsync(hash, cancellationToken);
            IReadOnlyList<string> batchResult;
            double batchCostUsd = 0;

            if (cached is not null) {
                batchResult = JsonSerializer.Deserialize<List<string>>(cached) ?? [];
            } else {
                ChapterTranslationOutcome outcome = await TranslateResilientAsync(
                    request,
                    memo,
                    batch,
                    null,
                    carriedContext,
                    onDelta,
                    batching.maxAttempts,
                    cancellationToken
                );

                batchResult = outcome.segments;
                batchCostUsd = outcome.costUsd;
                totalCostUsd += outcome.costUsd;
                sessionId = outcome.sessionId;

                await cache.StoreAsync(
                    hash,
                    batchPosition,
                    JsonSerializer.Serialize(batchResult),
                    outcome.costUsd,
                    cancellationToken
                );
            }

            translatedPieces.AddRange(batchResult);
            pieceCursor += batch.body.Count;
            stepIndex++;

            // Reported for a cached batch too, at zero cost: the bar still needs to move past it, and
            // that batch's money was already charged to the run that first translated it.
            progress?.Report(new RunStep(
                CarefulPass.ParagraphRangeTitle("translating", pieceCursor - batch.body.Count + 1, pieceCursor, pieces.Count),
                stepIndex,
                stepCount,
                batchCostUsd
            ));

            if (batches.Count > 1) {
                carriedContext = request.recentContext
                    .Concat([string.Join("\n", batchResult.TakeLast(batching.carryParagraphs))])
                    .ToList();
            }
        }

        List<string> finalSegments = SegmentChunker.Rejoin(translatedPieces, owners, request.segments.Count);

        if (request.proofread) {
            // The cross-chapter voice window, not carriedContext: a re-pass batch is not
            // necessarily adjacent to wherever the main loop's last batch happened to end, so the
            // within-chapter tail that window grew is not this batch's neighbour and would only
            // read as a non sequitur. Its own contextBefore, drawn from its actual position, already
            // covers what a reader needs for continuity.
            (finalSegments, double proofreadCostUsd) = await ProofreadAndRepassAsync(
                request,
                memo,
                pieces,
                owners,
                translatedPieces,
                finalSegments,
                request.recentContext,
                stepIndex,
                stepCount,
                onDelta,
                progress,
                cancellationToken
            );

            totalCostUsd += proofreadCostUsd;
        }

        return new ChapterTranslationOutcome(finalSegments, sessionId, totalCostUsd);
    }


    // Reads the finished chapter back once, source against translation, and redoes whatever it
    // flags through the same careful pass, with the critique attached. At most one re-pass: what
    // comes back from it is final, whether or not it would still satisfy a second reading.
    private async Task<(List<string> segments, double costUsd)> ProofreadAndRepassAsync(
        ChapterTranslationRequest request,
        CarefulPass.Memo memo,
        List<string> pieces,
        List<int> owners,
        List<string> translatedPieces,
        List<string> reassembledSegments,
        IReadOnlyList<string> recentContext,
        int stepIndex,
        int stepCount,
        Func<string, Task>? onDelta,
        IProgress<RunStep>? progress,
        CancellationToken cancellationToken
    ) {
        double costUsd = 0;

        string proofreadSystemPrompt = CarefulPass.BuildTranslateProofreadSystemPrompt(
            request.sourceLanguage,
            request.targetLanguage,
            request.segments.Count,
            request.glossary,
            request.voiceSummary
        );
        string passage = CarefulPass.BuildTranslateProofreadPassage(request.segments, reassembledSegments);

        (string reply, double proofreadCostUsd) = await AskAsync(
            proofreadSystemPrompt,
            passage,
            request.model,
            request.thinkingTokens,
            cancellationToken
        );

        costUsd += proofreadCostUsd;
        stepIndex++;

        IReadOnlyList<CarefulPass.ProofreadFinding> findings = CarefulPass.ParseProofreadFindings(
            reply,
            request.segments.Count
        );

        progress?.Report(new RunStep("proofreading", stepIndex, stepCount, proofreadCostUsd));

        if (findings.Count == 0) {
            return (reassembledSegments, costUsd);
        }

        Dictionary<int, string> problemsBySegment = findings
            .GroupBy(finding => finding.segmentIndex)
            .ToDictionary(
                group => group.Key,
                group => string.Join("; ", group.Select(finding => CarefulPass.FormatCritique(finding.type, finding.problem)))
            );

        List<int> flaggedPieces = Enumerable.Range(0, pieces.Count)
            .Where(pieceIndex => problemsBySegment.ContainsKey(owners[pieceIndex]))
            .ToList();

        List<CarefulPass.RepassGroup> repassGroups = CarefulPass.GroupFlaggedForRepass(
            flaggedPieces,
            pieces,
            request.passSegments,
            request.passContextBefore,
            request.passContextAfter
        );

        double repassCostUsd = 0;

        foreach (CarefulPass.RepassGroup group in repassGroups) {
            List<string?> critiques = Enumerable.Range(0, group.batch.body.Count)
                .Select(offset => problemsBySegment.GetValueOrDefault(owners[group.firstPieceIndex + offset]))
                .ToList();

            ChapterTranslationOutcome redone = await TranslateResilientAsync(
                request,
                memo,
                group.batch,
                critiques,
                recentContext,
                onDelta,
                batching.maxAttempts,
                cancellationToken
            );

            repassCostUsd += redone.costUsd;

            for (int offset = 0; offset < redone.segments.Count; offset++) {
                translatedPieces[group.firstPieceIndex + offset] = redone.segments[offset];
            }
        }

        costUsd += repassCostUsd;
        // The re-pass is a step the chapter did not know it would need until the proofread had
        // answered, so the count grows with it rather than the index running past the count.
        stepCount++;
        stepIndex++;

        progress?.Report(new RunStep(
            CarefulPass.RedoneParagraphsTitle(problemsBySegment.Count),
            stepIndex,
            stepCount,
            repassCostUsd
        ));

        return (SegmentChunker.Rejoin(translatedPieces, owners, request.segments.Count), costUsd);
    }


    // A batch the model could not answer in the required format after the configured number of
    // attempts is halved and each half tried once, instead of failing the chapter - a smaller batch
    // is what usually fixes a model that lost the markers, not another attempt at the same size. On
    // a book-length chapter that is the difference between losing one paragraph's worth of work and
    // losing everything already paid for, and it also recovers from the ordinary cause - a batch
    // that turned out to be too large for one reply.
    //
    // Subdivision stops at a single segment: at that point the format is not the problem. Both
    // halves keep the whole batch's context - halving the body must not also halve what it is read
    // against.
    private async Task<ChapterTranslationOutcome> TranslateResilientAsync(
        ChapterTranslationRequest request,
        CarefulPass.Memo memo,
        SegmentChunker.ContextBatch batch,
        IReadOnlyList<string?>? critiques,
        IReadOnlyList<string> context,
        Func<string, Task>? onDelta,
        int attempts,
        CancellationToken cancellationToken
    ) {
        try {
            return await TranslateBatchAsync(request, memo, batch, critiques, context, onDelta, attempts, cancellationToken);
        } catch (SegmentProtocolException) when (batch.body.Count > 1) {
            int half = batch.body.Count / 2;

            SegmentChunker.ContextBatch firstHalf = batch with { body = batch.body.Take(half).ToList() };
            SegmentChunker.ContextBatch secondHalf = batch with { body = batch.body.Skip(half).ToList() };

            ChapterTranslationOutcome first = await TranslateResilientAsync(
                request,
                memo,
                firstHalf,
                critiques?.Take(half).ToList(),
                context,
                onDelta,
                1,
                cancellationToken
            );

            ChapterTranslationOutcome second = await TranslateResilientAsync(
                request,
                memo,
                secondHalf,
                critiques?.Skip(half).ToList(),
                context,
                onDelta,
                1,
                cancellationToken
            );

            return new ChapterTranslationOutcome(
                first.segments.Concat(second.segments).ToList(),
                second.sessionId,
                first.costUsd + second.costUsd
            );
        }
    }


    private async Task<ChapterTranslationOutcome> TranslateBatchAsync(
        ChapterTranslationRequest request,
        CarefulPass.Memo memo,
        SegmentChunker.ContextBatch batch,
        IReadOnlyList<string?>? critiques,
        IReadOnlyList<string> context,
        Func<string, Task>? onDelta,
        int attempts,
        CancellationToken cancellationToken
    ) {
        ChapterTranslationRequest batchRequest = request with {
            segments = batch.body,
            recentContext = context,
            memo = memo
        };

        string prompt = CarefulPass.BuildBatchPrompt(batch.contextBefore, batch.body, batch.contextAfter, critiques);
        double costUsd = 0;
        SegmentProtocolException? lastFailure = null;

        for (int attempt = 1; attempt <= attempts; attempt++) {
            ClaudeCodeOptions options = new ClaudeCodeOptions {
                Model = request.model,
                SystemPrompt = BuildSystemPrompt(batchRequest, attempt),
                MaxTurns = 1,
                IncludePartialMessages = onDelta is not null,
                EnvironmentVariables = ThinkingEnvironment.BuildEnvironmentVariables(request.thinkingTokens),
                ExtraArgs = new Dictionary<string, string?> {
                    { "safe-mode", null },
                    { "tools", "" },
                    { "no-session-persistence", null }
                }
            };

            StringBuilder reply = new StringBuilder();
            string sessionId = string.Empty;
            MarkerFilter filter = new MarkerFilter();

            await foreach (IMessage message in ClaudeQuery.QueryAsync(prompt, options, null, cancellationToken)) {
                if (onDelta is not null && message is StreamEvent partial) {
                    string clean = filter.Push(ReadTextDelta(partial));

                    if (clean.Length > 0) {
                        await onDelta(clean);
                    }
                }

                if (message is AssistantMessage assistant) {
                    foreach (TextBlock block in assistant.Content.OfType<TextBlock>()) {
                        reply.Append(block.Text);
                    }
                }

                if (message is ResultMessage result) {
                    sessionId = result.SessionId;
                    costUsd += result.TotalCostUsd ?? 0;

                    if (result.IsError) {
                        throw new InvalidOperationException($"Claude Code returned an error: {result.Result}");
                    }
                }
            }

            try {
                IReadOnlyList<string> parsed = SegmentProtocol.Parse(reply.ToString(), batch.body.Count);

                return new ChapterTranslationOutcome(parsed, sessionId, costUsd);
            } catch (SegmentProtocolException failure) {
                lastFailure = failure;
            }
        }

        throw lastFailure!;
    }


    // Reads the whole chapter once, before any paragraph of it is translated, on the glossary
    // model rather than the book's own - listing which established terms this chapter mentions and
    // describing its register is reading comprehension, not the literary judgement a translation
    // pays for.
    private static async Task<(CarefulPass.Memo memo, double costUsd)> BuildMemoAsync(
        ChapterTranslationRequest request,
        CancellationToken cancellationToken
    ) {
        string systemPrompt = CarefulPass.BuildTranslateMemoPrompt(
            request.sourceLanguage,
            request.targetLanguage,
            request.glossary
        );

        (string reply, double costUsd) = await AskAsync(
            systemPrompt,
            request.sourcePlainText,
            request.glossaryModel,
            0,
            cancellationToken
        );

        return (CarefulPass.ParseTranslateMemo(reply), costUsd);
    }


    // The same one-shot, tool-less, single-turn call the memo and the proofread both make - listing
    // or reading back rather than writing prose is not what the translation model's careful-pass
    // machinery above is built for. thinkingTokens is 0 for the memo (a mechanical reading) and
    // request.thinkingTokens for the proofread (a pass call).
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


    public static string BuildSystemPrompt(ChapterTranslationRequest request, int attempt) {
        StringBuilder prompt = new StringBuilder();

        // Naming each unwanted reflex separately is what suppresses it. A general "output only the
        // translation" holds on some runs and not others.
        prompt.AppendLine(
            $"You are a translation engine, not an assistant. The input is a fragment of a "
            + $"{request.sourceLanguage} light novel. Your entire output is its {request.targetLanguage} "
            + "translation."
        );
        prompt.AppendLine(
            "Never write a preamble, a heading, a note, a gloss, a word-by-word breakdown, a literal "
            + "alternative, or a comment on the source. Never address the reader."
        );
        prompt.AppendLine("If a fragment is speech addressed to a character, translate it. Do not answer it.");
        prompt.AppendLine(
            $"Preserve honorifics as {request.targetLanguage} transliterations, and preserve each "
            + "speaker's register and level of politeness."
        );

        if (request.normalizeQuotes) {
            prompt.AppendLine($"Use the quotation marks conventional in written {request.targetLanguage}.");
        } else {
            prompt.AppendLine("Keep the quotation marks used in the source.");
        }

        prompt.AppendLine();
        prompt.AppendLine("FORMAT");
        prompt.AppendLine(
            $"The input is a numbered list of segments, one per line, each starting with a marker "
            + $"such as {Marker(0)}. Return exactly {request.segments.Count} segments, each on its own "
            + "line, each starting with its own unchanged marker, in the same order."
        );
        prompt.AppendLine(
            "Never merge, split, drop, reorder or add a segment, even when the target language would "
            + "read better that way. One source segment is one output segment."
        );
        prompt.AppendLine("Write nothing before the first marker and nothing after the last segment.");

        // The segments are Markdown. Saying so, and saying which constructs to leave alone, is what
        // keeps a heading from losing its hashes or a link from being flattened into bare text.
        prompt.AppendLine(
            "Segment text is Markdown. Preserve its syntax: *emphasis*, **strong**, headings, "
            + "> quotes, list bullets, [links](url) and ![images](src) must come back in the same "
            + "form, with only the words translated. Do not add Markdown the source did not have, "
            + "and do not invent markers or tags of any kind."
        );
        prompt.AppendLine(
            "A segment may contain inline HTML such as <ruby>. Leave any such tag exactly as it is."
        );

        if (attempt > 1) {
            prompt.AppendLine();
            prompt.AppendLine(
                "The previous reply did not follow this format. Emit only marked segment lines, one per "
                + "segment, and nothing else."
            );
        }

        if (request.glossary.Count > 0) {
            prompt.AppendLine();
            prompt.AppendLine("GLOSSARY - these renderings are already established. Use them exactly.");

            foreach (GlossaryTerm term in request.glossary) {
                prompt.Append("- ").Append(term.sourceTerm).Append(" -> ").Append(term.targetTerm);

                if (!string.IsNullOrWhiteSpace(term.notes)) {
                    prompt.Append(" (").Append(term.notes).Append(')');
                }

                prompt.AppendLine();
            }
        }

        // Learned once from the chapters a human actually translated, as opposed to the short
        // window below sampled from whatever chapters happen to be nearby. Placed first because it
        // is the standing voice of the book; the recent-context window beneath it is a reminder,
        // not a second, competing source of the same thing.
        if (!string.IsNullOrWhiteSpace(request.voiceSummary)) {
            prompt.AppendLine();
            prompt.AppendLine(
                "TRANSLATOR'S VOICE - how this book's human translator writes, learned from their "
                + "chapters. Match it."
            );
            prompt.AppendLine(request.voiceSummary);
        }

        if (request.recentContext.Count > 0) {
            prompt.AppendLine();
            // The window now contains source lines as well as their translations, so the instruction
            // has to be explicit that none of it is work to be done. Without that, a model handed
            // untranslated source in the prompt will helpfully translate it and return the wrong
            // number of segments.
            prompt.AppendLine(
                "RECENT WORK - already-finished work from earlier chapters, shown as source lines "
                + "each followed by its translation after \"-> \". It is there so you can match the "
                + "established voice and the way this translator handles names, register and "
                + "sentence structure. None of it is work to be done: do not translate it, do not "
                + "repeat it, and do not include it in your output."
            );

            foreach (string passage in request.recentContext) {
                prompt.AppendLine(passage);
            }
        }

        // Two layers, general first and this book's last, with the precedence stated rather than
        // implied. Left to infer it, a model handed two style instructions that disagree will pick
        // one at random from chapter to chapter, which is worse than either.
        if (!string.IsNullOrWhiteSpace(request.globalStyleGuide)) {
            prompt.AppendLine();
            prompt.AppendLine("STYLE - general, applies to every book");
            prompt.AppendLine(request.globalStyleGuide);
        }

        if (!string.IsNullOrWhiteSpace(request.styleGuide)) {
            prompt.AppendLine();

            prompt.AppendLine(
                string.IsNullOrWhiteSpace(request.globalStyleGuide)
                    ? "STYLE - this book"
                    : "STYLE - this book. Where it disagrees with the general style above, this wins."
            );

            prompt.AppendLine(request.styleGuide);
        }

        // What the chapter's own memo already read, and how to read each paragraph closely rather
        // than skimming a whole batch - the two things a careful pass adds on top of everything
        // above, which stays exactly what it already was.
        if (request.memo is not null) {
            CarefulPass.AppendThisChapter(prompt, request.memo);
        }

        CarefulPass.AppendHowToReadForTranslate(prompt, request.sourceLanguage, request.targetLanguage);

        return prompt.ToString();
    }


    // The CLI wraps the raw API event, so the text of a delta sits several levels down. Anything
    // that is not a text delta - and there are several other event types, thinking among them - is
    // filtered out here and reaches neither the reader's stream nor the segment parser.
    private static string ReadTextDelta(StreamEvent partial) {
        if (!partial.Event.TryGetProperty("delta", out JsonElement delta)) {
            return string.Empty;
        }

        if (!delta.TryGetProperty("type", out JsonElement type) || type.GetString() != "text_delta") {
            return string.Empty;
        }

        return delta.TryGetProperty("text", out JsonElement text) ? text.GetString() ?? string.Empty : string.Empty;
    }


    private static string Marker(int index) {
        return $"⟦#{index}⟧";
    }
}
