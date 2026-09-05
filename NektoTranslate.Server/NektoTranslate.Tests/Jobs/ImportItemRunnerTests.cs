using Microsoft.Extensions.Logging.Abstractions;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Jobs.Services;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Services;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Jobs;


// The decision ImportJobWorker's sequential run and a lone retry of one failed item both have to
// make the same way: which import a job's kind calls for, what counts as landed versus rejected, and
// how a thrown exception reads as a status. No database involved - the runner never touches one, it
// only decides an outcome and writes it onto the item it was handed.
public class ImportItemRunnerTests {

    private static ImportJob OriginalsJob(int startAt = 0) {
        return new ImportJob { novelId = 1, kind = ImportKind.Originals, startAtChapterIndex = startAt };
    }


    private static ImportJob TranslationJob(int startAt = 0) {
        return new ImportJob { novelId = 1, kind = ImportKind.Translation, language = "Russian", startAtChapterIndex = startAt };
    }


    private static ImportJobItem Item(int position = 0, string sourceUrl = "https://example.test/1", string title = "Chapter") {
        return new ImportJobItem { position = position, sourceUrl = sourceUrl, title = title };
    }


    private static ImportItemRunner Runner(
        FakeSiteParser? parser = null,
        FakeChapterImportService? originals = null,
        FakeTranslationImportService? translations = null
    ) {
        return new ImportItemRunner(
            parser ?? new FakeSiteParser(),
            originals ?? new FakeChapterImportService(),
            translations ?? new FakeTranslationImportService(),
            NullLogger<ImportItemRunner>.Instance
        );
    }


    [Fact]
    public async Task AnOriginalsItemLandsAsANewChapter() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("Fetched title", "<p>Text</p>", "https://example.test/1") };
        Chapter created = new Chapter { id = 42, index = 7, title = "x", sourceMarkdown = "x", sourcePlainText = "x" };
        FakeChapterImportService originals = new FakeChapterImportService { result = new ChapterImportResult([new ChapterImportOutcome(0, created, null)]) };

        ImportJobItem item = Item(title: "Chosen title");
        await Runner(parser, originals).RunAsync(OriginalsJob(), item);

        Assert.Equal(ImportItemState.Imported, item.state);
        Assert.Equal(42, item.chapterId);
        Assert.Equal(7, item.chapterIndex);
        Assert.Null(item.statusCode);

        // The title the user picked on the contents screen wins over whatever the parser read from
        // the page itself.
        Assert.Equal("Chosen title", originals.lastChapters![0].title);
    }


    // An empty title is what a translator's-note-only entry can leave behind; the parser's own title
    // is what fills it rather than importing a nameless chapter.
    [Fact]
    public async Task AnEmptyChosenTitleFallsBackToTheParsersTitle() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("Parsed title", "<p>Text</p>", "https://example.test/1") };
        Chapter created = new Chapter { id = 1, index = 0, title = "x", sourceMarkdown = "x", sourcePlainText = "x" };
        FakeChapterImportService originals = new FakeChapterImportService { result = new ChapterImportResult([new ChapterImportOutcome(0, created, null)]) };

        await Runner(parser, originals).RunAsync(OriginalsJob(), Item(title: "  "));

        Assert.Equal("Parsed title", originals.lastChapters![0].title);
    }


    // The Originals branch now takes the same explicit-index path a translation import always has,
    // for the same reason: a contents page can open with something that is not chapter one.
    [Fact]
    public async Task AnOriginalsItemPassesStartIndexPlusPositionAsTheExplicitIndex() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };
        Chapter created = new Chapter { id = 5, index = 13, title = "x", sourceMarkdown = "x", sourcePlainText = "x" };
        FakeChapterImportService originals = new FakeChapterImportService { result = new ChapterImportResult([new ChapterImportOutcome(0, created, null)]) };

        ImportJobItem item = Item(position: 3);
        await Runner(parser, originals).RunAsync(OriginalsJob(startAt: 10), item);

        Assert.Equal(13, originals.lastChapters![0].index);
    }


    // Not a failure: the address is already in the book, so the item has to point at where the
    // chapter actually lives rather than at the slot this run aimed for.
    [Fact]
    public async Task AnOriginalAlreadyImportedBySourceUrlIsSkippedAndPointsAtTheExistingChapter() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };
        Chapter existing = new Chapter { id = 4, index = 2, title = "x", sourceMarkdown = "x", sourcePlainText = "x" };

        FakeChapterImportService originals = new FakeChapterImportService {
            result = new ChapterImportResult([new ChapterImportOutcome(0, existing, Statuses.ChapterAlreadyImported.With(("index", 2)))])
        };

        ImportJobItem item = Item();
        await Runner(parser, originals).RunAsync(OriginalsJob(), item);

        Assert.Equal(ImportItemState.Skipped, item.state);
        Assert.Equal("CHAPTER_ALREADY_IMPORTED", item.statusCode);
        Assert.Equal(4, item.chapterId);
        Assert.Equal(2, item.chapterIndex);
    }


    // The requested index belongs to a chapter that came from a different page. Nothing was written,
    // so there is nothing for the item to point at either.
    [Fact]
    public async Task AnOriginalWhoseIndexIsOccupiedByADifferentPageFails() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };

        FakeChapterImportService originals = new FakeChapterImportService {
            result = new ChapterImportResult([new ChapterImportOutcome(0, null, Statuses.ChapterIndexOccupied.With(("index", 0)))])
        };

        ImportJobItem item = Item();
        await Runner(parser, originals).RunAsync(OriginalsJob(), item);

        Assert.Equal(ImportItemState.Failed, item.state);
        Assert.Equal("CHAPTER_INDEX_OCCUPIED", item.statusCode);
        Assert.Null(item.chapterId);
        Assert.Null(item.chapterIndex);
    }


    // A replace overwrote the chapter rather than creating one, but the outcome ImportAsync hands
    // back looks exactly like a fresh import - a chapter, no rejection - so it has to read the same
    // way here too, with no branch of its own for replaced.
    [Fact]
    public async Task AReplacedOriginalIsReportedAsImported() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };
        Chapter existing = new Chapter { id = 4, index = 2, title = "x", sourceMarkdown = "x", sourcePlainText = "x" };

        FakeChapterImportService originals = new FakeChapterImportService {
            result = new ChapterImportResult([new ChapterImportOutcome(0, existing, null, replaced: true)])
        };

        ImportJob job = OriginalsJob();
        job.replaceExisting = true;

        ImportJobItem item = Item();
        await Runner(parser, originals).RunAsync(job, item);

        Assert.Equal(ImportItemState.Imported, item.state);
        Assert.Equal(4, item.chapterId);
        Assert.Equal(2, item.chapterIndex);
        Assert.Null(item.statusCode);
    }


    [Fact]
    public async Task ATranslationItemLandsOnStartIndexPlusPosition() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };

        FakeTranslationImportService translations = new FakeTranslationImportService {
            result = new TranslationImportResult(1, [], 0, [new TranslationLanding(13, 1, 1, false)])
        };

        ImportJobItem item = Item(position: 3);
        await Runner(parser, translations: translations).RunAsync(TranslationJob(startAt: 10), item);

        Assert.Equal(ImportItemState.Imported, item.state);
        Assert.Equal(13, item.chapterIndex);
        Assert.Null(item.statusCode);
    }


    // The gap this fixes: the translation branch used to leave chapterId null on every item it
    // landed, because nothing before this line ever read one off the result.
    [Fact]
    public async Task ATranslationItemRecordsTheChapterIdItLandedOn() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };

        FakeTranslationImportService translations = new FakeTranslationImportService {
            result = new TranslationImportResult(1, [], 0, [new TranslationLanding(0, 77, 501, false)])
        };

        ImportJobItem item = Item();
        await Runner(parser, translations: translations).RunAsync(TranslationJob(), item);

        Assert.Equal(ImportItemState.Imported, item.state);
        Assert.Equal(77, item.chapterId);
    }


    // A book can arrive as somebody else's translation with no original anywhere, and then the entry
    // has to carry its own title and the job's answer about making the missing chapter. Both were
    // lost once already when this decision was moved out of the worker, and neither loss showed up
    // as a failure - the import simply reported every chapter as having nowhere to land.
    [Fact]
    public async Task ATranslationItemCarriesItsTitleAndTheJobsAnswerAboutMissingChapters() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("Fetched", "<p>Text</p>", "https://example.test/1") };
        FakeTranslationImportService translations = new FakeTranslationImportService();

        ImportJob job = TranslationJob();
        job.createMissingChapters = true;

        await Runner(parser, translations: translations).RunAsync(job, Item(title: "Глава 1"));

        Assert.True(translations.lastCreateMissingChapters);
        Assert.Equal("Глава 1", translations.lastTranslations[0].title);
    }


    // The one outcome that is not a failure: the chapter already carries this language, and nothing
    // was overwritten.
    [Fact]
    public async Task ATranslationAlreadyPresentIsSkippedRatherThanFailed() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };

        FakeTranslationImportService translations = new FakeTranslationImportService {
            result = new TranslationImportResult(0, [new TranslationImportRejection(0, Statuses.TranslationAlreadyExists.With(("index", 0), ("language", "Russian")))], 0, [])
        };

        ImportJobItem item = Item();
        await Runner(parser, translations: translations).RunAsync(TranslationJob(), item);

        Assert.Equal(ImportItemState.Skipped, item.state);
        Assert.Equal("TRANSLATION_ALREADY_EXISTS", item.statusCode);
    }


    [Fact]
    public async Task ATranslationRejectedForAnyOtherReasonFails() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };

        FakeTranslationImportService translations = new FakeTranslationImportService {
            result = new TranslationImportResult(0, [new TranslationImportRejection(0, Statuses.NoChapterAtIndex.With(("index", 0)))], 0, [])
        };

        ImportJobItem item = Item();
        await Runner(parser, translations: translations).RunAsync(TranslationJob(), item);

        Assert.Equal(ImportItemState.Failed, item.state);
        Assert.Equal("NO_CHAPTER_AT_INDEX", item.statusCode);
    }


    // The job's own answer, not a per-item choice - it has to reach whichever import a run's kind
    // calls for, the same way createMissingChapters already does.
    [Fact]
    public async Task ReplaceExistingReachesBothServices() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };

        Chapter created = new Chapter { id = 1, index = 0, title = "x", sourceMarkdown = "x", sourcePlainText = "x" };
        FakeChapterImportService originals = new FakeChapterImportService { result = new ChapterImportResult([new ChapterImportOutcome(0, created, null)]) };

        ImportJob originalsJob = OriginalsJob();
        originalsJob.replaceExisting = true;
        await Runner(parser, originals).RunAsync(originalsJob, Item());

        Assert.True(originals.lastReplaceExisting);

        FakeTranslationImportService translations = new FakeTranslationImportService();

        ImportJob translationJob = TranslationJob();
        translationJob.replaceExisting = true;
        await Runner(parser, translations: translations).RunAsync(translationJob, Item());

        Assert.True(translations.lastReplaceExisting);
    }


    [Fact]
    public async Task AParserThatFoundNoContentIsReportedByName() {
        FakeSiteParser parser = new FakeSiteParser { throwOnFetch = new InvalidOperationException("Error: the page found no content to extract") };

        ImportJobItem item = Item(sourceUrl: "https://example.test/empty");
        await Runner(parser).RunAsync(OriginalsJob(), item);

        Assert.Equal(ImportItemState.Failed, item.state);
        Assert.Equal("PARSER_FOUND_NO_CONTENT", item.statusCode);
        Assert.Contains("https://example.test/empty", item.statusText);
    }


    [Fact]
    public async Task AnUnsupportedSiteIsReportedByName() {
        FakeSiteParser parser = new FakeSiteParser { throwOnFetch = new InvalidOperationException("No parser is registered for this host") };

        ImportJobItem item = Item();
        await Runner(parser).RunAsync(OriginalsJob(), item);

        Assert.Equal(ImportItemState.Failed, item.state);
        Assert.Equal("NO_PARSER_FOR_SITE", item.statusCode);
    }


    // Everything else a fetch can throw - a timeout, a broken connection - falls back to the generic
    // reason, with only the exception's first line kept: what follows is a stack trace, not something
    // written for a person to read.
    [Fact]
    public async Task AnyOtherFetchFailureFallsBackToTheGenericReason() {
        FakeSiteParser parser = new FakeSiteParser { throwOnFetch = new InvalidOperationException("Connection reset\nat Some.Internal.Method()") };

        ImportJobItem item = Item();
        await Runner(parser).RunAsync(OriginalsJob(), item);

        Assert.Equal(ImportItemState.Failed, item.state);
        Assert.Equal("CHAPTER_FETCH_FAILED", item.statusCode);
        Assert.Contains("Connection reset", item.statusText);
        Assert.DoesNotContain("Some.Internal.Method", item.statusText);
    }


    // A cancellation is the caller's to handle - the run stops the loop, a retry lets the request
    // itself unwind - so it must reach the caller as itself rather than being folded into a Failed
    // item the way every other exception is.
    [Fact]
    public async Task ACancellationPropagatesRatherThanBeingRecordedAsAFailure() {
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        FakeSiteParser parser = new FakeSiteParser { throwOnFetch = new OperationCanceledException() };

        ImportJobItem item = Item();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Runner(parser).RunAsync(OriginalsJob(), item, cancellation.Token)
        );

        Assert.Equal(ImportItemState.Pending, item.state);
    }


    // Retrying a chapter that failed for one reason and lands for another must not leave the earlier
    // reason on the row - a landed item reads as landed, with nothing left over to explain.
    [Fact]
    public async Task ASuccessfulRetryClearsAStatusLeftByAnEarlierFailure() {
        FakeSiteParser parser = new FakeSiteParser { chapter = new ParsedChapter("t", "<p>Text</p>", "https://example.test/1") };
        Chapter created = new Chapter { id = 9, index = 0, title = "x", sourceMarkdown = "x", sourcePlainText = "x" };
        FakeChapterImportService originals = new FakeChapterImportService { result = new ChapterImportResult([new ChapterImportOutcome(0, created, null)]) };

        ImportJobItem item = Item();
        item.state = ImportItemState.Failed;
        item.statusCode = "CHAPTER_FETCH_FAILED";
        item.statusText = "Could not read the page";
        item.statusArgsJson = "{}";

        await Runner(parser, originals).RunAsync(OriginalsJob(), item);

        Assert.Equal(ImportItemState.Imported, item.state);
        Assert.Null(item.statusCode);
        Assert.Null(item.statusText);
        Assert.Null(item.statusArgsJson);
    }


    private sealed class FakeSiteParser : ISiteParser {

        public ParsedChapter? chapter;

        public Exception? throwOnFetch;


        public Task<bool> IsSupportedAsync(string url, CancellationToken cancellationToken = default) {
            return Task.FromResult(true);
        }


        public Task<string?> ParserNameAsync(string url, CancellationToken cancellationToken = default) {
            return Task.FromResult<string?>("fake");
        }


        public Task<ParserDiagnostics> ProbeAsync(CancellationToken cancellationToken = default) {
            return Task.FromResult(new ParserDiagnostics(0, 0, []));
        }


        public Task<IReadOnlyList<ParsedChapterLink>> GetChapterListAsync(string url, CancellationToken cancellationToken = default) {
            return Task.FromResult<IReadOnlyList<ParsedChapterLink>>([]);
        }


        public Task<ParsedChapter> GetChapterAsync(string url, CancellationToken cancellationToken = default) {
            if (throwOnFetch is not null) {
                throw throwOnFetch;
            }

            return Task.FromResult(chapter!);
        }
    }


    private sealed class FakeChapterImportService : IChapterImportService {

        public ChapterImportResult result = new ChapterImportResult([]);

        public IReadOnlyList<ImportedChapter>? lastChapters;

        public bool lastReplaceExisting;


        public Task<ChapterImportResult> ImportAsync(
            long novelId,
            IReadOnlyList<ImportedChapter> chapters,
            bool replaceExisting = false,
            CancellationToken cancellationToken = default
        ) {
            lastChapters = chapters;
            lastReplaceExisting = replaceExisting;

            return Task.FromResult(result);
        }
    }


    private sealed class FakeTranslationImportService : ITranslationImportService {

        public TranslationImportResult result = new TranslationImportResult(1, [], 0, [new TranslationLanding(0, 1, 1, false)]);

        public IReadOnlyList<ImportedTranslation> lastTranslations = [];

        public bool lastCreateMissingChapters;

        public bool lastReplaceExisting;


        public Task<TranslationImportResult> ImportAsync(
            long novelId,
            string language,
            IReadOnlyList<ImportedTranslation> translations,
            bool createMissingChapters = false,
            bool replaceExisting = false,
            CancellationToken cancellationToken = default
        ) {
            lastTranslations = translations;
            lastCreateMissingChapters = createMissingChapters;
            lastReplaceExisting = replaceExisting;

            return Task.FromResult(result);
        }
    }
}
