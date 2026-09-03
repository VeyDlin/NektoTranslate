namespace NektoTranslate.Chapters.Contracts;


// Plain text is accepted too: a paragraph per line is wrapped into <p> before sanitizing, so the
// manual-paste path and the parser path converge on the same stored shape.
public sealed record ImportedChapter(
    string title,
    string html,
    int? index = null,
    string? sourceUrl = null
);
