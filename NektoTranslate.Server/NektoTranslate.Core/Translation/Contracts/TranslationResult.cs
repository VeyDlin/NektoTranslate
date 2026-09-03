namespace NektoTranslate.Translation.Contracts;


public record TranslationResult(
    string text,
    string sessionId,
    double costUsd
);
