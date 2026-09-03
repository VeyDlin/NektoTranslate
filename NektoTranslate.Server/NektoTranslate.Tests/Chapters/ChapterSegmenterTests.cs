using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Services;
using Xunit;


namespace NektoTranslate.Tests.Chapters;


public class ChapterSegmenterTests {

    private readonly ChapterSegmenter segmenter = new ChapterSegmenter();


    [Fact]
    public void EachBlockBecomesOneSegment() {
        SegmentedChapter chapter = segmenter.Segment("первый\n\nвторой");

        Assert.Equal(2, chapter.segments.Count);
        Assert.Equal("первый", chapter.segments[0].text);
        Assert.Equal("второй", chapter.segments[1].text);
    }


    [Fact]
    public void SeveralBlankLinesAreStillOneSeparation() {
        SegmentedChapter chapter = segmenter.Segment("первый\n\n\n\nвторой");

        Assert.Equal(2, chapter.segments.Count);
    }


    [Fact]
    public void AListStaysOneBlock() {
        SegmentedChapter chapter = segmenter.Segment("- раз\n- два");

        Assert.Equal("- раз\n- два", Assert.Single(chapter.segments).text);
    }


    [Fact]
    public void RubyContributesOnlyItsBaseCharacters() {
        SegmentedChapter chapter = segmenter.Segment("<ruby>兄<rt>あに</rt></ruby>は眠っている");

        Assert.Equal("兄は眠っている", chapter.segments[0].text);
    }


    [Fact]
    public void InlineFormattingTravelsWithTheText() {
        SegmentedChapter chapter = segmenter.Segment("он *уже* проснулся");
        ChapterSegment segment = chapter.segments[0];

        Assert.Equal("он *уже* проснулся", segment.text);
        Assert.Equal("он уже проснулся", segment.PlainText());
    }


    [Fact]
    public void TranslatingEveryBlockRebuildsTheChapter() {
        SegmentedChapter chapter = segmenter.Segment("первый\n\nвторой");

        string markdown = segmenter.Reassemble(chapter, ["first", "second"]);

        Assert.Equal("first\n\nsecond", markdown);
    }


    [Fact]
    public void FormattingTheModelReturnsIsKept() {
        SegmentedChapter chapter = segmenter.Segment("он *уже* проснулся");

        string markdown = segmenter.Reassemble(chapter, ["he had *already* woken"]);

        Assert.Equal("he had *already* woken", markdown);
    }


    [Fact]
    public void LosingASegmentIsRefused() {
        SegmentedChapter chapter = segmenter.Segment("первый\n\nвторой");

        SegmentCountMismatchException failure = Assert.Throws<SegmentCountMismatchException>(
            () => segmenter.Reassemble(chapter, ["only one"])
        );

        Assert.Equal(2, failure.expected);
        Assert.Equal(1, failure.actual);
    }


    [Fact]
    public void PlainTextProjectionIsOneBlockPerLine() {
        SegmentedChapter chapter = segmenter.Segment("первый\n\nвто*рой*");

        Assert.Equal("первый\nвторой", chapter.PlainText());
    }


    [Fact]
    public void AnImageOnlyBlockCarriesNoTranslatableText() {
        SegmentedChapter chapter = segmenter.Segment("![](illust_01.jpg)\n\nтекст");

        Assert.Equal("текст", Assert.Single(chapter.segments).text);
    }


    [Fact]
    public void HeadingsAndQuotesKeepTheirMarkers() {
        SegmentedChapter chapter = segmenter.Segment("## Заголовок\n\n> цитата");

        Assert.Equal("## Заголовок", chapter.segments[0].text);
        Assert.Equal("> цитата", chapter.segments[1].text);
    }
}
