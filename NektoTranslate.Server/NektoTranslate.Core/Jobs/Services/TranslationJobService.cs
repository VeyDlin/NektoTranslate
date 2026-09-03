using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;


namespace NektoTranslate.Jobs.Services;


public interface ITranslationJobService {

    Task<TranslationJob> EnqueueAsync(
        long novelId,
        JobScopeKind scopeKind,
        int? fromIndex,
        int? toIndex,
        IReadOnlyList<long> chapterIds,
        double? budgetUsd,
        bool force,
        CancellationToken cancellationToken = default
    );


    Task<IReadOnlyList<long>> ResolveScopeAsync(
        TranslationJob job,
        CancellationToken cancellationToken = default
    );
}


public class TranslationJobService(NektoDbContext database, TranslationJobQueue queue) : ITranslationJobService {

    public async Task<TranslationJob> EnqueueAsync(
        long novelId,
        JobScopeKind scopeKind,
        int? fromIndex,
        int? toIndex,
        IReadOnlyList<long> chapterIds,
        double? budgetUsd,
        bool force,
        CancellationToken cancellationToken = default
    ) {
        TranslationJob job = new TranslationJob {
            novelId = novelId,
            scopeKind = scopeKind,
            fromIndex = fromIndex,
            toIndex = toIndex,
            chapterIds = chapterIds.ToList(),
            budgetUsd = budgetUsd,
            force = force,
            state = JobState.Queued
        };

        database.translationJobs.Add(job);
        await database.SaveChangesAsync(cancellationToken);

        job.totalCount = (await ResolveScopeAsync(job, cancellationToken)).Count;
        await database.SaveChangesAsync(cancellationToken);

        await queue.EnqueueAsync(job.id, cancellationToken);

        return job;
    }


    // Chapters always come back in reading order. Chapter N+1 must be translated with the glossary
    // as it stood after chapter N; running them out of order, or in parallel, is exactly how two
    // spellings of one name end up in the same book.
    public async Task<IReadOnlyList<long>> ResolveScopeAsync(
        TranslationJob job,
        CancellationToken cancellationToken = default
    ) {
        IQueryable<Chapter> chapters = database.chapters
            .AsNoTracking()
            .Where(chapter => chapter.novelId == job.novelId);

        chapters = job.scopeKind switch {
            JobScopeKind.Range => chapters.Where(chapter =>
                chapter.index >= (job.fromIndex ?? int.MinValue)
                && chapter.index <= (job.toIndex ?? int.MaxValue)),
            JobScopeKind.Single or JobScopeKind.Selection => chapters.Where(chapter =>
                job.chapterIds.Contains(chapter.id)),
            _ => chapters
        };

        if (!job.force) {
            chapters = chapters.Where(chapter =>
                chapter.translationState != ChapterTranslationState.Translated);
        }

        return await chapters
            .OrderBy(chapter => chapter.index)
            .Select(chapter => chapter.id)
            .ToListAsync(cancellationToken);
    }
}
