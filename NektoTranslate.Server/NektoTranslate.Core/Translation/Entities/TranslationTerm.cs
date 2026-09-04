using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Novels.Entities;


namespace NektoTranslate.Translation.Entities;


// A term as an existing human translation already rendered it, read off during voice learning and
// repair rather than proposed by a source-language pass. Keyed by the rendering itself because that
// is the side these two jobs actually see - there may be no original to key on at all.
[Table("translation_terms")]
public class TranslationTerm {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long novelId { get; set; }

    public Novel? novel { get; set; }

    [MaxLength(32)]
    public required string language { get; set; }

    // Canonical form exactly as the human translator wrote it.
    [MaxLength(200)]
    public required string term { get; set; }

    // JSON array of inflections and alternate spellings seen.
    public required string variantsJson { get; set; }

    public GlossaryCategory category { get; set; } = GlossaryCategory.Other;

    public string? notes { get; set; }

    public int occurrences { get; set; }

    public long? firstSeenChapterId { get; set; }

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;
}
