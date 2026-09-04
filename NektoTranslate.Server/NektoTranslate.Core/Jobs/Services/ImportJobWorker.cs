using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Services;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Jobs.Services;


// Runs queued imports, one chapter at a time, one job at a time.
//
// Each chapter is fetched, converted and committed on its own, so a cancel at the thirtieth keeps
// twenty-nine and a crash loses at most the one in flight. Sequential because the site sees every
// fetch: the scheduler already spaces requests per host, and two imports racing for the same site
// would only queue behind each other there anyway.
//
// A run interrupted by a restart is put back on the queue at startup and continues from the first
// chapter with no outcome. That is what the per-chapter rows are for.
public class ImportJobWorker(
    IServiceScopeFactory scopes,
    ImportJobQueue queue,
    ILogger<ImportJobWorker> logger
) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await RequeueInterruptedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested) {
            long jobId;

            try {
                jobId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) {
                return;
            }

            try {
                await RunAsync(jobId, stoppingToken);
            }
            catch (Exception failure) {
                logger.LogError(failure, "Import job {JobId} failed outside the chapter loop", jobId);
            }
        }
    }


    private async Task RequeueInterruptedAsync(CancellationToken stoppingToken) {
        try {
            using IServiceScope scope = scopes.CreateScope();
            NektoDbContext database = scope.ServiceProvider.GetRequiredService<NektoDbContext>();

            List<long> interrupted = await database.importJobs
                .Where(job => job.state == JobState.Running || job.state == JobState.Queued)
                .OrderBy(job => job.createdAt)
                .Select(job => job.id)
                .ToListAsync(stoppingToken);

            foreach (long jobId in interrupted) {
                await queue.EnqueueAsync(jobId, stoppingToken);
            }

            if (interrupted.Count > 0) {
                logger.LogInformation("Requeued {Count} imports left unfinished by a previous process", interrupted.Count);
            }
        }
        catch (Exception failure) {
            logger.LogWarning(failure, "Could not requeue interrupted imports at startup");
        }
    }


    private async Task RunAsync(long jobId, CancellationToken stoppingToken) {
        using IServiceScope scope = scopes.CreateScope();

        NektoDbContext database = scope.ServiceProvider.GetRequiredService<NektoDbContext>();
        ISiteParser parser = scope.ServiceProvider.GetRequiredService<ISiteParser>();
        IChapterImportService originals = scope.ServiceProvider.GetRequiredService<IChapterImportService>();
        ITranslationImportService translations = scope.ServiceProvider.GetRequiredService<ITranslationImportService>();
        ITranslationNotifier notifier = scope.ServiceProvider.GetRequiredService<ITranslationNotifier>();

        ImportJob? job = await database.importJobs
            .Include(candidate => candidate.items)
            .FirstOrDefaultAsync(candidate => candidate.id == jobId, stoppingToken);

        // Queued is the normal case; Running is a run a previous process was interrupted in.
        if (job is null || job.state is not (JobState.Queued or JobState.Running)) {
            return;
        }

        ImportRunHandle handle = queue.Register(jobId);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            handle.cancellationToken
        );

        job.state = JobState.Running;
        job.startedAt ??= DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(stoppingToken);
        await Publish(notifier, job);

        try {
            foreach (ImportJobItem item in job.items.OrderBy(item => item.position)) {
                if (item.state != ImportItemState.Pending) {
                    continue;
                }

                if (linked.Token.IsCancellationRequested) {
                    await CancelRemainingAsync(database, notifier, job);
                    await Finish(database, notifier, job, JobState.Cancelled, null);

                    return;
                }

                if (handle.isPauseRequested) {
                    job.state = JobState.Paused;
                    job.currentTitle = null;
                    await database.SaveChangesAsync(stoppingToken);
                    await Publish(notifier, job);

                    await handle.WaitWhilePausedAsync(stoppingToken);

                    if (linked.Token.IsCancellationRequested || stoppingToken.IsCancellationRequested) {
                        await CancelRemainingAsync(database, notifier, job);
                        await Finish(database, notifier, job, JobState.Cancelled, null);

                        return;
                    }

                    job.state = JobState.Running;
                    await database.SaveChangesAsync(stoppingToken);
                    await Publish(notifier, job);
                }

                job.currentTitle = item.title;
                await database.SaveChangesAsync(stoppingToken);
                await Publish(notifier, job);

                await ImportOneAsync(database, parser, originals, translations, notifier, job, item, linked.Token);
            }

            await Finish(database, notifier, job, JobState.Completed, null);
        }
        catch (OperationCanceledException) {
            await CancelRemainingAsync(database, notifier, job);
            await Finish(database, notifier, job, JobState.Cancelled, null);
        }
        catch (Exception failure) {
            logger.LogError(failure, "Import job {JobId} failed", jobId);
            await Finish(database, notifier, job, JobState.Failed, failure.Message);
        }
        finally {
            queue.Release(jobId);
        }
    }


    // One chapter, one commit. A failure is recorded on the item and the run moves on - one page a
    // site would not serve must not stop the other forty.
    private async Task ImportOneAsync(
        NektoDbContext database,
        ISiteParser parser,
        IChapterImportService originals,
        ITranslationImportService translations,
        ITranslationNotifier notifier,
        ImportJob job,
        ImportJobItem item,
        CancellationToken cancellationToken
    ) {
        try {
            ParsedChapter chapter = await parser.GetChapterAsync(item.sourceUrl, cancellationToken);
            string title = string.IsNullOrWhiteSpace(item.title) ? chapter.title : item.title;

            if (job.kind == ImportKind.Originals) {
                IReadOnlyList<Chapter> created = await originals.ImportAsync(
                    job.novelId,
                    [new ImportedChapter(title, chapter.html, null, item.sourceUrl)],
                    cancellationToken
                );

                item.chapterId = created[0].id;
                item.chapterIndex = created[0].index;
                item.state = ImportItemState.Imported;
            }
            else {
                int chapterIndex = job.startAtChapterIndex + item.position;

                TranslationImportResult result = await translations.ImportAsync(
                    job.novelId,
                    job.language ?? string.Empty,
                    [new ImportedTranslation(chapterIndex, chapter.html, item.sourceUrl)],
                    cancellationToken
                );

                item.chapterIndex = chapterIndex;

                if (result.imported == 1) {
                    item.state = ImportItemState.Imported;
                }
                else {
                    Status reason = result.rejected.Count > 0 ? result.rejected[0].reason : Statuses.ImportedTextEmpty;
                    bool skipped = reason.code == Statuses.TranslationAlreadyExists.code;

                    item.state = skipped ? ImportItemState.Skipped : ImportItemState.Failed;
                    Record(item, reason);
                }
            }
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception failure) {
            logger.LogWarning(failure, "Could not import {Url}", item.sourceUrl);
            item.state = ImportItemState.Failed;
            Record(item, Describe(item.sourceUrl, failure));
        }

        item.finishedAt = DateTimeOffset.UtcNow;
        job.processedCount++;
        job.currentTitle = null;
        await database.SaveChangesAsync(CancellationToken.None);

        await notifier.ImportItemFinishedAsync(job.novelId, job.id, ImportJobItemView.Of(item));
        await Publish(notifier, job);
    }


    // The runner speaks in exceptions whose messages were written for a log. What the user needs is
    // which of the few things that go wrong went wrong, without a JavaScript stack after it.
    private static Status Describe(string url, Exception failure) {
        string message = failure.Message.ReplaceLineEndings("\n").Split('\n')[0].Trim();

        if (message.StartsWith("Error: ", StringComparison.Ordinal)) {
            message = message["Error: ".Length..];
        }

        if (message.Contains("found no content", StringComparison.OrdinalIgnoreCase)) {
            return Statuses.ParserFoundNoContent.With(("url", url));
        }

        if (message.Contains("No parser is registered", StringComparison.OrdinalIgnoreCase)) {
            return Statuses.NoParserForSite.With(("url", url));
        }

        return Statuses.ChapterFetchFailed.With(("url", url), ("reason", message.Length > 200 ? message[..200] : message));
    }


    private static void Record(ImportJobItem item, Status status) {
        item.statusCode = status.code;
        item.statusText = status.text;
        item.statusArgsJson = IssueStatus.ArgsOf(status);
    }


    private static async Task CancelRemainingAsync(NektoDbContext database, ITranslationNotifier notifier, ImportJob job) {
        foreach (ImportJobItem item in job.items.Where(item => item.state == ImportItemState.Pending)) {
            item.state = ImportItemState.Cancelled;
            item.finishedAt = DateTimeOffset.UtcNow;
            Record(item, Statuses.ImportCancelled);

            await notifier.ImportItemFinishedAsync(job.novelId, job.id, ImportJobItemView.Of(item));
        }

        await database.SaveChangesAsync(CancellationToken.None);
    }


    private static async Task Finish(
        NektoDbContext database,
        ITranslationNotifier notifier,
        ImportJob job,
        JobState state,
        string? error
    ) {
        job.state = state;
        job.error = error;
        job.currentTitle = null;
        job.finishedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(CancellationToken.None);
        await Publish(notifier, job);
    }


    private static Task Publish(ITranslationNotifier notifier, ImportJob job) {
        return notifier.ImportStateChangedAsync(
            job.novelId,
            job.id,
            job.kind,
            job.state,
            job.processedCount,
            job.totalCount,
            job.currentTitle
        );
    }
}
