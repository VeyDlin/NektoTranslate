using NektoTranslate.Chapters.Entities;
using NektoTranslate.Common.Contracts;


namespace NektoTranslate.Chapters.Contracts;


// Plain text is accepted too: a paragraph per line is wrapped into <p> before sanitizing, so the
// manual-paste path and the parser path converge on the same stored shape.
public sealed record ImportedChapter(
    string title,
    string html,
    int? index = null,
    string? sourceUrl = null
);


// What became of one incoming chapter. Exactly one of chapter and rejection is set, except for an
// address already in the book: that rejection still carries the chapter it points at, so the caller
// never has to look one up separately. replaced is the other exception in the other direction - true
// only when replaceExisting matched this same address and overwrote the chapter instead of refusing
// it, which leaves chapter set and rejection null, the identical shape a freshly created chapter has.
public sealed record ChapterImportOutcome(
    int position,
    Chapter? chapter,
    Status? rejection,
    bool replaced = false
);


public sealed record ChapterImportResult(
    IReadOnlyList<ChapterImportOutcome> outcomes
);
