using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Data;


namespace NektoTranslate.Chapters.Services;


public interface IChapterImportService {

    Task<ChapterImportResult> ImportAsync(
        long novelId,
        IReadOnlyList<ImportedChapter> chapters,
        CancellationToken cancellationToken = default
    );
}


// The single door through which chapter content enters the database.
//
// Whatever arrives - a parser's HTML, a manual paste of bare lines - is sanitized to the tag
// allowlist and then converted to Markdown, so exactly one format is stored. No later stage has to
// ask where a chapter came from, and the projection the glossary searches can never drift out of
// step with the text it was derived from.
//
// An incoming chapter is matched against what the novel already holds before anything is written,
// because a position alone was never an identity: the same page read twice used to make two
// chapters, and chapters deleted and re-imported came back at the wrong numbers. The address a
// chapter was fetched from is the one thing that names it the same way every time, so it is checked
// first and an explicit index second - the index only ever wins a slot nothing has already claimed.
public class ChapterImportService(
    NektoDbContext database,
    IMarkdownConversion conversion,
    IChapterSegmenter segmenter
) : IChapterImportService {

    public async Task<ChapterImportResult> ImportAsync(
        long novelId,
        IReadOnlyList<ImportedChapter> chapters,
        CancellationToken cancellationToken = default
    ) {
        int? highest = await database.chapters
            .Where(chapter => chapter.novelId == novelId)
            .Select(chapter => (int?)chapter.index)
            .MaxAsync(cancellationToken);

        int nextIndex = highest is int last ? last + 1 : 0;

        // One query for the whole batch rather than one per incoming chapter, the same discipline
        // TranslationImportService keeps: knowing what a novel already holds is the entire point of
        // this method, and it must not cost a query per candidate to find out.
        HashSet<string> wantedUrls = chapters
            .Where(incoming => incoming.sourceUrl is not null)
            .Select(incoming => incoming.sourceUrl!)
            .ToHashSet();

        HashSet<int> wantedIndices = chapters
            .Where(incoming => incoming.index.HasValue)
            .Select(incoming => incoming.index!.Value)
            .ToHashSet();

        List<Chapter> matches = wantedUrls.Count == 0 && wantedIndices.Count == 0
            ? []
            : await database.chapters
                .Where(chapter => chapter.novelId == novelId
                    && ((chapter.sourceUrl != null && wantedUrls.Contains(chapter.sourceUrl)) || wantedIndices.Contains(chapter.index)))
                .ToListAsync(cancellationToken);

        Dictionary<string, Chapter> bySourceUrl = new();
        Dictionary<int, Chapter> byIndex = new();

        foreach (Chapter match in matches) {
            byIndex[match.index] = match;

            if (match.sourceUrl is not null) {
                bySourceUrl[match.sourceUrl] = match;
            }
        }

        List<ChapterImportOutcome> outcomes = [];

        for (int position = 0; position < chapters.Count; position++) {
            ImportedChapter incoming = chapters[position];

            if (incoming.sourceUrl is not null && bySourceUrl.TryGetValue(incoming.sourceUrl, out Chapter? already)) {
                outcomes.Add(new ChapterImportOutcome(
                    position,
                    already,
                    Statuses.ChapterAlreadyImported.With(("index", already.index))
                ));

                continue;
            }

            if (incoming.index is int wantedIndex && byIndex.ContainsKey(wantedIndex)) {
                outcomes.Add(new ChapterImportOutcome(
                    position,
                    null,
                    Statuses.ChapterIndexOccupied.With(("index", wantedIndex))
                ));

                continue;
            }

            string markdown = conversion.ToMarkdown(incoming.html);
            SegmentedChapter segmented = segmenter.Segment(markdown);

            Chapter chapter = new Chapter {
                novelId = novelId,
                index = incoming.index ?? nextIndex++,
                title = incoming.title,
                sourceMarkdown = markdown,
                sourcePlainText = segmented.PlainText(),
                sourceUrl = incoming.sourceUrl
            };

            database.chapters.Add(chapter);

            // Recorded into both lookups immediately, not only after SaveChanges: two incoming
            // chapters in the same batch must not both claim the same address or the same index
            // just because neither of them is in the database yet.
            byIndex[chapter.index] = chapter;

            if (chapter.sourceUrl is not null) {
                bySourceUrl[chapter.sourceUrl] = chapter;
            }

            outcomes.Add(new ChapterImportOutcome(position, chapter, null));
        }

        await database.SaveChangesAsync(cancellationToken);

        return new ChapterImportResult(outcomes);
    }
}
