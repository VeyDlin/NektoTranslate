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


// What became of one incoming chapter. Exactly one of chapter and rejection is set: a chapter that
// landed has nothing to explain, and one that did not has nothing to point at except, for an address
// already in the book, the chapter that is already there.
public sealed record ChapterImportOutcome(
    int position,
    Chapter? chapter,
    Status? rejection
);


public sealed record ChapterImportResult(
    IReadOnlyList<ChapterImportOutcome> outcomes
);
