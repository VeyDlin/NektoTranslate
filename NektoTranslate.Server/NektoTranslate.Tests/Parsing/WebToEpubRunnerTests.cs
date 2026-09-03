using Microsoft.Extensions.Logging.Abstractions;
using NektoTranslate.Common.Models;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Services;
using NektoTranslate.Tests.Common;
using Xunit;
using Xunit.Abstractions;


namespace NektoTranslate.Tests.Parsing;


// Launches a real Chromium, so it stays inert unless NEKTOTRANSLATE_BROWSER_TESTS is set.
//
// It answers the question that cannot be answered by reading the code: do several hundred scripts
// written for a browser extension actually evaluate inside a plain page, and does host-name routing
// still find a parser afterwards. Neither needs a site to be online, so the test does not depend on
// anyone else's server.
public class WebToEpubRunnerTests(ITestOutputHelper output) {

    private const string EnableVariable = "NEKTOTRANSLATE_BROWSER_TESTS";


    [Fact]
    public async Task TheParserScriptsEvaluateInsideARealPage() {
        if (Environment.GetEnvironmentVariable(EnableVariable) is null) {
            return;
        }

        await using PlaywrightBrowserSession session = new PlaywrightBrowserSession();
        WebToEpubRunner runner = BuildRunner(session);

        ParserDiagnostics diagnostics = await runner.ProbeAsync(CancellationToken.None);

        output.WriteLine($"core loaded   : {diagnostics.coreLoaded}");
        output.WriteLine($"parsers loaded: {diagnostics.parsersLoaded}");
        output.WriteLine($"failures      : {diagnostics.failures.Count}");

        foreach (string failure in diagnostics.failures.Take(10)) {
            output.WriteLine($"  {failure}");
        }

        Assert.True(diagnostics.coreLoaded > 0, "no core script evaluated");
        Assert.True(diagnostics.parsersLoaded > 300, $"only {diagnostics.parsersLoaded} parsers evaluated");
    }


    [Theory]
    [InlineData("https://www.royalroad.com/fiction/12345/some-story")]
    [InlineData("https://www.novelupdates.com/series/some-series/")]
    public async Task AKnownHostResolvesToAParser(string url) {
        if (Environment.GetEnvironmentVariable(EnableVariable) is null) {
            return;
        }

        await using PlaywrightBrowserSession session = new PlaywrightBrowserSession();
        WebToEpubRunner runner = BuildRunner(session);

        bool supported = await runner.IsSupportedAsync(url, CancellationToken.None);

        Assert.True(supported, $"no parser matched {url}");
    }


    [Fact]
    public async Task AnUnknownHostResolvesToNothing() {
        if (Environment.GetEnvironmentVariable(EnableVariable) is null) {
            return;
        }

        await using PlaywrightBrowserSession session = new PlaywrightBrowserSession();
        WebToEpubRunner runner = BuildRunner(session);

        string? parser = await runner.ParserNameAsync(
            "https://nekto-translate.invalid/whatever",
            CancellationToken.None
        );

        output.WriteLine($"matched parser: {parser ?? "<none>"}");

        Assert.Null(parser);
    }


    private static WebToEpubRunner BuildRunner(PlaywrightBrowserSession session) {
        return new WebToEpubRunner(
            session,
            new FileParserScriptStore(ParserDirectory()),
            new StubSettingsService(),
            new PageScheduler(new EngineOptions()),
            NullLogger<WebToEpubRunner>.Instance
        );
    }


    private static string ParserDirectory() {
        return Path.Combine(AppContext.BaseDirectory, "parsers");
    }
}
