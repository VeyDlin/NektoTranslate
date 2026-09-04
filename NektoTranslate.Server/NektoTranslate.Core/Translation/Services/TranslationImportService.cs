using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Entities;
using NektoTranslate.Translation.Enums;


namespace NektoTranslate.Translation.Services;


public interface ITranslationImportService {

    Task<TranslationImportResult> ImportAsync(
        long novelId,
        string language,
        IReadOnlyList<ImportedTranslation> translations,
        CancellationToken cancellationToken = default
    );
}


// Brings an existing translation of a book into the database and attaches it to the originals.
//
// This is the thing that makes continuing someone else's translation possible. A reader who has
// followed a book for twenty chapters knows the characters by the names that translation gave them,
// and our chapter twenty-one has to use the same ones. ExistingTranslationResolver already knows how
// to recover a rendering from a translation the book has - it simply had nothing but our own output
// to read until now.
//
// Stored as ChapterTranslation with origin Imported, beside anything else the chapter has. Nothing
// is overwritten: a chapter that already has a translation in this language is reported and skipped,
// because replacing one silently is how a user loses work they paid for.
public class TranslationImportService(
    NektoDbContext database,
    IMarkdownConversion conversion,
    IChapterTranslationStateSync stateSync
) : ITranslationImportService {

    public async Task<TranslationImportResult> ImportAsync(
        long novelId,
        string language,
        IReadOnlyList<ImportedTranslation> translations,
        CancellationToken cancellationToken = default
    ) {
        List<TranslationImportRejection> rejected = [];

        if (translations.Count == 0) {
            return new TranslationImportResult(0, rejected);
        }

        // Both lookups are done once for the whole batch rather than per entry. A two-thousand
        // chapter novel would otherwise issue two queries per imported chapter, and the import is
        // already waiting on a website for every one of them.
        HashSet<int> wanted = translations.Select(entry => entry.chapterIndex).ToHashSet();

        Dictionary<int, long> chapterIds = await database.chapters
            .Where(chapter => chapter.novelId == novelId && wanted.Contains(chapter.index))
            .ToDictionaryAsync(chapter => chapter.index, chapter => chapter.id, cancellationToken);

        HashSet<long> alreadyTranslated = (await database.chapterTranslations
            .Where(translation => translation.chapter!.novelId == novelId
                && translation.language == language
                && chapterIds.Values.Contains(translation.chapterId))
            .Select(translation => translation.chapterId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        int imported = 0;
        List<long> touched = [];

        foreach (ImportedTranslation entry in translations) {
            if (!chapterIds.TryGetValue(entry.chapterIndex, out long chapterId)) {
                rejected.Add(new TranslationImportRejection(
                    entry.chapterIndex,
                    Statuses.NoChapterAtIndex.With(("index", entry.chapterIndex))
                ));

                continue;
            }

            if (!alreadyTranslated.Add(chapterId)) {
                rejected.Add(new TranslationImportRejection(
                    entry.chapterIndex,
                    Statuses.TranslationAlreadyExists.With(("index", entry.chapterIndex), ("language", language))
                ));

                continue;
            }

            string markdown = conversion.ToMarkdown(entry.html);
            List<string> blocks = TranslationBlocks.Split(markdown);

            if (blocks.Count == 0) {
                rejected.Add(new TranslationImportRejection(
                    entry.chapterIndex,
                    Statuses.ImportedTextEmpty.With(("index", entry.chapterIndex))
                ));

                continue;
            }

            database.chapterTranslations.Add(new ChapterTranslation {
                chapterId = chapterId,
                language = language,
                markdown = TranslationBlocks.Join(blocks),
                // Rebuilt from the blocks by the same helper the translator and the editor use, so
                // an imported translation lines up paragraph for paragraph the way ours does. This
                // is what the glossary reads to recover how a name was rendered.
                plainText = TranslationBlocks.PlainTextOf(blocks),
                origin = TranslationOrigin.Imported,
                model = null,
                costUsd = null
            });

            touched.Add(chapterId);
            imported++;
        }

        await database.SaveChangesAsync(cancellationToken);

        // A chapter that now holds somebody else's translation is translated, and every count and
        // every scope in the application has to agree with that — not least the one that decides
        // which chapters a run pays to translate.
        await stateSync.SyncAsync(novelId, touched, cancellationToken);

        return new TranslationImportResult(imported, rejected);
    }
}
