using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Common.Data;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Settings.Services;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Entities;
using Xunit;


namespace NektoTranslate.Tests.Glossary;


public class GlossaryLearnerTests {

    [Fact]
    public void AChapterWithBothSidesAndAnUnanalyzedGlossaryQualifies() {
        Chapter chapter = MakeChapter(index: 5, sourceMarkdown: "text", language: "Russian");

        Assert.True(GlossaryLearner.Qualifies(chapter, "Russian", 0, 10));
    }


    // The whole reason this pass exists: a translation with no original beside it has nothing to
    // compare a term's rendering against.
    [Fact]
    public void AChapterWithNoOriginalDoesNotQualify() {
        Chapter chapter = MakeChapter(index: 5, sourceMarkdown: null, language: "Russian");

        Assert.False(GlossaryLearner.Qualifies(chapter, "Russian", 0, 10));
    }


    [Fact]
    public void AChapterWithNoTranslationInTheRequestedLanguageDoesNotQualify() {
        Chapter chapter = MakeChapter(index: 5, sourceMarkdown: "text", language: "Spanish");

        Assert.False(GlossaryLearner.Qualifies(chapter, "Russian", 0, 10));
    }


    // Analyzed means an earlier pass - a translate run or an earlier learning run - already spent
    // the money to read this chapter's terms. Re-reading it would pay twice for the same answer.
    [Fact]
    public void AnAlreadyAnalyzedChapterDoesNotQualify() {
        Chapter chapter = MakeChapter(index: 5, sourceMarkdown: "text", language: "Russian");
        chapter.glossaryState = ChapterGlossaryState.Analyzed;

        Assert.False(GlossaryLearner.Qualifies(chapter, "Russian", 0, 10));
    }


    [Fact]
    public void AChapterOutsideTheRequestedRangeDoesNotQualify() {
        Chapter chapter = MakeChapter(index: 20, sourceMarkdown: "text", language: "Russian");

        Assert.False(GlossaryLearner.Qualifies(chapter, "Russian", 0, 10));
    }


    [Fact]
    public void RangeEndpointsThemselvesQualify() {
        Chapter atFrom = MakeChapter(index: 0, sourceMarkdown: "text", language: "Russian");
        Chapter atTo = MakeChapter(index: 10, sourceMarkdown: "text", language: "Russian");

        Assert.True(GlossaryLearner.Qualifies(atFrom, "Russian", 0, 10));
        Assert.True(GlossaryLearner.Qualifies(atTo, "Russian", 0, 10));
    }


    // End to end against a real (in-memory) SQLite database: a chapter with both sides is read and
    // marked Analyzed, a chapter already Analyzed and a translation-only chapter are both left
    // alone, and the counts the caller reports come back from the fakes standing in for the model.
    [Fact]
    public async Task LearnAsyncReadsOnlyTheQualifyingChaptersAndMarksThemAnalyzed() {
        using SqliteConnection connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        DbContextOptions<NektoDbContext> options = new DbContextOptionsBuilder<NektoDbContext>()
            .UseSqlite(connection)
            .Options;

        using NektoDbContext database = new NektoDbContext(options);
        await database.Database.EnsureCreatedAsync();

        Novel novel = new Novel { title = "Book", sourceLanguage = "Japanese", targetLanguage = "Russian" };
        database.novels.Add(novel);
        await database.SaveChangesAsync();

        Chapter qualifying = new Chapter { novelId = novel.id, index = 0, title = "1", sourceMarkdown = "one" };
        Chapter alreadyAnalyzed = new Chapter {
            novelId = novel.id, index = 1, title = "2", sourceMarkdown = "two", glossaryState = ChapterGlossaryState.Analyzed
        };
        Chapter translationOnly = new Chapter { novelId = novel.id, index = 2, title = "3", sourceMarkdown = null };

        database.chapters.AddRange(qualifying, alreadyAnalyzed, translationOnly);
        await database.SaveChangesAsync();

        database.chapterTranslations.AddRange(
            new ChapterTranslation { chapterId = qualifying.id, language = "Russian", markdown = "один", plainText = "один" },
            new ChapterTranslation { chapterId = alreadyAnalyzed.id, language = "Russian", markdown = "два", plainText = "два" },
            new ChapterTranslation { chapterId = translationOnly.id, language = "Russian", markdown = "три", plainText = "три" }
        );
        await database.SaveChangesAsync();

        FakeTermExtractor extractor = new FakeTermExtractor { candidates = ["Alice", "Bob"] };
        FakeGlossaryService glossary = new FakeGlossaryService { stillUnknown = ["Bob"] };

        GlossaryLearner learner = new GlossaryLearner(database, new ChapterSegmenter(), extractor, glossary, new SettingsService(database));
        LearnedGlossary result = await learner.LearnAsync(novel.id, "Russian", 0, 2);

        Assert.Equal(1, result.chaptersRead);
        Assert.Equal(1, result.renderingsLearned);
        Assert.Equal(1, result.stillUnknown);

        await database.Entry(qualifying).ReloadAsync();
        await database.Entry(alreadyAnalyzed).ReloadAsync();
        await database.Entry(translationOnly).ReloadAsync();

        Assert.Equal(ChapterGlossaryState.Analyzed, qualifying.glossaryState);
        Assert.Equal(ChapterGlossaryState.Analyzed, alreadyAnalyzed.glossaryState);
        Assert.Equal(ChapterGlossaryState.NotAnalyzed, translationOnly.glossaryState);
    }


    private static Chapter MakeChapter(int index, string? sourceMarkdown, string language) {
        return new Chapter {
            index = index,
            title = "Chapter",
            sourceMarkdown = sourceMarkdown,
            translations = [new ChapterTranslation { language = language, markdown = "x", plainText = "x" }]
        };
    }


    private sealed class FakeTermExtractor : ITermExtractor {
        public IReadOnlyList<string> candidates = [];


        public Task<(IReadOnlyList<string> terms, double costUsd)> ExtractCandidatesAsync(
            IReadOnlyList<string> segments,
            string sourceLanguage,
            string model,
            CancellationToken cancellationToken = default
        ) {
            return Task.FromResult((candidates, 0.01));
        }


        public Task<(string? rendering, double costUsd)> ReadRenderingAsync(
            string sourceFragment,
            string translatedFragment,
            string term,
            string targetLanguage,
            string model,
            CancellationToken cancellationToken = default
        ) {
            throw new NotSupportedException("GlossaryLearner never asks for a rendering directly.");
        }
    }


    private sealed class FakeGlossaryService : IGlossaryService {
        public IReadOnlyList<string> stillUnknown = [];


        public Task<IReadOnlyList<GlossaryTerm>> SelectForChapterAsync(
            long novelId,
            string language,
            string chapterPlainText,
            CancellationToken cancellationToken = default
        ) {
            throw new NotSupportedException("GlossaryLearner never selects a chapter's glossary.");
        }


        public Task<(IReadOnlyList<string> stillUnknown, double costUsd)> ReconcileWithExistingAsync(
            long novelId,
            string language,
            IReadOnlyList<string> candidates,
            string model,
            CancellationToken cancellationToken = default
        ) {
            return Task.FromResult((stillUnknown, 0.02));
        }


        public Task RecordAsync(
            long novelId,
            string language,
            string sourceTerm,
            string targetTerm,
            GlossaryEntryOrigin origin,
            long? firstSeenChapterId,
            CancellationToken cancellationToken = default
        ) {
            throw new NotSupportedException("GlossaryLearner records through ReconcileWithExistingAsync only.");
        }
    }
}
