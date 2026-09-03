using Microsoft.Playwright;


namespace NektoTranslate.Parsing.Services;


public interface IBrowserSession {

    Task<IBrowserContext> NewContextAsync(CancellationToken cancellationToken = default);
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
public sealed class PlaywrightBrowserSession : IBrowserSession, IAsyncDisposable {

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    private IPlaywright? playwright;

    private IBrowser? browser;


    public async Task<IBrowserContext> NewContextAsync(CancellationToken cancellationToken = default) {
        IBrowser running = await EnsureBrowserAsync(cancellationToken);

        return await running.NewContextAsync(new BrowserNewContextOptions {
            // Sites that gate on a real browser signature turn away the default automation string.
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                + "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            IgnoreHTTPSErrors = true
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

            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions {
                Headless = true
            });

            return browser;
        } finally {
            gate.Release();
        }
    }
}
