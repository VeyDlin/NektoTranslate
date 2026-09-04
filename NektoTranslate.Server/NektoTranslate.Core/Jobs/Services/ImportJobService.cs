using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;


namespace NektoTranslate.Jobs.Services;


public interface IImportJobService {

    Task<ImportJob> EnqueueAsync(
        long novelId,
        StartImportJobRequest request,
        CancellationToken cancellationToken = default
    );


    Task<bool> PauseAsync(long novelId, long jobId, CancellationToken cancellationToken = default);


    Task<bool> ResumeAsync(long novelId, long jobId, CancellationToken cancellationToken = default);


    Task<bool> CancelAsync(long novelId, long jobId, CancellationToken cancellationToken = default);
}


public class ImportJobService(NektoDbContext database, ImportJobQueue queue) : IImportJobService {

    public async Task<ImportJob> EnqueueAsync(
        long novelId,
        StartImportJobRequest request,
        CancellationToken cancellationToken = default
    ) {
        ImportJob job = new ImportJob {
            novelId = novelId,
            kind = request.kind,
            language = request.kind == ImportKind.Translation ? request.language : null,
            startAtChapterIndex = request.startAtChapterIndex,
            state = JobState.Queued,
            totalCount = request.chapters.Count
        };

        for (int position = 0; position < request.chapters.Count; position++) {
            job.items.Add(new ImportJobItem {
                position = position,
                sourceUrl = request.chapters[position].sourceUrl,
                title = request.chapters[position].title
            });
        }

        database.importJobs.Add(job);
        await database.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(job.id, cancellationToken);

        return job;
    }


    public async Task<bool> PauseAsync(long novelId, long jobId, CancellationToken cancellationToken = default) {
        ImportJob? job = await FindAsync(novelId, jobId, cancellationToken);

        if (job is null || job.state != JobState.Running) {
            return false;
        }

        return queue.Pause(jobId);
    }


    // A paused run that is still in memory is simply woken. One that is not - the process was
    // restarted while it was paused - is put back on the queue, and the worker continues from the
    // first chapter that has no outcome yet. Either way nothing already fetched is fetched again.
    public async Task<bool> ResumeAsync(long novelId, long jobId, CancellationToken cancellationToken = default) {
        ImportJob? job = await FindAsync(novelId, jobId, cancellationToken);

        if (job is null || job.state != JobState.Paused) {
            return false;
        }

        if (queue.IsInFlight(jobId)) {
            return queue.Resume(jobId);
        }

        job.state = JobState.Queued;
        await database.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(jobId, cancellationToken);

        return true;
    }


    public async Task<bool> CancelAsync(long novelId, long jobId, CancellationToken cancellationToken = default) {
        ImportJob? job = await FindAsync(novelId, jobId, cancellationToken);

        if (job is null) {
            return false;
        }

        if (queue.IsInFlight(jobId)) {
            return queue.Cancel(jobId);
        }

        // Queued or paused-and-not-in-memory: nothing is running, so the state can be written
        // directly and the worker will pass over it if the id is still on the queue.
        if (job.state is JobState.Queued or JobState.Paused) {
            job.state = JobState.Cancelled;
            job.finishedAt = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);

            return true;
        }

        return false;
    }


    private Task<ImportJob?> FindAsync(long novelId, long jobId, CancellationToken cancellationToken) {
        return database.importJobs.FirstOrDefaultAsync(
            job => job.id == jobId && job.novelId == novelId,
            cancellationToken
        );
    }
}
