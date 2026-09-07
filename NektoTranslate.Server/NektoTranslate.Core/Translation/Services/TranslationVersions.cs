using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Translation.Entities;


namespace NektoTranslate.Translation.Services;


public interface ITranslationVersions {

    Task MakeCurrentAsync(ChapterTranslation translation, CancellationToken cancellationToken = default);


    IQueryable<ChapterTranslation> CurrentOf(long chapterId, string language);


    Task<BulkVersionChangeOutcome> SetCurrentForChaptersAsync(
        long novelId,
        IReadOnlyList<long> chapterIds,
        TranslationVersionPick pick,
        CancellationToken cancellationToken = default
    );
}


// The one writer of ChapterTranslation.isCurrent, and the one reader of "the current version" - so
// that "current" means the same row everywhere it is asked for, and the unique filtered index never
// sees two rows of the same (chapterId, language) claim it at once.
public class TranslationVersions(NektoDbContext database) : ITranslationVersions {

    // Clears every other current row of translation's own (chapterId, language) and sets it on
    // translation itself, which may not be saved yet - a caller building a new row passes it here
    // before its own SaveChangesAsync, never after.
    //
    // Siblings are loaded through this same DbContext rather than with ExecuteUpdateAsync, so a
    // sibling a caller already holds tracked - a repair's own "current", about to become the
    // previous version - is the very same instance EF's identity map already knows, and this only
    // flips a field on it rather than racing a second, untracked copy of the same row to disk.
    //
    // The clear is saved here, ahead of setting translation.isCurrent below, rather than left for the
    // caller's own SaveChangesAsync to batch together with it. SQLite checks the unique filtered
    // index per statement, not once at commit, and a single SaveChangesAsync covering both changes
    // is not guaranteed to write the clear before the set - EF is as free to insert the new current
    // row first as the other way round, and the index refuses the instant that happens. Saving the
    // clear alone first is what guarantees the old row is already gone before anything claiming the
    // flag next is ever written.
    public async Task MakeCurrentAsync(ChapterTranslation translation, CancellationToken cancellationToken = default) {
        List<ChapterTranslation> previouslyCurrent = await database.chapterTranslations
            .Where(candidate => candidate.chapterId == translation.chapterId
                && candidate.language == translation.language
                && candidate.isCurrent)
            .ToListAsync(cancellationToken);

        bool clearedAnotherRow = false;

        foreach (ChapterTranslation sibling in previouslyCurrent) {
            // translation itself can already be the current row - a caller re-confirming a pick
            // that was already in effect - and clearing it here would only be undone by the line
            // below, at the cost of a save this method has no reason to make.
            if (ReferenceEquals(sibling, translation)) {
                continue;
            }

            sibling.isCurrent = false;
            clearedAnotherRow = true;
        }

        if (clearedAnotherRow) {
            await database.SaveChangesAsync(cancellationToken);
        }

        translation.isCurrent = true;
    }


    public IQueryable<ChapterTranslation> CurrentOf(long chapterId, string language) {
        return database.chapterTranslations.Where(translation => translation.chapterId == chapterId
            && translation.language == language
            && translation.isCurrent);
    }


    // Chapters with no translation in the picked language, or busy with a run that is about to write
    // one, are skipped rather than failing the whole request - the same tolerance a partial import
    // already extends to a batch of chapters that are not all in the same state.
    public async Task<BulkVersionChangeOutcome> SetCurrentForChaptersAsync(
        long novelId,
        IReadOnlyList<long> chapterIds,
        TranslationVersionPick pick,
        CancellationToken cancellationToken = default
    ) {
        List<long> distinctChapterIds = chapterIds.Distinct().ToList();

        if (distinctChapterIds.Count == 0) {
            return new BulkVersionChangeOutcome(0, 0, []);
        }

        string language = await database.novels
            .Where(novel => novel.id == novelId)
            .Select(novel => novel.targetLanguage)
            .FirstAsync(cancellationToken);

        Dictionary<long, ChapterTranslationState> stateByChapterId = await database.chapters
            .Where(chapter => chapter.novelId == novelId && distinctChapterIds.Contains(chapter.id))
            .ToDictionaryAsync(chapter => chapter.id, chapter => chapter.translationState, cancellationToken);

        // Every version of every requested chapter, loaded and tracked once rather than once per
        // chapter, so both the pick below and the write it leads to work off this same in-memory set
        // instead of a second round trip for each one.
        List<ChapterTranslation> versions = await database.chapterTranslations
            .Where(translation => translation.language == language && distinctChapterIds.Contains(translation.chapterId))
            .ToListAsync(cancellationToken);

        ILookup<long, ChapterTranslation> versionsByChapterId = versions.ToLookup(translation => translation.chapterId);

        int changed = 0;
        int skipped = 0;
        List<long> changedChapterIds = [];

        foreach (long chapterId in distinctChapterIds) {
            if (!stateByChapterId.TryGetValue(chapterId, out ChapterTranslationState state)
                || state is ChapterTranslationState.Running or ChapterTranslationState.Queued) {
                skipped++;

                continue;
            }

            List<ChapterTranslation> chapterVersions = versionsByChapterId[chapterId].ToList();

            if (chapterVersions.Count == 0) {
                skipped++;

                continue;
            }

            ChapterTranslation picked = Pick(chapterVersions.AsQueryable(), pick).First();

            await MakeCurrentAsync(picked, cancellationToken);
            changed++;
            changedChapterIds.Add(chapterId);
        }

        await database.SaveChangesAsync(cancellationToken);

        return new BulkVersionChangeOutcome(changed, skipped, changedChapterIds);
    }


    // The one place the three picks are defined - First is the smallest createdAt, Newest the
    // greatest, both tied by id for the two rows a single batch import can leave with the same
    // timestamp, and Current is simply the flagged row. Over IQueryable rather than a loaded list so
    // a top-level query (one chapter's own versions) picks in SQL instead of after loading every row;
    // a call site that already has the versions in memory - the bulk operation above - is free to
    // hand it a plain in-memory queryable instead.
    public static IQueryable<ChapterTranslation> Pick(IQueryable<ChapterTranslation> versions, TranslationVersionPick pick) {
        return pick switch {
            TranslationVersionPick.First => versions
                .OrderBy(translation => translation.createdAt)
                .ThenBy(translation => translation.id),

            TranslationVersionPick.Newest => versions
                .OrderByDescending(translation => translation.createdAt)
                .ThenByDescending(translation => translation.id),

            _ => versions.Where(translation => translation.isCurrent)
        };
    }
}


// What a bulk "make current" request actually did: how many chapters changed, how many were skipped
// because they had no translation in this language or were busy, and which chapters changed - the
// last kept apart from the wire result (SetCurrentVersionsResult) because a caller that needs to
// notify per chapter should not have to re-derive the list the service already built.
public sealed record BulkVersionChangeOutcome(
    int changed,
    int skipped,
    IReadOnlyList<long> changedChapterIds
);
