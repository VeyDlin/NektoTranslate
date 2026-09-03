using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Models;
using NektoTranslate.Glossary.Contracts;
using NektoTranslate.Glossary.Entities;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Glossary.Services;


public interface IGlossaryService {

    Task<IReadOnlyList<GlossaryTerm>> SelectForChapterAsync(
        long novelId,
        string language,
        string chapterPlainText,
        CancellationToken cancellationToken = default
    );


    Task<(IReadOnlyList<string> stillUnknown, double costUsd)> ReconcileWithExistingAsync(
        long novelId,
        string language,
        IReadOnlyList<string> candidates,
        string model,
        CancellationToken cancellationToken = default
    );


    Task RecordAsync(
        long novelId,
        string language,
        string sourceTerm,
        string targetTerm,
        GlossaryEntryOrigin origin,
        long? firstSeenChapterId,
        CancellationToken cancellationToken = default
    );
}


public class GlossaryService(
    NektoDbContext database,
    IExistingTranslationResolver resolver,
    EngineOptions options
) : IGlossaryService {

    private readonly int maxInflectionLength = options.glossary.maxInflectionLength;

    // Only the terms this chapter actually uses go into the prompt. Sending the whole glossary
    // would make every request grow with the book, which on a long novel costs more than the
    // translation it is meant to help.
    //
    // Ordered longest source term first. A short name is a substring of a longer one - 田中 sits
    // inside 田中太郎 - so listing the short one first lets it shadow the longer, more specific
    // entry both when matching and in the model's reading of the list.
    public async Task<IReadOnlyList<GlossaryTerm>> SelectForChapterAsync(
        long novelId,
        string language,
        string chapterPlainText,
        CancellationToken cancellationToken = default
    ) {
        List<GlossaryEntry> entries = await database.glossaryEntries
            .AsNoTracking()
            .Where(entry => entry.novelId == novelId && entry.language == language)
            .ToListAsync(cancellationToken);

        return entries
            .Where(entry => Mentions(chapterPlainText, entry))
            .OrderByDescending(entry => entry.sourceTerm.Length)
            .ThenBy(entry => entry.sourceTerm, StringComparer.Ordinal)
            .Select(entry => new GlossaryTerm(entry.sourceTerm, entry.targetTerm, entry.notes))
            .ToList();
    }


    // Gives whatever the book's own translation already established the final say.
    //
    // Runs over terms the glossary has never seen *and* over terms the model invented on an earlier
    // chapter: an invented rendering is provisional, and the moment the existing translation turns
    // out to have an answer, that answer replaces it. Terms already backed by the translation, or
    // set by hand, are left alone - re-asking would spend money to confirm what is already settled.
    //
    // Whatever cannot be resolved comes back to be settled from our own translation once the
    // chapter is done.
    public async Task<(IReadOnlyList<string> stillUnknown, double costUsd)> ReconcileWithExistingAsync(
        long novelId,
        string language,
        IReadOnlyList<string> candidates,
        string model,
        CancellationToken cancellationToken = default
    ) {
        Dictionary<string, GlossaryEntryOrigin> established = await database.glossaryEntries
            .AsNoTracking()
            .Where(entry => entry.novelId == novelId && entry.language == language)
            .ToDictionaryAsync(entry => entry.sourceTerm, entry => entry.origin, cancellationToken);

        List<string> stillUnknown = [];
        double costUsd = 0;

        foreach (string candidate in candidates) {
            bool known = established.TryGetValue(candidate, out GlossaryEntryOrigin origin);

            if (known && !GlossaryPrecedence.IsOpenToRevision(origin)) {
                continue;
            }

            ResolvedTerm? resolved = await resolver.ResolveAsync(novelId, candidate, language, model, cancellationToken);

            if (resolved is null) {
                if (!known) {
                    stillUnknown.Add(candidate);
                }

                continue;
            }

            costUsd += resolved.costUsd;

            await RecordAsync(
                novelId,
                language,
                resolved.sourceTerm,
                resolved.targetTerm,
                GlossaryEntryOrigin.FromExistingTranslation,
                resolved.evidenceChapterId,
                cancellationToken
            );
        }

        return (stillUnknown, costUsd);
    }


    public async Task RecordAsync(
        long novelId,
        string language,
        string sourceTerm,
        string targetTerm,
        GlossaryEntryOrigin origin,
        long? firstSeenChapterId,
        CancellationToken cancellationToken = default
    ) {
        GlossaryEntry? existing = await database.glossaryEntries.FirstOrDefaultAsync(
            entry => entry.novelId == novelId
                && entry.language == language
                && entry.sourceTerm == sourceTerm,
            cancellationToken
        );

        if (existing is null) {
            database.glossaryEntries.Add(new GlossaryEntry {
                novelId = novelId,
                language = language,
                sourceTerm = sourceTerm,
                targetTerm = targetTerm,
                origin = origin,
                needsReview = origin == GlossaryEntryOrigin.AiExtracted,
                firstSeenChapterId = firstSeenChapterId,
                updatedAt = DateTimeOffset.UtcNow
            });

            await database.SaveChangesAsync(cancellationToken);

            return;
        }

        // Equal rank leaves the earlier entry standing: the first chapter to establish a name is
        // the one the reader met it in.
        if (!GlossaryPrecedence.Outranks(origin, existing.origin)) {
            return;
        }

        existing.targetTerm = targetTerm;
        existing.origin = origin;
        existing.needsReview = origin == GlossaryEntryOrigin.AiExtracted;
        existing.firstSeenChapterId = firstSeenChapterId ?? existing.firstSeenChapterId;
        existing.updatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);
    }


    private bool Mentions(string chapterPlainText, GlossaryEntry entry) {
        if (TermMatching.Contains(chapterPlainText, entry.sourceTerm, maxInflectionLength)) {
            return true;
        }

        return entry.aliases.Any(
            alias => TermMatching.Contains(chapterPlainText, alias, maxInflectionLength)
        );
    }
}
