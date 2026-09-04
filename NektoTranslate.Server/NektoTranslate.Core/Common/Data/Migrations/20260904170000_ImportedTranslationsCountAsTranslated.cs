using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportedTranslationsCountAsTranslated : Migration
    {
        // Chapters that already carry an imported translation were left marked untranslated, because
        // nothing but the translator ever wrote that flag. Every count in the application read low,
        // and - the expensive part - a whole-book run would have paid to translate a chapter that
        // already had a human translation attached. New imports keep the flag right from now on;
        // this is the books that were imported before they did.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE chapters
                SET translationState = 3
                WHERE translationState = 0
                  AND EXISTS (
                      SELECT 1
                      FROM chapter_translations
                      JOIN novels ON novels.id = chapters.novelId
                      WHERE chapter_translations.chapterId = chapters.id
                        AND chapter_translations.language = novels.targetLanguage
                  );
                """);
        }


        // Not reversible: which of the marked chapters were marked by this migration and which by a
        // run that finished afterwards is not recorded anywhere, and guessing would un-translate
        // chapters that really are translated.
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
