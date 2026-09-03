using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Common.Data;


namespace NektoTranslate.Chapters.Services;


public interface IChapterImportService {

    Task<IReadOnlyList<Chapter>> ImportAsync(
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
public class ChapterImportService(
    NektoDbContext database,
    IMarkdownConversion conversion,
    IChapterSegmenter segmenter
) : IChapterImportService {

    public async Task<IReadOnlyList<Chapter>> ImportAsync(
        long novelId,
        IReadOnlyList<ImportedChapter> chapters,
        CancellationToken cancellationToken = default
    ) {
        int? highest = await database.chapters
            .Where(chapter => chapter.novelId == novelId)
            .Select(chapter => (int?)chapter.index)
            .MaxAsync(cancellationToken);

        int nextIndex = highest is int last ? last + 1 : 0;
        List<Chapter> created = [];

        foreach (ImportedChapter incoming in chapters) {
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
            created.Add(chapter);
        }

        await database.SaveChangesAsync(cancellationToken);

        return created;
    }


}
