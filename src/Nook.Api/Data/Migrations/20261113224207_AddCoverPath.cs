using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cover_path",
                table: "works",
                type: "text",
                nullable: true);

            // Works ingested before this point have a cover hash but no cover path, so their covers cannot be found.
            // They lose the hash. A work gets a cover again when another file with a cover is filed under it, such
            // as another edition matched by ISBN. Uploading the same file again does not do it: that is refused as
            // a duplicate.
            migrationBuilder.Sql("UPDATE works SET cover_hash = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cover_path",
                table: "works");
        }
    }
}
