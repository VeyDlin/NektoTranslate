using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Novels.Entities;


namespace NektoTranslate.Translation.Entities;


// What a voice-learning pass concluded about how a particular human translator renders this novel,
// read from a sample of chapters that already carry that person's translation. Kept separate from
// the translation itself so a repair or a future translate pass can be told to imitate this voice
// without re-reading the whole sample every time.
[Table("voice_profiles")]
public class VoiceProfile {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long novelId { get; set; }

    public Novel? novel { get; set; }

    [MaxLength(32)]
    public required string language { get; set; }

    // Prose description of how this translator writes.
    public required string summary { get; set; }

    // 0-based, inclusive, the range it was learned from.
    public int fromChapterIndex { get; set; }

    public int toChapterIndex { get; set; }

    [MaxLength(64)]
    public string? model { get; set; }

    public double? costUsd { get; set; }

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;
}
