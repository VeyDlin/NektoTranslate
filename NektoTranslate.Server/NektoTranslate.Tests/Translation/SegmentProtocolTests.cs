using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


public class SegmentProtocolTests {

    [Fact]
    public void EachSegmentGetsItsOwnMarkedLine() {
        string formatted = SegmentProtocol.Format(["первый", "второй"]);

        Assert.Equal("⟦#0⟧первый\n⟦#1⟧второй", formatted.ReplaceLineEndings("\n"));
    }


    [Fact]
    public void AWellFormedReplyRoundTrips() {
        IReadOnlyList<string> parsed = SegmentProtocol.Parse("⟦#0⟧first\n⟦#1⟧second", 2);

        Assert.Equal(["first", "second"], parsed);
    }


    [Fact]
    public void MarkersCarryPositionSoAReorderedReplyStillLandsCorrectly() {
        IReadOnlyList<string> parsed = SegmentProtocol.Parse("⟦#1⟧second\n⟦#0⟧first", 2);

        Assert.Equal(["first", "second"], parsed);
    }


    [Fact]
    public void APreambleBeforeTheFirstMarkerIsDiscarded() {
        IReadOnlyList<string> parsed = SegmentProtocol.Parse("Here is the translation:\n⟦#0⟧first", 1);

        Assert.Equal(["first"], parsed);
    }


    [Fact]
    public void AWrappedSegmentIsJoinedBackTogether() {
        IReadOnlyList<string> parsed = SegmentProtocol.Parse("⟦#0⟧a long line\nthat wrapped", 1);

        Assert.Equal(["a long line that wrapped"], parsed);
    }


    [Fact]
    public void InlinePlaceholdersSurviveTheRoundTrip() {
        IReadOnlyList<string> parsed = SegmentProtocol.Parse("⟦#0⟧he had ⟦1⟧already⟧ woken", 1);

        Assert.Contains("⟦1⟧", parsed[0]);
    }


    [Fact]
    public void ALostSegmentIsRefused() {
        SegmentProtocolException failure = Assert.Throws<SegmentProtocolException>(
            () => SegmentProtocol.Parse("⟦#0⟧first", 2)
        );

        Assert.Contains("Segment 1 is missing", failure.Message);
    }


    [Fact]
    public void MergedSegmentsAreRefusedRatherThanShiftingTheChapter() {
        SegmentProtocolException failure = Assert.Throws<SegmentProtocolException>(
            () => SegmentProtocol.Parse("⟦#0⟧first and second together", 2)
        );

        Assert.Contains("1 of 2 arrived", failure.Message);
    }


    [Fact]
    public void MultilineSegmentsAreFlattenedOnTheWayOut() {
        string formatted = SegmentProtocol.Format(["two\nlines"]);

        Assert.Equal("⟦#0⟧two lines", formatted.ReplaceLineEndings("\n"));
    }
}
