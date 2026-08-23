using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 12.5 — SupportTicket table plus its RLS policy, in one migration.
    ///
    /// The two halves are inseparable: SupportTicket implements IUserOwned, so
    /// UserOwnedModel.RlsTables derives it from the EF model the moment the entity
    /// exists. ParityTests then fails and RlsParityStartupCheck refuses to boot
    /// until a matching user_isolation policy is installed. Splitting the table and
    /// the policy across two migrations leaves the build red in between.
    ///
    /// Table privileges are NOT granted here: pg_default_acl already grants
    /// SELECT/INSERT/UPDATE/DELETE to ceres_app and ceres_admin on every table
    /// ceres_migrator creates (verified 2026-08-23).
    /// </summary>
    public partial class AddSupportTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    PrecedingTicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTickets_SupportTickets_PrecedingTicketId",
                        column: x => x.PrecedingTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_PrecedingTicketId",
                table: "SupportTickets",
                column: "PrecedingTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_UserId_CreatedAt",
                table: "SupportTickets",
                columns: new[] { "UserId", "CreatedAt" });

            // FORCE as well as ENABLE: without FORCE the policy is skipped for the
            // table's owner, and migrations run as ceres_migrator, which owns it.
            migrationBuilder.Sql(@"
                ALTER TABLE ""SupportTickets"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""SupportTickets"" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS user_isolation ON ""SupportTickets"";
                CREATE POLICY user_isolation ON ""SupportTickets""
                  USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
                  WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS user_isolation ON ""SupportTickets"";
                ALTER TABLE ""SupportTickets"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""SupportTickets"" DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "SupportTickets");
        }
    }
}
