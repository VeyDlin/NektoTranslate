using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Models;
using NektoTranslate.Glossary.Contracts;


namespace NektoTranslate.Glossary.Services;


// Exact substring search over the flat projection of each chapter.
//
// The search runs against the source text rather than the translation, because the source is the
// stable side - the rendering is precisely what drifts. It runs against the flat projection rather
// than the markup, because a name split by a tag does not exist as a substring of the HTML.
public class ExactTermLocator(NektoDbContext database, EngineOptions options) : ITermLocator {

    private readonly int maxInflectionLength = options.glossary.maxInflectionLength;



    public async Task<IReadOnlyList<TermOccurrence>> FindAsync(
        long novelId,
        string term,
        int limit,
        CancellationToken cancellationToken = default
    ) {
        if (string.IsNullOrWhiteSpace(term)) {
            return [];
        }

        // Ordered by chapter so the earliest use wins: that is the one a reader met first, and the
        // one an existing translation is most likely to have settled.
        var candidates = await database.chapters
            .AsNoTracking()
            .Where(chapter => chapter.novelId == novelId && chapter.sourcePlainText.Contains(term))
            .OrderBy(chapter => chapter.index)
            .Take(limit)
            .Select(chapter => new { chapter.id, chapter.index, chapter.sourcePlainText })
            .ToListAsync(cancellationToken);

        List<TermOccurrence> occurrences = [];

        foreach (var candidate in candidates) {
            string[] lines = candidate.sourcePlainText.ReplaceLineEndings("\n").Split('\n');

            for (int segmentIndex = 0; segmentIndex < lines.Length; segmentIndex++) {
                if (TermMatching.Contains(lines[segmentIndex], term, maxInflectionLength)) {
                    occurrences.Add(new TermOccurrence(
                        candidate.id,
                        candidate.index,
                        segmentIndex,
                        lines[segmentIndex]
                    ));

                    break;
                }
            }
        }

        return occurrences;
    }
}
