using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;


namespace NektoTranslate.Translation.Services;


public interface IChapterTranslationStateSync {

    Task SyncAsync(
        long novelId,
        IReadOnlyCollection<long> chapterIds,
        CancellationToken cancellationToken = default
    );
}


// Whether a chapter counts as translated is a fact about the translations it holds, not a flag the
// code that happened to write one remembers to set.
//
// Three operations change which chapters hold a translation without going through the translator -
// importing somebody else's, deleting a range of them, and re-attaching them to different chapters -
// and all three have to leave the chapter list, the progress counter and the "what still needs
// translating" query telling the same story. That last one spends money: twenty chapters imported
// precisely so they would not be paid for are twenty chapters a whole-book run would translate
// again, at full price, over the top of a human translation the reader chose to keep.
//
// Judged against the novel's target language alone. A Polish translation attached to an
// English-to-Russian book is not something the Russian run would skip.
public class ChapterTranslationStateSync(NektoDbContext database) : IChapterTranslationStateSync {

    public async Task SyncAsync(
        long novelId,
        IReadOnlyCollection<long> chapterIds,
        CancellationToken cancellationToken = default
    ) {
        if (chapterIds.Count == 0) {
            return;
        }

        string? language = await database.novels
            .Where(novel => novel.id == novelId)
            .Select(novel => novel.targetLanguage)
            .FirstOrDefaultAsync(cancellationToken);

        if (language is null) {
            return;
        }

        List<Chapter> chapters = await database.chapters
            .Where(chapter => chapter.novelId == novelId && chapterIds.Contains(chapter.id))
            .ToListAsync(cancellationToken);

        HashSet<long> translated = (await database.chapterTranslations
            .Where(translation => translation.language == language
                && chapterIds.Contains(translation.chapterId))
            .Select(translation => translation.chapterId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (Chapter chapter in chapters) {
            // A chapter the worker is holding writes its own state when it lands. Overwriting it
            // here would lose the one row a running job is reporting on.
            if (chapter.translationState is ChapterTranslationState.Queued or ChapterTranslationState.Running) {
                continue;
            }

            if (translated.Contains(chapter.id)) {
                chapter.translationState = ChapterTranslationState.Translated;
            }
            else if (chapter.translationState == ChapterTranslationState.Translated) {
                // Only a chapter that claimed to be translated is demoted. A failure is a fact about
                // an attempt, not about the absence of text, and it stays until something retries.
                chapter.translationState = ChapterTranslationState.None;
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
