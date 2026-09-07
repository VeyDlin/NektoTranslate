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


// A move re-attaches a whole (chapterId, language) group to a different chapter in one step, never
// splitting it - which is exactly what keeps the isCurrent invariant intact without Move having to
// know anything about it: the row that was current before the move is still the only current row of
// the group after it, just hanging off a different chapter.
public class TranslationMappingCurrentVersionTests {

    [Fact]
    public async Task AMoveCarriesTheCurrentFlagWithTheGroupItMoves() {
        await using NektoDbContext database = await InMemoryDatabaseAsync();

        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        Chapter source = new Chapter { novelId = novel.id, index = 0, title = "1" };
        Chapter target = new Chapter { novelId = novel.id, index = 5, title = "6" };
        database.chapters.AddRange(source, target);
        await database.SaveChangesAsync();

        DateTimeOffset baseline = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        ChapterTranslation pinned = new ChapterTranslation {
            chapterId = source.id,
            language = "Russian",
            markdown = "Old but pinned.",
            plainText = "Old but pinned.",
            createdAt = baseline.AddDays(-5),
            isCurrent = true
        };

        ChapterTranslation newest = new ChapterTranslation {
            chapterId = source.id,
            language = "Russian",
            markdown = "Newer, unpinned.",
            plainText = "Newer, unpinned.",
            createdAt = baseline.AddDays(-1),
            isCurrent = false
        };

        database.chapterTranslations.AddRange(pinned, newest);
        await database.SaveChangesAsync();

        TranslationMapping mapping = new TranslationMapping(database, new ChapterTranslationStateSync(database));

        MoveTranslationsResult result = await mapping.MoveAsync(
            novel.id,
            new MoveTranslationsRequest("Russian", fromIndex: 0, toIndex: 0, offset: 5)
        );

        Assert.True(result.applied);
        Assert.Empty(result.collisions);

        List<ChapterTranslation> moved = await database.chapterTranslations
            .Where(translation => translation.chapterId == target.id)
            .ToListAsync();

        Assert.Equal(2, moved.Count);
        Assert.Empty(await database.chapterTranslations
            .Where(translation => translation.chapterId == source.id)
            .ToListAsync());

        ChapterTranslation onlyCurrent = Assert.Single(moved, translation => translation.isCurrent);
        Assert.Equal(pinned.id, onlyCurrent.id);
        Assert.Equal("Old but pinned.", onlyCurrent.markdown);
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
