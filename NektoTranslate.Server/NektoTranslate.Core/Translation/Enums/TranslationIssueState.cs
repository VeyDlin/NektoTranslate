namespace NektoTranslate.Translation.Enums;


public enum TranslationIssueState {

    Open = 0,

    // The text was actually changed - by hand, or by translating the block again.
    Resolved = 1,

    // Looked at and judged wrong. Kept as a separate state rather than folded into Resolved because
    // the two say opposite things about the check that raised it: one is a defect it caught, the
    // other is a false positive. Only by keeping them apart can a check's usefulness be judged
    // later instead of guessed at.
    Dismissed = 2
}
