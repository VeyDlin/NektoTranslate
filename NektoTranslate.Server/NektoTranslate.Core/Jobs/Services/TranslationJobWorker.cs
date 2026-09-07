using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Jobs.Services;


// Runs queued translation jobs, one chapter at a time, one job at a time.
//
// Sequential on purpose. Each chapter is translated with the glossary as the previous chapter left
// it, so parallelism here would let two chapters independently invent competing renderings of the
// same name - the single failure this whole design exists to prevent.
//
// Every chapter is committed as it finishes rather than at the end of the run, so a reader can
// start on chapter twelve while thirteen is still being written, and an interrupted run keeps
// everything it had already done.
public class TranslationJobWorker(
    IServiceScopeFactory scopes,
    TranslationJobQueue queue,
    ILogger<TranslationJobWorker> logger
) : BackgroundService {

    // Bridges a doer's synchronous IProgress<RunStep> callback to the async work of applying it,
    // saving it and publishing it. Blocking inside Report is safe here: nothing in this pipeline runs
    // under a captured synchronization context, and blocking is what keeps one step's publish
    // finished, in order, before the doer moves on to report the next one.
    private sealed class StepReporter(Func<RunStep, Task> handler) : IProgress<RunStep> {

        public void Report(RunStep value) {
            handler(value).GetAwaiter().GetResult();
        }
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await ReleaseStrandedChaptersAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested) {
            long jobId;

            try {
                jobId = await queue.DequeueAsync(stoppingToken);
            } catch (OperationCanceledException) {
                return;
            }

            try {
                await RunAsync(jobId, stoppingToken);
            } catch (Exception failure) {
                logger.LogError(failure, "Translation job {JobId} failed outside the chapter loop", jobId);
            }
        }
    }


    // A chapter left Running by a process that died has no run behind it any more, so nothing will
    // ever move it. Clearing these at startup is what makes a crash recoverable by restarting -
    // otherwise the affected chapters would look busy forever and be skipped by nothing.
    private async Task ReleaseStrandedChaptersAsync(CancellationToken stoppingToken) {
        try {
            using IServiceScope scope = scopes.CreateScope();

            NektoDbContext database = scope.ServiceProvider.GetRequiredService<NektoDbContext>();

            // To what is on file, the same way a finished run releases them: a process that died
            // mid-repair must not leave a translated chapter looking untranslated. Two plain updates
            // rather than one with a conditional: EF cannot translate a set-to-subquery that joins
            // the novel, and a startup step that throws is one that never releases anything.
            var stranded = await database.chapters
                .Where(chapter => chapter.translationState == ChapterTranslationState.Running
                    || chapter.translationState == ChapterTranslationState.Queued)
                .Select(chapter => new {
                    chapter.id,
                    hasTranslation = database.chapterTranslations.Any(translation =>
                        translation.chapterId == chapter.id && translation.language == chapter.novel!.targetLanguage)
                })
                .ToListAsync(stoppingToken);

            List<long> translated = stranded.Where(chapter => chapter.hasTranslation).Select(chapter => chapter.id).ToList();
            List<long> untranslated = stranded.Where(chapter => !chapter.hasTranslation).Select(chapter => chapter.id).ToList();

            await database.chapters
                .Where(chapter => translated.Contains(chapter.id))
                .ExecuteUpdateAsync(
                    update => update.SetProperty(chapter => chapter.translationState, ChapterTranslationState.Translated),
                    stoppingToken
                );

            await database.chapters
                .Where(chapter => untranslated.Contains(chapter.id))
                .ExecuteUpdateAsync(
                    update => update.SetProperty(chapter => chapter.translationState, ChapterTranslationState.None),
                    stoppingToken
                );

            int released = stranded.Count;

            if (released > 0) {
                logger.LogInformation(
                    "Released {Count} chapters left running by a previous process",
                    released
                );
            }
        } catch (Exception failure) {
            logger.LogWarning(failure, "Could not release stranded chapters at startup");
        }
    }


    private async Task RunAsync(long jobId, CancellationToken stoppingToken) {
        using IServiceScope scope = scopes.CreateScope();

        NektoDbContext database = scope.ServiceProvider.GetRequiredService<NektoDbContext>();
        ITranslationJobService jobs = scope.ServiceProvider.GetRequiredService<ITranslationJobService>();
        IChapterTranslator translator = scope.ServiceProvider.GetRequiredService<IChapterTranslator>();
        IChapterRepairer repairer = scope.ServiceProvider.GetRequiredService<IChapterRepairer>();
        IVoiceLearner voiceLearner = scope.ServiceProvider.GetRequiredService<IVoiceLearner>();
        IGlossaryLearner glossaryLearner = scope.ServiceProvider.GetRequiredService<IGlossaryLearner>();
        ITranslationNotifier notifier = scope.ServiceProvider.GetRequiredService<ITranslationNotifier>();

        TranslationJob? job = await database.translationJobs.FirstOrDefaultAsync(j => j.id == jobId, stoppingToken);

        if (job is null || job.state != JobState.Queued) {
            return;
        }

        CancellationTokenSource cancellation = queue.Register(jobId);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            cancellation.Token
        );

        // LearnVoice does not walk the chapter loop below at all - it reads its sample directly and
        // writes one profile - so it is handled and finished here, before ResolveScopeAsync (which
        // returns nothing for this mode) would otherwise leave the run looking like an empty,
        // instantly-completed translate pass.
        if (job.mode == TranslationJobMode.LearnVoice) {
            await RunLearnVoiceAsync(database, voiceLearner, glossaryLearner, notifier, job, linked.Token);
            queue.Release(jobId);

            return;
        }

        IReadOnlyList<long> chapterIds = await jobs.ResolveScopeAsync(job, stoppingToken);

        job.state = JobState.Running;
        job.startedAt = DateTimeOffset.UtcNow;
        job.totalCount = chapterIds.Count;
        await database.SaveChangesAsync(stoppingToken);
        await Publish(notifier, job);
        await MarkQueuedAsync(database, notifier, job, chapterIds);

        try {
            foreach (long chapterId in chapterIds) {
                if (linked.Token.IsCancellationRequested) {
                    await Finish(database, notifier, job, JobState.Cancelled, null, stoppingToken);

                    return;
                }

                // An advisory ceiling, checked between chapters. Stopping mid-chapter would waste
                // what was already spent on it and leave nothing to show for the money.
                if (job.budgetUsd is not null && job.costUsd >= job.budgetUsd) {
                    await notifier.AgentMessageAsync(
                        job.novelId,
                        $"Stopped: the run reached its budget of ${job.budgetUsd:F2}."
                    );
                    await Finish(database, notifier, job, JobState.Paused, null, stoppingToken);

                    return;
                }

                if (job.mode == TranslationJobMode.Repair) {
                    await RepairOneAsync(database, repairer, notifier, job, chapterId, linked.Token);
                } else {
                    await TranslateOneAsync(database, translator, notifier, job, chapterId, linked.Token);
                }
            }

            await Finish(database, notifier, job, JobState.Completed, null, stoppingToken);
        } catch (OperationCanceledException) {
            await Finish(database, notifier, job, JobState.Cancelled, null, CancellationToken.None);
        } catch (Exception failure) {
            logger.LogError(failure, "Translation job {JobId} failed", jobId);
            await Finish(database, notifier, job, JobState.Failed, failure.Message, CancellationToken.None);
        } finally {
            queue.Release(jobId);
        }
    }


    // A chapter that fails is marked and skipped rather than ending the run. On a long book one bad
    // chapter should not stop the other nine hundred, and the failure stays visible on the chapter
    // itself for the user to retry.
    private async Task TranslateOneAsync(
        NektoDbContext database,
        IChapterTranslator translator,
        ITranslationNotifier notifier,
        TranslationJob job,
        long chapterId,
        CancellationToken cancellationToken
    ) {
        await notifier.ChapterStateChangedAsync(job.novelId, chapterId, ChapterTranslationState.Running);

        int chapterNumber = await ChapterNumberAsync(database, chapterId, cancellationToken);

        // What the batches reported have already added to job.costUsd as they came in - tracked here
        // so the summary's own total, which counts them again, only contributes what is genuinely
        // new: extraction, glossary reconciliation, settling unresolved terms.
        double stepCostReported = 0;

        IProgress<RunStep> progress = new StepReporter(async step => {
            stepCostReported += step.costUsd;
            ApplyStep(job, 0, chapterNumber, step);
            await database.SaveChangesAsync(CancellationToken.None);
            await Publish(notifier, job);
        });

        try {
            ChapterTranslationSummary summary = await translator.TranslateAsync(chapterId, progress, cancellationToken);

            job.costUsd += summary.costUsd - stepCostReported;
            await notifier.ChapterTranslatedAsync(job.novelId, chapterId);
        } catch (OperationCanceledException) {
            // Nothing more to add here: every batch this attempt finished before cancelling already
            // charged its own cost to the job the moment it was reported.
            throw;
        } catch (Exception failure) {
            logger.LogError(failure, "Chapter {ChapterId} failed to translate", chapterId);

            // The reason goes to the reader as well as to the log: a chapter marked Failed with the
            // cause visible only in the server's console is a red dot nobody can act on.
            await notifier.AgentMessageAsync(
                job.novelId,
                $"Chapter {chapterNumber} could not be translated: {failure.Message}"
            );

            await database.chapters
                .Where(chapter => chapter.id == chapterId)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(
                        chapter => chapter.translationState,
                        ChapterTranslationState.Failed
                    ),
                    CancellationToken.None
                );

            await notifier.ChapterStateChangedAsync(job.novelId, chapterId, ChapterTranslationState.Failed);
        }

        job.processedCount++;
        job.currentStep = null;
        job.stepIndex = null;
        job.stepCount = null;
        await database.SaveChangesAsync(CancellationToken.None);
        await Publish(notifier, job);
    }


    // Mirrors TranslateOneAsync's shape, so a repair reports through the exact events the strip and
    // the reader already listen to and no client change is needed to show one running.
    //
    // It differs in one respect: without ClaudeSegmentTranslator's per-batch cache, a repair cannot
    // resume from where a previous, interrupted attempt left off - a cancelled or failed repair
    // starts its next attempt from the first batch again. Partial spend is not lost, though: the
    // same RunStep progress that moves the bar mid-chapter already charges each batch's cost to the
    // job the moment it is reported, cancelled or not.
    private async Task RepairOneAsync(
        NektoDbContext database,
        IChapterRepairer repairer,
        ITranslationNotifier notifier,
        TranslationJob job,
        long chapterId,
        CancellationToken cancellationToken
    ) {
        // Written, not only announced: ChapterTranslator marks its own chapter Running, but the
        // repairer leaves the state to whoever calls it, and a page opened mid-repair reads the
        // database, not the events it missed.
        await database.chapters
            .Where(chapter => chapter.id == chapterId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(
                    chapter => chapter.translationState,
                    ChapterTranslationState.Running
                ),
                cancellationToken
            );

        await notifier.ChapterStateChangedAsync(job.novelId, chapterId, ChapterTranslationState.Running);

        int chapterNumber = await ChapterNumberAsync(database, chapterId, cancellationToken);
        double stepCostReported = 0;

        IProgress<RunStep> progress = new StepReporter(async step => {
            stepCostReported += step.costUsd;
            ApplyStep(job, 0, chapterNumber, step);
            await database.SaveChangesAsync(CancellationToken.None);
            await Publish(notifier, job);
        });

        try {
            RepairedChapter outcome = await repairer.RepairAsync(chapterId, progress, cancellationToken);

            // The alignment pass and every batch already reported their own cost; a repair, unlike a
            // translation, has nothing beyond them, so this is normally an addition of zero.
            job.costUsd += outcome.costUsd - stepCostReported;
            await notifier.ChapterTranslatedAsync(job.novelId, chapterId);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception failure) {
            logger.LogError(failure, "Chapter {ChapterId} failed to repair", chapterId);

            await notifier.AgentMessageAsync(
                job.novelId,
                $"Chapter {chapterNumber} could not be repaired: {failure.Message}"
            );

            // The rendering this chapter already had is untouched - a repair never overwrites or
            // deletes it. Marking the chapter Failed here carries the same meaning it already carries
            // for a forced re-translation that breaks on an already-translated chapter: the last
            // attempt did not add a better version, not that nothing usable is on file.
            await database.chapters
                .Where(chapter => chapter.id == chapterId)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(
                        chapter => chapter.translationState,
                        ChapterTranslationState.Failed
                    ),
                    CancellationToken.None
                );

            await notifier.ChapterStateChangedAsync(job.novelId, chapterId, ChapterTranslationState.Failed);
        }

        job.processedCount++;
        job.currentStep = null;
        job.stepIndex = null;
        job.stepCount = null;
        await database.SaveChangesAsync(CancellationToken.None);
        await Publish(notifier, job);
    }


    // LearnVoice never claims a chapter from the queue - ResolveScopeAsync returns nothing for this
    // mode - so it has no per-chapter loop to share with the other two. Two calls over the same
    // range, one unit of progress, reported through the same job-state event the strip already
    // listens to.
    //
    // The voice pass reads every chapter's translation on its own; the glossary pass only has
    // something to learn from the chapters of that range that also carry their original, which is
    // usually a smaller set - the message says so rather than implying the two counts must match.
    private async Task RunLearnVoiceAsync(
        NektoDbContext database,
        IVoiceLearner voiceLearner,
        IGlossaryLearner glossaryLearner,
        ITranslationNotifier notifier,
        TranslationJob job,
        CancellationToken cancellationToken
    ) {
        job.state = JobState.Running;
        job.startedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await Publish(notifier, job);

        try {
            string? language = await database.novels
                .Where(novel => novel.id == job.novelId)
                .Select(novel => novel.targetLanguage)
                .FirstOrDefaultAsync(cancellationToken);

            if (language is null) {
                throw new InvalidOperationException($"Novel {job.novelId} was not found.");
            }

            if (job.fromIndex is null || job.toIndex is null) {
                throw new InvalidOperationException(
                    "A voice-learning run needs an explicit chapter range - fromIndex and toIndex - "
                    + "to know which chapters already carry the human translation it is meant to "
                    + "learn from."
                );
            }

            // How many steps the voice phase reported, read back off the job the moment it finishes -
            // the glossary phase that follows offsets its own step numbers by this, so the two phases
            // fill one continuous "N of M steps" instead of each restarting from one.
            int voiceSteps = 0;

            IProgress<RunStep> voiceProgress = new StepReporter(async step => {
                voiceSteps = step.count;
                ApplyStep(job, 0, null, step);
                await database.SaveChangesAsync(CancellationToken.None);
                await Publish(notifier, job);
            });

            LearnedVoice learnedVoice = await voiceLearner.LearnAsync(
                job.novelId,
                language,
                job.fromIndex.Value,
                job.toIndex.Value,
                voiceProgress,
                cancellationToken
            );

            IProgress<RunStep> glossaryProgress = new StepReporter(async step => {
                ApplyStep(job, voiceSteps, null, step);
                await database.SaveChangesAsync(CancellationToken.None);
                await Publish(notifier, job);
            });

            LearnedGlossary learnedGlossary = await glossaryLearner.LearnAsync(
                job.novelId,
                language,
                job.fromIndex.Value,
                job.toIndex.Value,
                glossaryProgress,
                cancellationToken
            );

            await notifier.AgentMessageAsync(
                job.novelId,
                BuildLearnedMessage(language, job.fromIndex.Value, job.toIndex.Value, learnedVoice, learnedGlossary)
            );

            await Finish(database, notifier, job, JobState.Completed, null, CancellationToken.None);
        } catch (OperationCanceledException) {
            await Finish(database, notifier, job, JobState.Cancelled, null, CancellationToken.None);
        } catch (Exception failure) {
            logger.LogError(failure, "Voice-learning job {JobId} failed", job.id);
            await Finish(database, notifier, job, JobState.Failed, failure.Message, CancellationToken.None);
        }
    }


    // job.fromIndex/toIndex are stored in the same zero-based terms as Chapter.index - the same
    // range the client shifted down by one when it sent the run, so a range typed as "1-20" arrives
    // here as 0-19. The reader of this message never saw a zero-based chapter, so the numbers are
    // shifted back before they are printed, the same shift the client applies wherever a job's range
    // is shown.
    //
    // The glossary clause is dropped entirely when nothing qualified for it - a translation-only
    // range with no original anywhere has nothing to say here, and a sentence reporting zero
    // renderings from zero chapters would only read as a second failure.
    public static string BuildLearnedMessage(
        string language,
        int fromIndex,
        int toIndex,
        LearnedVoice learnedVoice,
        LearnedGlossary learnedGlossary
    ) {
        string message = $"Learned the {language} voice from chapters {fromIndex + 1}-{toIndex + 1}: "
            + $"{learnedVoice.termCount} terms noted";

        if (learnedGlossary.chaptersRead == 0) {
            return message + ".";
        }

        string renderings = learnedGlossary.renderingsLearned == 1
            ? "1 rendering"
            : $"{learnedGlossary.renderingsLearned} renderings";

        string chaptersPhrase = learnedGlossary.chaptersRead == 1
            ? "the chapter that has its original"
            : $"the {learnedGlossary.chaptersRead} chapters that have their original";

        return message + $"; {renderings} learned from {chaptersPhrase}.";
    }


    // The 1-based number a reader sees on the chapter list, for the "Chapter N · ..." prefix a
    // reported step is shown under. Read fresh rather than threaded through from the caller: the
    // chapter loop above only ever carries ids, the same ones ResolveScopeAsync handed back.
    private static async Task<int> ChapterNumberAsync(
        NektoDbContext database,
        long chapterId,
        CancellationToken cancellationToken
    ) {
        int index = await database.chapters
            .Where(chapter => chapter.id == chapterId)
            .Select(chapter => chapter.index)
            .FirstAsync(cancellationToken);

        return index + 1;
    }


    // Turns one reported RunStep into the job fields the strip and the jobs table read, for every
    // mode. Translate and Repair have a chapter to hang the step under: the title is prefixed with
    // it and the step's own index/count move the bar inside that chapter, leaving processedCount and
    // totalCount alone - those still count chapters, exactly as they did before this existed.
    // LearnVoice has no chapter to hang a step under - voice and glossary are each one continuous
    // pass over a range - so there it drives processedCount/totalCount directly instead, offset by
    // whatever phase (voice, then glossary) has already claimed, and stepIndex/stepCount stay null.
    //
    // Pure and static - no database, no queue, no job in flight - so the one place an off-by-one
    // would silently show the wrong "N of M" can be tested directly.
    public static void ApplyStep(TranslationJob job, int phaseOffset, int? chapterNumber, RunStep step) {
        if (chapterNumber is not null) {
            job.currentStep = $"Chapter {chapterNumber} · {step.title}";
            job.stepIndex = step.index;
            job.stepCount = step.count;
        } else {
            job.totalCount = phaseOffset + step.count;
            job.processedCount = phaseOffset + step.index;
            job.currentStep = step.title;
        }

        job.costUsd += step.costUsd;
    }


    private static async Task Finish(
        NektoDbContext database,
        ITranslationNotifier notifier,
        TranslationJob job,
        JobState state,
        string? error,
        CancellationToken cancellationToken
    ) {
        job.state = state;
        job.error = error;
        job.finishedAt = DateTimeOffset.UtcNow;
        job.currentStep = null;
        job.stepIndex = null;
        job.stepCount = null;

        await database.SaveChangesAsync(cancellationToken);
        await ReleaseUnfinishedChaptersAsync(database, notifier, job);
        await Publish(notifier, job);
    }


    // Every chapter the run is about to visit is marked Queued the moment the run starts, so the
    // list shows what is coming as well as what is happening. A chapter the run never reaches is
    // released again by ReleaseUnfinishedChaptersAsync when the run stops.
    private static async Task MarkQueuedAsync(
        NektoDbContext database,
        ITranslationNotifier notifier,
        TranslationJob job,
        IReadOnlyList<long> chapterIds
    ) {
        if (chapterIds.Count == 0) {
            return;
        }

        await database.chapters
            .Where(chapter => chapterIds.Contains(chapter.id))
            .ExecuteUpdateAsync(
                update => update.SetProperty(
                    chapter => chapter.translationState,
                    ChapterTranslationState.Queued
                ),
                CancellationToken.None
            );

        foreach (long chapterId in chapterIds) {
            await notifier.ChapterStateChangedAsync(job.novelId, chapterId, ChapterTranslationState.Queued);
        }
    }


    // A run that ends leaves the chapter in flight marked Running and every chapter it never reached
    // marked Queued, and nothing will ever move them: the run is over. The interface would show
    // them as busy forever, and the user would have no way to tell a working chapter from an
    // abandoned one.
    //
    // Released to what is actually on file - Translated where a rendering exists, None where not -
    // rather than blindly to None: a repair or a forced re-translation queues chapters that already
    // have a translation, and a cancelled run must not make the book look untranslated.
    private static async Task ReleaseUnfinishedChaptersAsync(
        NektoDbContext database,
        ITranslationNotifier notifier,
        TranslationJob job
    ) {
        var stranded = await database.chapters
            .Where(chapter => chapter.novelId == job.novelId
                && (chapter.translationState == ChapterTranslationState.Running
                    || chapter.translationState == ChapterTranslationState.Queued))
            .Select(chapter => new {
                chapter.id,
                hasTranslation = database.chapterTranslations.Any(translation =>
                    translation.chapterId == chapter.id && translation.language == chapter.novel!.targetLanguage)
            })
            .ToListAsync(CancellationToken.None);

        if (stranded.Count == 0) {
            return;
        }

        List<long> translated = stranded.Where(chapter => chapter.hasTranslation).Select(chapter => chapter.id).ToList();
        List<long> untranslated = stranded.Where(chapter => !chapter.hasTranslation).Select(chapter => chapter.id).ToList();

        await database.chapters
            .Where(chapter => translated.Contains(chapter.id))
            .ExecuteUpdateAsync(
                update => update.SetProperty(chapter => chapter.translationState, ChapterTranslationState.Translated),
                CancellationToken.None
            );

        await database.chapters
            .Where(chapter => untranslated.Contains(chapter.id))
            .ExecuteUpdateAsync(
                update => update.SetProperty(chapter => chapter.translationState, ChapterTranslationState.None),
                CancellationToken.None
            );

        foreach (long chapterId in translated) {
            await notifier.ChapterStateChangedAsync(job.novelId, chapterId, ChapterTranslationState.Translated);
        }

        foreach (long chapterId in untranslated) {
            await notifier.ChapterStateChangedAsync(job.novelId, chapterId, ChapterTranslationState.None);
        }
    }


    private static Task Publish(ITranslationNotifier notifier, TranslationJob job) {
        return notifier.JobStateChangedAsync(
            job.novelId,
            job.id,
            job.state.ToString(),
            job.processedCount,
            job.totalCount,
            job.costUsd,
            job.currentStep,
            job.stepIndex,
            job.stepCount
        );
    }
}
