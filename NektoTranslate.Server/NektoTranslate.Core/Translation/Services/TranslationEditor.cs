using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Entities;
using NektoTranslate.Translation.Enums;


namespace NektoTranslate.Translation.Services;


public interface ITranslationEditor {

    Task<TranslationEditOutcome> EditBlockAsync(
        long novelId,
        long chapterId,
        int blockIndex,
        EditTranslationBlockRequest request,
        CancellationToken cancellationToken = default
    );
}


// Hand corrections to a finished translation.
//
// The reason this exists at all is that the quality checks flag exactly the places where the model
// already failed once. Asking the same model to try again is the weakest of the available answers -
// a block it copied instead of translating is a block it judged untranslatable, and it will judge
// the same way a second time. Somebody has to be able to just write the right words.
//
// A correction is a new version, never an overwrite. The chapter already holds several translations
// by design, so keeping the machine pass beside the corrected one costs a row and makes every edit
// reversible. Overwriting would make a mistyped correction unrecoverable.
public class TranslationEditor(NektoDbContext database, ITranslationVersions translationVersions) : ITranslationEditor {

    public async Task<TranslationEditOutcome> EditBlockAsync(
        long novelId,
        long chapterId,
        int blockIndex,
        EditTranslationBlockRequest request,
        CancellationToken cancellationToken = default
    ) {
        Chapter? chapter = await database.chapters
            .FirstOrDefaultAsync(
                candidate => candidate.id == chapterId && candidate.novelId == novelId,
                cancellationToken
            );

        if (chapter is null) {
            return new TranslationEditOutcome(TranslationEditResult.ChapterNotFound);
        }

        // The worker translates chapter by chapter and writes each one when it finishes. An edit to
        // a chapter it is holding would be replaced by that write, so it is refused while the
        // chapter is claimed rather than accepted and lost.
        if (chapter.translationState is ChapterTranslationState.Running or ChapterTranslationState.Queued) {
            return new TranslationEditOutcome(TranslationEditResult.Busy);
        }

        // The current version, not the newest - a person who pinned an older one and is now fixing a
        // word in it means to edit that version, not whatever a later, unrelated pass produced.
        ChapterTranslation? current = await translationVersions
            .CurrentOf(chapterId, request.language)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null) {
            return new TranslationEditOutcome(TranslationEditResult.NoTranslation);
        }

        if (current.id != request.baseTranslationId) {
            return new TranslationEditOutcome(TranslationEditResult.Stale);
        }

        List<string> blocks = TranslationBlocks.Split(current.markdown);

        if (blockIndex < 0 || blockIndex >= blocks.Count) {
            return new TranslationEditOutcome(TranslationEditResult.BlockOutOfRange);
        }

        blocks[blockIndex] = request.text.Trim();

        ChapterTranslation edited = new ChapterTranslation {
            chapterId = chapterId,
            language = request.language,
            markdown = TranslationBlocks.Join(blocks),
            plainText = TranslationBlocks.PlainTextOf(blocks),
            origin = TranslationOrigin.Manual,
            // No model and no cost: nothing was spent, and recording the previous model here would
            // credit it with words a person wrote.
            model = null,
            costUsd = null
        };

        database.chapterTranslations.Add(edited);
        // The person editing was reading the current version and fixed a word in it - the edited row
        // takes over as current whatever the base was, pinned or not.
        await translationVersions.MakeCurrentAsync(edited, cancellationToken);

        // The findings on this block are answered by definition - the user looked at the block and
        // rewrote it. Resolved rather than dismissed, because the text really did change.
        //
        // Only this block. A finding elsewhere in the chapter is untouched, and the chapter-wide
        // ones stay open because editing one paragraph says nothing about them.
        List<ChapterTranslationIssue> answered = await database.chapterTranslationIssues
            .Where(issue => issue.chapterId == chapterId
                && issue.language == request.language
                && issue.blockIndex == blockIndex
                && issue.state == TranslationIssueState.Open)
            .ToListAsync(cancellationToken);

        foreach (ChapterTranslationIssue issue in answered) {
            issue.state = TranslationIssueState.Resolved;
            issue.closedAt = DateTimeOffset.UtcNow;
        }

        await database.SaveChangesAsync(cancellationToken);

        return new TranslationEditOutcome(TranslationEditResult.Applied, edited.id, answered.Count);
    }
}
