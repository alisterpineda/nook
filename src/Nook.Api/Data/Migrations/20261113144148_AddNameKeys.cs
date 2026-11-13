using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNameKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_key",
                table: "tags",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "name_key",
                table: "series",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "name_key",
                table: "people",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Existing rows get the key their name has: trimmed, each run of whitespace made one space, lowercased.
            // The database's lowercasing and its whitespace class can differ from NameKey.Of for a few characters,
            // and such a row's name simply goes unmatched.
            migrationBuilder.Sql(@"UPDATE people SET name_key = lower(regexp_replace(btrim(name), '\s+', ' ', 'g'));");
            migrationBuilder.Sql(@"UPDATE series SET name_key = lower(regexp_replace(btrim(name), '\s+', ' ', 'g'));");
            migrationBuilder.Sql(@"UPDATE tags SET name_key = lower(regexp_replace(btrim(name), '\s+', ' ', 'g'));");

            migrationBuilder.CreateIndex(
                name: "ix_tags_name_key",
                table: "tags",
                column: "name_key");

            migrationBuilder.CreateIndex(
                name: "ix_series_name_key",
                table: "series",
                column: "name_key");

            migrationBuilder.CreateIndex(
                name: "ix_people_name_key",
                table: "people",
                column: "name_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tags_name_key",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "ix_series_name_key",
                table: "series");

            migrationBuilder.DropIndex(
                name: "ix_people_name_key",
                table: "people");

            migrationBuilder.DropColumn(
                name: "name_key",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "name_key",
                table: "series");

            migrationBuilder.DropColumn(
                name: "name_key",
                table: "people");
        }
    }
}
