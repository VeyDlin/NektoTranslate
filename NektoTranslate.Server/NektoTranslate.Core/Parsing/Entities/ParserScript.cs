using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace NektoTranslate.Parsing.Entities;


// A site parser the user added or edited.
//
// Only overrides live here. The four hundred bundled parsers stay as files on disk, because copying
// them into the database on first run would mean every upstream update needed a migration, and a
// user who had edited one would have no way to tell their change from the original.
//
// A row whose hostName matches a bundled parser replaces it; a row with a new hostName adds one.
// `bundledOverride` records which of the two this is, so the interface can offer "revert to the
// version that ships with the application" rather than leaving the user to remember.
[Table("parser_scripts")]
public class ParserScript {

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    [MaxLength(255)]
    public required string hostName { get; set; }

    [MaxLength(255)]
    public required string displayName { get; set; }

    public required string scriptSource { get; set; }

    public bool enabled { get; set; } = true;

    public bool bundledOverride { get; set; }

    public DateTimeOffset updatedAt { get; set; } = DateTimeOffset.UtcNow;
}
