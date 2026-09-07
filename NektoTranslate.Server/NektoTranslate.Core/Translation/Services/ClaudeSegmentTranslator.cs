using System.Text;
using System.Text.Json;
using ClaudeCodeSdk;
using ClaudeCodeSdk.Types;
using NektoTranslate.Common.Models;
using NektoTranslate.Jobs.Contracts;
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


// Translates one chapter's worth of segments in a single call to the Claude Code CLI.
//
// Three flags strip the coding agent down to a plain translation engine: --safe-mode drops
// CLAUDE.md, skills, plugins, hooks and MCP servers while leaving authentication working;
// --system-prompt replaces the built-in prompt instead of appending to it; --tools "" sends no
// tool definitions. Measured on a single sentence that is the difference between ~29k and ~358
// input tokens.
public class ClaudeSegmentTranslator(EngineOptions options) : ISegmentTranslator {

    private readonly BatchingOptions batching = options.batching;


    // A chapter is normally one request. A long one is split on paragraph boundaries, because the
    // alternative is a reply truncated at the token ceiling - which arrives as a segment-count
    // mismatch and costs the whole chapter rather than one batch.
    //
    // Batches run in order and each is given the tail of the previous batch's translation, so the
    // voice does not reset in the middle of a chapter. The glossary is in the system prompt and so
    // reaches every batch unchanged.
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
        List<IReadOnlyList<string>> batches = SegmentChunker.Partition(pieces, budget);

        List<string> translatedPieces = [];
        List<string> carriedContext = request.recentContext.ToList();
        string sessionId = string.Empty;
        double costUsd = 0;

        for (int index = 0; index < batches.Count; index++) {
            IReadOnlyList<string> batch = batches[index];
            string hash = BatchHash.Of(batch, request.targetLanguage, request.model, promptFingerprint);

            // A batch already translated in an earlier, interrupted run is reused rather than paid
            // for again. On a chapter that takes seventeen requests, a failure on the fifteenth
            // otherwise discards fourteen that succeeded - and the retry spends that money twice.
            string? cached = await cache.TryGetAsync(hash, cancellationToken);
            IReadOnlyList<string> batchResult;
            double batchCostUsd = 0;

            if (cached is not null) {
                batchResult = JsonSerializer.Deserialize<List<string>>(cached) ?? [];
            } else {
                ChapterTranslationOutcome outcome = await TranslateResilientAsync(
                    request,
                    batch,
                    carriedContext,
                    onDelta,
                    cancellationToken
                );

                batchResult = outcome.segments;
                batchCostUsd = outcome.costUsd;
                costUsd += outcome.costUsd;
                sessionId = outcome.sessionId;

                await cache.StoreAsync(
                    hash,
                    index,
                    JsonSerializer.Serialize(batchResult),
                    outcome.costUsd,
                    cancellationToken
                );
            }

            translatedPieces.AddRange(batchResult);

            // Reported for a cached batch too, at zero cost: the bar still needs to move past it, and
            // that batch's money was already charged to the run that first translated it.
            progress?.Report(new RunStep(
                $"translating batch {index + 1} of {batches.Count}",
                index + 1,
                batches.Count,
                batchCostUsd
            ));

            if (batches.Count > 1) {
                carriedContext = request.recentContext
                    .Concat([string.Join("\n", batchResult.TakeLast(batching.carryParagraphs))])
                    .ToList();
            }
        }

        return new ChapterTranslationOutcome(
            SegmentChunker.Rejoin(translatedPieces, owners, request.segments.Count),
            sessionId,
            costUsd
        );
    }


    // A batch the model could not answer in the required format is halved and each half retried,
    // instead of failing the chapter. On a book-length chapter that is the difference between
    // losing one paragraph's worth of work and losing everything already paid for, and it also
    // recovers from the ordinary cause - a batch that turned out to be too large for one reply.
    //
    // Subdivision stops at a single segment: at that point the format is not the problem.
    private async Task<ChapterTranslationOutcome> TranslateResilientAsync(
        ChapterTranslationRequest request,
        IReadOnlyList<string> batch,
        IReadOnlyList<string> context,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken
    ) {
        try {
            return await TranslateBatchAsync(request, batch, context, onDelta, cancellationToken);
        } catch (SegmentProtocolException) when (batch.Count > 1) {
            int half = batch.Count / 2;

            ChapterTranslationOutcome first = await TranslateResilientAsync(
                request,
                batch.Take(half).ToList(),
                context,
                onDelta,
                cancellationToken
            );

            ChapterTranslationOutcome second = await TranslateResilientAsync(
                request,
                batch.Skip(half).ToList(),
                context,
                onDelta,
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
        IReadOnlyList<string> segments,
        IReadOnlyList<string> context,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken
    ) {
        request = request with { segments = segments, recentContext = context };

        string prompt = SegmentProtocol.Format(segments);
        double costUsd = 0;
        SegmentProtocolException? lastFailure = null;

        for (int attempt = 1; attempt <= batching.maxAttempts; attempt++) {
            ClaudeCodeOptions options = new ClaudeCodeOptions {
                Model = request.model,
                SystemPrompt = BuildSystemPrompt(request, attempt),
                MaxTurns = 1,
                IncludePartialMessages = onDelta is not null,
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
                IReadOnlyList<string> parsed = SegmentProtocol.Parse(reply.ToString(), request.segments.Count);

                return new ChapterTranslationOutcome(parsed, sessionId, costUsd);
            } catch (SegmentProtocolException failure) {
                lastFailure = failure;
            }
        }

        throw lastFailure!;
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

        return prompt.ToString();
    }


    // The CLI wraps the raw API event, so the text of a delta sits several levels down. Anything
    // that is not a text delta - and there are several other event types - yields nothing.
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
