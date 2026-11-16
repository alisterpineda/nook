using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceLibrariesWithSections : Migration
    {
        /// <summary>The library the first migration seeded, which every install started with.</summary>
        private const string SeededLibraryId = "01a1c7e7-8000-7000-8000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Libraries become sections in place, so their rows and the works that point at them stay. Keys cannot be
            // renamed, so they are dropped here and added again under their new names.
            migrationBuilder.DropForeignKey(
                name: "fk_works_libraries_library_id",
                table: "works");

            migrationBuilder.DropPrimaryKey(
                name: "pk_libraries",
                table: "libraries");

            migrationBuilder.RenameTable(
                name: "libraries",
                newName: "sections");

            migrationBuilder.RenameColumn(
                name: "library_id",
                table: "works",
                newName: "section_id");

            migrationBuilder.RenameIndex(
                name: "ix_works_library_id",
                table: "works",
                newName: "ix_works_section_id");

            migrationBuilder.AlterColumn<Guid>(
                name: "section_id",
                table: "works",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // The seeded library was the whole collection, not a grouping in it, so its works end in no section and
            // the row goes.
            migrationBuilder.Sql(
                $"""
                UPDATE works SET section_id = NULL WHERE section_id = '{SeededLibraryId}';
                DELETE FROM sections WHERE id = '{SeededLibraryId}';
                """);

            // Every section shares the one library folder. Files under another library's own folder are not moved.
            migrationBuilder.DropColumn(
                name: "folder_name",
                table: "sections");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sections",
                table: "sections",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_works_sections_section_id",
                table: "works",
                column: "section_id",
                principalTable: "sections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // An ingest job names the section its file goes into, and none where it named the seeded library.
            migrationBuilder.Sql(
                $"""
                UPDATE jobs
                SET payload = (payload - 'libraryId') || jsonb_build_object(
                    'sectionId',
                    CASE WHEN lower(payload ->> 'libraryId') = '{SeededLibraryId}' THEN NULL ELSE payload -> 'libraryId' END)
                WHERE type = 'ingest' AND payload -> 'libraryId' IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An ingest job names a library again, the seeded one where it named no section. No file is moved.
            migrationBuilder.Sql(
                $"""
                UPDATE jobs
                SET payload = (payload - 'sectionId') || jsonb_build_object(
                    'libraryId',
                    CASE WHEN jsonb_typeof(payload -> 'sectionId') = 'null'
                        THEN to_jsonb('{SeededLibraryId}'::text)
                        ELSE payload -> 'sectionId' END)
                WHERE type = 'ingest' AND payload -> 'sectionId' IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_works_sections_section_id",
                table: "works");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sections",
                table: "sections");

            // Each section becomes a library whose folder takes its name, and the seeded library comes back to hold
            // the works in no section.
            migrationBuilder.AddColumn<string>(
                name: "folder_name",
                table: "sections",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                $"""
                UPDATE sections SET folder_name = name;
                INSERT INTO sections (id, name, folder_name, created_at, updated_at)
                VALUES ('{SeededLibraryId}', 'Library', 'Library', '2026-11-11T00:00:00+00:00', '2026-11-11T00:00:00+00:00');
                UPDATE works SET section_id = '{SeededLibraryId}' WHERE section_id IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "folder_name",
                table: "sections",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "section_id",
                table: "works",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.RenameIndex(
                name: "ix_works_section_id",
                table: "works",
                newName: "ix_works_library_id");

            migrationBuilder.RenameColumn(
                name: "section_id",
                table: "works",
                newName: "library_id");

            migrationBuilder.RenameTable(
                name: "sections",
                newName: "libraries");

            migrationBuilder.AddPrimaryKey(
                name: "pk_libraries",
                table: "libraries",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_works_libraries_library_id",
                table: "works",
                column: "library_id",
                principalTable: "libraries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
