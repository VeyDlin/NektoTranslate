namespace NektoTranslate.Parsing.Contracts;


// Raw output of a site parser. Still unsanitized: it goes through the same import path as a manual
// paste, so cleaning happens in one place rather than once per source.
public sealed record ParsedChapter(
    string title,
    string html,
    string sourceUrl
);
