using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class TranslationIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chapter_translation_issues",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    chapterId = table.Column<long>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    check = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    blockIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false),
                    closedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_translation_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_translation_issues_chapters_chapterId",
                        column: x => x.chapterId,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chapter_translation_issues_chapterId_language",
                table: "chapter_translation_issues",
                columns: new[] { "chapterId", "language" });

            migrationBuilder.CreateIndex(
                name: "IX_chapter_translation_issues_state",
                table: "chapter_translation_issues",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chapter_translation_issues");
        }
    }
}
