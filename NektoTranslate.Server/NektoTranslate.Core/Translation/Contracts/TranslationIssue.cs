namespace NektoTranslate.Translation.Contracts;


// Something a check found worth reporting. Never fatal on its own: a chapter is written and the
// issues travel with it, because a translation that is imperfect is still worth far more to the
// reader than one that was refused.
public sealed record TranslationIssue(
    string check,
    string message,
    // Which block the issue sits in, when the check knows. Carried separately rather than left
    // inside the message so the interface can take the reader to the place instead of describing
    // it - a report that says "somewhere in this chapter" is one the user has to search by hand.
    //
    // Null is honest rather than lazy: an ignored glossary term is a statement about the whole
    // chapter's treatment of a name, and picking one of its occurrences would imply the others are
    // fine.
    int? blockIndex = null
);
