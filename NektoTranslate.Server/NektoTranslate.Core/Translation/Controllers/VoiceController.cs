using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Entities;


namespace NektoTranslate.Translation.Controllers;


// Read access to what a voice-learning pass produced. Learning itself runs through the job pipeline
// the same way a translation does - LearnVoice is a TranslationJobMode, started and tracked like any
// other run - so there is no POST here to kick one off, only its results to look at afterwards, and
// the one correction a person might want to make by hand: dropping a term that should never have
// been kept.
[ApiController]
[Route("api/novels/{novelId:long}")]
public class VoiceController(NektoDbContext database) : ControllerBase {

    // Null when nothing has been learned for this language yet, which is a perfectly normal state
    // for a novel that has never had a human translation to learn from - not an error, so this
    // answers with a plain empty body rather than a 404.
    [HttpGet("voice")]
    public async Task<VoiceProfile?> Voice(
        long novelId,
        [FromQuery] string language,
        CancellationToken cancellationToken
    ) {
        return await database.voiceProfiles
            .AsNoTracking()
            .Where(profile => profile.novelId == novelId && profile.language == language)
            .OrderByDescending(profile => profile.createdAt)
            .FirstOrDefaultAsync(cancellationToken);
    }


    // Most frequent first - the names a reader would actually recognise the book by, ahead of
    // something a single chunk happened to mention once.
    [HttpGet("terms")]
    public async Task<IReadOnlyList<TranslationTerm>> Terms(
        long novelId,
        [FromQuery] string language,
        CancellationToken cancellationToken
    ) {
        return await database.translationTerms
            .AsNoTracking()
            .Where(term => term.novelId == novelId && term.language == language)
            .OrderByDescending(term => term.occurrences)
            .ThenBy(term => term.term, StringComparer.Ordinal)
            .ToListAsync(cancellationToken);
    }


    [HttpDelete("terms/{termId:long}")]
    public async Task<ActionResult> DeleteTerm(long novelId, long termId, CancellationToken cancellationToken) {
        int removed = await database.translationTerms
            .Where(term => term.novelId == novelId && term.id == termId)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0 ? NotFound() : NoContent();
    }
}
