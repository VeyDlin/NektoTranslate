using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Novels.Entities;


namespace NektoTranslate.Glossary.Entities;


// Keyed by the term as it appears in the source language. The source is the stable side; the
// rendering is precisely what drifts between chapters and between translators, so indexing the
// glossary by it would build the reference on its least reliable part.
[Table("glossary_entries")]
public class GlossaryEntry {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long novelId { get; set; }

    public Novel? novel { get; set; }

    [MaxLength(32)]
    public required string language { get; set; }

    [MaxLength(500)]
    public required string sourceTerm { get; set; }

    [MaxLength(500)]
    public required string targetTerm { get; set; }

    public GlossaryCategory category { get; set; } = GlossaryCategory.Other;

    // Other spellings of the same term in the source language. Also what makes a name findable
    // when the source language inflects it.
    public List<string> aliases { get; set; } = [];

    // What the spelling alone does not carry: gender, register, form of address, name suffix.
    public string? notes { get; set; }

    public GlossaryEntryOrigin origin { get; set; }

    public double confidence { get; set; } = 1;

    public bool needsReview { get; set; }

    public long? firstSeenChapterId { get; set; }

    public DateTimeOffset updatedAt { get; set; } = DateTimeOffset.UtcNow;
}
