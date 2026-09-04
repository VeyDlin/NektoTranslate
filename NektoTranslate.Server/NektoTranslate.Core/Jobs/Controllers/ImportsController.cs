using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Jobs.Services;


namespace NektoTranslate.Jobs.Controllers;


[ApiController]
[Route("api/novels/{novelId:long}/imports")]
public class ImportsController(NektoDbContext database, IImportJobService imports) : ControllerBase {

    [HttpPost]
    public async Task<ActionResult<ImportJobView>> Start(
        long novelId,
        [FromBody] StartImportJobRequest request,
        CancellationToken cancellationToken
    ) {
        if (request.chapters.Count == 0) {
            return BadRequest();
        }

        if (request.kind == ImportKind.Translation && string.IsNullOrWhiteSpace(request.language)) {
            return BadRequest();
        }

        ImportJob job = await imports.EnqueueAsync(novelId, request, cancellationToken);

        return ImportJobView.Of(job, withItems: true);
    }


    // Newest first, without rows: the history screen wants the runs, not forty chapters each.
    [HttpGet]
    public async Task<IReadOnlyList<ImportJobView>> List(long novelId, CancellationToken cancellationToken) {
        List<ImportJob> jobs = await database.importJobs
            .AsNoTracking()
            .Where(job => job.novelId == novelId)
            .OrderByDescending(job => job.createdAt)
            .ToListAsync(cancellationToken);

        return jobs.Select(job => ImportJobView.Of(job, withItems: false)).ToList();
    }


    [HttpGet("{jobId:long}")]
    public async Task<ActionResult<ImportJobView>> Get(long novelId, long jobId, CancellationToken cancellationToken) {
        ImportJob? job = await database.importJobs
            .AsNoTracking()
            .Include(candidate => candidate.items)
            .FirstOrDefaultAsync(candidate => candidate.id == jobId && candidate.novelId == novelId, cancellationToken);

        return job is null ? NotFound() : ImportJobView.Of(job, withItems: true);
    }


    // All three take effect between chapters. The chapter being fetched finishes and is kept.
    [HttpPost("{jobId:long}/pause")]
    public async Task<ActionResult> Pause(long novelId, long jobId, CancellationToken cancellationToken) {
        return await imports.PauseAsync(novelId, jobId, cancellationToken) ? Accepted() : NotFound();
    }


    [HttpPost("{jobId:long}/resume")]
    public async Task<ActionResult> Resume(long novelId, long jobId, CancellationToken cancellationToken) {
        return await imports.ResumeAsync(novelId, jobId, cancellationToken) ? Accepted() : NotFound();
    }


    [HttpPost("{jobId:long}/cancel")]
    public async Task<ActionResult> Cancel(long novelId, long jobId, CancellationToken cancellationToken) {
        return await imports.CancelAsync(novelId, jobId, cancellationToken) ? Accepted() : NotFound();
    }


    // The one entry the report screens actually need again: a chapter the run could not fetch, gone
    // back to the site for a second try without disturbing the thirty-nine that already landed.
    [HttpPost("{jobId:long}/items/{position:int}/retry")]
    public async Task<ActionResult<ImportJobItemView>> RetryItem(
        long novelId,
        long jobId,
        int position,
        CancellationToken cancellationToken
    ) {
        ImportItemRetryOutcome outcome = await imports.RetryItemAsync(novelId, jobId, position, cancellationToken);

        return outcome.result switch {
            ImportItemRetryResult.Retried => Ok(outcome.item),

            ImportItemRetryResult.JobNotFound or ImportItemRetryResult.ItemNotFound => NotFound(),

            // 409 rather than 400: the request itself is fine, the moment is wrong, and the caller's
            // remedy is to wait rather than to change what it sent.
            ImportItemRetryResult.JobStillActive => Conflict(new { status = Statuses.ImportJobStillActive }),

            ImportItemRetryResult.ItemNotFailed => Conflict(new { status = Statuses.ImportItemNotFailed }),

            _ => StatusCode(500)
        };
    }
}
