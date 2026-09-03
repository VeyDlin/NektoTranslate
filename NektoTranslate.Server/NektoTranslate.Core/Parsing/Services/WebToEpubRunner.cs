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

        // Runtime errors inside an injected script do not reject AddScriptTagAsync - the tag was
        // added, which is all that call promises. They surface as page errors instead, so without
        // this listener a script whose body threw on its first line counts as loaded. That is
        // exactly how UIText went missing for weeks while the probe reported every core script fine.
        List<string> pageErrors = [];

        void OnPageError(object? sender, string message) {
            pageErrors.Add(message);
        }

        page.PageError += OnPageError;

        try {
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = ExtensionShim });

            foreach (ParserScriptFile script in scripts.CoreScripts()) {
                if (await TryAddAsync(page, script, failures, pageErrors)) {
                    coreLoaded++;
                }
            }

            foreach (ParserScriptFile script in scripts.ParserScripts()) {
                if (await TryAddAsync(page, script, failures, pageErrors)) {
                    parsersLoaded++;
                }
            }
        }
        finally {
            page.PageError -= OnPageError;
        }

        if (failures.Count > 0) {
            logger.LogDebug("{Count} parser scripts did not evaluate: {Failures}", failures.Count, failures);
        }

        return new ParserDiagnostics(coreLoaded, parsersLoaded, failures);
    }


    private static async Task<bool> TryAddAsync(
        IPage page,
        ParserScriptFile script,
        List<string> failures,
        List<string> pageErrors
    ) {
        int before = pageErrors.Count;

        try {
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = script.content });
        }
        catch (PlaywrightException failure) {
            failures.Add($"{script.name}: {failure.Message}");

            return false;
        }

        // A synchronous throw during evaluation is reported before the tag finishes loading, so
        // anything new here belongs to this script.
        if (pageErrors.Count > before) {
            failures.Add($"{script.name}: {pageErrors[^1]}");

            return false;
        }

        return true;
    }


    // The slice of the browser-extension API the scripts touch at load time.
    //
    // UIText builds every user-facing string through chrome.i18n.getMessage in static field
    // initialisers, which run the moment the class is declared. With no chrome object the class
    // throws before it exists, and from then on every error path that tries to describe a failure
    // dies on "UIText is not defined" instead - burying the real reason a site refused a request
    // under a bug in the message formatter. Returning the message key keeps the text readable enough
    // to act on without shipping the extension's locale files.
    private const string ExtensionShim = """
        if (typeof chrome === "undefined" || !chrome.i18n) {
            const noop = () => {};
            globalThis.chrome = Object.assign(globalThis.chrome ?? {}, {
                i18n: {
                    // The key with its substitutions appended, so "htmlFetchFailed" arrives as
                    // "htmlFetchFailed: <url> <error>" - the parts that say what actually went wrong.
                    getMessage: (key, substitutions) => {
                        const text = String(key ?? "").replace(/^__MSG_/, "").replace(/__$/, "");
                        const values = (Array.isArray(substitutions) ? substitutions : [substitutions])
                            .filter(value => value !== undefined && value !== null && String(value) !== "");
                        return values.length === 0 ? text : `${text}: ${values.map(String).join(" ")}`;
                    }
                },
                runtime: { getURL: (path) => String(path ?? ""), onMessage: { addListener: noop } },
                // Download.js subscribes to chrome.downloads at load time. Nothing here ever saves an
                // EPUB, but a script that throws on its first line takes every symbol it defines
                // down with it, and other core files reach for some of them.
                downloads: { onChanged: { addListener: noop, removeListener: noop }, download: async () => 0 },
                storage: {
                    onChanged: { addListener: noop, removeListener: noop },
                    local: { get: async () => ({}), set: async () => {}, remove: async () => {} },
                    sync: { get: async () => ({}), set: async () => {}, remove: async () => {} }
                }
            });
        }
        """;


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
