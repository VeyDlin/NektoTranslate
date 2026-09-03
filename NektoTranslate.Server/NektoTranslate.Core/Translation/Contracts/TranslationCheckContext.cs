namespace NektoTranslate.Translation.Contracts;


public sealed record TranslationCheckContext(
    string sourceLanguage,
    string targetLanguage,
    string sourcePlainText,
    string translatedPlainText,
    IReadOnlyList<GlossaryTerm> suppliedTerms
);
