using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Glossary.Entities;


namespace NektoTranslate.Novels.Entities;


[Table("novels")]
public class Novel {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    [MaxLength(500)]
    public required string title { get; set; }

    // BCP 47 tags, or whatever the user typed. Kept as free text because the model reads them as
    // language names, and forcing a closed enum would block unusual pairs for no gain.
    [MaxLength(32)]
    public required string sourceLanguage { get; set; }

    [MaxLength(32)]
    public required string targetLanguage { get; set; }

    [MaxLength(2000)]
    public string? sourceUrl { get; set; }

    // Free-text tone and formatting instructions, mixed into the system prompt. Edited by hand
    // and from the agent chat.
    public string? styleGuide { get; set; }

    [MaxLength(64)]
    public string model { get; set; } = "sonnet";

    public bool normalizeQuotes { get; set; } = true;

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Chapter> chapters { get; set; } = [];

    public List<GlossaryEntry> glossary { get; set; } = [];
}
