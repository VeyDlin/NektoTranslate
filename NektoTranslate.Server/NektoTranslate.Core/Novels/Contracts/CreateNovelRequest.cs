namespace NektoTranslate.Novels.Contracts;


public sealed record CreateNovelRequest(
    string title,
    string sourceLanguage,
    string targetLanguage,
    string? sourceUrl = null,
    string? styleGuide = null,
    // Omitted, the application-wide default applies. Named here so a caller that cares does not
    // have to create the novel and then immediately edit it.
    string? model = null
);
