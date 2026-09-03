using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NektoTranslate.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class SettingsExpansion : Migration
    {
        // The default values below are written by hand, and they matter.
        //
        // A new column's default is what every existing row gets, and EF cannot see the C# field
        // initialisers on the entity - so it scaffolded zero for each of these. Zero is not a
        // harmless placeholder here: maxOutputTokens = 0 and expansionFactor = 0 make the batch
        // budget a division by zero, pageLoadTimeoutMs = 0 fails every import, and chatMaxRounds = 0
        // means the agent may never answer. An installation that already had a settings row - which
        // is every installation, since the row is created on first read - would come back from this
        // migration unable to translate anything.
        //
        // Each value matches the entity's own default, so an upgraded database and a fresh one
        // start out identical.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "chatMaxRounds",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<double>(
                name: "expansionFactor",
                table: "application_settings",
                type: "REAL",
                nullable: false,
                defaultValue: 2.0);

            migrationBuilder.AddColumn<string>(
                name: "localModelApiKey",
                table: "application_settings",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "not-needed");

            // Null is correct here and is the one column that should stay empty: no endpoint means
            // no local model, which is the state every existing installation is already in.
            migrationBuilder.AddColumn<string>(
                name: "localModelEndpoint",
                table: "application_settings",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "localModelName",
                table: "application_settings",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "qwen2.5:7b");

            migrationBuilder.AddColumn<int>(
                name: "maxOutputTokens",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 16000);

            migrationBuilder.AddColumn<int>(
                name: "pageLoadTimeoutMs",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 45000);

            migrationBuilder.AddColumn<int>(
                name: "voiceWindowChapters",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "voiceWindowParagraphs",
                table: "application_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chatMaxRounds",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "expansionFactor",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "localModelApiKey",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "localModelEndpoint",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "localModelName",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "maxOutputTokens",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "pageLoadTimeoutMs",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "voiceWindowChapters",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "voiceWindowParagraphs",
                table: "application_settings");
        }
    }
}
