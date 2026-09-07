using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Translation.Enums;


namespace NektoTranslate.Translation.Entities;


// A translation of one chapter into one language. Separate from the chapter because a chapter may
// be translated into several languages, and one language may have several versions of the same
// chapter - a machine pass followed by a hand correction.
[Table("chapter_translations")]
public class ChapterTranslation {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long chapterId { get; set; }

    public Chapter? chapter { get; set; }

    [MaxLength(32)]
    public required string language { get; set; }

    // Markdown, the same format as the source. This is the text a user edits, so it is stored in
    // the format editors exist for rather than in markup that would need a bespoke one.
    public required string markdown { get; set; }

    public required string plainText { get; set; }

    public TranslationOrigin origin { get; set; }

    // Exactly one row per (chapterId, language) carries this flag whenever at least one row exists -
    // enforced by a filtered unique index in NektoDbContext, and kept true by ITranslationVersions
    // rather than by any writer touching it directly. "Current" is a choice, not a recency fact: by
    // default it lands on the newest row a writer adds, but a reader may pin an older one, and every
    // reader of "the" translation - the reader, a repair, voice learning - reads this flag rather than
    // guessing from createdAt.
    public bool isCurrent { get; set; }

    // The address it was matched from, so a re-import of the same page can recognise a translation
    // that is already here instead of relying on its position among the others. Null for anything
    // the application wrote itself or that a person pasted in by hand - those have no address to
    // remember.
    [MaxLength(2000)]
    public string? sourceUrl { get; set; }

    [MaxLength(64)]
    public string? model { get; set; }

    public double? costUsd { get; set; }

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;
}
