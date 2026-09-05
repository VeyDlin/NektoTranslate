using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class SourceListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_listings",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    novelId = table.Column<long>(type: "INTEGER", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    entriesJson = table.Column<string>(type: "TEXT", nullable: true),
                    entryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    statusCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    statusText = table.Column<string>(type: "TEXT", nullable: true),
                    statusArgsJson = table.Column<string>(type: "TEXT", nullable: true),
                    startedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    readAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_listings", x => x.id);
                    table.ForeignKey(
                        name: "FK_source_listings_novels_novelId",
                        column: x => x.novelId,
                        principalTable: "novels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_source_listings_novelId_kind",
                table: "source_listings",
                columns: new[] { "novelId", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_listings");
        }
    }
}
