using Mediator;
using Microsoft.AspNetCore.Mvc;
using NektoTranslate.Translation.Commands;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Controllers;


[ApiController]
[Route("api/translation")]
public class TranslationController(IMediator mediator) : ControllerBase {

    [HttpPost("text")]
    public async Task<TranslationResult> TranslateText(
        [FromBody] TranslateTextRequest request,
        CancellationToken cancellationToken
    ) {
        return await mediator.Send(new TranslateTextCommand(request), cancellationToken);
    }
}
