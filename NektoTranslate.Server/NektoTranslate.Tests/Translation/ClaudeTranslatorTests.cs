using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


public class ClaudeTranslatorTests {

    [Fact]
    public void ImplementsTranslatorContract() {
        ClaudeTranslator translator = new ClaudeTranslator();

        Assert.IsAssignableFrom<ITranslator>(translator);
    }


    [Fact]
    public void RequestCarriesLanguagePair() {
        TranslateTextRequest request = new TranslateTextRequest("「おはよう」", "Japanese", "Russian");

        Assert.Equal("Japanese", request.sourceLanguage);
        Assert.Equal("Russian", request.targetLanguage);
    }
}
