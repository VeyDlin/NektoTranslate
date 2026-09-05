namespace NektoTranslate.Translation.Contracts;


public sealed record ChapterTranslationRequest(
    string sourceLanguage,
    string targetLanguage,
    IReadOnlyList<string> segments,
    IReadOnlyList<GlossaryTerm> glossary,
    IReadOnlyList<string> recentContext,
    // Two additive layers, never replacements. The base prompt carries the segment protocol, and a
    // user-supplied prompt that could overwrite it would break the pipeline in a way that reads as
    // a model failure rather than a configuration mistake.
    //
    // The global one applies to every novel; the book's own comes after it and so has the last word.
    string? globalStyleGuide,
    string? styleGuide,
    bool normalizeQuotes,
    // Carried per request rather than fixed when the translator is constructed: the model is a
    // per-novel setting, and a translator that ignored it would record one model on the translation
    // while having used another.
    string model,
    // How much source may go into one request. Travels with the request for the same reason the
    // model does - it is derived from settings the user can change, and a translator holding its
    // own copy would keep cutting to a budget that no longer matches the configured model.
    ChunkBudget? budget = null,
    // The latest VoiceProfile learned for this novel and language, if one exists. Distinct from
    // recentContext: that is a short window of nearby chapters shown as source/translation pairs,
    // while this is the standing summary of how the book's human translator writes, learned once
    // from every chapter that had a translation to learn from.
    string? voiceSummary = null
) {

    public ChunkBudget effectiveBudget => budget ?? ChunkBudget.Default;
}
