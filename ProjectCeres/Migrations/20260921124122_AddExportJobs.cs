using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 13.8 Task 2 — ExportJobs table plus its RLS policy, in one migration
    /// (same reasoning as AddSupportTickets: splitting them leaves ParityTests red).
    /// </summary>
    public partial class AddExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    StoredPath = table.Column<string>(type: "text", nullable: true),
                    TokenLookup = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    EmailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_TokenLookup",
                table: "ExportJobs",
                column: "TokenLookup",
                unique: true,
                filter: "octet_length(\"TokenLookup\") > 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_UserId_Status",
                table: "ExportJobs",
                columns: new[] { "UserId", "Status" });

            // FORCE as well as ENABLE: without FORCE the policy is skipped for the
            // table's owner, and migrations run as ceres_migrator, which owns it.
            migrationBuilder.Sql(@"
                ALTER TABLE ""ExportJobs"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""ExportJobs"" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS user_isolation ON ""ExportJobs"";
                CREATE POLICY user_isolation ON ""ExportJobs""
                  USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
                  WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS user_isolation ON ""ExportJobs"";
                ALTER TABLE ""ExportJobs"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""ExportJobs"" DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "ExportJobs");
        }
    }
}
