using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class IssueStatusCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "argsJson",
                table: "chapter_translation_issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "chapter_translation_issues",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Findings recorded before codes existed still name their check, and each check has
            // only ever raised one status - so the code is recoverable, and a row left with an
            // empty code would be one the interface could never translate.
            migrationBuilder.Sql(
                """
                UPDATE chapter_translation_issues
                SET code = CASE "check"
                    WHEN 'untranslated-block' THEN 'BLOCK_UNCHANGED'
                    WHEN 'untranslated-residue' THEN 'SOURCE_SCRIPT_RESIDUE'
                    WHEN 'glossary-ignored' THEN 'GLOSSARY_TERM_IGNORED'
                    ELSE code
                END
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "argsJson",
                table: "chapter_translation_issues");

            migrationBuilder.DropColumn(
                name: "code",
                table: "chapter_translation_issues");
        }
    }
}
