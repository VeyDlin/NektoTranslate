using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class VoiceAndRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "mode",
                table: "translation_jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "translation_terms",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    novelId = table.Column<long>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    term = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    variantsJson = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<int>(type: "INTEGER", nullable: false),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    occurrences = table.Column<int>(type: "INTEGER", nullable: false),
                    firstSeenChapterId = table.Column<long>(type: "INTEGER", nullable: true),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_terms", x => x.id);
                    table.ForeignKey(
                        name: "FK_translation_terms_novels_novelId",
                        column: x => x.novelId,
                        principalTable: "novels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "voice_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    novelId = table.Column<long>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "TEXT", nullable: false),
                    fromChapterIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    toChapterIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    model = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    costUsd = table.Column<double>(type: "REAL", nullable: true),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voice_profiles", x => x.id);
                    table.ForeignKey(
                        name: "FK_voice_profiles_novels_novelId",
                        column: x => x.novelId,
                        principalTable: "novels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_translation_terms_novelId_language_term",
                table: "translation_terms",
                columns: new[] { "novelId", "language", "term" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_voice_profiles_novelId_language",
                table: "voice_profiles",
                columns: new[] { "novelId", "language" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translation_terms");

            migrationBuilder.DropTable(
                name: "voice_profiles");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "translation_jobs");
        }
    }
}
