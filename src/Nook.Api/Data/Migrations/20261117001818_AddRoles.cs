using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Nook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoles : Migration
    {
        /// <summary>The fixed ids of the roles a user could hold before roles were a table.</summary>
        private const string AdminId = "01a1c7e7-8000-7000-8000-0000000000a1";
        private const string ContributorId = "01a1c7e7-8000-7000-8000-0000000000a2";
        private const string ViewerId = "01a1c7e7-8000-7000-8000-0000000000a3";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    built_in = table.Column<string>(type: "text", nullable: true),
                    content_access = table.Column<string>(type: "text", nullable: false),
                    default_area_access = table.Column<string>(type: "text", nullable: false),
                    all_sections = table.Column<bool>(type: "boolean", nullable: false),
                    can_upload = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "all_sections", "built_in", "can_upload", "content_access", "created_at", "default_area_access", "name", "updated_at" },
                values: new object[,]
                {
                    { new Guid("01a1c7e7-8000-7000-8000-0000000000a1"), true, "Admin", true, "Download", new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Download", "Admin", new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("01a1c7e7-8000-7000-8000-0000000000a2"), true, "Contributor", false, "Download", new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Download", "Contributor", new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("01a1c7e7-8000-7000-8000-0000000000a3"), true, "Viewer", false, "Download", new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Download", "Viewer", new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("01a1c7e7-8000-7000-8000-0000000000a4"), false, "Anonymous", false, "None", new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "None", "Anonymous", new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            // Each user keeps the role their old one names, so the column is filled before it is made required, and the
            // old one goes last. Member is now Contributor and Guest is now Viewer.
            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "asp_net_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                $"""
                UPDATE asp_net_users SET role_id = CASE role
                    WHEN 'Admin' THEN '{AdminId}'::uuid
                    WHEN 'Member' THEN '{ContributorId}'::uuid
                    ELSE '{ViewerId}'::uuid
                END;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "asp_net_users",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_role_id",
                table: "asp_net_users",
                column: "role_id");

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_users_roles_role_id",
                table: "asp_net_users",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "role",
                table: "asp_net_users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The role goes back to text under the names the code before roles were a table reads.
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "asp_net_users",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                $"""
                UPDATE asp_net_users SET role = CASE role_id
                    WHEN '{AdminId}'::uuid THEN 'Admin'
                    WHEN '{ContributorId}'::uuid THEN 'Member'
                    ELSE 'Guest'
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "asp_net_users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_users_roles_role_id",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "ix_asp_net_users_role_id",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "asp_net_users");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
