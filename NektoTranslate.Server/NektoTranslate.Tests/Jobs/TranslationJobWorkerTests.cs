using NektoTranslate.Glossary.Services;
using NektoTranslate.Jobs.Services;
using NektoTranslate.Translation.Contracts;
using Xunit;


namespace NektoTranslate.Tests.Jobs;


// TranslationJobWorker.BuildLearnedMessage is the one part of the LearnVoice job's report worth
// testing on its own: it stitches together what two independent passes found, over a range stored
// in the zero-based terms Chapter.index uses rather than the one-based numbers the run was actually
// started with. None of it touches the database, the queue or a model, which is what makes it safe
// to check here rather than through a full worker run.
public class TranslationJobWorkerTests {

    [Fact]
    public void TheRangeIsShiftedBackToTheNumbersTheUserTyped() {
        string message = TranslationJobWorker.BuildLearnedMessage(
            "Russian",
            0,
            19,
            new LearnedVoice(1, "summary", 37, 0),
            new LearnedGlossary(0, 0, 0, 0)
        );

        Assert.StartsWith("Learned the Russian voice from chapters 1-20:", message);
    }


    [Fact]
    public void TheGlossaryClauseIsDroppedWhenNoChapterHadBothSides() {
        string message = TranslationJobWorker.BuildLearnedMessage(
            "Russian",
            0,
            19,
            new LearnedVoice(1, "summary", 37, 0),
            new LearnedGlossary(0, 0, 0, 0)
        );

        Assert.Equal("Learned the Russian voice from chapters 1-20: 37 terms noted.", message);
    }


    [Fact]
    public void TheGlossaryClauseReportsRenderingsAndTheChaptersTheyCameFrom() {
        string message = TranslationJobWorker.BuildLearnedMessage(
            "Russian",
            0,
            19,
            new LearnedVoice(1, "summary", 37, 0),
            new LearnedGlossary(4, 12, 3, 0)
        );

        Assert.Equal(
            "Learned the Russian voice from chapters 1-20: 37 terms noted; "
            + "12 renderings learned from the 4 chapters that have their original.",
            message
        );
    }


    [Fact]
    public void ASingleQualifyingChapterReadsAsOneChapterNotAsTheNumberOne() {
        string message = TranslationJobWorker.BuildLearnedMessage(
            "Russian",
            0,
            19,
            new LearnedVoice(1, "summary", 37, 0),
            new LearnedGlossary(1, 1, 0, 0)
        );

        Assert.Equal(
            "Learned the Russian voice from chapters 1-20: 37 terms noted; "
            + "1 rendering learned from the chapter that has its original.",
            message
        );
    }
}
