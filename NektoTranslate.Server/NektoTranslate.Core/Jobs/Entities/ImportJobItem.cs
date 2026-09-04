using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Jobs.Enums;


namespace NektoTranslate.Jobs.Entities;


// One chapter of an import, with its own outcome.
//
// Persisted rather than kept in memory because the outcome is what the user comes back for: which
// chapters landed, which were skipped and why, which failed and with what reason - after the run,
// after a reload, after a restart. A job that only remembered its counts could not answer that.
[Table("import_job_items")]
public class ImportJobItem {

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long jobId { get; set; }

    public ImportJob? job { get; set; }

    // Order in the run, which is also the order the site listed the chapters in.
    public int position { get; set; }

    [MaxLength(2000)]
    public required string sourceUrl { get; set; }

    [MaxLength(1000)]
    public required string title { get; set; }

    // Where it landed: the chapter's index for originals, the target chapter for a translation.
    public int? chapterIndex { get; set; }

    public long? chapterId { get; set; }

    public ImportItemState state { get; set; } = ImportItemState.Pending;

    // The reason it was skipped or failed, as a Status taken apart into columns - the same way a
    // quality finding is stored. Null for an item that landed.
    [MaxLength(64)]
    public string? statusCode { get; set; }

    [MaxLength(2000)]
    public string? statusText { get; set; }

    public string? statusArgsJson { get; set; }

    public DateTimeOffset? finishedAt { get; set; }
}
