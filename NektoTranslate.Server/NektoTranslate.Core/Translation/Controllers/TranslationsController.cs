using Microsoft.AspNetCore.Mvc;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Translation.Controllers;


// Bringing in a translation the book already has, and putting it against the right chapters.
//
// A translation site rarely lines up with the original by position. A translator's note as the first
// entry shifts everything by one; merged or split chapters shift part of it. So importing is only
// half the job - the other half is being able to correct the mapping afterwards in bulk, which is
// what the range operations here are for.
[ApiController]
[Route("api/novels/{novelId:long}/translations")]
public class TranslationsController(
    ITranslationImportService importer,
    ITranslationMapping mapping
) : ControllerBase {

    [HttpPost("import")]
    public async Task<TranslationImportResult> Import(
        long novelId,
        [FromBody] ImportTranslationRequest request,
        CancellationToken cancellationToken
    ) {
        return await importer.ImportAsync(
            novelId,
            request.language,
            request.translations,
            cancellationToken
        );
    }


    // Deletes the translation of a span of chapters, not the chapters. Used for the case this whole
    // feature exists for: an existing translation that is sound to chapter twenty and unusable after
    // it, where the tail has to go so our own engine can take over.
    [HttpDelete]
    public async Task<DeleteTranslationsResult> DeleteRange(
        long novelId,
        [FromQuery] string language,
        [FromQuery] int from,
        [FromQuery] int to,
        CancellationToken cancellationToken
    ) {
        return await mapping.DeleteRangeAsync(novelId, language, from, to, cancellationToken);
    }


    // Shifts a span of translations onto different chapters.
    //
    // Answers 409 with the list of collisions rather than moving what it can. A partly applied shift
    // leaves a mapping the user cannot reason about without checking every chapter by hand, which is
    // a worse outcome than being told to fix the conflict first.
    [HttpPost("move")]
    public async Task<ActionResult<MoveTranslationsResult>> Move(
        long novelId,
        [FromBody] MoveTranslationsRequest request,
        CancellationToken cancellationToken
    ) {
        MoveTranslationsResult result = await mapping.MoveAsync(novelId, request, cancellationToken);

        if (result.collisions.Count > 0) {
            return Conflict(result);
        }

        return Ok(result);
    }
}
