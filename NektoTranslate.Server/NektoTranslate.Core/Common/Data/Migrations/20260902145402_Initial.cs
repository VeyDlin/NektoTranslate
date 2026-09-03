using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "novels",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    sourceLanguage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    targetLanguage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    sourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    styleGuide = table.Column<string>(type: "TEXT", nullable: true),
                    model = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    normalizeQuotes = table.Column<bool>(type: "INTEGER", nullable: false),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_novels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chapters",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    novelId = table.Column<long>(type: "INTEGER", nullable: false),
                    chapter_index = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    sourceMarkdown = table.Column<string>(type: "TEXT", nullable: false),
                    sourcePlainText = table.Column<string>(type: "TEXT", nullable: false),
                    sourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    glossaryState = table.Column<int>(type: "INTEGER", nullable: false),
                    translationState = table.Column<int>(type: "INTEGER", nullable: false),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapters", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapters_novels_novelId",
                        column: x => x.novelId,
                        principalTable: "novels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "glossary_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    novelId = table.Column<long>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    sourceTerm = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    targetTerm = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    category = table.Column<int>(type: "INTEGER", nullable: false),
                    aliases = table.Column<string>(type: "TEXT", nullable: false),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    origin = table.Column<int>(type: "INTEGER", nullable: false),
                    confidence = table.Column<double>(type: "REAL", nullable: false),
                    needsReview = table.Column<bool>(type: "INTEGER", nullable: false),
                    firstSeenChapterId = table.Column<long>(type: "INTEGER", nullable: true),
                    updatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_glossary_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_glossary_entries_novels_novelId",
                        column: x => x.novelId,
                        principalTable: "novels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "translation_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    novelId = table.Column<long>(type: "INTEGER", nullable: false),
                    scopeKind = table.Column<int>(type: "INTEGER", nullable: false),
                    fromIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    toIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    chapterIds = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    processedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    totalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    costUsd = table.Column<double>(type: "REAL", nullable: false),
                    budgetUsd = table.Column<double>(type: "REAL", nullable: true),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false),
                    startedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    finishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_translation_jobs_novels_novelId",
                        column: x => x.novelId,
                        principalTable: "novels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapter_translations",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    chapterId = table.Column<long>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    markdown = table.Column<string>(type: "TEXT", nullable: false),
                    plainText = table.Column<string>(type: "TEXT", nullable: false),
                    origin = table.Column<int>(type: "INTEGER", nullable: false),
                    model = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    costUsd = table.Column<double>(type: "REAL", nullable: true),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_translations", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_translations_chapters_chapterId",
                        column: x => x.chapterId,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chapter_translations_chapterId_language",
                table: "chapter_translations",
                columns: new[] { "chapterId", "language" });

            migrationBuilder.CreateIndex(
                name: "IX_chapters_novelId_chapter_index",
                table: "chapters",
                columns: new[] { "novelId", "chapter_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chapters_novelId_translationState",
                table: "chapters",
                columns: new[] { "novelId", "translationState" });

            migrationBuilder.CreateIndex(
                name: "IX_glossary_entries_novelId_language_sourceTerm",
                table: "glossary_entries",
                columns: new[] { "novelId", "language", "sourceTerm" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_translation_jobs_novelId_state",
                table: "translation_jobs",
                columns: new[] { "novelId", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chapter_translations");

            migrationBuilder.DropTable(
                name: "glossary_entries");

            migrationBuilder.DropTable(
                name: "translation_jobs");

            migrationBuilder.DropTable(
                name: "chapters");

            migrationBuilder.DropTable(
                name: "novels");
        }
    }
}
