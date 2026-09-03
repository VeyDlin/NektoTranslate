using System.Text;
using Microsoft.Extensions.AI;
using NektoTranslate.Common.Models;


namespace NektoTranslate.Glossary.Services;


// Runs the glossary's two small calls against a locally hosted model instead of the subscription.
//
// These are the calls worth moving. "List the proper nouns in this passage" and "what did this
// translation call X" are mechanical: they need reading comprehension, not literary judgement, and a
// seven-billion-parameter model does them adequately. They are also the numerous ones - a chapter
// introducing forty names costs forty-one calls, of which one is the translation.
//
// The translation itself stays on the subscription. Prose is where model quality shows, and moving
// it to save quota would trade the thing the reader notices for the thing they do not.
//
// Falls back to the supplied extractor whenever no local endpoint is configured or the local call
// fails, so a stopped server degrades to the previous behaviour rather than failing the chapter.
public class LocalTermExtractor(
    IChatClientFactory factory,
    ITermExtractor fallback,
    EngineOptions options
) : ITermExtractor {

    private const string NotFound = "NONE";

    private readonly int maxTermLength = options.glossary.maxTermLength;


    public async Task<(IReadOnlyList<string> terms, double costUsd)> ExtractCandidatesAsync(
        IReadOnlyList<string> segments,
        string sourceLanguage,
        string model,
        CancellationToken cancellationToken = default
    ) {
        using IChatClient? client = segments.Count == 0 ? null : await factory.CreateAsync(cancellationToken);

        if (client is null) {
            return await fallback.ExtractCandidatesAsync(segments, sourceLanguage, model, cancellationToken);
        }

        string instruction =
            $"You extract proper nouns from {sourceLanguage} prose. List every personal name, place, "
            + "organisation, named technique and named item that appears in the text.\n"
            + "Write one term per line, exactly as it is spelled in the source, and nothing else. No "
            + "numbering, no explanations, no translations, no headings.\n"
            + $"Ignore common nouns and pronouns. If the text contains no proper nouns, write {NotFound}.";

        try {
            string reply = await AskAsync(client, instruction, string.Join("\n", segments), cancellationToken);

            List<string> terms = reply
                .ReplaceLineEndings("\n")
                .Split('\n')
                .Select(line => line.Trim().TrimStart('-', '*', ' ').Trim())
                .Where(line => line.Length > 0 && !line.Equals(NotFound, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // A local model that answers with prose instead of a list has told us nothing, and
            // accepting it would fill the glossary with sentences. Falling back costs one call.
            if (terms.Any(term => term.Length > maxTermLength)) {
                return await fallback.ExtractCandidatesAsync(segments, sourceLanguage, model, cancellationToken);
            }

            return (terms, 0);
        } catch (Exception) {
            return await fallback.ExtractCandidatesAsync(segments, sourceLanguage, model, cancellationToken);
        }
    }


    public async Task<(string? rendering, double costUsd)> ReadRenderingAsync(
        string sourceFragment,
        string translatedFragment,
        string term,
        string targetLanguage,
        string model,
        CancellationToken cancellationToken = default
    ) {
        using IChatClient? client = await factory.CreateAsync(cancellationToken);

        if (client is null) {
            return await fallback.ReadRenderingAsync(
                sourceFragment,
                translatedFragment,
                term,
                targetLanguage,
                model,
                cancellationToken
            );
        }

        string instruction =
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

        try {
            string rendering = (await AskAsync(client, instruction, question.ToString(), cancellationToken)).Trim();

            if (rendering.Length == 0
                || rendering.Length > maxTermLength
                || rendering.Contains(NotFound, StringComparison.OrdinalIgnoreCase)
                || rendering.Contains('\n')) {
                return (null, 0);
            }

            return (rendering, 0);
        } catch (Exception) {
            return await fallback.ReadRenderingAsync(
                sourceFragment,
                translatedFragment,
                term,
                targetLanguage,
                model,
                cancellationToken
            );
        }
    }


    private static async Task<string> AskAsync(
        IChatClient client,
        string instruction,
        string prompt,
        CancellationToken cancellationToken
    ) {
        ChatResponse response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, instruction),
                new ChatMessage(ChatRole.User, prompt)
            ],
            new ChatOptions { Temperature = 0 },
            cancellationToken
        );

        return response.Text;
    }
}
