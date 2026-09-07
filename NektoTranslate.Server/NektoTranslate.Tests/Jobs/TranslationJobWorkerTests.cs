using NektoTranslate.Glossary.Services;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Services;
using NektoTranslate.Translation.Contracts;
using Xunit;


namespace NektoTranslate.Tests.Jobs;


// TranslationJobWorker.BuildLearnedMessage and TranslationJobWorker.ApplyStep are the two pure parts
// of the worker's own reporting - the first stitches together what two independent LearnVoice passes
// found, the second turns one reported RunStep into the job fields the strip and the jobs table
// read. Neither touches the database, the queue or a model, which is what makes them safe to check
// directly rather than only by running a whole job through a fake model.
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


    [Fact]
    public void AChapterStepIsPrefixedWithTheChapterAndMovesItsOwnStepFields() {
        TranslationJob job = new TranslationJob { processedCount = 4, totalCount = 10 };

        TranslationJobWorker.ApplyStep(job, 0, 12, new RunStep("translating batch 3 of 7", 3, 7, 0.02));

        Assert.Equal("Chapter 12 · translating batch 3 of 7", job.currentStep);
        Assert.Equal(3, job.stepIndex);
        Assert.Equal(7, job.stepCount);

        // Chapters, not batches, are what processedCount and totalCount count for Translate and
        // Repair - a step inside a chapter must not move either.
        Assert.Equal(4, job.processedCount);
        Assert.Equal(10, job.totalCount);
    }


    [Fact]
    public void ALearnVoiceStepHasNoChapterPrefixAndDrivesProcessedAndTotalDirectly() {
        TranslationJob job = new TranslationJob();

        TranslationJobWorker.ApplyStep(job, 0, null, new RunStep("Writing the voice profile", 5, 5, 0.01));

        Assert.Equal("Writing the voice profile", job.currentStep);
        Assert.Equal(5, job.processedCount);
        Assert.Equal(5, job.totalCount);
        Assert.Null(job.stepIndex);
        Assert.Null(job.stepCount);
    }


    [Fact]
    public void TheGlossaryPhaseOfLearnVoiceOffsetsByTheVoicePhasesStepCount() {
        TranslationJob job = new TranslationJob();

        // The voice phase finished at step 5 of 5 - the glossary phase that follows continues the
        // same "N of M" from there instead of restarting at one.
        TranslationJobWorker.ApplyStep(job, 5, null, new RunStep("Learning renderings from chapter 2 of 4", 2, 4, 0.01));

        Assert.Equal("Learning renderings from chapter 2 of 4", job.currentStep);
        Assert.Equal(7, job.processedCount);
        Assert.Equal(9, job.totalCount);
        Assert.Null(job.stepIndex);
        Assert.Null(job.stepCount);
    }


    [Fact]
    public void EveryStepsCostAddsToWhatTheJobHasAlreadySpent() {
        TranslationJob job = new TranslationJob { costUsd = 1.5 };

        TranslationJobWorker.ApplyStep(job, 0, 3, new RunStep("translating batch 1 of 2", 1, 2, 0.02));
        TranslationJobWorker.ApplyStep(job, 0, 3, new RunStep("translating batch 2 of 2", 2, 2, 0.03));

        Assert.Equal(1.55, job.costUsd, 3);
    }


    // A cached batch is still reported so the bar moves, but at zero cost - it was already paid for
    // by the run that first translated it.
    [Fact]
    public void AZeroCostStepMovesTheStepFieldsWithoutAddingToSpend() {
        TranslationJob job = new TranslationJob { costUsd = 0.4 };

        TranslationJobWorker.ApplyStep(job, 0, 3, new RunStep("translating batch 2 of 5", 2, 5, 0));

        Assert.Equal(2, job.stepIndex);
        Assert.Equal(5, job.stepCount);
        Assert.Equal(0.4, job.costUsd, 3);
    }
}
