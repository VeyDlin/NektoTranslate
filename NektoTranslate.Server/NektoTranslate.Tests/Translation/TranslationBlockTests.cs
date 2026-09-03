using NektoTranslate.Chapters.Services;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// The alignment these tests pin is the whole basis of editing a block.
//
// A quality check reports "block 7". The reader marks the seventh paragraph. The editor replaces
// the seventh block. If those three ever mean different things, a correction lands in the wrong
// paragraph and quietly damages a translation the user thought they were fixing - a failure with no
// error message and no obvious symptom.
public class TranslationBlockTests {

    private const string Translation =
        "Танака всё ещё спал.\n\nСестра открыла занавеску.\n\nЗа окном шёл дождь.";


    [Fact]
    public void JoiningWhatWasSplitGivesBackTheSameDocument() {
        Assert.Equal(Translation, TranslationBlocks.Join(TranslationBlocks.Split(Translation)));
    }


    [Fact]
    public void ReplacingABlockLeavesEveryOtherBlockAlone() {
        List<string> blocks = TranslationBlocks.Split(Translation);
        blocks[1] = "Сестра распахнула занавеску.";

        Assert.Equal(
            "Танака всё ещё спал.\n\nСестра распахнула занавеску.\n\nЗа окном шёл дождь.",
            TranslationBlocks.Join(blocks)
        );
    }


    // The invariant itself: one block, one line of plain text, at the same index.
    [Fact]
    public void PlainTextHasOneLinePerBlockInTheSameOrder() {
        List<string> blocks = TranslationBlocks.Split(Translation);
        string[] lines = TranslationBlocks.PlainTextOf(blocks).Split('\n');

        Assert.Equal(blocks.Count, lines.Length);
        Assert.Equal("Сестра открыла занавеску.", lines[1]);
    }


    // A block carrying Markdown must still occupy exactly one line of plain text, or an index taken
    // from the plain text would drift past every formatted paragraph in the chapter.
    [Fact]
    public void MarkdownInsideABlockDoesNotSplitIt() {
        List<string> blocks = TranslationBlocks.Split(
            "## Глава первая\n\n*Танака* открыл [дверь](http://example.invalid).\n\nОн вышел."
        );

        string[] lines = TranslationBlocks.PlainTextOf(blocks).Split('\n');

        Assert.Equal(3, blocks.Count);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Танака открыл дверь.", lines[1]);
    }


    // What the translator produces and what the editor reads back have to agree, so the round trip
    // is checked against the segmenter rather than only against itself.
    [Fact]
    public void TheEditorSeesTheSameBlocksTheSegmenterProduced() {
        ChapterSegmenter segmenter = new ChapterSegmenter();
        List<string> translated = ["Первый абзац.", "Второй абзац.", "Третий абзац."];

        string markdown = segmenter.Reassemble(
            segmenter.Segment("A\n\nB\n\nC"),
            translated
        );

        Assert.Equal(translated, TranslationBlocks.Split(markdown));
    }
}
