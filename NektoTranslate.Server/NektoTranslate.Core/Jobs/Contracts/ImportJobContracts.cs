using NektoTranslate.Common.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Jobs.Contracts;


// Takes the chapters the user chose, never a contents URL. A novel with two thousand entries must
// not be pulled down wholesale because someone pasted a link.
public sealed record StartImportJobRequest(
    ImportKind kind,
    IReadOnlyList<ParsedChapterLink> chapters,
    // Translation imports only.
    string? language = null,
    // Which chapter the first entry lands on, for either kind: an original is now matched by
    // address and takes an explicit index the same way a translation always has.
    int startAtChapterIndex = 0,
    // Translation imports only: make a chapter with no original for any entry that has nowhere to
    // land. This is what lets a book exist as somebody else's translation and nothing more.
    bool createMissingChapters = false
);


public sealed partial record ImportJobItemView(
    int position,
    string sourceUrl,
    string title,
    int? chapterIndex,
    long? chapterId,
    ImportItemState state,
    Status? status,
    DateTimeOffset? finishedAt
);


public sealed record ImportJobView(
    long id,
    long novelId,
    ImportKind kind,
    string? language,
    int startAtChapterIndex,
    JobState state,
    int processedCount,
    int totalCount,
    string? currentTitle,
    DateTimeOffset createdAt,
    DateTimeOffset? startedAt,
    DateTimeOffset? finishedAt,
    string? error,
    // Null when the caller asked for the list without its rows; an empty list means the run had
    // nothing in it.
    IReadOnlyList<ImportJobItemView>? items
) {

    public static ImportJobView Of(ImportJob job, bool withItems) {
        return new ImportJobView(
            job.id,
            job.novelId,
            job.kind,
            job.language,
            job.startAtChapterIndex,
            job.state,
            job.processedCount,
            job.totalCount,
            job.currentTitle,
            job.createdAt,
            job.startedAt,
            job.finishedAt,
            job.error,
            withItems ? job.items.OrderBy(item => item.position).Select(ImportJobItemView.Of).ToList() : null
        );
    }
}


public sealed partial record ImportJobItemView {

    public static ImportJobItemView Of(ImportJobItem item) {
        return new ImportJobItemView(
            item.position,
            item.sourceUrl,
            item.title,
            item.chapterIndex,
            item.chapterId,
            item.state,
            item.statusCode is null
                ? null
                : IssueStatus.Rebuild(item.statusCode, item.statusText ?? string.Empty, item.statusArgsJson),
            item.finishedAt
        );
    }
}


public enum ImportItemRetryResult {

    Retried = 0,

    JobNotFound = 1,

    ItemNotFound = 2,

    // The run itself is still going. Retrying one of its items would race the worker for that very
    // item, and the only honest answer is to wait for the run to settle.
    JobStillActive = 3,

    // Nothing to redo: the item never failed, so re-running it could only duplicate what already
    // landed.
    ItemNotFailed = 4
}


public sealed record ImportItemRetryOutcome(
    ImportItemRetryResult result,
    ImportJobItemView? item = null
);
