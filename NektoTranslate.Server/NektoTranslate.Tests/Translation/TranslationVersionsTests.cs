using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Translation.Entities;
using NektoTranslate.Translation.Enums;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// TranslationVersions.Pick is the one place the three reading picks are defined - checked here with
// no database at all, over a plain in-memory queryable, including the tie every batch import can
// leave two rows with the same createdAt in. MakeCurrentAsync and SetCurrentForChaptersAsync need a
// real database to prove the unique filtered index never sees two current rows at once, so those run
// against an in-memory SQLite database the same way GlossaryLearnerTests does.
public class TranslationVersionsTests {

    [Fact]
    public void FirstIsTheSmallestCreatedAt() {
        List<ChapterTranslation> versions = [
            Version(id: 1, createdAt: Days(2)),
            Version(id: 2, createdAt: Days(5)),
            Version(id: 3, createdAt: Days(1))
        ];

        ChapterTranslation picked = TranslationVersions.Pick(versions.AsQueryable(), TranslationVersionPick.First).First();

        // Days(5) is five days before the baseline - the smallest, i.e. earliest, of the three.
        Assert.Equal(2, picked.id);
    }


    [Fact]
    public void NewestIsTheGreatestCreatedAt() {
        List<ChapterTranslation> versions = [
            Version(id: 1, createdAt: Days(2)),
            Version(id: 2, createdAt: Days(5)),
            Version(id: 3, createdAt: Days(1))
        ];

        ChapterTranslation picked = TranslationVersions.Pick(versions.AsQueryable(), TranslationVersionPick.Newest).First();

        // Days(1) is one day before the baseline - the greatest, i.e. most recent, of the three.
        Assert.Equal(3, picked.id);
    }


    // A batch import can write several rows in the same instant, and First must not pick between
    // them at random - the smallest id, matching every "newest" query this replaces, which broke the
    // same tie by the greatest id.
    [Fact]
    public void FirstBreaksATieOnCreatedAtByTheSmallestId() {
        DateTimeOffset tied = Days(3);
        List<ChapterTranslation> versions = [
            Version(id: 5, createdAt: tied),
            Version(id: 2, createdAt: tied),
            Version(id: 9, createdAt: tied)
        ];

        ChapterTranslation picked = TranslationVersions.Pick(versions.AsQueryable(), TranslationVersionPick.First).First();

        Assert.Equal(2, picked.id);
    }


    [Fact]
    public void NewestBreaksATieOnCreatedAtByTheGreatestId() {
        DateTimeOffset tied = Days(3);
        List<ChapterTranslation> versions = [
            Version(id: 5, createdAt: tied),
            Version(id: 2, createdAt: tied),
            Version(id: 9, createdAt: tied)
        ];

        ChapterTranslation picked = TranslationVersions.Pick(versions.AsQueryable(), TranslationVersionPick.Newest).First();

        Assert.Equal(9, picked.id);
    }


    [Fact]
    public void CurrentIsWhicheverRowIsFlaggedRegardlessOfAge() {
        List<ChapterTranslation> versions = [
            Version(id: 1, createdAt: Days(1)),
            Version(id: 2, createdAt: Days(9), isCurrent: true),
            Version(id: 3, createdAt: Days(5))
        ];

        ChapterTranslation picked = TranslationVersions.Pick(versions.AsQueryable(), TranslationVersionPick.Current).First();

        Assert.Equal(2, picked.id);
    }


    [Fact]
    public async Task MakeCurrentLeavesExactlyOneCurrentRowAmongSiblings() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Chapter chapter = await SeedChapterWithTranslationsAsync(
            database,
            (Days(3), true),
            (Days(6), false)
        );

        ChapterTranslation newer = await database.chapterTranslations
            .Where(translation => translation.chapterId == chapter.id)
            .OrderByDescending(translation => translation.createdAt)
            .FirstAsync();

        ITranslationVersions versions = new TranslationVersions(database);
        await versions.MakeCurrentAsync(newer);
        await database.SaveChangesAsync();

        List<ChapterTranslation> all = await database.chapterTranslations
            .Where(translation => translation.chapterId == chapter.id)
            .ToListAsync();

        ChapterTranslation onlyCurrent = Assert.Single(all, translation => translation.isCurrent);
        Assert.Equal(newer.id, onlyCurrent.id);
    }


    // MakeCurrentAsync has to work on a row that has never been saved - every writer calls it before
    // its own SaveChangesAsync, on the very entity it just constructed.
    [Fact]
    public async Task MakeCurrentWorksOnARowNotYetSaved() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Chapter chapter = await SeedChapterWithTranslationsAsync(database, (Days(1), true));

        ChapterTranslation unsaved = new ChapterTranslation {
            chapterId = chapter.id,
            language = "Russian",
            markdown = "new",
            plainText = "new",
            origin = TranslationOrigin.Repaired
        };

        database.chapterTranslations.Add(unsaved);

        ITranslationVersions versions = new TranslationVersions(database);
        await versions.MakeCurrentAsync(unsaved);
        await database.SaveChangesAsync();

        List<ChapterTranslation> all = await database.chapterTranslations
            .Where(translation => translation.chapterId == chapter.id)
            .ToListAsync();

        ChapterTranslation onlyCurrent = Assert.Single(all, translation => translation.isCurrent);
        Assert.Equal(unsaved.id, onlyCurrent.id);
    }


    // Two rows of the same chapter added in one unit of work - an import batch can do that - and
    // neither saved yet when the second is made current. No query can return the first, so this is
    // the case that needs the walk over the tracker's own Local rather than over a query result.
    [Fact]
    public async Task MakeCurrentClearsAnUnsavedSiblingToo() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Chapter chapter = await SeedChapterWithTranslationsAsync(database, (Days(1), true));

        ChapterTranslation first = UnsavedTranslation(chapter.id, "first");
        ChapterTranslation second = UnsavedTranslation(chapter.id, "second");

        database.chapterTranslations.Add(first);
        database.chapterTranslations.Add(second);

        ITranslationVersions versions = new TranslationVersions(database);
        await versions.MakeCurrentAsync(first);
        await versions.MakeCurrentAsync(second);
        await database.SaveChangesAsync();

        List<ChapterTranslation> all = await database.chapterTranslations
            .Where(translation => translation.chapterId == chapter.id)
            .ToListAsync();

        Assert.Equal(3, all.Count);
        ChapterTranslation onlyCurrent = Assert.Single(all, translation => translation.isCurrent);
        Assert.Equal(second.id, onlyCurrent.id);
    }


    // The flip stays in the caller's unit of work: until the caller saves, the database still has
    // the old row current, so there is never a moment on disk with no current version and a failed
    // repair or a half-done import leaves nothing behind.
    [Fact]
    public async Task MakeCurrentWritesNothingOnItsOwn() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Chapter chapter = await SeedChapterWithTranslationsAsync(database, (Days(1), true));

        ChapterTranslation unsaved = UnsavedTranslation(chapter.id, "new");
        database.chapterTranslations.Add(unsaved);

        ITranslationVersions versions = new TranslationVersions(database);
        await versions.MakeCurrentAsync(unsaved);

        List<ChapterTranslation> onDisk = await database.chapterTranslations
            .AsNoTracking()
            .Where(translation => translation.chapterId == chapter.id)
            .ToListAsync();

        ChapterTranslation stillCurrent = Assert.Single(onDisk);
        Assert.True(stillCurrent.isCurrent);
    }


    // RepairAsync itself calls the real Claude Code CLI and cannot run in a test, but this is exactly
    // the composition it performs once it has read a chapter's versions: pick the one sourceVersion
    // asks for, then always make the new row current, whatever was read.
    [Fact]
    public async Task ARepairThatReadFirstWritesARowThatIsCurrent() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Chapter chapter = await SeedChapterWithTranslationsAsync(
            database,
            (Days(10), false),
            (Days(1), true)
        );

        ITranslationVersions versions = new TranslationVersions(database);

        IQueryable<ChapterTranslation> ofChapter = database.chapterTranslations
            .Where(translation => translation.chapterId == chapter.id && translation.language == "Russian");

        ChapterTranslation readFrom = await TranslationVersions.Pick(ofChapter, TranslationVersionPick.First)
            .FirstAsync();

        Assert.Equal(Days(10), readFrom.createdAt);

        ChapterTranslation repaired = new ChapterTranslation {
            chapterId = chapter.id,
            language = "Russian",
            markdown = readFrom.markdown,
            plainText = readFrom.plainText,
            origin = TranslationOrigin.Repaired
        };

        database.chapterTranslations.Add(repaired);
        await versions.MakeCurrentAsync(repaired);
        await database.SaveChangesAsync();

        List<ChapterTranslation> stored = await database.chapterTranslations
            .Where(translation => translation.chapterId == chapter.id)
            .ToListAsync();

        ChapterTranslation onlyCurrent = Assert.Single(stored, translation => translation.isCurrent);
        Assert.Equal(repaired.id, onlyCurrent.id);
    }


    [Fact]
    public async Task BulkSkipsBusyAndUntranslatedChaptersAndCountsThem() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        Chapter ready = new Chapter { novelId = novel.id, index = 0, title = "1" };
        Chapter busy = new Chapter {
            novelId = novel.id, index = 1, title = "2", translationState = ChapterTranslationState.Running
        };
        Chapter untranslated = new Chapter { novelId = novel.id, index = 2, title = "3" };

        database.chapters.AddRange(ready, busy, untranslated);
        await database.SaveChangesAsync();

        database.chapterTranslations.AddRange(
            new ChapterTranslation {
                chapterId = ready.id, language = "Russian", markdown = "first", plainText = "first", createdAt = Days(5)
            },
            new ChapterTranslation {
                chapterId = ready.id, language = "Russian", markdown = "second", plainText = "second",
                createdAt = Days(1), isCurrent = true
            },
            new ChapterTranslation {
                chapterId = busy.id, language = "Russian", markdown = "third", plainText = "third", isCurrent = true
            }
        );
        await database.SaveChangesAsync();

        ITranslationVersions versions = new TranslationVersions(database);
        BulkVersionChangeOutcome outcome = await versions.SetCurrentForChaptersAsync(
            novel.id,
            [ready.id, busy.id, untranslated.id],
            TranslationVersionPick.First
        );

        Assert.Equal(1, outcome.changed);
        Assert.Equal(2, outcome.skipped);
        Assert.Equal([ready.id], outcome.changedChapterIds);

        ChapterTranslation current = await database.chapterTranslations
            .Where(translation => translation.chapterId == ready.id && translation.isCurrent)
            .SingleAsync();

        Assert.Equal("first", current.markdown);
    }


    private static ChapterTranslation Version(long id, DateTimeOffset createdAt, bool isCurrent = false) {
        return new ChapterTranslation {
            id = id,
            chapterId = 1,
            language = "Russian",
            markdown = "x",
            plainText = "x",
            createdAt = createdAt,
            isCurrent = isCurrent
        };
    }


    private static DateTimeOffset Days(int ago) {
        return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-ago);
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


    // One chapter of a novel with the given translations, in the given order, unsaved id order
    // matching the order the tuples are given - not the createdAt order - so a test can name a "10
    // days ago, not current" row and a "1 day ago, current" one without depending on insertion order
    // to find them again.
    private static async Task<Chapter> SeedChapterWithTranslationsAsync(
        NektoDbContext database,
        params (DateTimeOffset createdAt, bool isCurrent)[] translations
    ) {
        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        Chapter chapter = new Chapter { novelId = novel.id, index = 0, title = "1" };
        database.chapters.Add(chapter);
        await database.SaveChangesAsync();

        foreach ((DateTimeOffset createdAt, bool isCurrent) in translations) {
            database.chapterTranslations.Add(new ChapterTranslation {
                chapterId = chapter.id,
                language = "Russian",
                markdown = $"text {createdAt:O}",
                plainText = $"text {createdAt:O}",
                createdAt = createdAt,
                isCurrent = isCurrent
            });
        }

        await database.SaveChangesAsync();

        return chapter;
    }


    private static ChapterTranslation UnsavedTranslation(long chapterId, string text) {
        return new ChapterTranslation {
            chapterId = chapterId,
            language = "Russian",
            markdown = text,
            plainText = text,
            origin = TranslationOrigin.Repaired
        };
    }
}
