using NektoTranslate.Jobs.Enums;


namespace NektoTranslate.Jobs.Contracts;


public sealed record StartTranslationJobRequest(
    JobScopeKind scopeKind,
    int? fromIndex = null,
    int? toIndex = null,
    IReadOnlyList<long>? chapterIds = null,
    double? budgetUsd = null,
    // Include chapters that are already translated. Without it a run skips them, which is what you
    // want when resuming and not what you want after correcting a name or a style instruction.
    //
    // Not a guarantee of new output: batches are cached against the instructions that produced
    // them, so a forced run with nothing changed returns the same text at no cost. Change the
    // glossary, a style prompt or the model and it genuinely re-translates.
    //
    // Nullable because it only means anything for a translate run, and the interface sends null
    // for the other two modes. A non-nullable bool made that null a 400 before the request reached
    // anything that could say so - "The JSON value could not be converted", for a voice-learning
    // run that had nothing to do with forcing.
    bool? force = null,
    // What this run does. Translate is the default and needs an original. LearnVoice reads a
    // sample of chapters that already carry a human translation and writes a VoiceProfile from
    // them - it never touches a chapter's own translation. Repair rewrites chapters that already
    // hold a rendering but have no original text left to translate: it is a rewrite of the existing
    // words, not a translation checked against a source, and it cannot recover meaning an earlier
    // machine pass lost or invented - only fix names, terms, register and sentence flow.
    TranslationJobMode mode = TranslationJobMode.Translate,
    // Which rendering Repair and LearnVoice read; Translate ignores it. Nullable for the same
    // reason force is: the client sends JSON null rather than omitting the field, and null means
    // Current, the choice every run made before this field existed.
    TranslationVersionPick? sourceVersion = null
);
