using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Entities;
using NektoTranslate.Parsing.Services;


namespace NektoTranslate.Parsing.Controllers;


// The contents of a site, as read for one book and kept until read again.
//
// Separate from ParsingController, which answers questions about an address without committing to
// anything and holds nothing. This one owns the expensive answer and its state.
[ApiController]
[Route("api/novels/{novelId:long}/listings")]
public class ListingsController(NektoDbContext database, IListingReader reader) : ControllerBase {

    // Nothing rather than a 404 when this book has never had its contents read for that kind: an
    // empty workbench is the ordinary starting state, not a missing resource.
    [HttpGet("{kind}")]
    public async Task<SourceListingView?> Get(
        long novelId,
        ImportKind kind,
        CancellationToken cancellationToken
    ) {
        SourceListing? listing = await database.sourceListings
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.novelId == novelId && candidate.kind == kind, cancellationToken);

        return listing is null ? null : SourceListingView.Of(listing);
    }


    // Answers as soon as the read is recorded, not when the site has been visited. The screen
    // follows it from here through the hub, which is what lets the user leave the page.
    [HttpPost("{kind}/read")]
    public async Task<ActionResult<SourceListingView>> Read(
        long novelId,
        ImportKind kind,
        [FromBody] ReadListingRequest request,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(request.url)) {
            return BadRequest();
        }

        SourceListingView? started = await reader.StartAsync(novelId, kind, request.url.Trim(), cancellationToken);

        if (started is null) {
            return Conflict(new { status = Statuses.ListingAlreadyReading });
        }

        return Accepted(started);
    }


    [HttpPost("{kind}/cancel")]
    public ActionResult Cancel(long novelId, ImportKind kind) {
        return reader.Cancel(novelId, kind) ? Accepted() : NotFound();
    }
}
