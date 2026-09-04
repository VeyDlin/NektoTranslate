using Microsoft.AspNetCore.Mvc;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Translation.Controllers;


[ApiController]
[Route("api/novels/{novelId:long}/chapters/{chapterId:long}/translation")]
public class ChapterTranslationController(ITranslationEditor editor) : ControllerBase {

    // Replaces one block of the latest translation with text the user wrote.
    //
    // Block-level rather than whole-chapter because that is the unit everything else already speaks
    // in: the quality checks report a block index, the reader renders blocks, and replacing only the
    // block that was wrong leaves the rest of the chapter provably untouched.
    [HttpPut("blocks/{blockIndex:int}")]
    public async Task<ActionResult<object>> EditBlock(
        long novelId,
        long chapterId,
        int blockIndex,
        [FromBody] EditTranslationBlockRequest request,
        CancellationToken cancellationToken
    ) {
        TranslationEditOutcome outcome = await editor.EditBlockAsync(
            novelId,
            chapterId,
            blockIndex,
            request,
            cancellationToken
        );

        // 409 for both refusals, because both mean "not now, look again" rather than "you asked
        // wrongly" - and the status code tells the interface which, so it can react to the busy case
        // differently from the stale one without reading the sentence.
        return outcome.result switch {
            TranslationEditResult.Applied => Ok(new {
                translationId = outcome.translationId,
                resolvedIssues = outcome.resolvedIssues
            }),

            TranslationEditResult.ChapterNotFound => NotFound(),

            TranslationEditResult.NoTranslation => NotFound(new {
                status = Statuses.NoTranslationInLanguage.With(("language", request.language))
            }),

            TranslationEditResult.Busy => Conflict(new {
                status = Statuses.ChapterBusy
            }),

            TranslationEditResult.Stale => Conflict(new {
                status = Statuses.TranslationStale
            }),

            TranslationEditResult.BlockOutOfRange => BadRequest(new {
                status = Statuses.BlockOutOfRange
            }),

            _ => StatusCode(500)
        };
    }
}
