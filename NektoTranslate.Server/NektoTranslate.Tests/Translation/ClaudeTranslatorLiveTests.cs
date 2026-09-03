using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;
using Xunit;
using Xunit.Abstractions;


namespace NektoTranslate.Tests.Translation;


// Hits the real Claude Code CLI and spends the caller's quota, so it stays inert unless
// NEKTOTRANSLATE_LIVE_TESTS is set.
//
// It guards the two things that fail silently rather than loudly: the context-stripping flags
// not reaching the CLI, which shows up only as a cost per sentence two orders of magnitude too
// high; and non-ASCII source text being destroyed on the way into the subprocess, which shows
// up only as the model politely reporting that it received nothing to translate.
public class ClaudeTranslatorLiveTests(ITestOutputHelper output) {
    private const string EnableVariable = "NEKTOTRANSLATE_LIVE_TESTS";
    private const double CostCeilingUsd = 0.01;


    [Fact]
    public async Task TranslatesWithoutLoadingTheClaudeCodeHarness() {
        if (Environment.GetEnvironmentVariable(EnableVariable) is null) {
            return;
        }

        ClaudeTranslator translator = new ClaudeTranslator();
        TranslateTextRequest request = new TranslateTextRequest(
            "「お兄ちゃん、もう朝だよ。起きて」",
            "Japanese",
            "Russian"
        );

        TranslationResult result = await translator.TranslateAsync(request, CancellationToken.None);

        output.WriteLine($"text: {result.text}");
        output.WriteLine($"cost: ${result.costUsd:F6}");

        Assert.False(string.IsNullOrWhiteSpace(result.text));
        Assert.InRange(result.costUsd, 0, CostCeilingUsd);
        Assert.DoesNotContain("??", result.text);
        Assert.Contains(result.text, character => character is >= 'а' and <= 'я' or >= 'А' and <= 'Я');
    }
}
