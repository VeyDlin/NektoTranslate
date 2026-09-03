using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NektoTranslate.Chat.Enums;
using NektoTranslate.Novels.Entities;


namespace NektoTranslate.Chat.Entities;


// One turn of the conversation about one novel.
//
// Tied to a novel rather than global because corrections are contextual: "call him that from now
// on" only means anything against a particular book, and the agent needs the same book's glossary
// to act on it.
[Table("chat_messages")]
public class ChatMessage {

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    public long novelId { get; set; }

    public Novel? novel { get; set; }

    public ChatRole role { get; set; }

    public required string text { get; set; }

    public double costUsd { get; set; }

    public DateTimeOffset createdAt { get; set; } = DateTimeOffset.UtcNow;
}
