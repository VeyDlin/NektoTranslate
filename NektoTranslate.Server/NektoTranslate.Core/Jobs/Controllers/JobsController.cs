using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Commands;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Services;


namespace NektoTranslate.Jobs.Controllers;


[ApiController]
[Route("api/novels/{novelId:long}/jobs")]
public class JobsController(
    IMediator mediator,
    NektoDbContext database,
    TranslationJobQueue queue
) : ControllerBase {

    [HttpPost]
    public async Task<TranslationJob> Start(
        long novelId,
        [FromBody] StartTranslationJobRequest request,
        CancellationToken cancellationToken
    ) {
        return await mediator.Send(new StartTranslationJobCommand(novelId, request), cancellationToken);
    }


    // Cancellation takes effect between chapters. The chapter in flight is allowed to finish, since
    // abandoning it would throw away what has already been paid for and leave nothing to show.
    [HttpPost("{jobId:long}/cancel")]
    public ActionResult Cancel(long jobId) {
        return queue.Cancel(jobId) ? Accepted() : NotFound();
    }


    [HttpGet]
    public async Task<IReadOnlyList<TranslationJob>> List(long novelId, CancellationToken cancellationToken) {
        return await database.translationJobs
            .AsNoTracking()
            .Where(job => job.novelId == novelId)
            .OrderByDescending(job => job.createdAt)
            .ToListAsync(cancellationToken);
    }
}
