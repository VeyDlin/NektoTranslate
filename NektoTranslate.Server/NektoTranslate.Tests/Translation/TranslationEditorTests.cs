using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Common.Data;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Entities;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// A chapter can carry an older version pinned current and a newer, unpinned one beside it -
// TranslationEditor has to treat an edit against the pinned one as fresh, and an edit against the
// merely newest one as stale, which is the opposite of what "newest" used to mean before isCurrent
// existed.
public class TranslationEditorTests {

    [Fact]
    public async Task AnEditBasedOnThePinnedCurrentVersionIsAppliedEvenThoughItIsNotTheNewest() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        (Chapter chapter, ChapterTranslation pinned, ChapterTranslation newest) = await SeedChapterAsync(database);

        TranslationEditor editor = new TranslationEditor(database, new TranslationVersions(database));

        TranslationEditOutcome outcome = await editor.EditBlockAsync(
            chapter.novelId,
            chapter.id,
            0,
            new EditTranslationBlockRequest("Russian", "Fixed text.", pinned.id)
        );

        Assert.Equal(TranslationEditResult.Applied, outcome.result);
        Assert.NotNull(outcome.translationId);
        Assert.NotEqual(newest.id, outcome.translationId);
    }


    [Fact]
    public async Task AnEditBasedOnTheNewestButNotCurrentVersionIsStale() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        (Chapter chapter, _, ChapterTranslation newest) = await SeedChapterAsync(database);

        TranslationEditor editor = new TranslationEditor(database, new TranslationVersions(database));

        TranslationEditOutcome outcome = await editor.EditBlockAsync(
            chapter.novelId,
            chapter.id,
            0,
            new EditTranslationBlockRequest("Russian", "Fixed text.", newest.id)
        );

        Assert.Equal(TranslationEditResult.Stale, outcome.result);
    }


    // The edited row itself becomes current - a person who pinned a version and then fixed a word in
    // it means the correction to be what everyone reads from now on.
    [Fact]
    public async Task TheEditedRowBecomesCurrent() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        (Chapter chapter, ChapterTranslation pinned, _) = await SeedChapterAsync(database);

        TranslationEditor editor = new TranslationEditor(database, new TranslationVersions(database));

        TranslationEditOutcome outcome = await editor.EditBlockAsync(
            chapter.novelId,
            chapter.id,
            0,
            new EditTranslationBlockRequest("Russian", "Fixed text.", pinned.id)
        );

        List<ChapterTranslation> all = await database.chapterTranslations
            .Where(translation => translation.chapterId == chapter.id)
            .ToListAsync();

        ChapterTranslation onlyCurrent = Assert.Single(all, translation => translation.isCurrent);
        Assert.Equal(outcome.translationId, onlyCurrent.id);
    }


    // chapter.novelId is set on the seeded chapter so EditBlockAsync's own novel/chapter match check
    // finds it; pinned is the older row with isCurrent set, newest is the younger, unpinned one.
    private static async Task<(Chapter chapter, ChapterTranslation pinned, ChapterTranslation newest)> SeedChapterAsync(
        NektoDbContext database
    ) {
        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        Chapter chapter = new Chapter { novelId = novel.id, index = 0, title = "1" };
        database.chapters.Add(chapter);
        await database.SaveChangesAsync();

        DateTimeOffset baseline = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        ChapterTranslation pinned = new ChapterTranslation {
            chapterId = chapter.id,
            language = "Russian",
            markdown = "Old but pinned.",
            plainText = "Old but pinned.",
            createdAt = baseline.AddDays(-5),
            isCurrent = true
        };

        ChapterTranslation newest = new ChapterTranslation {
            chapterId = chapter.id,
            language = "Russian",
            markdown = "Newer, unpinned.",
            plainText = "Newer, unpinned.",
            createdAt = baseline.AddDays(-1),
            isCurrent = false
        };

        database.chapterTranslations.AddRange(pinned, newest);
        await database.SaveChangesAsync();

        return (chapter, pinned, newest);
    }


    private static async Task<NektoDbContext> InMemoryDatabaseAsync() {
        SqliteConnection connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        DbContextOptions<NektoDbContext> options = new DbContextOptionsBuilder<NektoDbContext>()
            .UseSqlite(connection)
            .Options;

        NektoDbContext database = new NektoDbContext(options);
        await database.Database.EnsureCreatedAsync();

        return database;
    }
}
