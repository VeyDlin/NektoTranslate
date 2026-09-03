namespace NektoTranslate.Parsing.Contracts;


public sealed record ImportFromUrlResult(
    int imported,
    // Chapters that could not be fetched, with the reason. Reported rather than thrown: a batch
    // that got forty-nine of fifty is a success with a note, not a failure.
    IReadOnlyList<string> failures
);
