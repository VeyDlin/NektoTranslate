using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Novels.Entities;


namespace NektoTranslate.Jobs.Entities;


// An import of chapters from a site, as a job rather than as a request.
//
// A request that fetches forty chapters and answers when it is done has nothing to say while it
// runs and nothing to show if the page is closed. A job persists its progress chapter by chapter,
// so the interface can show where it is, a cancel keeps what was already fetched, and a screen
// opened mid-run finds the run rather than an empty form.
[Table("import_jobs")]
public class ImportJob {

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long novelId { get; set; }

    public Novel? novel { get; set; }

    public ImportKind kind { get; set; }

    // For a translation import: the language the chapters are attached under. Null for originals.
    [MaxLength(32)]
    public string? language { get; set; }

    // Which chapter the first entry lands on; everything after follows in order. Written for
    // translation imports first, to absorb a translator's note at the top of a site's contents
    // page, but an original import takes the same explicit index now that it is matched by address
    // rather than only appended - a site's contents page can open with a prologue or an author's
    // note there too.
    public int startAtChapterIndex { get; set; }

    // For a translation import: whether an entry that lands on a chapter which does not exist may
    // make one, with no original in it. Held on the job rather than passed per chapter because a run
    // that is paused, resumed or picked up after a restart has to decide the same way throughout.
    public bool createMissingChapters { get; set; }

    public JobState state { get; set; } = JobState.Queued;

    public int processedCount { get; set; }

    public int totalCount { get; set; }

    // What the run is fetching right now, for the strip. Cleared when the run stops.
    [MaxLength(1000)]
    public string? currentTitle { get; set; }

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? startedAt { get; set; }

    public DateTimeOffset? finishedAt { get; set; }

    public string? error { get; set; }

    public List<ImportJobItem> items { get; set; } = [];
}
