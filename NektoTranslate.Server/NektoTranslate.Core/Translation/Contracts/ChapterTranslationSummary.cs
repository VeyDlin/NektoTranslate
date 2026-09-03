namespace NektoTranslate.Translation.Contracts;


public sealed record ChapterTranslationSummary(
    long chapterId,
    int segmentCount,
    double costUsd,
    IReadOnlyList<string> candidateTerms,
    // What the quality checks found. Never fatal: the chapter is written and the issues travel with
    // it, because an imperfect translation is worth far more to the reader than a refused one.
    IReadOnlyList<TranslationIssue> issues
);
