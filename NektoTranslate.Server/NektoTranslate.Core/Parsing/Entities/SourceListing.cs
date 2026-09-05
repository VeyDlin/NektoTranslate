using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Parsing.Enums;


namespace NektoTranslate.Parsing.Entities;


// A site's table of contents, as read for one book, kept.
//
// Reading it is a page load through a real browser behind a queue that allows one tab per site and
// a second between visits, so it takes seconds at best and a minute at worst. Returning that list
// only in the response to the request that asked for it made it the most expensive thing in the
// application to obtain and the easiest to lose: navigating away threw it out, refreshing threw it
// out, and pressing the button again silently queued a second visit to the same site behind the
// first with nothing on screen to say so.
//
// Kept per book and per kind, because the two import screens ask about different sites - the
// original comes from one, somebody else's translation from another - and each screen has to find
// its own listing where it left it.
[Table("source_listings")]
public class SourceListing {

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long novelId { get; set; }

    public Novel? novel { get; set; }

    // Which import screen this listing belongs to. The same enum the import job uses, because the
    // listing exists to feed exactly that import.
    public ImportKind kind { get; set; }

    [MaxLength(2000)]
    public required string url { get; set; }

    public ListingState state { get; set; } = ListingState.Reading;

    // The parsed entries as JSON, in the site's own order, which is the order the import maps onto
    // consecutive chapters. Null until a read succeeds.
    public string? entriesJson { get; set; }

    public int entryCount { get; set; }

    // Why the read failed, in the three columns a Status is stored as everywhere else. Null unless
    // the state is Failed.
    [MaxLength(64)]
    public string? statusCode { get; set; }

    public string? statusText { get; set; }

    public string? statusArgsJson { get; set; }

    public DateTimeOffset startedAt { get; set; } = DateTimeOffset.UtcNow;

    // When the entries below were read. Shown to the user, because a contents page read three days
    // ago may be missing the chapters they came back for.
    public DateTimeOffset? readAt { get; set; }
}
