using System.Text;
using ClaudeCodeSdk;
using ClaudeCodeSdk.Types;
using NektoTranslate.Common.Models;


namespace NektoTranslate.Glossary.Services;


public interface ITermExtractor {

    Task<(IReadOnlyList<string> terms, double costUsd)> ExtractCandidatesAsync(
        IReadOnlyList<string> segments,
        string sourceLanguage,
        string model,
        CancellationToken cancellationToken = default
    );


    Task<(string? rendering, double costUsd)> ReadRenderingAsync(
        string sourceFragment,
        string translatedFragment,
        string term,
        string targetLanguage,
        string model,
        CancellationToken cancellationToken = default
    );
}


// The two small model calls the glossary needs. Both are deliberately narrow: one paragraph or one
// chapter of source at a time, no tools, no agent context, so a lookup costs a fraction of a cent
// and a book of a thousand chapters never triggers a full pass over itself.
public class ClaudeTermExtractor(EngineOptions options) : ITermExtractor {

    private const string NotFound = "NONE";

    private readonly int maxBatchCharacters = options.batching.maxCharacters;


    public async Task<(IReadOnlyList<string> terms, double costUsd)> ExtractCandidatesAsync(
        IReadOnlyList<string> segments,
        string sourceLanguage,
        string model,
        CancellationToken cancellationToken = default
    ) {
        if (segments.Count == 0) {
            return ([], 0);
        }

        // Batched for the same reason translation is: a chapter that runs to the length of a book
        // would otherwise be sent whole in a single request. Terms are merged across batches, which
        // is safe because the answer is a set of names rather than an ordered transformation.
        if (segments.Count > 1) {
            List<string> merged = [];
            double batchedCost = 0;

            foreach (IReadOnlyList<string> batch in PartitionByLength(segments)) {
                (IReadOnlyList<string> found, double cost) = await ExtractOneBatchAsync(
                    batch,
                    sourceLanguage,
                    model,
                    cancellationToken
                );

                merged.AddRange(found);
                batchedCost += cost;
            }

            return (merged.Distinct(StringComparer.Ordinal).ToList(), batchedCost);
        }

        return await ExtractOneBatchAsync(segments, sourceLanguage, model, cancellationToken);
    }


    private List<IReadOnlyList<string>> PartitionByLength(IReadOnlyList<string> segments) {
        List<IReadOnlyList<string>> batches = [];
        List<string> current = [];
        int size = 0;

        foreach (string segment in segments) {
            if (current.Count > 0 && size + segment.Length > maxBatchCharacters) {
                batches.Add(current);
                current = [];
                size = 0;
            }

            current.Add(segment);
            size += segment.Length;
        }

        if (current.Count > 0) {
            batches.Add(current);
        }

        return batches;
    }


    private async Task<(IReadOnlyList<string> terms, double costUsd)> ExtractOneBatchAsync(
        IReadOnlyList<string> segments,
        string sourceLanguage,
        string model,
        CancellationToken cancellationToken
    ) {
        string systemPrompt =
            $"You extract proper nouns from {sourceLanguage} prose. List every personal name, place, "
            + "organisation, named technique and named item that appears in the text.\n"
            + "Write one term per line, exactly as it is spelled in the source, and nothing else. No "
            + "numbering, no explanations, no translations, no headings.\n"
            + "Ignore common nouns and pronouns. If the text contains no proper nouns, write NONE.";

        (string reply, double costUsd) = await AskAsync(
            systemPrompt,
            string.Join("\n", segments),
            model,
            cancellationToken
        );

        List<string> terms = reply
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim().TrimStart('-', '*', ' ').Trim())
            .Where(line => line.Length > 0 && !line.Equals(NotFound, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return (terms, costUsd);
    }


    // Given a source paragraph and its counterpart from a translation that already exists, recover
    // how that translation rendered the term. This is what stops the model from inventing a second
    // spelling for a character the reader already knows by name.
    public async Task<(string? rendering, double costUsd)> ReadRenderingAsync(
        string sourceFragment,
        string translatedFragment,
        string term,
        string targetLanguage,
        string model,
        CancellationToken cancellationToken = default
    ) {
        string systemPrompt =
            $"You are given a passage, its existing {targetLanguage} translation, and one term from the "
            + "passage.\n"
            + $"Answer with the {targetLanguage} words that the translation used for that term, in their "
            + "dictionary form, and nothing else.\n"
            + $"If the translation does not render the term at all, answer exactly {NotFound}.\n"
            + "No explanations, no quotation marks, no alternatives.";

        StringBuilder question = new StringBuilder();
        question.AppendLine("SOURCE");
        question.AppendLine(sourceFragment);
        question.AppendLine();
        question.AppendLine("TRANSLATION");
        question.AppendLine(translatedFragment);
        question.AppendLine();
        question.Append("TERM: ").Append(term);

        (string reply, double costUsd) = await AskAsync(systemPrompt, question.ToString(), model, cancellationToken);

        string rendering = reply.Trim();

        if (rendering.Length == 0
            || rendering.Contains(NotFound, StringComparison.OrdinalIgnoreCase)
            || rendering.Contains('\n')) {
            return (null, costUsd);
        }

        return (rendering, costUsd);
    }


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
