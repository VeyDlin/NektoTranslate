using NektoTranslate.Common.Contracts;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Parsing.Entities;
using NektoTranslate.Parsing.Enums;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Parsing.Contracts;


// What the import screens read back: the state of the read, and the entries once there are any.
//
// The entries are sent decoded rather than as the stored JSON string, for the same reason every
// other view in this application decodes its storage shape - a screen that has to re-parse a column
// is a screen that will one day forget to.
public sealed record SourceListingView(
    ImportKind kind,
    string url,
    ListingState state,
    IReadOnlyList<ListingEntryView> entries,
    Status? error,
    DateTimeOffset startedAt,
    DateTimeOffset? readAt
) {

    // Entries are not decoded here: they are IListingAnnotator's answer, not a plain reading of the
    // stored column, because whether one is already in the book or failed last time is not something
    // this row knows by itself. The caller reads that answer once and hands it in.
    public static SourceListingView Of(SourceListing listing, IReadOnlyList<ListingEntryView> entries) {
        return new SourceListingView(
            listing.kind,
            listing.url,
            listing.state,
            entries,
            listing.statusCode is null
                ? null
                : IssueStatus.Rebuild(listing.statusCode, listing.statusText ?? string.Empty, listing.statusArgsJson),
            listing.startedAt,
            listing.readAt
        );
    }
}


public sealed record ReadListingRequest(string url);


// Where one entry of a listing stands relative to the book it was read for.
public enum ListingEntryState {

    NotImported = 0,

    Imported = 1,

    Failed = 2
}


// One entry of a site's contents, as it relates to this book rather than as the site described it.
//
// A listing is read the same way whether the book already has every one of these chapters or none
// of them; this is what turns that flat list into something that says what pressing "import" would
// actually do.
public sealed record ListingEntryView(
    string sourceUrl,
    string title,
    ListingEntryState state,

    // Set when state is Imported: the chapter this address is already in the book as.
    int? chapterIndex,
    long? chapterId,

    // Set when state is Failed: why, from the most recent import attempt at this address.
    Status? error,

    // Appeared in the latest read of this listing and not in the read before it. Never true together
    // with Imported - a chapter already in the book is not news, whichever read first found it.
    bool isNew
);
