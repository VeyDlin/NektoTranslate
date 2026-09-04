using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Contracts;
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
    public async Task<VoiceProfileView?> Voice(
        long novelId,
        [FromQuery] string language,
        CancellationToken cancellationToken
    ) {
        return await database.voiceProfiles
            .AsNoTracking()
            .Where(profile => profile.novelId == novelId && profile.language == language)
            .OrderByDescending(profile => profile.createdAt)
            .Select(profile => new VoiceProfileView(
                profile.id,
                profile.summary,
                profile.fromChapterIndex,
                profile.toChapterIndex,
                profile.model,
                profile.costUsd,
                profile.createdAt
            ))
            .FirstOrDefaultAsync(cancellationToken);
    }


    // Most frequent first - the names a reader would actually recognise the book by, ahead of
    // something a single chunk happened to mention once.
    [HttpGet("terms")]
    public async Task<IReadOnlyList<TranslationTermView>> Terms(
        long novelId,
        [FromQuery] string language,
        CancellationToken cancellationToken
    ) {
        List<TranslationTerm> rows = await database.translationTerms
            .AsNoTracking()
            .Where(term => term.novelId == novelId && term.language == language)
            .OrderByDescending(term => term.occurrences)
            // No explicit comparer: EF cannot translate one, and asking for StringComparer.Ordinal
            // here threw at runtime rather than at compile time. The tie-break only has to be stable
            // for a reader's eye, so the database's own collation is enough.
            .ThenBy(term => term.term)
            .ToListAsync(cancellationToken);

        return rows
            .Select(term => new TranslationTermView(
                term.id,
                term.term,
                Variants(term.variantsJson),
                term.category,
                term.notes,
                term.occurrences,
                term.firstSeenChapterId
            ))
            .ToList();
    }


    [HttpDelete("terms/{termId:long}")]
    public async Task<ActionResult> DeleteTerm(long novelId, long termId, CancellationToken cancellationToken) {
        int removed = await database.translationTerms
            .Where(term => term.novelId == novelId && term.id == termId)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0 ? NotFound() : NoContent();
    }


    // Stored as JSON because a column cannot hold a list. A row written by an older pass, or edited
    // by hand, is not worth failing a whole screen over: the term itself is the useful part and the
    // variants are an extra, so unreadable JSON yields none rather than an error.
    private static IReadOnlyList<string> Variants(string variantsJson) {
        if (string.IsNullOrWhiteSpace(variantsJson)) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<List<string>>(variantsJson) ?? [];
        }
        catch (JsonException) {
            return [];
        }
    }
}
