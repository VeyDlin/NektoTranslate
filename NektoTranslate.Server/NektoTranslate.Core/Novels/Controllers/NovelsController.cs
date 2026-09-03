using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
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


    [HttpGet]
    public async Task<IReadOnlyList<Novel>> List(CancellationToken cancellationToken) {
        return await database.novels
            .AsNoTracking()
            .OrderByDescending(novel => novel.createdAt)
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
