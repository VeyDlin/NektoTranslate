using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Settings.Services;


namespace NektoTranslate.Parsing.Services;


public interface ISiteParser {

    Task<bool> IsSupportedAsync(string url, CancellationToken cancellationToken = default);


    Task<string?> ParserNameAsync(string url, CancellationToken cancellationToken = default);


    // What actually evaluated. Loading no site of its own, so it answers the "are the parsers
    // working at all" question without depending on anyone's server being up.
    Task<ParserDiagnostics> ProbeAsync(CancellationToken cancellationToken = default);


    Task<IReadOnlyList<ParsedChapterLink>> GetChapterListAsync(
        string url,
        CancellationToken cancellationToken = default
    );


    Task<ParsedChapter> GetChapterAsync(string url, CancellationToken cancellationToken = default);
}


// Runs the vendored site parsers inside a real browser page.
//
// The scripts are evaluated against the page that was actually loaded, so `document` is the live
// DOM, `fetch` is the browser's, and promises are real. Nothing here emulates a browser API, which
// is the whole reason this approach was chosen over a JavaScript engine hosted in .NET: the parsers
// lean on querySelector, textContent, async/await and fetch several thousand times between them,
// and every emulated corner would be a parser that fails for reasons no one can see.
public class WebToEpubRunner(
    IBrowserSession session,
    IParserScriptStore scripts,
    ISettingsService settings,
    IPageScheduler scheduler,
    ILogger<WebToEpubRunner> logger
) : ISiteParser {

    private const string BlankPage = "about:blank";


    // Answered without visiting the site: routing to a parser depends only on the host name, so
    // asking "can you read this site" should not require the site to be reachable, or cost the user
    // a page load before they have committed to anything.
    public async Task<bool> IsSupportedAsync(string url, CancellationToken cancellationToken = default) {
        return await ParserNameAsync(url, cancellationToken) is not null;
    }


    // Returns which parser claims the URL, or null. More useful than a yes/no: the interface can
    // tell the user *what* will read the site, and a wrong match becomes visible instead of hiding
    // behind a boolean.
    public async Task<string?> ParserNameAsync(string url, CancellationToken cancellationToken = default) {
        string name = await WithPageAsync(BlankPage, async page => {
            return await page.EvaluateAsync<string>(ParserNameScript, url) ?? string.Empty;
        }, cancellationToken);

        return name.Length == 0 ? null : name;
    }


    public async Task<ParserDiagnostics> ProbeAsync(CancellationToken cancellationToken = default) {
        await using IBrowserContext context = await session.NewContextAsync(cancellationToken);
        IPage page = await context.NewPageAsync();

        await page.GotoAsync(BlankPage);

        return await InjectAsync(page);
    }


    public async Task<IReadOnlyList<ParsedChapterLink>> GetChapterListAsync(
        string url,
        CancellationToken cancellationToken = default
    ) {
        string json = await WithPageAsync(url, async page => {
            return await page.EvaluateAsync<string>(ChapterListScript, url) ?? "[]";
        }, cancellationToken);

        return Deserialize<List<ParsedChapterLink>>(json) ?? [];
    }


    public async Task<ParsedChapter> GetChapterAsync(string url, CancellationToken cancellationToken = default) {
        string json = await WithPageAsync(url, async page => {
            return await page.EvaluateAsync<string>(ChapterScript, url) ?? "null";
        }, cancellationToken);

        return Deserialize<ParsedChapter>(json)
            ?? throw new InvalidOperationException($"The parser returned nothing for {url}");
    }


    // Results cross the boundary as a JSON string rather than as a mapped object. Playwright's own
    // conversion handles primitives, and quietly refuses anything shaped like a record - so the
    // page serializes, and deserialization happens here where the contract is known.
    private static T? Deserialize<T>(string json) {
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });
    }


    private async Task<T> WithPageAsync<T>(
        string url,
        Func<IPage, Task<T>> work,
        CancellationToken cancellationToken
    ) {
        // A setting rather than a constant: novel sites vary enormously in how long they take, and
        // the trade is entirely the user's - a longer wait rescues a slow site, a shorter one stops
        // an import hanging on a dead one.
        int timeoutMs = (await settings.GetAsync(cancellationToken)).pageLoadTimeoutMs;

        // Everything that opens a tab goes through the scheduler, so one site is read in order and
        // two sites are read in parallel. The alternative is that a long import turns into a burst
        // of requests against a server that never agreed to host us.
        return await scheduler.RunAsync(url, async () => {
            await using IBrowserContext context = await session.NewContextAsync(cancellationToken);
            IPage page = await context.NewPageAsync();

            await page.GotoAsync(url, new PageGotoOptions {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = timeoutMs
            });

            await InjectAsync(page);

            return await work(page);
        }, cancellationToken);
    }


    // Each script is injected on its own and a failure is recorded rather than thrown. One parser
    // out of four hundred reaching for a browser-extension API must not take the other
    // three hundred and ninety-nine down with it.
    private async Task<ParserDiagnostics> InjectAsync(IPage page) {
        List<string> failures = [];
        int coreLoaded = 0;
        int parsersLoaded = 0;

        foreach (ParserScriptFile script in scripts.CoreScripts()) {
            if (await TryAddAsync(page, script, failures)) {
                coreLoaded++;
            }
        }

        foreach (ParserScriptFile script in scripts.ParserScripts()) {
            if (await TryAddAsync(page, script, failures)) {
                parsersLoaded++;
            }
        }

        if (failures.Count > 0) {
            logger.LogDebug("{Count} parser scripts did not evaluate: {Failures}", failures.Count, failures);
        }

        return new ParserDiagnostics(coreLoaded, parsersLoaded, failures);
    }


    private static async Task<bool> TryAddAsync(IPage page, ParserScriptFile script, List<string> failures) {
        try {
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = script.content });

            return true;
        } catch (PlaywrightException failure) {
            failures.Add($"{script.name}: {failure.Message}");

            return false;
        }
    }


    // The factory hands back a parser instance, a bare `undefined` for its manual-selection
    // placeholder, or null when nothing claims the URL. Only a real instance counts as support, so
    // the placeholder is filtered out here rather than being reported to the user as a match.
    private const string ParserNameScript = """
        (url) => {
            if (typeof parserFactory === "undefined") {
                return "";
            }

            const parser = parserFactory.fetchByUrl(url);

            if (parser === null || parser === undefined) {
                return "";
            }

            return parser.constructor?.name ?? "";
        }
        """;

    private const string ChapterListScript = """
        async (url) => {
            const parser = parserFactory.fetchByUrl(url) ?? parserFactory.fetch(url, document);

            if (parser === null || parser === undefined) {
                return "[]";
            }

            const chapters = await parser.getChapterUrls(document, null) ?? [];

            return JSON.stringify(chapters
                .map(chapter => ({
                    sourceUrl: chapter.sourceUrl ?? chapter.url ?? chapter.href ?? "",
                    title: (chapter.title ?? chapter.text ?? "").trim()
                }))
                .filter(chapter => chapter.sourceUrl.length > 0));
        }
        """;

    private const string ChapterScript = """
        async (url) => {
            const parser = parserFactory.fetchByUrl(url) ?? parserFactory.fetch(url, document);

            if (parser === null || parser === undefined) {
                throw new Error("No parser is registered for " + url);
            }

            const content = parser.findContent(document);

            if (content === null || content === undefined) {
                throw new Error("The parser found no content at " + url);
            }

            let title = "";

            try {
                const found = parser.findChapterTitle(document);
                title = typeof found === "string" ? found : (found?.textContent ?? "");
            } catch {
                title = "";
            }

            return JSON.stringify({
                title: (title || document.title || "").trim(),
                html: content.innerHTML,
                sourceUrl: url
            });
        }
        """;
}
