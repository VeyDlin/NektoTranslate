using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Entities;


namespace NektoTranslate.Translation.Services;


public interface ITranslationMapping {

    Task<DeleteTranslationsResult> DeleteRangeAsync(
        long novelId,
        string language,
        int fromIndex,
        int toIndex,
        CancellationToken cancellationToken = default
    );


    Task<MoveTranslationsResult> MoveAsync(
        long novelId,
        MoveTranslationsRequest request,
        CancellationToken cancellationToken = default
    );
}


// Re-attaching imported translations to the right original chapters, in bulk.
//
// The operation moves the *attachment*, never the chapters. Renumbering Chapter.index would be the
// obvious reading of "shift these down by two" and it is the wrong one: the index is the identity
// that job scopes, imported source URLs and stored quality findings are all written against, so
// renumbering silently invalidates all three. A translation is attached to a chapter by id, and
// changing which chapter it hangs on is both sufficient and reversible.
//
// A chapter can hold several versions in one language - an import, then a hand correction - and they
// move together. Splitting a chapter's history across two chapters would leave the older version
// sitting against text it was never a translation of.
public class TranslationMapping(NektoDbContext database) : ITranslationMapping {

    public async Task<DeleteTranslationsResult> DeleteRangeAsync(
        long novelId,
        string language,
        int fromIndex,
        int toIndex,
        CancellationToken cancellationToken = default
    ) {
        List<long> chapterIds = await database.chapters
            .Where(chapter => chapter.novelId == novelId
                && chapter.index >= fromIndex
                && chapter.index <= toIndex)
            .Select(chapter => chapter.id)
            .ToListAsync(cancellationToken);

        List<ChapterTranslation> versions = await database.chapterTranslations
            .Where(translation => chapterIds.Contains(translation.chapterId)
                && translation.language == language)
            .ToListAsync(cancellationToken);

        // The findings go with the translation they describe. Left behind they would point at
        // paragraphs of text that no longer exists, which is worse than not reporting them at all.
        List<ChapterTranslationIssue> issues = await database.chapterTranslationIssues
            .Where(issue => chapterIds.Contains(issue.chapterId) && issue.language == language)
            .ToListAsync(cancellationToken);

        database.chapterTranslations.RemoveRange(versions);
        database.chapterTranslationIssues.RemoveRange(issues);

        await database.SaveChangesAsync(cancellationToken);

        return new DeleteTranslationsResult(
            versions.Select(version => version.chapterId).Distinct().Count(),
            versions.Count
        );
    }


    public async Task<MoveTranslationsResult> MoveAsync(
        long novelId,
        MoveTranslationsRequest request,
        CancellationToken cancellationToken = default
    ) {
        if (request.offset == 0) {
            return new MoveTranslationsResult(false, 0, []);
        }

        // Every chapter of the novel, by index. The whole novel rather than the range, because the
        // targets of a move lie outside the range being moved.
        Dictionary<int, long> chapterByIndex = await database.chapters
            .Where(chapter => chapter.novelId == novelId)
            .ToDictionaryAsync(chapter => chapter.index, chapter => chapter.id, cancellationToken);

        Dictionary<long, int> indexByChapter = chapterByIndex.ToDictionary(
            pair => pair.Value,
            pair => pair.Key
        );

        List<ChapterTranslation> all = await database.chapterTranslations
            .Where(translation => translation.chapter!.novelId == novelId
                && translation.language == request.language)
            .ToListAsync(cancellationToken);

        // Planned in index space, where the overlap rules are decidable and testable, then mapped
        // back onto chapter ids here.
        HashSet<int> occupied = all
            .Select(translation => indexByChapter[translation.chapterId])
            .ToHashSet();

        TranslationMovePlan.Plan plan = TranslationMovePlan.Build(
            chapterByIndex.Keys.ToHashSet(),
            occupied,
            request.fromIndex,
            request.toIndex,
            request.offset
        );

        // All or nothing. A shift of a hundred chapters that stops halfway leaves a mapping nobody
        // can reason about, and the user cannot tell which half moved without checking every one.
        if (plan.collisions.Count > 0 || !request.apply) {
            return new MoveTranslationsResult(false, plan.moves.Count, plan.collisions);
        }

        Dictionary<long, long> destination = plan.moves.ToDictionary(
            move => chapterByIndex[move.source],
            move => chapterByIndex[move.target]
        );

        List<ChapterTranslationIssue> issues = await database.chapterTranslationIssues
            .Where(issue => issue.chapter!.novelId == novelId && issue.language == request.language)
            .ToListAsync(cancellationToken);

        foreach (ChapterTranslation translation in all) {
            if (destination.TryGetValue(translation.chapterId, out long target)) {
                translation.chapterId = target;
            }
        }

        // Findings travel with the translation, or they end up describing a paragraph of a chapter
        // they were never about.
        foreach (ChapterTranslationIssue issue in issues) {
            if (destination.TryGetValue(issue.chapterId, out long target)) {
                issue.chapterId = target;
            }
        }

        await database.SaveChangesAsync(cancellationToken);

        return new MoveTranslationsResult(true, destination.Count, []);
    }
}
