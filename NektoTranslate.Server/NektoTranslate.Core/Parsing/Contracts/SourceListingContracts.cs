using System.Text.Json;
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
    IReadOnlyList<ParsedChapterLink> entries,
    Status? error,
    DateTimeOffset startedAt,
    DateTimeOffset? readAt
) {

    public static SourceListingView Of(SourceListing listing) {
        return new SourceListingView(
            listing.kind,
            listing.url,
            listing.state,
            Entries(listing.entriesJson),
            listing.statusCode is null
                ? null
                : IssueStatus.Rebuild(listing.statusCode, listing.statusText ?? string.Empty, listing.statusArgsJson),
            listing.startedAt,
            listing.readAt
        );
    }


    // A listing written by an older build, or edited by hand, is not worth failing the screen over:
    // an unreadable column reads as no entries, which the interface already knows how to show.
    private static IReadOnlyList<ParsedChapterLink> Entries(string? entriesJson) {
        if (string.IsNullOrWhiteSpace(entriesJson)) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<List<ParsedChapterLink>>(entriesJson) ?? [];
        }
        catch (JsonException) {
            return [];
        }
    }
}


public sealed record ReadListingRequest(string url);
