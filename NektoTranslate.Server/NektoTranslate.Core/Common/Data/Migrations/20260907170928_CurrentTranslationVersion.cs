using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class CurrentTranslationVersion : Migration
    {
        // The SQL below is hand-written, the way SettingsExpansion's own scaffolded defaults are and
        // for the same reason: EF can add the column, but it cannot know which existing row should
        // carry the flag a brand-new installation never has to choose. Every "current" query in the
        // application used to mean "newest by createdAt, ties by id" - ChaptersController.Get,
        // ChapterRepairer, VoiceLearner and the rest all ordered translations the same way - so an
        // upgraded database has to mark that exact row current, per (chapterId, language), or a book
        // mid-translation would read as though every chapter had just lost its translation. The
        // filtered index is created after this runs, not before: it would refuse to exist over rows
        // that do not have their one current row picked out yet.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sourceVersion",
                table: "translation_jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "isCurrent",
                table: "chapter_translations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE chapter_translations
                SET isCurrent = 1
                WHERE id = (
                    SELECT candidate.id
                    FROM chapter_translations AS candidate
                    WHERE candidate.chapterId = chapter_translations.chapterId
                      AND candidate.language = chapter_translations.language
                    ORDER BY candidate.createdAt DESC, candidate.id DESC
                    LIMIT 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_chapter_translations_chapterId_language_current",
                table: "chapter_translations",
                columns: new[] { "chapterId", "language" },
                unique: true,
                filter: "isCurrent = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chapter_translations_chapterId_language_current",
                table: "chapter_translations");

            migrationBuilder.DropColumn(
                name: "sourceVersion",
                table: "translation_jobs");

            migrationBuilder.DropColumn(
                name: "isCurrent",
                table: "chapter_translations");
        }
    }
}
