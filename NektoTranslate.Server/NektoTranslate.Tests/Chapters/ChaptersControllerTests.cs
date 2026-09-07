using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Controllers;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Common.Data;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Translation.Entities;
using Xunit;


namespace NektoTranslate.Tests.Chapters;


// List's currentIsOlder is a nested correlated Any() rather than two separately fetched ids, which
// is exactly the shape most likely to fail to translate against a real provider even though it reads
// fine as C# - checked here against an in-memory SQLite database rather than trusted from the source.
public class ChaptersControllerTests {

    [Fact]
    public async Task CurrentIsOlderIsFalseForAChapterWithOneVersion() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        Chapter chapter = new Chapter { novelId = novel.id, index = 0, title = "1" };
        database.chapters.Add(chapter);
        await database.SaveChangesAsync();

        database.chapterTranslations.Add(new ChapterTranslation {
            chapterId = chapter.id, language = "Russian", markdown = "x", plainText = "x", isCurrent = true
        });
        await database.SaveChangesAsync();

        object row = (await ListAsync(database, novel.id)).Single();

        Assert.False(CurrentIsOlderOf(row));
    }


    [Fact]
    public async Task CurrentIsOlderIsFalseForAChapterWithNoTranslationAtAll() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        database.chapters.Add(new Chapter { novelId = novel.id, index = 0, title = "1" });
        await database.SaveChangesAsync();

        object row = (await ListAsync(database, novel.id)).Single();

        Assert.False(CurrentIsOlderOf(row));
    }


    [Fact]
    public async Task CurrentIsOlderIsTrueWhenAnOlderVersionIsPinnedCurrent() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        Chapter chapter = new Chapter { novelId = novel.id, index = 0, title = "1" };
        database.chapters.Add(chapter);
        await database.SaveChangesAsync();

        DateTimeOffset baseline = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        database.chapterTranslations.AddRange(
            new ChapterTranslation {
                chapterId = chapter.id, language = "Russian", markdown = "old", plainText = "old",
                createdAt = baseline.AddDays(-5), isCurrent = true
            },
            new ChapterTranslation {
                chapterId = chapter.id, language = "Russian", markdown = "new", plainText = "new",
                createdAt = baseline.AddDays(-1), isCurrent = false
            }
        );
        await database.SaveChangesAsync();

        object row = (await ListAsync(database, novel.id)).Single();

        Assert.True(CurrentIsOlderOf(row));
    }


    [Fact]
    public async Task CurrentIsOlderIsFalseWhenTheCurrentVersionIsAlsoTheNewest() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        Chapter chapter = new Chapter { novelId = novel.id, index = 0, title = "1" };
        database.chapters.Add(chapter);
        await database.SaveChangesAsync();

        DateTimeOffset baseline = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        database.chapterTranslations.AddRange(
            new ChapterTranslation {
                chapterId = chapter.id, language = "Russian", markdown = "old", plainText = "old",
                createdAt = baseline.AddDays(-5), isCurrent = false
            },
            new ChapterTranslation {
                chapterId = chapter.id, language = "Russian", markdown = "new", plainText = "new",
                createdAt = baseline.AddDays(-1), isCurrent = true
            }
        );
        await database.SaveChangesAsync();

        object row = (await ListAsync(database, novel.id)).Single();

        Assert.False(CurrentIsOlderOf(row));
    }


    private static Task<IReadOnlyList<object>> ListAsync(NektoDbContext database, long novelId) {
        ChaptersController controller = new ChaptersController(null!, database, null!, null!);

        return controller.List(novelId, CancellationToken.None);
    }


    // List projects onto an anonymous type the test cannot name, so its one field of interest is
    // read back by reflection rather than by casting to dynamic - dynamic would work too, but every
    // other type in this codebase is explicit, and a Convert.ToBoolean-style cast is worth avoiding
    // when a named property read says exactly as much.
    private static bool CurrentIsOlderOf(object row) {
        object? value = row.GetType().GetProperty("currentIsOlder")?.GetValue(row);

        return value is true;
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
