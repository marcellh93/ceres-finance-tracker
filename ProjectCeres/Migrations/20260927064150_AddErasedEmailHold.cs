using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 13.9 Task 6b — ErasedEmailHolds table plus its RLS policy, in one
    /// migration (same reasoning as AddErasureRequest: splitting them leaves
    /// ParityTests red).
    /// </summary>
    public partial class AddErasedEmailHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErasedEmailHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailFingerprint = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ErasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErasedEmailHolds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErasedEmailHolds_EmailFingerprint",
                table: "ErasedEmailHolds",
                column: "EmailFingerprint",
                unique: true);

            // FORCE as well as ENABLE: without FORCE the policy is skipped for the
            // table's owner, and migrations run as ceres_migrator, which owns it.
            migrationBuilder.Sql(@"
                ALTER TABLE ""ErasedEmailHolds"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""ErasedEmailHolds"" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS user_isolation ON ""ErasedEmailHolds"";
                CREATE POLICY user_isolation ON ""ErasedEmailHolds""
                  USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
                  WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS user_isolation ON ""ErasedEmailHolds"";
                ALTER TABLE ""ErasedEmailHolds"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""ErasedEmailHolds"" DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "ErasedEmailHolds");
        }
    }
}
