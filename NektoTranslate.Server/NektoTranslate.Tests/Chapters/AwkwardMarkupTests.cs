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
            UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.PassThrough
        }
    );


    [Theory]
    [InlineData("<pre>первая строка\nвторая строка</pre>", "pre", 2)]
    [InlineData("<div>текст главы</div>", "div only", 1)]
    [InlineData("текст прямо в body", "bare text", 1)]
    [InlineData("<div>первый<br><br>второй</div>", "div with br breaks", 2)]
    [InlineData("<span>текст в инлайне</span>", "inline only", 1)]
    [InlineData("<p>абзац</p><p><ruby>兄<rt>あに</rt></ruby></p>", "ruby paragraph", 2)]
    public void ContentSurvivesWhateverWrapsIt(string html, string shape, int expectedSegments) {
        string markdown = converter.Convert(sanitizer.Sanitize(html)).Trim();
        SegmentedChapter chapter = segmenter.Segment(markdown);

        output.WriteLine($"[{shape}] markdown: {markdown.Replace("\n", "\\n")}");
        output.WriteLine($"[{shape}] segments: {chapter.segments.Count}");

        Assert.Equal(expectedSegments, chapter.segments.Count);
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
