using NektoTranslate.Chapters.Services;
using Xunit;
using Xunit.Abstractions;


namespace NektoTranslate.Tests.Chapters;


// Establishes what the converter actually does with the markup this application cares about,
// before anything is built on top of it. Ruby is the one that matters: Markdown has no syntax for
// furigana, so it either survives as inline HTML or it is lost at import, and there is no second
// chance to fetch it.
public class HtmlToMarkdownProbeTests(ITestOutputHelper output) {

    private readonly ChapterHtmlSanitizer sanitizer = new ChapterHtmlSanitizer();


    [Theory]
    [InlineData("<p>обычный абзац</p>", "paragraph")]
    [InlineData("<p>он <em>уже</em> проснулся</p>", "emphasis")]
    [InlineData("<p><strong>важно</strong></p>", "strong")]
    [InlineData("<h2>Заголовок</h2>", "heading")]
    [InlineData("<blockquote><p>цитата</p></blockquote>", "blockquote")]
    [InlineData("<ul><li>раз</li><li>два</li></ul>", "list")]
    [InlineData("<p><img src=\"illust_01.jpg\" alt=\"вид\"></p>", "image")]
    [InlineData("<p><a href=\"https://example.com\">ссылка</a></p>", "link")]
    [InlineData("<p><ruby>兄<rt>あに</rt></ruby>は眠っている</p>", "ruby")]
    [InlineData("<p>первый</p><p>второй</p>", "two paragraphs")]
    [InlineData("<hr>", "rule")]
    public void ShowWhatTheConverterProduces(string html, string shape) {
        string sanitized = sanitizer.Sanitize(html);
        string markdown = new ReverseMarkdown.Converter().Convert(sanitized);

        output.WriteLine($"[{shape}]");
        output.WriteLine($"  html : {sanitized}");
        output.WriteLine($"  md   : {markdown.Replace("\n", "\\n")}");

        Assert.NotNull(markdown);
    }
}
