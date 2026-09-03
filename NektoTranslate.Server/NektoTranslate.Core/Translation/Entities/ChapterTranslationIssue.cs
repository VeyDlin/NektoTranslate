using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Translation.Enums;


namespace NektoTranslate.Translation.Entities;


// A quality check's finding about one chapter's translation, kept rather than reported once.
//
// Stored because of what these findings are for. Each one marks a place worth going back to - a
// block copied instead of translated, a name the model paraphrased - and none of them is urgent
// enough to interrupt a run. A notification shown while a job is running is exactly the wrong shape
// for that: the user is not watching, and by the time they are, it is gone. What they need is to
// finish a run of two hundred chapters and then ask which ones need a look.
//
// Per language, like the translation itself: the same chapter can be sound in one target language
// and defective in another.
[Table("chapter_translation_issues")]
public class ChapterTranslationIssue {

    [Key]
    public long id { get; set; }

    public long chapterId { get; set; }

    public Chapter? chapter { get; set; }

    [MaxLength(32)]
    public required string language { get; set; }

    // The check's own name, as it declares it - "untranslated-block", "glossary-ignored". Stored as
    // text rather than an enum so that adding a check is adding a class, which is the whole point of
    // the check being an interface.
    [MaxLength(64)]
    public required string check { get; set; }

    [MaxLength(2000)]
    public required string message { get; set; }

    // Which block of the translation this is about, when the check could tell. Null means the
    // finding is about the chapter as a whole.
    public int? blockIndex { get; set; }

    public TranslationIssueState state { get; set; } = TranslationIssueState.Open;

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? closedAt { get; set; }
}
