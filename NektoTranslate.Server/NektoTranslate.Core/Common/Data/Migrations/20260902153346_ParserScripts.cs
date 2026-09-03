using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class ParserScripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parser_scripts",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    hostName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    displayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    scriptSource = table.Column<string>(type: "TEXT", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    bundledOverride = table.Column<bool>(type: "INTEGER", nullable: false),
                    updatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parser_scripts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parser_scripts_hostName",
                table: "parser_scripts",
                column: "hostName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parser_scripts");
        }
    }
}
