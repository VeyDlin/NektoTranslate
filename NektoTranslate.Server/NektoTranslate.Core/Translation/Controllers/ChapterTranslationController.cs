using Microsoft.AspNetCore.Mvc;
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
        // wrongly" - and the reason is spelled out so the interface can say which.
        return outcome.result switch {
            TranslationEditResult.Applied => Ok(new {
                translationId = outcome.translationId,
                resolvedIssues = outcome.resolvedIssues
            }),

            TranslationEditResult.ChapterNotFound => NotFound(),

            TranslationEditResult.NoTranslation => NotFound(new {
                error = "This chapter has no translation in that language yet."
            }),

            TranslationEditResult.Busy => Conflict(new {
                reason = "busy",
                error = "This chapter is queued or being translated. The run would overwrite the edit."
            }),

            TranslationEditResult.Stale => Conflict(new {
                reason = "stale",
                error = "The chapter has been translated again since this edit was started. "
                    + "Reload it and make the change on the current text."
            }),

            TranslationEditResult.BlockOutOfRange => BadRequest(new {
                error = "That block does not exist in this translation."
            }),

            _ => StatusCode(500)
        };
    }
}
