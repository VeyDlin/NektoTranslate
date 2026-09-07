using NektoTranslate.Translation.Services;


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
    string? voiceSummary = null,
    // What the chapter's own memo read before any paragraph was translated - filled in by the
    // translator itself once it has read the chapter, then carried into every batch's system
    // prompt under THIS CHAPTER. Null only for a caller that never asked for one, such as a test
    // exercising BuildSystemPrompt directly.
    CarefulPass.Memo? memo = null,
    // The careful pass's own batching: paragraphs per call and how many neighbours frame them.
    // Defaults match ApplicationSettings' own, so a caller that does not set them - a test, or code
    // that predates this setting - still batches sensibly rather than one paragraph at a time.
    int passSegments = 5,
    int passContextBefore = 2,
    int passContextAfter = 1,
    // Thinking budget for every pass and proofread call this request makes. 0 turns thinking off,
    // matching ApplicationSettings.thinkingTokens.
    int thinkingTokens = 0,
    // Whether the chapter is read back once as a whole after it is produced, and the paragraphs the
    // proofreader flags redone once more with its critique attached.
    bool proofread = false,
    // The glossary model's own name, for the one mechanical call this request makes on its own
    // account - the memo that reads the chapter before any paragraph is translated. Never the model
    // above, which is what the reader will see the chapter translated with.
    string glossaryModel = "",
    // The whole chapter, in plain text, for the memo to read. Distinct from segments: the memo
    // reads the chapter once as a reader would, not paragraph by paragraph.
    string sourcePlainText = ""
) {

    public ChunkBudget effectiveBudget => budget ?? ChunkBudget.Default;
}
