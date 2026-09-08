using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Entities;
using NektoTranslate.Parsing.Enums;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Parsing.Services;


public interface IListingReader {

    // Null when a read for this book and kind is already under way. The caller reports that rather
    // than starting a second visit to the same site behind the first.
    Task<SourceListingView?> StartAsync(
        long novelId,
        ImportKind kind,
        string url,
        CancellationToken cancellationToken = default
    );


    bool Cancel(long novelId, ImportKind kind);
}


// Reads a site's contents in the background and keeps the result.
//
// Deliberately not a job on the import queue. An import is many page loads with an outcome each,
// which is what earns pausing, resuming and a per-chapter report; reading a contents page is one
// visit with one answer. What it does share with a job is the part the user actually needs: it is
// persisted while it runs, it is visible from any screen, it cannot be started twice, and it can be
// abandoned.
public class ListingReader(
    IServiceScopeFactory scopes,
    ILogger<ListingReader> logger
) : IListingReader {

    private readonly ConcurrentDictionary<(long, ImportKind), CancellationTokenSource> running = new();


    public async Task<SourceListingView?> StartAsync(
        long novelId,
        ImportKind kind,
        string url,
        CancellationToken cancellationToken = default
    ) {
        CancellationTokenSource cancellation = new CancellationTokenSource();

        if (!running.TryAdd((novelId, kind), cancellation)) {
            cancellation.Dispose();

            return null;
        }

        SourceListingView view;

        try {
            using IServiceScope scope = scopes.CreateScope();
            NektoDbContext database = scope.ServiceProvider.GetRequiredService<NektoDbContext>();
            IListingAnnotator annotator = scope.ServiceProvider.GetRequiredService<IListingAnnotator>();

            SourceListing listing = await FindAsync(database, novelId, kind, cancellationToken)
                ?? Add(database, novelId, kind);

            // Entries survive a re-read of the same address, and only that. A read that comes back
            // with nothing - a slow page, a site that changed - would otherwise destroy a list that
            // was fine a minute ago, and the user would have no way back to it. A different address
            // is a different book's contents, so those entries do go, and so does what was new about
            // them - that answer belonged to the address that just left.
            if (!string.Equals(listing.url, url, StringComparison.Ordinal)) {
                listing.entriesJson = null;
                listing.entryCount = 0;
                listing.readAt = null;
                listing.newEntriesJson = null;
            }

            listing.url = url;
            listing.state = ListingState.Reading;
            listing.startedAt = DateTimeOffset.UtcNow;
            ClearStatus(listing);

            // The book learns where it came from here, from the address the originals are actually
            // read at, rather than asking for it a second time on the creation form. Only ever
            // filled in, never overwritten: a person may have set it by hand in the book's settings.
            if (kind == ImportKind.Originals) {
                Novel? novel = await database.novels.FirstOrDefaultAsync(
                    candidate => candidate.id == novelId,
                    cancellationToken
                );

                if (novel is not null && string.IsNullOrWhiteSpace(novel.sourceUrl)) {
                    novel.sourceUrl = url;
                }
            }

            await database.SaveChangesAsync(cancellationToken);

            IReadOnlyList<ListingEntryView> entries = await annotator.AnnotateAsync(listing, cancellationToken);
            view = SourceListingView.Of(listing, entries);
        }
        catch {
            Release(novelId, kind);

            throw;
        }

        // Deliberately not awaited: the request answers as soon as the read is recorded, and the
        // screen follows it from the persisted state and the hub rather than by holding a request
        // open for a minute.
        _ = Task.Run(() => RunAsync(novelId, kind, url, cancellation.Token), CancellationToken.None);

        return view;
    }


    public bool Cancel(long novelId, ImportKind kind) {
        if (!running.TryGetValue((novelId, kind), out CancellationTokenSource? cancellation)) {
            return false;
        }

        cancellation.Cancel();

        return true;
    }


    private async Task RunAsync(long novelId, ImportKind kind, string url, CancellationToken cancellationToken) {
        using IServiceScope scope = scopes.CreateScope();

        NektoDbContext database = scope.ServiceProvider.GetRequiredService<NektoDbContext>();
        ISiteParser parser = scope.ServiceProvider.GetRequiredService<ISiteParser>();
        ITranslationNotifier notifier = scope.ServiceProvider.GetRequiredService<ITranslationNotifier>();

        SourceListing? listing = await FindAsync(database, novelId, kind, CancellationToken.None);

        if (listing is null) {
            Release(novelId, kind);

            return;
        }

        // Read before anything below overwrites it: this is the only place that still knows what the
        // previous successful read of this same address found, and "new since last time" has nothing
        // else to compare against. Null here means this address has never been read before, or was
        // just reset because the address changed - either way, calling every entry new would only be
        // noise, not news.
        string? previousEntriesJson = listing.entriesJson;

        try {
            IReadOnlyList<ParsedChapterLink> entries = await parser.GetChapterListAsync(url, cancellationToken);

            // A parser that claimed the site and found nothing on it is a failure, however calmly it
            // returned. Recording it as a successful read of zero chapters produces the one thing
            // this whole screen exists to avoid: a state that reports completion and leaves the user
            // with nothing to do and no reason given.
            if (entries.Count == 0) {
                listing.state = ListingState.Failed;
                Record(listing, Statuses.ListingFoundNoEntries.With(("url", url)));
            }
            else {
                listing.entriesJson = JsonSerializer.Serialize(entries);
                listing.entryCount = entries.Count;
                listing.state = ListingState.Ready;
                listing.readAt = DateTimeOffset.UtcNow;
                listing.newEntriesJson = JsonSerializer.Serialize(NewSourceUrls(previousEntriesJson, entries));
                ClearStatus(listing);
            }
        }
        catch (OperationCanceledException) {
            listing.state = ListingState.Failed;
            Record(listing, Statuses.ImportCancelled);
        }
        catch (Exception failure) {
            logger.LogWarning(failure, "Could not read the contents of {Url}", url);

            listing.state = ListingState.Failed;
            Record(listing, await DescribeAsync(parser, url, failure));
        }
        finally {
            Release(novelId, kind);
        }

        await database.SaveChangesAsync(CancellationToken.None);
        await notifier.ListingStateChangedAsync(novelId, kind, listing.state, listing.entryCount);
    }


    // The two answers worth telling apart. A site nothing can read is a dead end the user has to act
    // on; anything else is a page that did not come back this time and may next time.
    private static async Task<Status> DescribeAsync(ISiteParser parser, string url, Exception failure) {
        try {
            if (await parser.ParserNameAsync(url, CancellationToken.None) is null) {
                return Statuses.NoParserForSite.With(("url", url));
            }
        }
        catch {
            // Deciding which failure to report must not fail in turn.
        }

        return Statuses.ChapterFetchFailed.With(("url", url), ("reason", failure.Message));
    }


    private static Task<SourceListing?> FindAsync(
        NektoDbContext database,
        long novelId,
        ImportKind kind,
        CancellationToken cancellationToken
    ) {
        return database.sourceListings.FirstOrDefaultAsync(
            listing => listing.novelId == novelId && listing.kind == kind,
            cancellationToken
        );
    }


    private static SourceListing Add(NektoDbContext database, long novelId, ImportKind kind) {
        SourceListing listing = new SourceListing {
            novelId = novelId,
            kind = kind,
            url = string.Empty
        };

        database.sourceListings.Add(listing);

        return listing;
    }


    private static void Record(SourceListing listing, Status status) {
        listing.statusCode = status.code;
        listing.statusText = status.text;
        listing.statusArgsJson = IssueStatus.ArgsOf(status);
    }


    private static void ClearStatus(SourceListing listing) {
        listing.statusCode = null;
        listing.statusText = null;
        listing.statusArgsJson = null;
    }


    // Null previousEntriesJson means there is nothing to compare against - the first read of an
    // address, or the one right after it changed - and the answer is deliberately empty rather than
    // "everything", which a plain diff against nothing would otherwise produce.
    private static List<string> NewSourceUrls(string? previousEntriesJson, IReadOnlyList<ParsedChapterLink> entries) {
        if (previousEntriesJson is null) {
            return [];
        }

        HashSet<string> previous = DecodeSourceUrls(previousEntriesJson);

        return entries
            .Select(entry => entry.sourceUrl)
            .Where(sourceUrl => !previous.Contains(sourceUrl))
            .ToList();
    }


    private static HashSet<string> DecodeSourceUrls(string entriesJson) {
        try {
            List<ParsedChapterLink>? decoded = JsonSerializer.Deserialize<List<ParsedChapterLink>>(entriesJson);

            return decoded is null ? [] : decoded.Select(entry => entry.sourceUrl).ToHashSet();
        }
        catch (JsonException) {
            return [];
        }
    }


    private void Release(long novelId, ImportKind kind) {
        if (running.TryRemove((novelId, kind), out CancellationTokenSource? cancellation)) {
            cancellation.Dispose();
        }
    }
}
