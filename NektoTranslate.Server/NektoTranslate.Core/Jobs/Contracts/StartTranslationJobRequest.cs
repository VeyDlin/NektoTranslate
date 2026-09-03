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
    bool force = false
);
