using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChapterChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chapter_chunks",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    chapterId = table.Column<long>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    sourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    index = table.Column<int>(type: "INTEGER", nullable: false),
                    translatedText = table.Column<string>(type: "TEXT", nullable: false),
                    model = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    costUsd = table.Column<double>(type: "REAL", nullable: false),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_chunks_chapters_chapterId",
                        column: x => x.chapterId,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chapter_chunks_chapterId_language_sourceHash",
                table: "chapter_chunks",
                columns: new[] { "chapterId", "language", "sourceHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chapter_chunks");
        }
    }
}
