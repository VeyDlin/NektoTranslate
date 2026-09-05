using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
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
    ITranslationMapping mapping,
    NektoDbContext database
) : ControllerBase {

    // Enough of a line to recognise a chapter by, and short enough that the whole book fits in one
    // response. SQLite's substr is happy with a length past the end of the string.
    private const int PreviewLength = 160;

    // Both sides of the book, side by side, which is what the alignment screen exists to show.
    //
    // Carries a short preview of each side rather than the text. Alignment is judged by eye - the
    // user reads "does this translation belong to this chapter" - and a glance at the opening line
    // answers that. Sending the prose itself would make a two-thousand-chapter novel cost megabytes
    // to render a list.
    [HttpGet("alignment")]
    public async Task<IReadOnlyList<object>> Alignment(
        long novelId,
        [FromQuery] string language,
        CancellationToken cancellationToken
    ) {
        return await database.chapters
            .AsNoTracking()
            .Where(chapter => chapter.novelId == novelId)
            .OrderBy(chapter => chapter.index)
            .Select(chapter => new {
                chapterId = chapter.id,
                chapter.index,
                chapter.title,
                chapter.translationState,
                // Null for a chapter that has no original, which the alignment screen shows as such.
                // That is a state worth seeing rather than an empty cell: it is the whole shape of a
                // book that arrived as a translation alone.
                source = chapter.sourcePlainText == null
                    ? null
                    : chapter.sourcePlainText.Substring(0, PreviewLength),

                // The newest version, because that is the one the reader sees. The count tells the
                // interface that earlier ones exist without sending them.
                translation = chapter.translations
                    .Where(translation => translation.language == language)
                    .OrderByDescending(translation => translation.createdAt)
                    .ThenByDescending(translation => translation.id)
                    .Select(translation => new {
                        translation.id,
                        translation.origin,
                        text = translation.plainText.Substring(0, PreviewLength)
                    })
                    .FirstOrDefault(),

                versions = chapter.translations.Count(translation => translation.language == language)
            })
            .ToListAsync<object>(cancellationToken);
    }


    [HttpPost("import")]
    public async Task<TranslationImportResult> Import(
        long novelId,
        [FromBody] ImportTranslationRequest request,
        CancellationToken cancellationToken
    ) {
        // A pasted batch has no site behind it to have changed since, so - the same as a pasted
        // original - there is nothing for it to replace.
        return await importer.ImportAsync(
            novelId,
            request.language,
            request.translations,
            request.createMissingChapters,
            replaceExisting: false,
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
