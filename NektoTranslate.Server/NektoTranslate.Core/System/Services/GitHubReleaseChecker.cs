using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NektoTranslate.System.Contracts;


namespace NektoTranslate.System.Services;


// Asks GitHub's own releases API for the latest published release, at most once every six hours - a
// person's own application does not need to know about a release five minutes after it ships, and
// caching here is what keeps every open window's own update banner from hitting the API on its own
// schedule. Never throws: the browser's update banner and the shell's own updater (which never calls
// this - it reads latest.json instead, see NektoTranslate.Desktop/src-tauri/src/updater.rs) both have
// a path for "could not tell," and a 500 here would be neither.
public class GitHubReleaseChecker(
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubReleaseChecker> logger,
    TimeProvider? timeProvider = null
) : IReleaseChecker {

    private const string ReleasesUrl = "https://api.github.com/repos/VeyDlin/NektoTranslate/releases/latest";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    // One release check ever runs at a time - a burst of requests the moment the cache expires
    // should ask GitHub once, not once per request in flight.
    private readonly SemaphoreSlim gate = new(1, 1);

    private UpdateAvailability? cached;

    private DateTimeOffset cachedAt;

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? tagName,
        [property: JsonPropertyName("html_url")] string? htmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset? publishedAt
    );


    public async Task<UpdateAvailability> CheckAsync(CancellationToken cancellationToken = default) {
        if (IsFresh()) {
            return cached!;
        }

        await gate.WaitAsync(cancellationToken);

        try {
            // Another caller may have refreshed it while this one waited for the gate.
            if (IsFresh()) {
                return cached!;
            }

            UpdateAvailability result = await FetchAsync(cancellationToken);

            cached = result;
            cachedAt = clock.GetUtcNow();

            return result;
        } finally {
            gate.Release();
        }
    }


    // Strips a `+<build metadata>` suffix (a git sha, once CI stamps one - see HealthResponse) before
    // parsing: System.Version has no notion of it and throws on the raw string. Public, and static,
    // so the comparison the contract asks to be unit-tested is reachable without a fake HTTP handler
    // for the cases that have nothing to do with the network at all.
    public static string CoreVersion(string informational) {
        int at = informational.IndexOf('+');

        return at < 0 ? informational : informational[..at];
    }


    // False, never an exception, for a tag or a running version this cannot parse as three dotted
    // numbers - "vNext" or some other non-numeric tag is not this application's own release scheme,
    // and a check that could not make sense of the answer should not crash a page over it.
    public static bool TryIsNewer(string latestTag, string currentCoreVersion, out string? latest) {
        latest = latestTag.TrimStart('v');

        if (!Version.TryParse(latest, out Version? latestVersion)) {
            return false;
        }

        if (!Version.TryParse(currentCoreVersion, out Version? currentVersion)) {
            return false;
        }

        return latestVersion > currentVersion;
    }


    private bool IsFresh() {
        return cached is not null && clock.GetUtcNow() - cachedAt < CacheDuration;
    }


    private async Task<UpdateAvailability> FetchAsync(CancellationToken cancellationToken) {
        string current = CoreVersion(CurrentInformationalVersion());

        try {
            HttpClient client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NektoTranslate-update-check");

            GitHubRelease? release = await client.GetFromJsonAsync<GitHubRelease>(ReleasesUrl, cancellationToken);

            if (release?.tagName is not { Length: > 0 } tag) {
                return Unreachable(current);
            }

            bool isNewer = TryIsNewer(tag, current, out string? latest);

            return new UpdateAvailability(current, latest, isNewer, release.htmlUrl, release.publishedAt, true);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // The client's own 10s timeout, not the caller asking to stop - GitHub being slow is not
            // this application's problem to surface as anything more than "could not tell."
            return Unreachable(current);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception error) {
            logger.LogWarning(error, "Could not check {Url} for a newer release", ReleasesUrl);

            return Unreachable(current);
        }
    }


    private static UpdateAvailability Unreachable(string current) {
        return new UpdateAvailability(current, null, false, null, null, false);
    }


    private static string CurrentInformationalVersion() {
        return typeof(GitHubReleaseChecker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? "0.0.0";
    }
}
