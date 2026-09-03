using Mediator;
using Microsoft.AspNetCore.Mvc;
using NektoTranslate.Parsing.Commands;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Services;


namespace NektoTranslate.Parsing.Controllers;


[ApiController]
[Route("api/parsing")]
public class ParsingController(IMediator mediator, ISiteParser parser) : ControllerBase {

    // Answered without visiting the site: which parser claims a URL depends only on its host name,
    // so the user can be told whether a link is readable before anything is downloaded.
    [HttpGet("support")]
    public async Task<object> Support([FromQuery] string url, CancellationToken cancellationToken) {
        string? name = await parser.ParserNameAsync(url, cancellationToken);

        return new { supported = name is not null, parser = name };
    }


    [HttpPost("table-of-contents")]
    public async Task<IReadOnlyList<ParsedChapterLink>> TableOfContents(
        [FromBody] TableOfContentsRequest request,
        CancellationToken cancellationToken
    ) {
        return await parser.GetChapterListAsync(request.url, cancellationToken);
    }


    // Takes the chapters the user chose, not a contents URL. A novel with two thousand entries must
    // never be pulled down wholesale because someone pasted a link.
    [HttpPost("novels/{novelId:long}/import")]
    public async Task<ImportFromUrlResult> Import(
        long novelId,
        [FromBody] IReadOnlyList<ParsedChapterLink> chapters,
        CancellationToken cancellationToken
    ) {
        return await mediator.Send(new ImportFromUrlCommand(novelId, chapters), cancellationToken);
    }
}


public sealed record TableOfContentsRequest(string url);
