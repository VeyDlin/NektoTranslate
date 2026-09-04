using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    novelId = table.Column<long>(type: "INTEGER", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    startAtChapterIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    processedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    totalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    currentTitle = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    createdAt = table.Column<long>(type: "INTEGER", nullable: false),
                    startedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    finishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_jobs_novels_novelId",
                        column: x => x.novelId,
                        principalTable: "novels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_job_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    jobId = table.Column<long>(type: "INTEGER", nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false),
                    sourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    chapterIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    chapterId = table.Column<long>(type: "INTEGER", nullable: true),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    statusCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    statusText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    statusArgsJson = table.Column<string>(type: "TEXT", nullable: true),
                    finishedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_job_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_job_items_import_jobs_jobId",
                        column: x => x.jobId,
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_job_items_jobId_position",
                table: "import_job_items",
                columns: new[] { "jobId", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_novelId_state",
                table: "import_jobs",
                columns: new[] { "novelId", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_job_items");

            migrationBuilder.DropTable(
                name: "import_jobs");
        }
    }
}
