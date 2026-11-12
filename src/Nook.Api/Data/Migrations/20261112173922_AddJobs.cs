using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ingest_runs");

            migrationBuilder.DropTable(
                name: "item_steps");

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    parent_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts_made = table.Column<int>(type: "integer", nullable: false),
                    attempts_allowed = table.Column<int>(type: "integer", nullable: false),
                    reclaims_made = table.Column<int>(type: "integer", nullable: false),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_jobs_jobs_parent_job_id",
                        column: x => x.parent_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_steps_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_steps_job_id",
                table: "job_steps",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_parent_job_id",
                table: "jobs",
                column: "parent_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_created_at",
                table: "jobs",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_steps");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.CreateTable(
                name: "ingest_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    library_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_path = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingest_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_ingest_runs_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ingest_runs_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    step_name = table.Column<string>(type: "text", nullable: false),
                    step_version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_steps_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ingest_runs_item_id",
                table: "ingest_runs",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingest_runs_library_id",
                table: "ingest_runs",
                column: "library_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_steps_item_id_step_name",
                table: "item_steps",
                columns: new[] { "item_id", "step_name" },
                unique: true);
        }
    }
}
