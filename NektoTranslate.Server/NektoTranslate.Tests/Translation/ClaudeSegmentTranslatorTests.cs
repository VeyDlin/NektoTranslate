using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// BuildSystemPrompt needs neither the database nor the model - it only reads the request - so it
// is worth testing directly rather than only through a real translation run.
public class ClaudeSegmentTranslatorTests {

    [Fact]
    public void ALearnedVoiceSummaryAddsItsOwnHeadingAndText() {
        ChapterTranslationRequest request = Request(voiceSummary: "Short sentences, dry humour.");

        string prompt = ClaudeSegmentTranslator.BuildSystemPrompt(request, 1);

        Assert.Contains("TRANSLATOR'S VOICE", prompt);
        Assert.Contains("Short sentences, dry humour.", prompt);
    }


    [Fact]
    public void NoVoiceSummaryAddsNoTranslatorsVoiceSection() {
        ChapterTranslationRequest request = Request(voiceSummary: null);

        string prompt = ClaudeSegmentTranslator.BuildSystemPrompt(request, 1);

        Assert.DoesNotContain("TRANSLATOR'S VOICE", prompt);
    }


    // The recent-context block used to share the word VOICE with the learned-voice block above it.
    // Now that both can appear in the same prompt, this one's heading must read as its own thing
    // rather than as a second, conflicting instruction about the same "voice".
    [Fact]
    public void RecentContextIsHeadedRecentWorkNotVoice() {
        ChapterTranslationRequest request = Request(recentContext: ["Раз.\n-> One."]);

        string prompt = ClaudeSegmentTranslator.BuildSystemPrompt(request, 1);

        Assert.Contains("RECENT WORK - already-finished work from earlier chapters", prompt);
        Assert.DoesNotContain("VOICE - already-finished work", prompt);
    }


    [Fact]
    public void BothVoiceAndRecentContextCanAppearTogether() {
        ChapterTranslationRequest request = Request(
            voiceSummary: "Short sentences, dry humour.",
            recentContext: ["Раз.\n-> One."]
        );

        string prompt = ClaudeSegmentTranslator.BuildSystemPrompt(request, 1);

        Assert.Contains("TRANSLATOR'S VOICE", prompt);
        Assert.Contains("RECENT WORK - already-finished work from earlier chapters", prompt);
    }


    // ---- the careful pass's own additions ----

    [Fact]
    public void HowToReadEachParagraphIsAlwaysPresent() {
        string prompt = ClaudeSegmentTranslator.BuildSystemPrompt(Request(), 1);

        Assert.Contains("HOW TO READ EACH PARAGRAPH", prompt);
        Assert.Contains("is rebuilt, not mirrored", prompt);
    }


    [Fact]
    public void NoMemoAddsNoThisChapterSection() {
        string prompt = ClaudeSegmentTranslator.BuildSystemPrompt(Request(), 1);

        Assert.DoesNotContain("THIS CHAPTER", prompt);
    }


    [Fact]
    public void AMemoAddsItsRegisterAndPresentTermsUnderThisChapter() {
        CarefulPass.Memo memo = new CarefulPass.Memo("A tense chapter.", ["田中"], []);
        string prompt = ClaudeSegmentTranslator.BuildSystemPrompt(Request(memo: memo), 1);

        Assert.Contains("THIS CHAPTER", prompt);
        Assert.Contains("A tense chapter.", prompt);
        Assert.Contains("田中", prompt);
    }


    private static ChapterTranslationRequest Request(
        string? voiceSummary = null,
        IReadOnlyList<string>? recentContext = null,
        CarefulPass.Memo? memo = null
    ) {
        return new ChapterTranslationRequest(
            "Japanese",
            "Russian",
            ["Text."],
            [],
            recentContext ?? [],
            null,
            null,
            false,
            "claude-sonnet",
            null,
            voiceSummary,
            memo
        );
    }
}
