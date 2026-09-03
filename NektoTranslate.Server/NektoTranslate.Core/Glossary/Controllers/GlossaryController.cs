using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Glossary.Commands;
using NektoTranslate.Glossary.Contracts;
using NektoTranslate.Glossary.Entities;


namespace NektoTranslate.Glossary.Controllers;


[ApiController]
[Route("api/novels/{novelId:long}/glossary")]
public class GlossaryController(IMediator mediator, NektoDbContext database) : ControllerBase {

    // Longest source term first, so a short name cannot bury the longer one that contains it.
    [HttpGet]
    public async Task<IReadOnlyList<GlossaryEntry>> List(
        long novelId,
        [FromQuery] bool onlyNeedsReview,
        CancellationToken cancellationToken
    ) {
        IQueryable<GlossaryEntry> entries = database.glossaryEntries
            .AsNoTracking()
            .Where(entry => entry.novelId == novelId);

        if (onlyNeedsReview) {
            entries = entries.Where(entry => entry.needsReview);
        }

        return await entries
            .OrderByDescending(entry => entry.sourceTerm.Length)
            .ThenBy(entry => entry.sourceTerm)
            .ToListAsync(cancellationToken);
    }


    [HttpPut]
    public async Task<GlossaryEntry> Upsert(
        long novelId,
        [FromBody] UpsertGlossaryEntryRequest request,
        CancellationToken cancellationToken
    ) {
        return await mediator.Send(new UpsertGlossaryEntryCommand(novelId, request), cancellationToken);
    }


    [HttpDelete("{entryId:long}")]
    public async Task<ActionResult> Delete(long novelId, long entryId, CancellationToken cancellationToken) {
        int removed = await database.glossaryEntries
            .Where(entry => entry.novelId == novelId && entry.id == entryId)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0 ? NotFound() : NoContent();
    }


    // Clearing the review flag without changing the rendering: the user read what the model invented
    // and is content with it. Distinct from an edit, and worth keeping distinct - it leaves the
    // origin as AiExtracted, which is still the truth about where the name came from.
    [HttpPost("{entryId:long}/approve")]
    public async Task<ActionResult> Approve(long novelId, long entryId, CancellationToken cancellationToken) {
        int updated = await database.glossaryEntries
            .Where(entry => entry.novelId == novelId && entry.id == entryId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(entry => entry.needsReview, false),
                cancellationToken
            );

        return updated == 0 ? NotFound() : NoContent();
    }
}
