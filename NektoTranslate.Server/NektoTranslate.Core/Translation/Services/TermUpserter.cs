using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Entities;


namespace NektoTranslate.Translation.Services;


// Writes one MergedTerm into TranslationTerms, growing what is already known rather than replacing
// it. Originally VoiceLearner's own private method; pulled out so a repair's decisions step - which
// reads new names off a just-repaired chapter the same way voice learning reads them off a sample -
// can upsert through the same merge instead of copying it.
public interface ITermUpserter {

    Task UpsertAsync(
        long novelId,
        string language,
        MergedTerm term,
        int occurrences,
        CancellationToken cancellationToken = default
    );
}


public class TermUpserter(NektoDbContext database) : ITermUpserter {

    // Re-running over the same or an overlapping range should grow what is already known rather
    // than replace it: a second pass reads a different slice of text and may catch a spelling or a
    // mention the first pass missed, but it must never forget what the first pass already settled.
    // firstSeenChapterId and category are written once, at creation, and left alone after - they
    // describe how the term was first met, and a later pass rereading the same book does not change
    // that history.
    public async Task UpsertAsync(
        long novelId,
        string language,
        MergedTerm term,
        int occurrences,
        CancellationToken cancellationToken = default
    ) {
        TranslationTerm? existing = await database.translationTerms.FirstOrDefaultAsync(
            row => row.novelId == novelId && row.language == language && row.term == term.term,
            cancellationToken
        );

        if (existing is null) {
            database.translationTerms.Add(new TranslationTerm {
                novelId = novelId,
                language = language,
                term = term.term,
                variantsJson = JsonSerializer.Serialize(term.variants),
                category = term.category,
                notes = term.notes,
                occurrences = occurrences,
                firstSeenChapterId = term.firstSeenChapterId
            });

            return;
        }

        HashSet<string> variants = VoicePrompt.DecodeVariants(existing.variantsJson);
        variants.UnionWith(term.variants);

        existing.variantsJson = JsonSerializer.Serialize(variants);
        existing.occurrences += occurrences;
        existing.notes ??= term.notes;
    }
}
