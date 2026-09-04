namespace NektoTranslate.Novels.Contracts;


// The library screen's own row shape. Counts are folded into the same query as the novel itself so
// rendering the shelf costs one round trip no matter how many books are on it - the endpoint used to
// hand back the bare entity and let the client open one GET .../chapters per row just to draw a
// progress bar. Twelve books meant twelve requests before anything was on screen; fifty would have
// meant fifty.
public sealed record NovelListItem(
    long id,
    string title,
    string sourceLanguage,
    string targetLanguage,
    string? sourceUrl,
    string? styleGuide,
    string model,
    bool normalizeQuotes,
    DateTimeOffset createdAt,
    int totalChapters,
    int translatedChapters,
    int failedChapters,

    // A translation job or an import job sits at Queued, Running or Paused for this novel right now.
    // Computed alongside the counts above rather than a call to GET .../activity per row, which would
    // reintroduce exactly the request-per-book this endpoint exists to avoid.
    bool isActive
);
