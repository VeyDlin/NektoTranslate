namespace NektoTranslate.Chapters.Services;


// The model returned a different number of segments than it was given. Losing or inventing a
// segment means paragraphs have been merged, split or dropped, so the chapter is refused rather
// than written - a silently mangled chapter surfaces a hundred chapters later, when the damage is
// no longer traceable.
public class SegmentCountMismatchException(int expected, int actual)
    : Exception($"Expected {expected} translated segments, received {actual}.") {

    public int expected { get; } = expected;

    public int actual { get; } = actual;
}
