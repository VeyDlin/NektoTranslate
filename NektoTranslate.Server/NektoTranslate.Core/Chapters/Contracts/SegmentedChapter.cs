namespace NektoTranslate.Chapters.Contracts;


// A chapter split into its translatable blocks.
//
// There is no skeleton to keep alongside them: in Markdown the blocks joined by blank lines *are*
// the document, so reassembly is a join rather than a substitution into saved markup. That is the
// structural simplification storing Markdown buys over storing HTML.
public sealed record SegmentedChapter(
    IReadOnlyList<ChapterSegment> segments
) {

    public string PlainText() {
        return string.Join("\n", segments.Select(segment => segment.PlainText()));
    }
}
