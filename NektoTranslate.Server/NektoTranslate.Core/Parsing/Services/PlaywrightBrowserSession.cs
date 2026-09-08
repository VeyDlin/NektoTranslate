using Microsoft.Extensions.Logging;
using Microsoft.Playwright;


namespace NektoTranslate.Parsing.Services;


public interface IBrowserSession {

    Task<IBrowserContext> NewContextAsync(CancellationToken cancellationToken = default);


    // Best-effort persistence, not a transaction: a second context saving around the same moment
    // can overwrite what this one wrote. Acceptable here, because all that rides on the file is a
    // cookie wall or a login surviving a restart, never the correctness of a translation.
    Task SaveStorageStateAsync(IBrowserContext context, CancellationToken cancellationToken = default);
}


// Owns one headless browser for the life of the application.
//
// A browser is needed regardless: a large share of the sites these parsers target render their
// chapter lists with scripts, so fetching the HTML would return an empty shell. Since a real
// browser has to be running anyway, the parser scripts are executed inside it rather than against
// an emulated DOM - they were written for a browser, and giving them one removes an entire class
// of "this parser mysteriously fails" bugs.
//
// Launching is expensive and the browser is stateless between novels, so it is created once and
// shared; each parse gets its own context, which isolates cookies and storage.
public sealed class PlaywrightBrowserSession(
    string browsersDirectory,
    string browserStatePath,
    ILogger<PlaywrightBrowserSession> logger
) : IBrowserSession, IAsyncDisposable {

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    private IPlaywright? playwright;

    private IBrowser? browser;


    public async Task<IBrowserContext> NewContextAsync(CancellationToken cancellationToken = default) {
        IBrowser running = await EnsureBrowserAsync(cancellationToken);

        BrowserNewContextOptions options = new BrowserNewContextOptions {
            // Sites that gate on a real browser signature turn away the default automation string.
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                + "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            IgnoreHTTPSErrors = true
        };

        // Whatever an earlier context saved - a login, a cookie wall the site cleared for a
        // returning visitor - is what this one starts from, rather than nothing.
        if (File.Exists(browserStatePath)) {
            options.StorageStatePath = browserStatePath;
        }

        return await running.NewContextAsync(options);
    }


    public async Task SaveStorageStateAsync(IBrowserContext context, CancellationToken cancellationToken = default) {
        await context.StorageStateAsync(new BrowserContextStorageStateOptions {
            Path = browserStatePath
        });
    }


    public async ValueTask DisposeAsync() {
        if (browser is not null) {
            await browser.CloseAsync();
        }

        playwright?.Dispose();
        gate.Dispose();
    }


    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken) {
        if (browser is not null) {
            return browser;
        }

        await gate.WaitAsync(cancellationToken);

        try {
            if (browser is not null) {
                return browser;
            }

            // Playwright reads this from the environment once, when the driver it launches starts
            // up inside CreateAsync below - so it has to be set before that call, every time,
            // rather than assumed to already be set by whoever started the process.
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browsersDirectory);

            EnsureChromiumInstalled();

            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions {
                Headless = true
            });

            return browser;
        } finally {
            gate.Release();
        }
    }


    // Runs once, the first time this installation ever needs a browser: the ordinary case after
    // that is an empty check against a folder that already has Chromium in it.
    private void EnsureChromiumInstalled() {
        bool alreadyInstalled = Directory.Exists(browsersDirectory)
            && Directory.EnumerateDirectories(browsersDirectory, "chromium*").Any();

        if (alreadyInstalled) {
            return;
        }

        logger.LogInformation("Installing Chromium for the site parsers into {Directory}", browsersDirectory);

        int exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);

        if (exitCode != 0) {
            // The same failure a broken launch would report today, just earlier and with
            // something a person can actually do about it.
            throw new InvalidOperationException(
                $"Playwright's Chromium install exited with code {exitCode}. Run "
                + "'npx playwright install chromium' by hand from the application's folder, or "
                + "check that this process can reach the download."
            );
        }

        logger.LogInformation("Chromium installed");
    }
}
