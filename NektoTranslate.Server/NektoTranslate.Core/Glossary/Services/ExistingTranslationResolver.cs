using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Models;
using NektoTranslate.Glossary.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Glossary.Services;


public interface IExistingTranslationResolver {

    Task<ResolvedTerm?> ResolveAsync(
        long novelId,
        string sourceTerm,
        string language,
        string model,
        CancellationToken cancellationToken = default
    );
}


// Recovers how a term was already rendered by a translation the book already has, so the agent
// continues an existing translation instead of renaming its characters halfway through.
//
// The whole point is to do this without reading the book. A term costs one indexed lookup, one
// paragraph of source, one paragraph of translation and one small model call - never a pass over
// the chapters, which on a thousand-chapter novel would cost more than the translation itself.
public class ExistingTranslationResolver(
    ITermLocator locator,
    ITermExtractor extractor,
    ITranslationVersions translationVersions,
    EngineOptions options
) : IExistingTranslationResolver {

    private readonly int maxCandidateChapters = options.glossary.maxCandidateChapters;

    // How far to look on either side when the two sides do not have the same number of paragraphs.
    private readonly int alignmentWindow = options.glossary.alignmentWindow;


    public async Task<ResolvedTerm?> ResolveAsync(
        long novelId,
        string sourceTerm,
        string language,
        string model,
        CancellationToken cancellationToken = default
    ) {
        IReadOnlyList<TermOccurrence> occurrences = await locator.FindAsync(
            novelId,
            sourceTerm,
            maxCandidateChapters,
            cancellationToken
        );

        if (occurrences.Count == 0) {
            return null;
        }

        double costUsd = 0;

        foreach (TermOccurrence occurrence in occurrences) {
            string? translated = await translationVersions.CurrentOf(occurrence.chapterId, language)
                .AsNoTracking()
                .Select(t => t.plainText)
                .FirstOrDefaultAsync(cancellationToken);

            if (translated is null) {
                continue;
            }

            string counterpart = Align(translated, occurrence.segmentIndex);

            if (counterpart.Length == 0) {
                continue;
            }

            (string? rendering, double askCost) = await extractor.ReadRenderingAsync(
                occurrence.segmentText,
                counterpart,
                sourceTerm,
                language,
                model,
                cancellationToken
            );

            costUsd += askCost;

            if (rendering is not null) {
                return new ResolvedTerm(sourceTerm, rendering, occurrence.chapterId, costUsd);
            }
        }

        return null;
    }


    // Paragraph index is the alignment key, because both sides are segmented the same way. When the
    // counts disagree - which is what an imported translation that merged or split paragraphs looks
    // like - the window widens rather than guessing, and the whole chapter is the last resort. A
    // wrong paragraph would poison the glossary with a term lifted from the wrong sentence.
    private string Align(string translatedPlainText, int segmentIndex) {
        string[] lines = translatedPlainText.ReplaceLineEndings("\n").Split('\n');

        if (lines.Length == 0) {
            return string.Empty;
        }

        if (segmentIndex >= 0 && segmentIndex < lines.Length && lines[segmentIndex].Trim().Length > 0) {
            return lines[segmentIndex];
        }

        int start = Math.Max(0, segmentIndex - alignmentWindow);
        int end = Math.Min(lines.Length - 1, segmentIndex + alignmentWindow);

        if (start <= end) {
            string window = string.Join("\n", lines[start..(end + 1)]).Trim();

            if (window.Length > 0) {
                return window;
            }
        }

        return translatedPlainText;
    }
}
