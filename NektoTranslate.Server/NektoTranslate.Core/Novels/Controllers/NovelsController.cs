using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Novels.Commands;
using NektoTranslate.Novels.Contracts;
using NektoTranslate.Novels.Entities;


namespace NektoTranslate.Novels.Controllers;


[ApiController]
[Route("api/novels")]
public class NovelsController(IMediator mediator, NektoDbContext database) : ControllerBase {

    [HttpPost]
    public async Task<Novel> Create(
        [FromBody] CreateNovelRequest request,
        CancellationToken cancellationToken
    ) {
        return await mediator.Send(new CreateNovelCommand(request), cancellationToken);
    }


    // Every row's progress in the one query that lists the library, not one GET .../chapters per
    // book fired to draw a progress bar. `novel.chapters.Count(...)` and the two `Any` checks below
    // are correlated subqueries EF folds into the same SQL statement - nothing here walks the result
    // in a loop, which is the shape that would turn this back into the N+1 it replaces.
    [HttpGet]
    public async Task<IReadOnlyList<NovelListItem>> List(CancellationToken cancellationToken) {
        JobState[] activeStates = [JobState.Queued, JobState.Running, JobState.Paused];

        return await database.novels
            .AsNoTracking()
            .OrderByDescending(novel => novel.createdAt)
            .Select(novel => new NovelListItem(
                novel.id,
                novel.title,
                novel.sourceLanguage,
                novel.targetLanguage,
                novel.sourceUrl,
                novel.styleGuide,
                novel.model,
                novel.normalizeQuotes,
                novel.createdAt,
                novel.chapters.Count,
                novel.chapters.Count(chapter => chapter.translationState == ChapterTranslationState.Translated),
                novel.chapters.Count(chapter => chapter.translationState == ChapterTranslationState.Failed),
                database.translationJobs.Any(job => job.novelId == novel.id && activeStates.Contains(job.state))
                    || database.importJobs.Any(job => job.novelId == novel.id && activeStates.Contains(job.state))
            ))
            .ToListAsync(cancellationToken);
    }


    // Settings a user only knows they want after reading some of the translation - the book's own
    // style prompt above all. Without this they could be chosen once, blind, and never again.
    [HttpPut("{novelId:long}")]
    public async Task<ActionResult<Novel>> Update(
        long novelId,
        [FromBody] UpdateNovelRequest request,
        CancellationToken cancellationToken
    ) {
        Novel? novel = await mediator.Send(new UpdateNovelCommand(novelId, request), cancellationToken);

        return novel is null ? NotFound() : novel;
    }


    // Cascades to chapters, translations, glossary, chat and jobs by the model's delete behaviour.
    // A novel the user imported by mistake - wrong language pair, wrong book - must be removable, or
    // the library fills with rubbish that cannot be cleared.
    [HttpDelete("{novelId:long}")]
    public async Task<ActionResult> Delete(long novelId, CancellationToken cancellationToken) {
        int removed = await database.novels
            .Where(novel => novel.id == novelId)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0 ? NotFound() : NoContent();
    }


    [HttpGet("{novelId:long}")]
    public async Task<ActionResult<Novel>> Get(long novelId, CancellationToken cancellationToken) {
        Novel? novel = await database.novels
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.id == novelId, cancellationToken);

        return novel is null ? NotFound() : novel;
    }
}
