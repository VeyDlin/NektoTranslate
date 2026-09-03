namespace NektoTranslate.Translation.Contracts;


public sealed record ChapterTranslationOutcome(
    IReadOnlyList<string> segments,
    string sessionId,
    double costUsd
);
