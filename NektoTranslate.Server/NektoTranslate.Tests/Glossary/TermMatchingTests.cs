using NektoTranslate.Glossary.Services;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Glossary;


// The application translates between arbitrary language pairs, so every matching rule is checked
// against more than one script. These tests exist because the first implementation was written
// while thinking only about Japanese, and was wrong for every alphabetic source.
public class TermMatchingTests {

    [Theory]
    [InlineData("田中はまだ眠っている。", "田中", "Japanese, plain")]
    [InlineData("田中さんは学校へ行った。", "田中", "Japanese, particle attached")]
    [InlineData("林秀在家里等着。", "林秀", "Chinese")]
    [InlineData("김민수는 학교에 갔다.", "김민수", "Korean")]
    public void IdeographicTermsMatchAsSubstrings(string text, string term, string language) {
        Assert.True(TermMatching.Contains(text, term), language);
    }


    [Theory]
    [InlineData("Иван вошёл в комнату.", "Иван", "Russian, nominative")]
    [InlineData("Он позвал Ивана.", "Иван", "Russian, accusative")]
    [InlineData("Письмо Ивану пришло утром.", "Иван", "Russian, dative")]
    [InlineData("Alexander opened the door.", "Alexander", "English")]
    [InlineData("alexander opened the door.", "Alexander", "English, lowercased")]
    public void AlphabeticTermsMatchAcrossCaseAndInflection(string text, string term, string language) {
        Assert.True(TermMatching.Contains(text, term), language);
    }


    [Theory]
    [InlineData("Кримсон стоял у окна.", "Рим", "Russian, term buried inside another word")]
    [InlineData("The announcement was long.", "Ann", "English, term buried inside another word")]
    [InlineData("Совершенно неважно.", "верш", "Russian, mid-word fragment")]
    public void AlphabeticTermsDoNotMatchInsideOtherWords(string text, string term, string language) {
        Assert.False(TermMatching.Contains(text, term), language);
    }


    [Fact]
    public void AnEmptyTermMatchesNothing() {
        Assert.False(TermMatching.Contains("любой текст", ""));
    }


    // A Japanese source and a Russian source of the same length are nothing like the same number of
    // tokens. Getting this wrong sends batches of the wrong size, and the failure only shows up as a
    // truncated reply on a long chapter.
    [Fact]
    public void TokenEstimatesFollowTheScript() {
        const string japanese = "田中はまだ眠っている。妹の美咲が窓のカーテンを開けた。";
        string russian = new string('а', japanese.Length);
        string english = new string('a', japanese.Length);

        int dense = SegmentChunker.EstimateTokens(japanese);
        int cyrillic = SegmentChunker.EstimateTokens(russian);
        int latin = SegmentChunker.EstimateTokens(english);

        Assert.True(dense > cyrillic, $"ideographic {dense} should exceed Cyrillic {cyrillic}");
        Assert.True(cyrillic > latin, $"Cyrillic {cyrillic} should exceed Latin {latin}");
    }
}
