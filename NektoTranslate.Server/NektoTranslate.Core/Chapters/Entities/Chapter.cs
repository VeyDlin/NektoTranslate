using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Translation.Entities;


namespace NektoTranslate.Chapters.Entities;


[Table("chapters")]
public class Chapter {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long novelId { get; set; }

    public Novel? novel { get; set; }

    [Column("chapter_index")]
    public int index { get; set; }

    [MaxLength(1000)]
    public required string title { get; set; }

    // The authoritative content, as Markdown. Whatever arrives - a parser's HTML or a manual paste -
    // is converted here, so there is one format to edit, diff and render.
    //
    // Furigana has no Markdown syntax and is kept as inline HTML (<ruby>兄<rt>あに</rt></ruby>),
    // which Markdown permits. That is what lets the format be uniform without losing the one thing
    // only the source carries.
    //
    // Null when the book arrived as somebody else's translation and the original is nowhere: the
    // site that carried it is gone, or never had it. Those chapters are real chapters - they are
    // read, edited, checked and repaired - they simply have nothing on the left-hand side. Every
    // reader of this field has to say what it does without one; nothing may assume it is there.
    public string? sourceMarkdown { get; set; }

    // Flat projection, one block per line, rebuilt whenever the source changes. Term search cannot
    // run against the Markdown: a name wrapped in emphasis or sitting inside a ruby annotation is
    // still that name, but the substring the glossary looks for is not there.
    //
    // Null exactly when sourceMarkdown is: the two are written together or not at all.
    public string? sourcePlainText { get; set; }

    [MaxLength(2000)]
    public string? sourceUrl { get; set; }

    public ChapterGlossaryState glossaryState { get; set; } = ChapterGlossaryState.NotAnalyzed;

    public ChapterTranslationState translationState { get; set; } = ChapterTranslationState.None;

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ChapterTranslation> translations { get; set; } = [];
}
