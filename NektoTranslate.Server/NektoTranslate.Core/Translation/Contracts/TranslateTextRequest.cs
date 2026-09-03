namespace NektoTranslate.Translation.Contracts;


public record TranslateTextRequest(
    string sourceText,
    string sourceLanguage,
    string targetLanguage
);
