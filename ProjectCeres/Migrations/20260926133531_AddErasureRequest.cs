using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 13.9 Task 1 — ErasureRequests table plus its RLS policy, in one migration
    /// (same reasoning as AddExportJobs: splitting them leaves ParityTests red). Also
    /// adds the two ApplicationUser seal columns; AspNetUsers isn't user-owned in the
    /// RLS sense, so those columns carry no policy.
    /// </summary>
    public partial class AddErasureRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ErasedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SealedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ErasureRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecuteAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelTokenLookup = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CancelTokenHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErasureRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErasureRequests_CancelTokenLookup",
                table: "ErasureRequests",
                column: "CancelTokenLookup",
                unique: true,
                filter: "octet_length(\"CancelTokenLookup\") > 0");

            migrationBuilder.CreateIndex(
                name: "IX_ErasureRequests_UserId_Status",
                table: "ErasureRequests",
                columns: new[] { "UserId", "Status" });

            // FORCE as well as ENABLE: without FORCE the policy is skipped for the
            // table's owner, and migrations run as ceres_migrator, which owns it.
            migrationBuilder.Sql(@"
                ALTER TABLE ""ErasureRequests"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""ErasureRequests"" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS user_isolation ON ""ErasureRequests"";
                CREATE POLICY user_isolation ON ""ErasureRequests""
                  USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
                  WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS user_isolation ON ""ErasureRequests"";
                ALTER TABLE ""ErasureRequests"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""ErasureRequests"" DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "ErasureRequests");

            migrationBuilder.DropColumn(
                name: "ErasedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SealedAt",
                table: "AspNetUsers");
        }
    }
}
