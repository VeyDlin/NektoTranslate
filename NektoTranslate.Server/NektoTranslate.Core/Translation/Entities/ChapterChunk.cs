using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Chapters.Entities;


namespace NektoTranslate.Translation.Entities;


// One translated batch of one chapter, kept so a run that dies partway does not throw away what it
// already paid for.
//
// A book-length chapter can be seventeen requests. Without this, a protocol failure on the
// fifteenth discards the fourteen that succeeded - real money, and the retry spends it again.
//
// Keyed by a hash of the batch's source text rather than by its position. Batching depends on
// budgets and on where sentences fall, so an index would stop matching the moment either changed,
// while the same source text always deserves the same translation.
[Table("chapter_chunks")]
public class ChapterChunk {

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long chapterId { get; set; }

    public Chapter? chapter { get; set; }

    [MaxLength(32)]
    public required string language { get; set; }

    [MaxLength(64)]
    public required string sourceHash { get; set; }

    // Position at the time it was translated. Kept for inspection, not used for lookup.
    public int index { get; set; }

    public required string translatedText { get; set; }

    [MaxLength(64)]
    public string? model { get; set; }

    public double costUsd { get; set; }

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;
}
