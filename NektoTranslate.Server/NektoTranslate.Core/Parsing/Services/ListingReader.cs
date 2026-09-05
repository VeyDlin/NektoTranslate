using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Enums;
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

            SourceListing listing = await FindAsync(database, novelId, kind, cancellationToken)
                ?? Add(database, novelId, kind);

            listing.url = url;
            listing.state = ListingState.Reading;
            listing.entriesJson = null;
            listing.entryCount = 0;
            listing.startedAt = DateTimeOffset.UtcNow;
            listing.readAt = null;
            ClearStatus(listing);

            await database.SaveChangesAsync(cancellationToken);

            view = SourceListingView.Of(listing);
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

        try {
            IReadOnlyList<ParsedChapterLink> entries = await parser.GetChapterListAsync(url, cancellationToken);

            listing.entriesJson = JsonSerializer.Serialize(entries);
            listing.entryCount = entries.Count;
            listing.state = ListingState.Ready;
            listing.readAt = DateTimeOffset.UtcNow;
            ClearStatus(listing);
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


    private void Release(long novelId, ImportKind kind) {
        if (running.TryRemove((novelId, kind), out CancellationTokenSource? cancellation)) {
            cancellation.Dispose();
        }
    }
}
