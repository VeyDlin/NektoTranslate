using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Novels.Entities;


namespace NektoTranslate.Jobs.Entities;


[Table("translation_jobs")]
public class TranslationJob {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long novelId { get; set; }

    public Novel? novel { get; set; }

    // What this run of the job actually does. One job type now covers three kinds of work -
    // translating chapters, learning a voice profile from a sample, and repairing an imported
    // translation - because they share the same queue, progress tracking and cost accounting, and
    // splitting them into separate tables would just duplicate all of that for no behavioural gain.
    public TranslationJobMode mode { get; set; } = TranslationJobMode.Translate;

    public JobScopeKind scopeKind { get; set; }

    public int? fromIndex { get; set; }

    public int? toIndex { get; set; }

    public List<long> chapterIds { get; set; } = [];

    public bool force { get; set; }

    public JobState state { get; set; } = JobState.Queued;

    public int processedCount { get; set; }

    public int totalCount { get; set; }

    public double costUsd { get; set; }

    public double? budgetUsd { get; set; }

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? startedAt { get; set; }

    public DateTimeOffset? finishedAt { get; set; }

    public string? error { get; set; }
}
