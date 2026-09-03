using System.Net;
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
    IChapterHtmlSanitizer sanitizer,
    IChapterSegmenter segmenter
) : IChapterImportService {

    private static readonly ReverseMarkdown.Converter converter = new ReverseMarkdown.Converter(
        new ReverseMarkdown.Config {
            // Tags with no Markdown equivalent are kept as inline HTML rather than discarded. That
            // is what carries furigana through: Markdown has no syntax for it, but it permits the
            // HTML, and dropping it at import would be irreversible.
            UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.PassThrough,
            GithubFlavored = false,
            SmartHrefHandling = true
        }
    );


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
            // Line endings are normalised on the way in. The converter emits the host platform's,
            // and stored text that carries CR is a trap for every later reader that splits on "\n\n"
            // - which is the natural way to write that check and wrong on exactly one platform.
            string markdown = converter.Convert(sanitizer.Sanitize(AsHtml(incoming.html)))
                .ReplaceLineEndings("\n")
                .Trim();
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


    // A manual paste arrives as bare lines. Wrapping them into paragraphs here means the rest of
    // the pipeline never has to ask where a chapter came from.
    private static string AsHtml(string content) {
        if (content.Contains('<')) {
            return content;
        }

        IEnumerable<string> paragraphs = content
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => $"<p>{WebUtility.HtmlEncode(line)}</p>");

        return string.Concat(paragraphs);
    }
}
