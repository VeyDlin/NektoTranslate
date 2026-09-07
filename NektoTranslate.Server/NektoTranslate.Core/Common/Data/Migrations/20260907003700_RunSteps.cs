using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currentStep",
                table: "translation_jobs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stepCount",
                table: "translation_jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stepIndex",
                table: "translation_jobs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currentStep",
                table: "translation_jobs");

            migrationBuilder.DropColumn(
                name: "stepCount",
                table: "translation_jobs");

            migrationBuilder.DropColumn(
                name: "stepIndex",
                table: "translation_jobs");
        }
    }
}
