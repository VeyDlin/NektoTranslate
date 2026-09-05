using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class DismissedImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "dismissedAt",
                table: "import_jobs",
                type: "INTEGER",
                nullable: true);

            // Every run that had already settled is treated as read. The column arrives together with
            // the rule that keeps a settled run on its screen until it is dismissed, and without this
            // the last run of each kind ever made would come back on every book at once.
            migrationBuilder.Sql(
                """
                UPDATE import_jobs
                SET dismissedAt = COALESCE(finishedAt, createdAt)
                WHERE state IN (3, 4, 5);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dismissedAt",
                table: "import_jobs");
        }
    }
}
