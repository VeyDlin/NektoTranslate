namespace NektoTranslate.Translation.Contracts;


// Null source text is not a broken context: a chapter can have a translation and no original at all.
// Every check that compares the two sides answers "nothing to report" in that case rather than
// inventing a comparison — a finding derived from an absent original would be a finding about
// nothing.
public sealed record TranslationCheckContext(
    string sourceLanguage,
    string targetLanguage,
    string? sourcePlainText,
    string translatedPlainText,
    IReadOnlyList<GlossaryTerm> suppliedTerms
);
