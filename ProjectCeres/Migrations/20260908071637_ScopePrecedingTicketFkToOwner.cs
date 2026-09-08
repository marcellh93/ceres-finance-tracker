using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 12.5 A1 — scopes the SupportTicket follow-up self-FK to the owner: the FK
    /// becomes composite (PrecedingTicketId, UserId) → (Id, UserId), so a follow-up can only
    /// ever reference a preceding ticket owned by the SAME user. The service already refused a
    /// cross-owner follow-up; this makes it structurally unrepresentable (Postgres runs FK
    /// checks through an RI trigger RLS does not touch). Pre-flight aborts with a readable
    /// message if any existing row is already divergent, rather than letting AddForeignKey
    /// fail with a bare 23503. Mirrors ScopeOlderAttachmentFksToOwner.
    /// </summary>
    public partial class ScopePrecedingTicketFkToOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                DECLARE divergent int;
                BEGIN
                    SELECT COUNT(*) INTO divergent
                      FROM ""SupportTickets"" f
                      JOIN ""SupportTickets"" p ON p.""Id"" = f.""PrecedingTicketId""
                     WHERE f.""PrecedingTicketId"" IS NOT NULL
                       AND f.""UserId"" <> p.""UserId"";
                    IF divergent > 0 THEN
                        RAISE EXCEPTION '% SupportTickets follow-up row(s) point at a preceding ticket owned by a different user; resolve them before scoping the FK', divergent;
                    END IF;
                END $$;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_SupportTickets_PrecedingTicketId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_PrecedingTicketId",
                table: "SupportTickets");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_PrecedingTicketId_UserId",
                table: "SupportTickets",
                columns: new[] { "PrecedingTicketId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_SupportTickets_PrecedingTicketId_UserId",
                table: "SupportTickets",
                columns: new[] { "PrecedingTicketId", "UserId" },
                principalTable: "SupportTickets",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_SupportTickets_PrecedingTicketId_UserId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_PrecedingTicketId_UserId",
                table: "SupportTickets");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_PrecedingTicketId",
                table: "SupportTickets",
                column: "PrecedingTicketId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_SupportTickets_PrecedingTicketId",
                table: "SupportTickets",
                column: "PrecedingTicketId",
                principalTable: "SupportTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
