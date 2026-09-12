namespace NektoTranslate.System.Contracts;


// What GET /api/system/update answers - for a browser user, who has no shell updater to tell them.
// `checked` is false whenever GitHub could not be reached at all, never an exception: a page that
// cannot learn whether it is current is not the same failure as a page that is out of date, and only
// the second one is worth a banner over.
public sealed record UpdateAvailability(
    string current,
    string? latest,
    bool isNewer,
    string? url,
    DateTimeOffset? publishedAt,
    bool @checked
);
