using Microsoft.Extensions.Logging;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Services;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Jobs.Services;


public interface IImportItemRunner {

    Task RunAsync(ImportJob job, ImportJobItem item, CancellationToken cancellationToken = default);
}


// One chapter, fetched and imported exactly the way a run does it.
//
// This is the only door either the sequential run or a retry of one failed item goes through. Both
// need the same three decisions - which import a job's kind calls for, what counts as landed versus
// rejected, and how a thrown exception reads as a status a person can act on - and a second copy of
// them here is a second place for those to quietly disagree with the run.
//
// Deliberately does not touch the database or the notifier: a run updates job-level bookkeeping
// (processedCount, currentTitle) around this call that a lone retry has no business touching, so the
// caller owns saving and publishing. This only decides the outcome and writes it onto the item.
public class ImportItemRunner(
    ISiteParser parser,
    IChapterImportService originals,
    ITranslationImportService translations,
    ILogger<ImportItemRunner> logger
) : IImportItemRunner {

    public async Task RunAsync(ImportJob job, ImportJobItem item, CancellationToken cancellationToken = default) {
        try {
            ParsedChapter chapter = await parser.GetChapterAsync(item.sourceUrl, cancellationToken);
            string title = string.IsNullOrWhiteSpace(item.title) ? chapter.title : item.title;

            if (job.kind == ImportKind.Originals) {
                int index = job.startAtChapterIndex + item.position;

                ChapterImportResult result = await originals.ImportAsync(
                    job.novelId,
                    [new ImportedChapter(title, chapter.html, index, item.sourceUrl)],
                    cancellationToken
                );

                ChapterImportOutcome outcome = result.outcomes[0];

                if (outcome.rejection is null) {
                    item.chapterId = outcome.chapter!.id;
                    item.chapterIndex = outcome.chapter.index;
                    item.state = ImportItemState.Imported;
                    ClearStatus(item);
                }
                else {
                    // The one rejection that is not a failure: the address is already in the book, so
                    // the item points at where it actually lives rather than at the slot this run
                    // aimed for. An occupied index, by contrast, wrote nothing - there is nowhere for
                    // the item to point.
                    bool alreadyImported = outcome.rejection.code == Statuses.ChapterAlreadyImported.code;

                    if (alreadyImported) {
                        item.chapterId = outcome.chapter!.id;
                        item.chapterIndex = outcome.chapter.index;
                    }

                    item.state = alreadyImported ? ImportItemState.Skipped : ImportItemState.Failed;
                    Record(item, outcome.rejection);
                }
            }
            else {
                int chapterIndex = job.startAtChapterIndex + item.position;

                // The title travels with the entry because a chapter created for a translation that
                // has no original has nowhere else to get one, and the job's own answer about
                // creating those chapters has to hold for a retry as much as for the original run.
                TranslationImportResult result = await translations.ImportAsync(
                    job.novelId,
                    job.language ?? string.Empty,
                    [new ImportedTranslation(chapterIndex, chapter.html, item.sourceUrl, title)],
                    job.createMissingChapters,
                    cancellationToken
                );

                item.chapterIndex = chapterIndex;

                if (result.imported == 1) {
                    item.state = ImportItemState.Imported;
                    ClearStatus(item);
                }
                else {
                    Status reason = result.rejected.Count > 0 ? result.rejected[0].reason : Statuses.ImportedTextEmpty;
                    bool skipped = reason.code == Statuses.TranslationAlreadyExists.code;

                    item.state = skipped ? ImportItemState.Skipped : ImportItemState.Failed;
                    Record(item, reason);
                }
            }
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception failure) {
            logger.LogWarning(failure, "Could not import {Url}", item.sourceUrl);
            item.state = ImportItemState.Failed;
            Record(item, Describe(item.sourceUrl, failure));
        }
    }


    // The runner speaks in exceptions whose messages were written for a log. What the user needs is
    // which of the few things that go wrong went wrong, without a JavaScript stack after it.
    private static Status Describe(string url, Exception failure) {
        string message = failure.Message.ReplaceLineEndings("\n").Split('\n')[0].Trim();

        if (message.StartsWith("Error: ", StringComparison.Ordinal)) {
            message = message["Error: ".Length..];
        }

        if (message.Contains("found no content", StringComparison.OrdinalIgnoreCase)) {
            return Statuses.ParserFoundNoContent.With(("url", url));
        }

        if (message.Contains("No parser is registered", StringComparison.OrdinalIgnoreCase)) {
            return Statuses.NoParserForSite.With(("url", url));
        }

        return Statuses.ChapterFetchFailed.With(("url", url), ("reason", message.Length > 200 ? message[..200] : message));
    }


    private static void Record(ImportJobItem item, Status status) {
        item.statusCode = status.code;
        item.statusText = status.text;
        item.statusArgsJson = IssueStatus.ArgsOf(status);
    }


    // A retry landing a chapter that had previously failed leaves the earlier reason on the row
    // unless something clears it - a fresh Pending item never had one to begin with.
    private static void ClearStatus(ImportJobItem item) {
        item.statusCode = null;
        item.statusText = null;
        item.statusArgsJson = null;
    }
}
