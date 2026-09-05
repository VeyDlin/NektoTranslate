using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "newEntriesJson",
                table: "source_listings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sourceUrl",
                table: "chapter_translations",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            // A translation imported before this column existed still has an address - the job that
            // brought it in recorded one on its own item - it was simply never copied onto the
            // translation itself. The import history is read once here to fill in what every one of
            // those rows already implied.
            migrationBuilder.Sql(
                """
                -- Joined through the chapter's index, not the item's chapterId: the translation
                -- branch of the import runner only ever recorded the index it landed on, and every
                -- translation item in the history has chapterId NULL. A join on it matched nothing.
                UPDATE chapter_translations
                SET sourceUrl = (
                    SELECT i.sourceUrl
                    FROM import_job_items i
                    JOIN import_jobs j ON j.id = i.jobId
                    JOIN chapters c ON c.id = chapter_translations.chapterId
                    WHERE j.kind = 1            -- ImportKind.Translation
                      AND i.state = 1           -- ImportItemState.Imported
                      AND j.novelId = c.novelId
                      AND i.chapterIndex = c.chapter_index
                      AND j.language = chapter_translations.language
                    ORDER BY i.finishedAt DESC
                    LIMIT 1
                )
                WHERE sourceUrl IS NULL AND origin = 1;   -- TranslationOrigin.Imported
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "newEntriesJson",
                table: "source_listings");

            migrationBuilder.DropColumn(
                name: "sourceUrl",
                table: "chapter_translations");
        }
    }
}
