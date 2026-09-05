using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Services;
using Xunit;
using Xunit.Abstractions;


namespace NektoTranslate.Tests.Chapters;


// Markup shapes real sites actually use, as opposed to the tidy paragraphs a fixture tends to have,
// carried through the whole import path: sanitize, convert to Markdown, segment.
//
// Anything that fails to produce segments here is a chapter that would be stored, marked translated
// and be empty - the worst failure this pipeline can have, because it is silent and the source is
// usually gone by the time anyone notices.
public class AwkwardMarkupTests(ITestOutputHelper output) {

    private readonly ChapterHtmlSanitizer sanitizer = new ChapterHtmlSanitizer();

    private readonly ChapterSegmenter segmenter = new ChapterSegmenter();

    private readonly ReverseMarkdown.Converter converter = new ReverseMarkdown.Converter(
        new ReverseMarkdown.Config {
            Tags = {
                Unknown = ReverseMarkdown.Config.UnknownTagsOption.PassThrough
            }
        }
    );


    [Theory]
    [InlineData("<pre>первая строка\nвторая строка</pre>", "pre", 2)]
    [InlineData("<div>текст главы</div>", "div only", 1)]
    [InlineData("текст прямо в body", "bare text", 1)]
    [InlineData("<div>первый<br><br>второй</div>", "div with br breaks", 2)]
    [InlineData("<span>текст в инлайне</span>", "inline only", 1)]
    [InlineData("<p>абзац</p><p><ruby>兄<rt>あに</rt></ruby></p>", "ruby paragraph", 2)]
    [InlineData("<div>первый</div><div>второй</div><div>третий</div>", "div per paragraph", 3)]
    [InlineData("<span>раз.</span><span>\n\n</span><span>два.</span><span> </span><span>три.</span>", "spans with whitespace structure", 2)]
    public void ContentSurvivesWhateverWrapsIt(string html, string shape, int expectedSegments) {
        string markdown = converter.Convert(sanitizer.Sanitize(html)).Trim();
        SegmentedChapter chapter = segmenter.Segment(markdown);

        output.WriteLine($"[{shape}] markdown: {markdown.Replace("\n", "\\n")}");
        output.WriteLine($"[{shape}] segments: {chapter.segments.Count}");

        Assert.Equal(expectedSegments, chapter.segments.Count);
    }


    // The shape novellunar.com actually serves: every sentence in its own <span>, a span holding
    // "\n\n" between paragraphs and one holding " " between sentences. Discarding whitespace-only
    // text while unwrapping the spans welded a whole chapter into one paragraph with the sentences
    // touching - "potions!Behind his counter". The whitespace is the structure.
    [Fact]
    public void WhitespaceBetweenUnwrappedSpansIsKeptAsStructure() {
        string html =
            "<div class=\"text-gray-800\">"
            + "<span class=\"t\">Tye, I need potions!</span><span>\n\n</span>"
            + "<span class=\"t\">Behind his counter, he smiled.</span><span> </span>"
            + "<span class=\"t\">Why did everyone joke?</span><span>\n\n</span>"
            + "<span class=\"t\">Welcome.</span>"
            + "</div>";

        List<string> blocks = MarkdownText.SplitBlocks(converter.Convert(sanitizer.Sanitize(html)).Trim());

        Assert.Equal(["Tye, I need potions!", "Behind his counter, he smiled. Why did everyone joke?", "Welcome."], blocks);
    }


    [Fact]
    public void FuriganaSurvivesTheConversionToMarkdown() {
        string markdown = converter.Convert(
            sanitizer.Sanitize("<p><ruby>兄<rt>あに</rt></ruby>は眠っている</p>")
        );

        output.WriteLine($"markdown: {markdown}");

        Assert.Contains("<rt>あに</rt>", markdown);
    }
}
