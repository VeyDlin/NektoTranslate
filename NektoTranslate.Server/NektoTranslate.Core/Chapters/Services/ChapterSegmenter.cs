using NektoTranslate.Chapters.Contracts;


namespace NektoTranslate.Chapters.Services;


public interface IChapterSegmenter {

    SegmentedChapter Segment(string markdown);


    string Reassemble(SegmentedChapter chapter, IReadOnlyList<string> translatedTexts);
}


// Splits a chapter into translatable blocks and joins translated blocks back into a chapter.
//
// Storing Markdown makes both halves trivial: a blank line separates blocks, and blocks joined by
// blank lines are the document again. There is no skeleton to preserve and no markup for the model
// to damage, because Markdown's inline syntax travels with the text and models reproduce it
// naturally.
//
// The guarantee that matters is unchanged: the block count must round-trip exactly. Losing or
// merging a block means paragraphs have shifted, which is refused rather than written.
public class ChapterSegmenter : IChapterSegmenter {

    public SegmentedChapter Segment(string markdown) {
        if (string.IsNullOrWhiteSpace(markdown)) {
            return new SegmentedChapter([]);
        }

        List<ChapterSegment> segments = [];

        foreach (string block in MarkdownText.SplitBlocks(markdown)) {
            // Furigana is a reading aid for the source script with no counterpart in the target
            // language, so the model is given the base characters. The stored source keeps its ruby.
            string forTranslation = MarkdownText.StripRuby(block);

            if (MarkdownText.Strip(forTranslation).Length == 0) {
                continue;
            }

            segments.Add(new ChapterSegment(segments.Count, forTranslation));
        }

        return new SegmentedChapter(segments);
    }


    public string Reassemble(SegmentedChapter chapter, IReadOnlyList<string> translatedTexts) {
        if (translatedTexts.Count != chapter.segments.Count) {
            throw new SegmentCountMismatchException(chapter.segments.Count, translatedTexts.Count);
        }

        return string.Join("\n\n", translatedTexts.Select(text => text.Trim()));
    }
}
