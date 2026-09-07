using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarefulPasses : Migration
    {
        // The default values below are written by hand, matching SettingsExpansion's own fix for the
        // same problem: EF cannot see the entity's C# field initialisers, so it scaffolded zero and
        // false for every new column. An installation that already had a settings row - every
        // installation, since the row is created on first read - would come back from this migration
        // with passSegments = 0 (a batch of zero paragraphs), thinkingTokens = 0 (indistinguishable
        // from a deliberate "off") and proofread = false, none of which is what shipping this feature
        // is meant to turn on. Each value matches the entity's own default, so an upgraded database
        // and a fresh one start out identical.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "passContextAfter",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "passContextBefore",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "passSegments",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "proofread",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Null is correct here and is the one column that should stay empty: no repair model
            // override means "the book's own model", which is the state every existing installation
            // is already in.
            migrationBuilder.AddColumn<string>(
                name: "repairModel",
                table: "application_settings",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "thinkingTokens",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 6000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "passContextAfter",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "passContextBefore",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "passSegments",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "proofread",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "repairModel",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "thinkingTokens",
                table: "application_settings");
        }
    }
}
