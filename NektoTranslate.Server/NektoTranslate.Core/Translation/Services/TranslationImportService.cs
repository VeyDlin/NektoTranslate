using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Entities;
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
        bool createMissingChapters = false,
        bool replaceExisting = false,
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
// Stored as ChapterTranslation with origin Imported, beside anything else the chapter has. A chapter
// that already has a translation in this language is matched by that identity - the chapter and the
// language, never the position it arrived at - and by default is reported and skipped, because
// replacing one silently is how a user loses work they paid for. replaceExisting turns that same
// match into a version instead of a refusal: the new text is added beside the old rather than in
// place of it, so nothing already read, edited or paid for disappears because the site changed.
public class TranslationImportService(
    NektoDbContext database,
    IMarkdownConversion conversion,
    IChapterTranslationStateSync stateSync,
    ITranslationVersions translationVersions
) : ITranslationImportService {

    public async Task<TranslationImportResult> ImportAsync(
        long novelId,
        string language,
        IReadOnlyList<ImportedTranslation> translations,
        bool createMissingChapters = false,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default
    ) {
        List<TranslationImportRejection> rejected = [];

        if (translations.Count == 0) {
            return new TranslationImportResult(0, rejected, 0, []);
        }

        // Both lookups are done once for the whole batch rather than per entry. A two-thousand
        // chapter novel would otherwise issue two queries per imported chapter, and the import is
        // already waiting on a website for every one of them.
        HashSet<int> wanted = translations.Select(entry => entry.chapterIndex).ToHashSet();

        Dictionary<int, long> chapterIds = await database.chapters
            .Where(chapter => chapter.novelId == novelId && wanted.Contains(chapter.index))
            .ToDictionaryAsync(chapter => chapter.index, chapter => chapter.id, cancellationToken);

        int createdChapters = createMissingChapters
            ? await CreateMissingAsync(novelId, translations, chapterIds, cancellationToken)
            : 0;

        HashSet<long> alreadyTranslated = (await database.chapterTranslations
            .Where(translation => translation.chapter!.novelId == novelId
                && translation.language == language
                && chapterIds.Values.Contains(translation.chapterId))
            .Select(translation => translation.chapterId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        int imported = 0;
        List<long> touched = [];

        // Rows added this call, kept alongside the chapter and the entry that produced them rather
        // than only their chapterId, because the one thing a caller needs about where a translation
        // landed - its own id - does not exist until SaveChangesAsync has run.
        List<(int chapterIndex, ChapterTranslation translation, bool replaced)> added = [];

        foreach (ImportedTranslation entry in translations) {
            if (!chapterIds.TryGetValue(entry.chapterIndex, out long chapterId)) {
                rejected.Add(new TranslationImportRejection(
                    entry.chapterIndex,
                    Statuses.NoChapterAtIndex.With(("index", entry.chapterIndex))
                ));

                continue;
            }

            // Add rather than Contains: this both reads whether the chapter already carried this
            // language - from the database, or from an earlier entry in this same batch - and marks
            // it as carrying one now, so a second entry aimed at the same chapter sees it too. Without
            // replaceExisting that already-carries is still a refusal, unchanged from before.
            bool alreadyHasTranslation = !alreadyTranslated.Add(chapterId);

            if (alreadyHasTranslation && !replaceExisting) {
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

            // A replace never touches the rows already on this chapter - it only adds one more, the
            // way a hand edit would. The earlier text stays exactly as translated, reachable the same
            // way any other past version is.
            ChapterTranslation translation = new ChapterTranslation {
                chapterId = chapterId,
                language = language,
                markdown = TranslationBlocks.Join(blocks),
                // Rebuilt from the blocks by the same helper the translator and the editor use, so
                // an imported translation lines up paragraph for paragraph the way ours does. This
                // is what the glossary reads to recover how a name was rendered.
                plainText = TranslationBlocks.PlainTextOf(blocks),
                origin = TranslationOrigin.Imported,
                sourceUrl = entry.sourceUrl,
                model = null,
                costUsd = null
            };

            database.chapterTranslations.Add(translation);
            // An imported row becomes current the same as any other new version - a replace of an
            // existing translation is meant to be read from now on, not sit beside the old one unread.
            await translationVersions.MakeCurrentAsync(translation, cancellationToken);
            added.Add((entry.chapterIndex, translation, alreadyHasTranslation));

            touched.Add(chapterId);
            imported++;
        }

        await database.SaveChangesAsync(cancellationToken);

        // A chapter that now holds somebody else's translation is translated, and every count and
        // every scope in the application has to agree with that — not least the one that decides
        // which chapters a run pays to translate.
        await stateSync.SyncAsync(novelId, touched, cancellationToken);

        // Read only now: translation.id does not exist until the row behind it has been saved.
        List<TranslationLanding> landed = added
            .Select(entry => new TranslationLanding(entry.chapterIndex, entry.translation.chapterId, entry.translation.id, entry.replaced))
            .ToList();

        return new TranslationImportResult(imported, rejected, createdChapters, landed);
    }


    // Makes a chapter for every entry that has nowhere to land, with no original text in it.
    //
    // This is what lets a book exist as a translation alone. The usual case has an original and the
    // translation is attached to it; this one has no original anywhere, and refusing the import
    // would mean the only copy of the book the user has cannot be held at all - not read, not
    // edited, not repaired.
    //
    // Saved before the translations are attached, because the rows that follow are addressed by
    // chapter id and a chapter that has not been written yet does not have one.
    private async Task<int> CreateMissingAsync(
        long novelId,
        IReadOnlyList<ImportedTranslation> translations,
        Dictionary<int, long> chapterIds,
        CancellationToken cancellationToken
    ) {
        List<Chapter> made = [];

        foreach (ImportedTranslation entry in translations) {
            if (chapterIds.ContainsKey(entry.chapterIndex) || made.Any(chapter => chapter.index == entry.chapterIndex)) {
                continue;
            }

            made.Add(new Chapter {
                novelId = novelId,
                index = entry.chapterIndex,
                title = string.IsNullOrWhiteSpace(entry.title)
                    ? $"Chapter {entry.chapterIndex + 1}"
                    : entry.title,
                sourceMarkdown = null,
                sourcePlainText = null,
                sourceUrl = null
            });
        }

        if (made.Count == 0) {
            return 0;
        }

        database.chapters.AddRange(made);
        await database.SaveChangesAsync(cancellationToken);

        foreach (Chapter chapter in made) {
            chapterIds[chapter.index] = chapter.id;
        }

        return made.Count;
    }
}
