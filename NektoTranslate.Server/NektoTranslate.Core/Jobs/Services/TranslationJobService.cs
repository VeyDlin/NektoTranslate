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
        TranslationJobMode mode,
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
        TranslationJobMode mode,
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
            mode = mode,
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
    //
    // What counts as in scope depends on the mode, because the three modes read a chapter for
    // different reasons. Translate needs an original and skips what already has a rendering unless
    // forced. Repair needs the opposite - a rendering to rewrite, with or without an original behind
    // it - and re-running it is always deliberate, so force plays no part in its scope. LearnVoice
    // does not scope chapters at all: it reads its sample directly from the range it was given.
    public async Task<IReadOnlyList<long>> ResolveScopeAsync(
        TranslationJob job,
        CancellationToken cancellationToken = default
    ) {
        if (job.mode == TranslationJobMode.LearnVoice) {
            return [];
        }

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

        if (job.mode == TranslationJobMode.Repair) {
            // Repair rewrites whatever rendering a chapter already has - source or no source, that
            // rendering is the only input it needs. A chapter with none is not in scope: there is
            // nothing on file yet for a repair to rewrite.
            string? language = await database.novels
                .Where(novel => novel.id == job.novelId)
                .Select(novel => novel.targetLanguage)
                .FirstOrDefaultAsync(cancellationToken);

            if (language is null) {
                return [];
            }

            chapters = chapters.Where(chapter => database.chapterTranslations.Any(
                translation => translation.chapterId == chapter.id && translation.language == language
            ));

            return await chapters
                .OrderBy(chapter => chapter.index)
                .Select(chapter => chapter.id)
                .ToListAsync(cancellationToken);
        }

        // A chapter with no original cannot be translated by any scope, force included. Scoping one
        // in would spend a slot to fail: repairing those is a different run with a different input.
        chapters = chapters.Where(chapter => chapter.sourceMarkdown != null);

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
