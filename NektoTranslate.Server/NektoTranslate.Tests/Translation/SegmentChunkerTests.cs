using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// WithContext is the shape a careful pass reads a chapter through: small, non-overlapping bodies
// with the neighbouring paragraphs attached only for the model to read, never to touch. An off-by-
// one here either drops a paragraph from the chapter or hands the model someone else's paragraph to
// translate, so the shape is worth asserting directly.
public class SegmentChunkerTests {

    private static readonly IReadOnlyList<string> TenParagraphs = [
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"
    ];


    [Fact]
    public void BodiesCoverEverySegmentExactlyOnceAndInOrder() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(TenParagraphs, 3, 2, 1);

        IEnumerable<string> covered = batches.SelectMany(batch => batch.body);

        Assert.Equal(TenParagraphs, covered);
    }


    [Fact]
    public void BodiesAreAtMostPassSegmentsLong() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(TenParagraphs, 3, 2, 1);

        Assert.All(batches, batch => Assert.True(batch.body.Count <= 3));
    }


    [Fact]
    public void ALaterBatchsContextBeforeIsTheParagraphsImmediatelyAheadOfItsBody() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(TenParagraphs, 3, 2, 1);

        // The second batch's body starts at "four" (index 3); its context is the two paragraphs
        // right before it, "two" and "three".
        Assert.Equal(["two", "three"], batches[1].contextBefore);
        Assert.Equal(["four", "five", "six"], batches[1].body);
    }


    [Fact]
    public void ABatchsContextAfterIsTheParagraphImmediatelyBehindItsBody() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(TenParagraphs, 3, 2, 1);

        Assert.Equal(["four"], batches[0].contextAfter);
    }


    [Fact]
    public void TheFirstBatchHasNoContextBeforeBecauseThereIsNothingBeforeTheChapter() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(TenParagraphs, 3, 2, 1);

        Assert.Empty(batches[0].contextBefore);
    }


    [Fact]
    public void TheLastBatchHasNoContextAfterBecauseThereIsNothingAfterTheChapter() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(TenParagraphs, 3, 2, 1);

        Assert.Empty(batches[^1].contextAfter);
    }


    // Ten paragraphs, three per body: the last body is "ten" alone, and its context reaches back
    // two paragraphs even though the body itself is shorter than a full pass.
    [Fact]
    public void TheLastBatchsContextBeforeIsStillTheFullWindowWhenThereIsRoomForIt() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(TenParagraphs, 3, 2, 1);

        Assert.Equal(["ten"], batches[^1].body);
        Assert.Equal(["eight", "nine"], batches[^1].contextBefore);
    }


    [Fact]
    public void AWindowLargerThanTheChapterYieldsOneBatchWithNoContext() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(["only one"], 5, 2, 1);

        SegmentChunker.ContextBatch batch = Assert.Single(batches);
        Assert.Empty(batch.contextBefore);
        Assert.Equal(["only one"], batch.body);
        Assert.Empty(batch.contextAfter);
    }


    [Fact]
    public void ZeroContextOnEitherSideYieldsNoContextAtAll() {
        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(TenParagraphs, 3, 0, 0);

        Assert.All(batches, batch => Assert.Empty(batch.contextBefore));
        Assert.All(batches, batch => Assert.Empty(batch.contextAfter));
    }


    [Fact]
    public void NoSegmentsYieldsNoBatches() {
        Assert.Empty(SegmentChunker.WithContext([], 3, 2, 1));
    }


    [Fact]
    public void EveryPieceThatFlattenWouldHaveSplitIsStillSplitBeforeThisRuns() {
        ChunkBudget tinyBudget = new ChunkBudget(10, 10);

        (List<string> pieces, _) = SegmentChunker.Flatten(
            ["a short one", "a paragraph far too long for a ten character budget to hold whole"],
            tinyBudget
        );

        List<SegmentChunker.ContextBatch> batches = SegmentChunker.WithContext(pieces, 2, 1, 1);

        Assert.All(
            batches.SelectMany(batch => batch.body),
            piece => Assert.True(SegmentChunker.Fits(piece, tinyBudget))
        );
    }
}
