using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 12.5 — scopes the attachment FK to the ticket's owner, closing a
    /// cross-tenant destructive write.
    ///
    /// Postgres runs foreign-key checks and ON DELETE CASCADE through an internal
    /// referential-integrity trigger that row-level security is NOT applied to, and
    /// FORCE ROW LEVEL SECURITY does not change that. With the previous single-column
    /// FK, both halves were reproduced against the real database on 2026-08-23:
    ///
    ///   1. User B inserted an attachment against user A's ticket, stamping UserId = B.
    ///      Every layer passed — the RLS WITH CHECK pins UserId to the WRITER and says
    ///      nothing about the parent's owner, and the FK existence check bypassed RLS.
    ///   2. User A then deleted their own ticket. The cascade destroyed B's row, with
    ///      no error and no row count that would reveal it.
    ///
    /// Referencing (Id, UserId) makes the divergence unrepresentable, so the cascade
    /// can only ever reach rows the deleter already owns. Both tables were empty when
    /// this ran, so no data migration was needed.
    ///
    /// Also adds the UserId index every other user-owned table has: the RLS policy
    /// injects "UserId" = $1 into every query, so without it each read is a seq scan.
    /// </summary>
    public partial class ScopeAttachmentFkToOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTicketAttachments_SupportTickets_SupportTicketId",
                table: "SupportTicketAttachments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SupportTickets_Id_UserId",
                table: "SupportTickets",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_SupportTicketId_UserId",
                table: "SupportTicketAttachments",
                columns: new[] { "SupportTicketId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTicketAttachments_SupportTickets_SupportTicketId_Use~",
                table: "SupportTicketAttachments",
                columns: new[] { "SupportTicketId", "UserId" },
                principalTable: "SupportTickets",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_UserId",
                table: "SupportTicketAttachments",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportTicketAttachments_UserId",
                table: "SupportTicketAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTicketAttachments_SupportTickets_SupportTicketId_Use~",
                table: "SupportTicketAttachments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SupportTickets_Id_UserId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTicketAttachments_SupportTicketId_UserId",
                table: "SupportTicketAttachments");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTicketAttachments_SupportTickets_SupportTicketId",
                table: "SupportTicketAttachments",
                column: "SupportTicketId",
                principalTable: "SupportTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
