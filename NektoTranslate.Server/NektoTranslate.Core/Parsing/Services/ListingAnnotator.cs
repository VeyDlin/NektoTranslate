using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Entities;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Parsing.Services;


public interface IListingAnnotator {

    Task<IReadOnlyList<ListingEntryView>> AnnotateAsync(SourceListing listing, CancellationToken cancellationToken = default);
}


// Turns a listing's flat, stored entries into what the screen actually asks about them: is this
// address already in the book, did the last attempt at it fail and why, is it new since the last
// read. None of that is stored on the entry itself - it is computed fresh every time, because it
// changes every time an import runs without the listing being read again, and an answer cached from
// the last read would go stale the moment a chapter lands or fails.
public class ListingAnnotator(NektoDbContext database) : IListingAnnotator {

    public async Task<IReadOnlyList<ListingEntryView>> AnnotateAsync(
        SourceListing listing,
        CancellationToken cancellationToken = default
    ) {
        IReadOnlyList<ParsedChapterLink> entries = DecodeEntries(listing.entriesJson);

        if (entries.Count == 0) {
            return [];
        }

        // Three queries for the whole listing, never one per entry: a book with a thousand chapters
        // must not turn "open the import screen" into a thousand round trips.
        Dictionary<string, (long chapterId, int chapterIndex)> imported = listing.kind == ImportKind.Originals
            ? await OriginalsImportedAsync(listing.novelId, cancellationToken)
            : await TranslationsImportedAsync(listing.novelId, cancellationToken);

        Dictionary<string, Status> failures = await MostRecentFailuresAsync(listing.novelId, listing.kind, cancellationToken);
        HashSet<string> newSourceUrls = DecodeSourceUrls(listing.newEntriesJson);

        List<ListingEntryView> annotated = new(entries.Count);

        foreach (ParsedChapterLink entry in entries) {
            if (imported.TryGetValue(entry.sourceUrl, out (long chapterId, int chapterIndex) landed)) {
                annotated.Add(new ListingEntryView(
                    entry.sourceUrl,
                    entry.title,
                    ListingEntryState.Imported,
                    landed.chapterIndex,
                    landed.chapterId,
                    null,
                    false
                ));

                continue;
            }

            bool isNew = newSourceUrls.Contains(entry.sourceUrl);

            // An entry that later imported fine is Imported above, whatever an older failed attempt
            // at the same address still says - this only runs for one still sitting where it failed.
            if (failures.TryGetValue(entry.sourceUrl, out Status? error)) {
                annotated.Add(new ListingEntryView(entry.sourceUrl, entry.title, ListingEntryState.Failed, null, null, error, isNew));

                continue;
            }

            annotated.Add(new ListingEntryView(entry.sourceUrl, entry.title, ListingEntryState.NotImported, null, null, null, isNew));
        }

        return annotated;
    }


    private async Task<Dictionary<string, (long chapterId, int chapterIndex)>> OriginalsImportedAsync(
        long novelId,
        CancellationToken cancellationToken
    ) {
        var rows = await database.chapters
            .Where(chapter => chapter.novelId == novelId && chapter.sourceUrl != null)
            .Select(chapter => new { sourceUrl = chapter.sourceUrl!, chapter.id, chapter.index })
            .ToListAsync(cancellationToken);

        Dictionary<string, (long chapterId, int chapterIndex)> bySourceUrl = new();

        foreach (var row in rows) {
            bySourceUrl[row.sourceUrl] = (row.id, row.index);
        }

        return bySourceUrl;
    }


    private async Task<Dictionary<string, (long chapterId, int chapterIndex)>> TranslationsImportedAsync(
        long novelId,
        CancellationToken cancellationToken
    ) {
        var rows = await database.chapterTranslations
            .Where(translation => translation.chapter!.novelId == novelId
                && translation.sourceUrl != null
                && translation.language == translation.chapter!.novel!.targetLanguage)
            .Select(translation => new { sourceUrl = translation.sourceUrl!, chapterId = translation.chapterId, chapterIndex = translation.chapter!.index })
            .ToListAsync(cancellationToken);

        Dictionary<string, (long chapterId, int chapterIndex)> bySourceUrl = new();

        foreach (var row in rows) {
            bySourceUrl[row.sourceUrl] = (row.chapterId, row.chapterIndex);
        }

        return bySourceUrl;
    }


    // The row this reads is chosen per address, most recent first, and then kept only if nothing
    // newer for that address exists - a page that failed twice must show the second reason, not the
    // first.
    private async Task<Dictionary<string, Status>> MostRecentFailuresAsync(
        long novelId,
        ImportKind kind,
        CancellationToken cancellationToken
    ) {
        List<ImportJobItem> failed = await database.importJobItems
            .Where(item => item.job!.novelId == novelId && item.job!.kind == kind && item.state == ImportItemState.Failed)
            .OrderByDescending(item => item.finishedAt)
            .ToListAsync(cancellationToken);

        Dictionary<string, Status> bySourceUrl = new();

        foreach (ImportJobItem item in failed) {
            if (item.statusCode is not null) {
                bySourceUrl.TryAdd(item.sourceUrl, IssueStatus.Rebuild(item.statusCode, item.statusText ?? string.Empty, item.statusArgsJson));
            }
        }

        return bySourceUrl;
    }


    // A listing written by an older build, or edited by hand, is not worth failing the screen over:
    // an unreadable column reads as no entries, which the interface already knows how to show.
    private static IReadOnlyList<ParsedChapterLink> DecodeEntries(string? entriesJson) {
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


    private static HashSet<string> DecodeSourceUrls(string? sourceUrlsJson) {
        if (string.IsNullOrWhiteSpace(sourceUrlsJson)) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<List<string>>(sourceUrlsJson)?.ToHashSet() ?? [];
        }
        catch (JsonException) {
            return [];
        }
    }
}
