namespace NektoTranslate.Novels.Contracts;


// Every field optional: null means "leave this one alone". That lets the interface save one field
// at a time without echoing back the rest, and stops a stale form from overwriting a change made
// somewhere else.
public sealed record UpdateNovelRequest(
    string? title = null,
    string? sourceLanguage = null,
    string? targetLanguage = null,
    string? model = null,
    string? styleGuide = null,
    bool? normalizeQuotes = null
);
