namespace NektoTranslate.Translation.Contracts;


public sealed record EditTranslationBlockRequest(
    string language,
    string text,
    // The translation the edit was made against. Sent back so a correction written while something
    // else was rewriting the same chapter is refused rather than silently applied on top of text the
    // user never saw.
    //
    // Without it the last write wins, and the loser is whoever spent longer thinking about the
    // wording.
    long baseTranslationId
);


public enum TranslationEditResult {

    Applied = 0,

    ChapterNotFound = 1,

    NoTranslation = 2,

    // Something else is writing this chapter's translation right now. Refusing is the only honest
    // answer: the run in flight will write its own version when it finishes, and an edit applied now
    // would be overwritten minutes later with no trace and no warning.
    Busy = 3,

    // The chapter has been translated again since the edit was started, so the block the user was
    // looking at is no longer the block they would be replacing.
    Stale = 4,

    BlockOutOfRange = 5
}


public sealed record TranslationEditOutcome(
    TranslationEditResult result,
    long? translationId = null,
    int resolvedIssues = 0
);
