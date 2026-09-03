using System.Net;


namespace NektoTranslate.Chapters.Services;


public interface IMarkdownConversion {

    string ToMarkdown(string content);
}


// Turns whatever arrived - a parser's HTML, a manual paste of bare lines - into the one stored
// format.
//
// Shared by chapter import and translation import rather than duplicated, and that is the point.
// Both sides of a book are compared paragraph against paragraph: the glossary reads how a term was
// rendered by lining up a source block with its counterpart, and a quality check reports a block
// index that has to mean the same thing in both. Two converters configured even slightly
// differently would break that alignment in a way that looks like a bad translation rather than a
// bad import.
public class MarkdownConversion(IChapterHtmlSanitizer sanitizer) : IMarkdownConversion {

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


    public string ToMarkdown(string content) {
        // Line endings are normalised on the way in. The converter emits the host platform's, and
        // stored text that carries CR is a trap for every later reader that splits on "\n\n" - which
        // is the natural way to write that check and wrong on exactly one platform.
        return converter.Convert(sanitizer.Sanitize(AsHtml(content)))
            .ReplaceLineEndings("\n")
            .Trim();
    }


    // A manual paste arrives as bare lines. Wrapping them into paragraphs here means the rest of the
    // pipeline never has to ask where the text came from.
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
