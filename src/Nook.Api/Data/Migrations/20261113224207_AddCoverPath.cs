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
            // They lose the hash, and nothing fills either in for them again: uploading the same file to the same
            // library is refused as a duplicate. A cover comes back only when the book is ingested where it is not
            // already held, such as into a new library or an emptied install.
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
