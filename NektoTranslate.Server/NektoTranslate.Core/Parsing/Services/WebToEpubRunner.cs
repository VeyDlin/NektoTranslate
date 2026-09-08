using System.Diagnostics;
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

            // Exposed before the first navigation so it exists in every document the page ever
            // shows. It is what a parser's refused fetch turns into - see FetchFallback.
            await page.ExposeFunctionAsync<string, Task<string>>(
                NavigateBinding,
                target => FetchByNavigationAsync(context, target, timeoutMs, cancellationToken)
            );

            // A line from the injected scripts into this logger. Without it a fetch that fails inside
            // the page is only ever seen as whatever the parser's error handler made of it.
            await page.ExposeFunctionAsync<string>(LogBinding, message => {
                // Every fetch a parser makes reports here, and a long import makes thousands. The
                // routine ones stay at debug; a challenge page, a tab fallback or a parser giving
                // up is rare and is the line someone will actually want when a site stops working.
                bool routine = message.StartsWith("fetch ", StringComparison.Ordinal)
                    && !message.Contains("challenge", StringComparison.Ordinal)
                    && !message.Contains("through a tab", StringComparison.Ordinal);

                if (routine) {
                    logger.LogDebug("Page {Url}: {Message}", url, message);
                }
                else {
                    logger.LogInformation("Page {Url}: {Message}", url, message);
                }
            });

            await page.GotoAsync(url, new PageGotoOptions {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = timeoutMs
            });

            await WaitOutChallengeAsync(page, timeoutMs);

            // A site built on a framework keeps throwing for a moment after it loads - React's
            // hydration errors are the usual ones - and a page error that lands while a script is
            // being injected gets charged to that script. Letting the page settle first keeps the
            // diagnostics honest; the cap keeps a site that never goes idle from stalling the parse.
            try {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions {
                    Timeout = 3_000
                });
            }
            catch (TimeoutException) {
            }

            await InjectAsync(page);

            string wiring = await page.EvaluateAsync<string>(
                "() => JSON.stringify({ binding: typeof window.__nektoNavigate, "
                + "fetchPatched: window.fetch.__nekto === true })"
            );

            logger.LogDebug("Fetch fallback wiring on {Url}: {Wiring}", url, wiring);

            try {
                return await work(page);
            }
            finally {
                // Whatever the site set - a login, a cookie wall it cleared for a returning
                // visitor - is worth keeping regardless of whether the parse itself succeeded, so
                // this runs in `finally` rather than only on the way out of a successful parse.
                await TrySaveStorageStateAsync(context, url, cancellationToken);
            }
        }, cancellationToken);
    }


    // Best-effort by design - see IBrowserSession.SaveStorageStateAsync. A failed save must not
    // turn a successful parse into a failed one, so it is logged and swallowed here rather than
    // left to propagate out of the `finally` above and replace whatever `work` itself threw.
    private async Task TrySaveStorageStateAsync(
        IBrowserContext context,
        string url,
        CancellationToken cancellationToken
    ) {
        try {
            await session.SaveStorageStateAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception failure) {
            logger.LogDebug(failure, "Could not save browser storage state after {Url}", url);
        }
    }


    // Reads a page the way a person would, for the cases a fetch cannot.
    //
    // A parser reaches other pages of its site with fetch from inside the page it is standing on.
    // Sites behind bot protection answer that with 403: the check wants a script to run, and a fetch
    // never runs one. A real tab does. So the same URL is opened as a tab in the same context - the
    // cookies the first page earned are what let this one through - the challenge is given time to
    // clear, and the finished document goes back to the parser as if the fetch had succeeded.
    //
    // Nested under the outer page's scheduling: the parser is waiting on this before it asks for
    // anything else, so it cannot fan out, and queuing for the host's lane would deadlock against the
    // page that holds it.
    private async Task<string> FetchByNavigationAsync(
        IBrowserContext context,
        string url,
        int timeoutMs,
        CancellationToken cancellationToken
    ) {
        logger.LogInformation("Navigation fallback for {Url}: refused by fetch, opening a tab", url);

        try {
            return await scheduler.RunAsync(url, async () => {
                IPage tab = await context.NewPageAsync();

                try {
                    await tab.GotoAsync(url, new PageGotoOptions {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = timeoutMs
                    });

                    await WaitOutChallengeAsync(tab, timeoutMs);

                    // A chapter list drawn by a script arrives after the load event. Network idle is
                    // the closest thing to "the page has finished becoming itself"; a site that never
                    // goes idle simply gets what it has when the cap runs out.
                    try {
                        await tab.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions {
                            Timeout = Math.Min(timeoutMs, 15_000)
                        });
                    }
                    catch (TimeoutException) {
                    }

                    string html = await tab.EvaluateAsync<string>("() => document.documentElement.outerHTML")
                        ?? string.Empty;

                    logger.LogInformation("Navigation fallback for {Url}: {Length} characters", url, html.Length);

                    return html;
                }
                finally {
                    await tab.CloseAsync();
                }
            }, cancellationToken, nested: true);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception failure) {
            // Empty hands the parser its original refusal back. The fallback failing is not worse
            // than the fetch failing; it is the same failure, and the parser already knows how to
            // report that one.
            logger.LogDebug(failure, "Navigation fallback for {Url} failed", url);

            return string.Empty;
        }
    }


    // Bot checks finish after DOMContentLoaded: the page that fired the event is the interstitial,
    // and the real one arrives by a later navigation once the script has run. Waiting on the load
    // event alone hands the parser the interstitial, which parses as a chapter with no text.
    private static async Task WaitOutChallengeAsync(IPage page, int timeoutMs) {
        long started = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < timeoutMs) {
            bool challenged;

            try {
                challenged = await page.EvaluateAsync<bool>(ChallengeProbe);
            }
            catch (PlaywrightException) {
                // The document is being replaced under us - which is the challenge clearing.
                challenged = true;
            }

            if (!challenged) {
                return;
            }

            await Task.Delay(750);
        }
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
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = FetchFallback });

            // The shim and the fallback are ours, so an error in them is a bug here rather than an
            // upstream script reaching for an API - it goes in the failures like any other.
            if (pageErrors.Count > 0) {
                failures.Add($"runner shim: {pageErrors[^1]}");
                logger.LogWarning("The runner's own page script failed to evaluate: {Error}", pageErrors[^1]);
            }

            foreach (ParserScriptFile script in scripts.CoreScripts()) {
                if (await TryAddAsync(page, script, failures, pageErrors)) {
                    coreLoaded++;
                }
            }

            // After the core, because it wraps a method the core defines.
            int beforeRetry = pageErrors.Count;
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = RetryViaTab });

            if (pageErrors.Count > beforeRetry) {
                failures.Add($"runner retry: {pageErrors[^1]}");
                logger.LogWarning("The runner's retry hook failed to evaluate: {Error}", pageErrors[^1]);
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

        // Information rather than debug: a script that fails to evaluate is a site that silently
        // stopped being supported, and that deserves a line in the ordinary log.
        if (failures.Count > 0) {
            logger.LogInformation("{Count} parser scripts did not evaluate: {Failures}", failures.Count, failures);
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
        // anything new here belongs to this script - unless the page has already thrown the very
        // same thing, in which case it is the site's own noise repeating and not this script's.
        if (pageErrors.Count > before) {
            string message = pageErrors[^1];
            bool seenBefore = pageErrors.Take(before).Contains(message, StringComparer.Ordinal);

            if (!seenBefore) {
                failures.Add($"{script.name}: {message}");

                return false;
            }
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
    private const string NavigateBinding = "__nektoNavigate";

    private const string LogBinding = "__nektoLog";

    // The tells of an interstitial standing in for the real page. Title first, because every
    // major protection product names its waiting page; the element ids are the ones Cloudflare's
    // own markup uses.
    private const string ChallengeProbe = """
        () => {
            const title = (document.title || "").toLowerCase();
            if (/just a moment|attention required|checking your browser|verify you are human|ddos-guard|один момент|подождите/.test(title)) {
                return true;
            }
            return document.querySelector("#challenge-running, #challenge-form, #challenge-stage, .cf-browser-verification, #cf-challenge-running, script[src*='ddos-guard']") !== null;
        }
        """;

    // Turns a refused fetch into a navigation, for the parser and without its knowledge.
    //
    // Only 403 and 503 qualify - the two codes a bot check answers with. A 404 is a 404, and a 429 is
    // a site asking for less traffic, which is not a request to open a tab and make more. Assets are
    // left alone too: a blocked image is not worth a navigation, and would not parse as one anyway.
    //
    // The replacement Response carries the URL the parser asked for, because HttpClient reads it
    // back for messages and for resolving relative links. A constructed Response has an empty url,
    // so it is set on the instance explicitly.
    private const string FetchFallback = """
        (() => {
            const original = window.fetch.bind(window);
            const navigate = window["__nektoNavigate"];
            if (typeof navigate !== "function") {
                return;
            }
            const log = typeof window["__nektoLog"] === "function" ? window["__nektoLog"] : () => {};
            const isAsset = (url) => /\.(png|jpe?g|gif|webp|svg|css|js|mjs|woff2?|ttf|ico)(\?|$)/i.test(url);
            // The same tells the runner uses on a live document, applied to raw HTML. Cloudflare and
            // DDoS-Guard between them front most of the sites these parsers read.
            const isChallenge = (html, title) =>
                /just a moment|attention required|checking your browser|verify you are human|ddos-guard|один момент|подождите/i.test(title)
                || /id="(challenge-running|challenge-form|challenge-stage|cf-challenge-running)"|cf-browser-verification|ddos-guard\.net/i.test(html);
            const viaTab = async (url) => {
                const html = await navigate(url);
                if (!html) {
                    return null;
                }
                const replacement = new Response(html, {
                    status: 200,
                    statusText: "OK",
                    headers: { "content-type": "text/html; charset=utf-8" }
                });
                Object.defineProperty(replacement, "url", { value: url });
                return replacement;
            };
            window.fetch = async (input, init) => {
                const url = typeof input === "string" ? input : (input && input.url) || String(input);
                // A URL the parser has already failed on once is not fetched again - it goes
                // straight to a tab. See RetryViaTab for who asks.
                const forced = window["__nektoForceTab"];
                if (forced && forced.has(url) && !isAsset(url)) {
                    log(`fetch ${url}: through a tab, the parser found nothing in the fetched page`);
                    const replacement = await viaTab(url);
                    if (replacement !== null) {
                        return replacement;
                    }
                }
                let response;
                try {
                    response = await original(input, init);
                }
                catch (error) {
                    // A fetch that throws is a refusal too - a redirect to a challenge on another
                    // origin, a connection the site reset - and a tab can often get past it.
                    log(`fetch threw for ${url}: ${error && error.message ? error.message : String(error)}`);
                    if (isAsset(url)) {
                        throw error;
                    }
                    const replacement = await viaTab(url);
                    if (replacement === null) {
                        throw error;
                    }
                    return replacement;
                }
                if (isAsset(url)) {
                    return response;
                }
                // The refusal that matters most does not come as a status at all. A bot check can
                // answer 200 with its own waiting page in place of the content, which the parser can
                // only report as "nothing there". The body has to be looked at.
                const type = (response.headers.get("content-type") || "").toLowerCase();
                let title = "";
                if (type.includes("html")) {
                    const peek = await response.clone().text();
                    title = ((peek.match(/<title[^>]*>([^<]*)<\/title>/i) || [])[1] || "").trim();
                    if (isChallenge(peek, title)) {
                        log(`fetch ${response.status} for ${url}: challenge page, title="${title}"`);
                        return (await viaTab(url)) ?? response;
                    }
                }
                log(`fetch ${response.status} ${type || "?"} for ${url}${title ? `: title="${title}"` : ""}`);
                if (response.status === 403 || response.status === 503) {
                    return (await viaTab(url)) ?? response;
                }
                return response;
            };
            window.fetch.__nekto = true;
        })();
        """;

    // A second chance through a tab, taken on the parser's own verdict rather than on anything the
    // runner can see in a response.
    //
    // A parser knows what its site's page has to contain; when the fetched document lacks it, the
    // core turns that into a failure. The usual reasons - a waiting page served as 200, a chapter
    // list drawn by a script after load - are exactly the ones a real tab gets past. So the failed
    // URL is marked, the same call is made once more, and the fetch override sends it through a
    // tab instead. One retry only: a page that has nothing in a tab either has nothing.
    private const string RetryViaTab = """
        (() => {
            if (typeof HttpClient === "undefined" || !window.fetch || window.fetch.__nekto !== true) {
                return;
            }
            const log = typeof window["__nektoLog"] === "function" ? window["__nektoLog"] : () => {};

            // A stand-in for any piece of the popup a parser reaches for: it accepts every call and
            // property, reports nothing checked and nothing entered, and never throws. The popup's
            // chapter-list panel is the usual one - parsers report paging progress to it.
            const inert = window["__nektoInert"] = new Proxy(function () {}, {
                get: (target, name) => name === "checked" ? false : name === "value" ? "" : inert,
                set: () => true,
                apply: () => inert
            });

            // What the extension's popup does before it lets a parser run, and what nothing here did
            // until now. A parser is constructed with userPreferences = null and reads it on the
            // first fetch it makes with itself as the parser - so every such fetch died on a null
            // dereference dressed up as "the site refused". Defaults are what the popup would have
            // read from an untouched install: all off, including the check that turns a page without
            // chapter text into a 403.
            window["__nektoPrepare"] = (parser) => {
                if (!parser || typeof parser.onUserPreferencesUpdate !== "function" || parser.userPreferences) {
                    return parser;
                }
                // The preferences object wires itself to the popup's checkboxes as it is built, by
                // id, and this page has none of them. For the moment of construction a missing
                // element is answered with the stand-in - the popup's untouched state. The page's
                // own elements are untouched.
                const byId = document.getElementById.bind(document);
                const bySelector = document.querySelector.bind(document);
                document.getElementById = (id) => byId(id) ?? inert;
                document.querySelector = (selector) => bySelector(selector) ?? inert;
                let preferences;
                try {
                    preferences = UserPreferences.readFromLocalStorage();
                }
                catch (error) {
                    log(`user preferences could not be built (${error && error.message}); using bare defaults`);
                    preferences = new UserPreferences();
                }
                finally {
                    document.getElementById = byId;
                    document.querySelector = bySelector;
                }
                parser.onUserPreferencesUpdate(preferences);
                return parser;
            };

            const forced = window["__nektoForceTab"] = new Set();
            const original = HttpClient.wrapFetchImpl.bind(HttpClient);
            HttpClient.wrapFetchImpl = async (url, wrapOptions) => {
                try {
                    return await original(url, wrapOptions);
                }
                catch (error) {
                    const message = String((error && error.message) || error);
                    // A crash inside the parser repeats identically in a tab; only a failed or
                    // refused fetch is worth the second attempt.
                    const worthRetrying = /htmlFetchFailed|CustomError/i.test(message)
                        && !/Cannot read propert|is not a function|is not defined|is not iterable/i.test(message);
                    if (forced.has(url) || !worthRetrying) {
                        throw error;
                    }
                    log(`parser found nothing at ${url} (${message}); trying once through a tab`);
                    forced.add(url);
                    try {
                        return await original(url, wrapOptions);
                    }
                    finally {
                        forced.delete(url);
                    }
                }
            };
        })();
        """;

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

            window.__nektoPrepare(parser);

            // The second argument is the popup's chapter-list panel, which parsers report paging
            // progress to. The stand-in takes the reports and does nothing with them.
            const chapters = await parser.getChapterUrls(document, window.__nektoInert) ?? [];

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

            window.__nektoPrepare(parser);

            // A site that draws its text with a script has none of it at DOMContentLoaded, and how
            // long it takes varies from page to page. The parser is asked again until it can see the
            // content or the wait runs out - bounded, because a page that genuinely has no chapter
            // must not cost the whole timeout to say so.
            const deadline = Date.now() + 8000;
            let content = parser.findContent(document);

            while ((content === null || content === undefined) && Date.now() < deadline) {
                await new Promise(resolve => setTimeout(resolve, 250));
                content = parser.findContent(document);
            }

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
