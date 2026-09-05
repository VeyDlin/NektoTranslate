using NektoTranslate.Common.Contracts;
using Xunit;


namespace NektoTranslate.Tests.Common;


// Chapters are indexed from zero in the database and numbered from one on every screen, and a
// status is read on a screen. "There is no chapter 0" reached a user once for the chapter the list
// called 1; these pin the shift to the one place that does it.
public class StatusTests {

    [Fact]
    public void AChapterPositionIsSaidAsTheNumberTheReaderCountsFrom() {
        Status status = Statuses.NoChapterAtIndex.With(("index", 0));

        Assert.Contains("chapter 1", status.text);
        Assert.Equal(1, status.args!["index"]);
    }


    [Fact]
    public void AMoveTargetIsShiftedTheSameWay() {
        Status status = Statuses.MoveTargetOccupied.With(("target", 4));

        Assert.Contains("Chapter 5", status.text);
        Assert.Equal(5, status.args!["target"]);
    }


    // Only positions shift. A count, an address or a language is what it is.
    [Fact]
    public void OtherValuesAreLeftAlone() {
        Status status = Statuses.TranslationAlreadyExists.With(("index", 2), ("language", "Russian"));

        Assert.Contains("Chapter 3 already has a Russian translation", status.text);
        Assert.Equal("Russian", status.args!["language"]);
    }


    // A position that is absent must not become a number: the placeholder stays, exactly as it did
    // before the shift existed, rather than reading as chapter one.
    [Fact]
    public void AMissingPositionIsNotInvented() {
        Status status = Statuses.NoChapterAtIndex.With(("language", "Russian"));

        Assert.Contains("{index}", status.text);
        Assert.False(status.args!.ContainsKey("index"));
    }
}
