using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;
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

            int released = await database.chapters
                .Where(chapter => chapter.translationState == ChapterTranslationState.Running)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(
                        chapter => chapter.translationState,
                        ChapterTranslationState.None
                    ),
                    stoppingToken
                );

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

        IReadOnlyList<long> chapterIds = await jobs.ResolveScopeAsync(job, stoppingToken);

        job.state = JobState.Running;
        job.startedAt = DateTimeOffset.UtcNow;
        job.totalCount = chapterIds.Count;
        await database.SaveChangesAsync(stoppingToken);
        await Publish(notifier, job);

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

                await TranslateOneAsync(database, translator, notifier, job, chapterId, linked.Token);
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

        // Marks where this attempt begins, so the batches it pays for can be identified even if it
        // never returns.
        DateTimeOffset attemptStarted = DateTimeOffset.UtcNow;

        try {
            ChapterTranslationSummary summary = await translator.TranslateAsync(chapterId, cancellationToken);

            job.costUsd += summary.costUsd;
            await notifier.ChapterTranslatedAsync(job.novelId, chapterId);
        } catch (OperationCanceledException) {
            // A cancelled chapter has usually already paid for some of its batches. Charging them
            // to the job before rethrowing is what keeps the reported spend honest - and the budget
            // ceiling reads the same field, so without this a series of cancelled runs could spend
            // past a limit that never noticed.
            job.costUsd += await SpentSinceAsync(database, chapterId, attemptStarted);
            await database.SaveChangesAsync(CancellationToken.None);

            throw;
        } catch (Exception failure) {
            logger.LogError(failure, "Chapter {ChapterId} failed to translate", chapterId);

            job.costUsd += await SpentSinceAsync(database, chapterId, attemptStarted);

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
        await database.SaveChangesAsync(CancellationToken.None);
        await Publish(notifier, job);
    }


    // What this attempt actually paid for, read from the batches it committed.
    //
    // Cost is recorded per batch as each one comes back, so it survives an attempt that never
    // finished. Summing the batches is the only way to know what a cancelled chapter cost, because
    // the chapter itself produced no result to report.
    private static async Task<double> SpentSinceAsync(
        NektoDbContext database,
        long chapterId,
        DateTimeOffset since
    ) {
        return await database.chapterChunks
            .Where(chunk => chunk.chapterId == chapterId && chunk.createdAt >= since)
            .SumAsync(chunk => chunk.costUsd, CancellationToken.None);
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

        await database.SaveChangesAsync(cancellationToken);
        await ReleaseRunningChaptersAsync(database, notifier, job);
        await Publish(notifier, job);
    }


    // A run that ends while a chapter is in flight leaves that chapter marked Running, and nothing
    // will ever move it: the run is over. The interface would show it as in progress forever, and
    // the user would have no way to tell a working chapter from an abandoned one.
    //
    // Releasing it back to None also makes it eligible for the next run, which is what the user
    // means when they restart after cancelling.
    private static async Task ReleaseRunningChaptersAsync(
        NektoDbContext database,
        ITranslationNotifier notifier,
        TranslationJob job
    ) {
        List<long> stranded = await database.chapters
            .Where(chapter => chapter.novelId == job.novelId
                && chapter.translationState == ChapterTranslationState.Running)
            .Select(chapter => chapter.id)
            .ToListAsync(CancellationToken.None);

        if (stranded.Count == 0) {
            return;
        }

        await database.chapters
            .Where(chapter => stranded.Contains(chapter.id))
            .ExecuteUpdateAsync(
                update => update.SetProperty(
                    chapter => chapter.translationState,
                    ChapterTranslationState.None
                ),
                CancellationToken.None
            );

        foreach (long chapterId in stranded) {
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
            job.costUsd
        );
    }
}
