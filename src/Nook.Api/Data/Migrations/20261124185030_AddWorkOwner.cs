using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "works",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_works_owner_id",
                table: "works",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_works_asp_net_users_owner_id",
                table: "works",
                column: "owner_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Works that existed before owners belong to whoever first added them, when that user still exists.
            migrationBuilder.Sql(
                """
                UPDATE works
                SET owner_id = first_upload.actor_id
                FROM (
                    SELECT DISTINCT ON (a.subject_id) a.subject_id, a.actor_id
                    FROM audit_entries a
                    WHERE a.action = 'upload.added'
                      AND a.subject_type = 'work'
                    ORDER BY a.subject_id, a.created_at, a.id) AS first_upload
                JOIN asp_net_users u ON u.id = first_upload.actor_id
                WHERE first_upload.subject_id = works.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_works_asp_net_users_owner_id",
                table: "works");

            migrationBuilder.DropIndex(
                name: "ix_works_owner_id",
                table: "works");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "works");
        }
    }
}
