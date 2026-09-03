using System.Diagnostics;
using NektoTranslate.Common.Models;
using NektoTranslate.Parsing.Services;
using Xunit;


namespace NektoTranslate.Tests.Parsing;


// The limits that keep an import from behaving like an attack.
//
// Worth testing rather than eyeballing: the failure here is invisible locally - everything still
// works, just faster - and shows up as somebody else's server refusing the reader's IP. There is no
// error message to notice, so the behaviour has to be pinned.
public class PageSchedulerTests {

    private static PageScheduler Scheduler(int global, int perHost, int intervalMs) {
        return new PageScheduler(new EngineOptions {
            browser = new BrowserOptions {
                maxConcurrentPages = global,
                perHostConcurrency = perHost,
                minHostIntervalMs = intervalMs,
            },
        });
    }


    // Counts how many pieces of work are in flight at once, and remembers the worst it saw.
    private sealed class Watcher {

        private int current;

        public int peak { get; private set; }


        public async Task<int> RunAsync(int holdMs) {
            int now = Interlocked.Increment(ref current);

            lock (this) {
                peak = Math.Max(peak, now);
            }

            await Task.Delay(holdMs);
            Interlocked.Decrement(ref current);

            return now;
        }
    }


    [Fact]
    public async Task OneSiteIsReadInOrder() {
        using PageScheduler scheduler = Scheduler(global: 8, perHost: 1, intervalMs: 0);
        Watcher watcher = new Watcher();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            scheduler.RunAsync("https://example.invalid/a", () => watcher.RunAsync(40))
        ));

        Assert.Equal(1, watcher.peak);
    }


    // The other half of the same rule, and the reason per-host is not simply a global limit of one:
    // two different sites have no reason to wait for each other.
    [Fact]
    public async Task TwoSitesAreReadAtTheSameTime() {
        using PageScheduler scheduler = Scheduler(global: 8, perHost: 1, intervalMs: 0);
        Watcher watcher = new Watcher();

        await Task.WhenAll(
            scheduler.RunAsync("https://one.invalid/x", () => watcher.RunAsync(120)),
            scheduler.RunAsync("https://two.invalid/x", () => watcher.RunAsync(120))
        );

        Assert.Equal(2, watcher.peak);
    }


    // Tabs cost memory on this machine whatever host they point at, so the global cap has to hold
    // even when every request is to a different site.
    [Fact]
    public async Task TheGlobalCapHoldsAcrossDifferentSites() {
        using PageScheduler scheduler = Scheduler(global: 2, perHost: 1, intervalMs: 0);
        Watcher watcher = new Watcher();

        await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
            scheduler.RunAsync($"https://site{index}.invalid/x", () => watcher.RunAsync(60))
        ));

        Assert.Equal(2, watcher.peak);
    }


    [Fact]
    public async Task ConsecutiveRequestsToOneSiteAreSpacedOut() {
        using PageScheduler scheduler = Scheduler(global: 8, perHost: 1, intervalMs: 150);
        long start = Stopwatch.GetTimestamp();

        for (int i = 0; i < 3; i++) {
            await scheduler.RunAsync("https://slowdown.invalid/x", () => Task.FromResult(0));
        }

        // Three requests, two gaps. The first is free, so the floor is two intervals rather than
        // three - charging the first request would make every single lookup feel broken.
        Assert.True(Stopwatch.GetElapsedTime(start) >= TimeSpan.FromMilliseconds(280));
    }


    // about:blank is how a parser is asked which host it claims. It reaches no server, so pausing
    // before it would add a second to every support check for no one's benefit.
    [Fact]
    public async Task APageThatReachesNoServerIsNotDelayed() {
        using PageScheduler scheduler = Scheduler(global: 8, perHost: 1, intervalMs: 400);
        long start = Stopwatch.GetTimestamp();

        for (int i = 0; i < 3; i++) {
            await scheduler.RunAsync("about:blank", () => Task.FromResult(0));
        }

        Assert.True(Stopwatch.GetElapsedTime(start) < TimeSpan.FromMilliseconds(300));
    }
}
