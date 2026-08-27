using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 12.6 — clean cutover from the 12.5 single-message ticket to a real
    /// conversation: creates <c>SupportMessages</c> (+ AK, + RLS policy), adds
    /// <c>ExternalRef</c>, remaps the widened <c>Status</c> ints, moves each ticket's
    /// <c>Message</c> body into its first <c>SupportMessage</c>, re-points attachments
    /// from ticket to message via both composite FKs, then drops <c>Message</c>.
    ///
    /// Ordering inside Up() is load-bearing: the status remap and the data-move both
    /// read/write pre-cutover shapes, so they run BEFORE the column rename that changes
    /// what "SupportTicketAttachments.SupportMessageId" (renamed in place) actually
    /// means, and the attachment repoint UPDATE runs AFTER SupportMessages is populated,
    /// since it looks up each ticket's first message id.
    /// </summary>
    public partial class AddSupportConversationModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Step 1: pre-flight guard — abort if a status value outside the old
            // 4-value range shows up (would indicate a concurrent write during migration
            // or a schema drift the remap below does not know how to handle). ----
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM ""SupportTickets"" WHERE ""Status"" NOT IN (0,1,2,3)) THEN
                    RAISE EXCEPTION 'AddSupportConversationModel: unexpected SupportTickets.Status value; aborting remap';
                  END IF;
                END $$;
            ");

            // ---- Step 2: status value-remap. This is a VALUE rewrite, not a name map —
            // old Closed=3 must become 4, or it collides into the new Solved=3. ----
            migrationBuilder.Sql(@"
                UPDATE ""SupportTickets"" SET ""Status"" = CASE ""Status""
                    WHEN 0 THEN 0   -- Open       -> Open
                    WHEN 1 THEN 0   -- InProgress -> Open
                    WHEN 2 THEN 3   -- Resolved   -> Solved
                    WHEN 3 THEN 4   -- Closed     -> Closed
                    ELSE ""Status"" END;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTicketAttachments_SupportTickets_SupportTicketId_Use~",
                table: "SupportTicketAttachments");

            migrationBuilder.AddColumn<string>(
                name: "ExternalRef",
                table: "SupportTickets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportTicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorRole = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportMessages", x => x.Id);
                    table.UniqueConstraint("AK_SupportMessages_Id_UserId", x => new { x.Id, x.UserId });
                    table.ForeignKey(
                        name: "FK_SupportMessages_SupportTickets_SupportTicketId_UserId",
                        columns: x => new { x.SupportTicketId, x.UserId },
                        principalTable: "SupportTickets",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_SupportTicketId_UserId",
                table: "SupportMessages",
                columns: new[] { "SupportTicketId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_UserId",
                table: "SupportMessages",
                column: "UserId");

            // FORCE as well as ENABLE: without FORCE the policy is skipped for the
            // table's owner, and migrations run as ceres_migrator, which owns it.
            migrationBuilder.Sql(@"
                ALTER TABLE ""SupportMessages"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""SupportMessages"" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS user_isolation ON ""SupportMessages"";
                CREATE POLICY user_isolation ON ""SupportMessages""
                  USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
                  WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
            ");

            // ---- Step 3: data move — one SupportMessage per existing ticket, carrying
            // the ticket's old Message body forward as the first message. Must run before
            // the attachment repoint (which looks up these rows) and before Message is
            // dropped (which would lose the data this reads). ----
            migrationBuilder.Sql(@"
                INSERT INTO ""SupportMessages"" (""Id"", ""UserId"", ""SupportTicketId"", ""AuthorRole"", ""Body"", ""CreatedAt"")
                SELECT gen_random_uuid(), t.""UserId"", t.""Id"", 0, t.""Message"", t.""CreatedAt""
                FROM ""SupportTickets"" t;
            ");

            // ---- Step 4: repoint attachments to their ticket's first message. The
            // column has NOT been renamed yet at this point, so this still targets
            // "SupportTicketId" by name — it is renamed to "SupportMessageId" right
            // after, once every row already holds a message id instead of a ticket id. ----
            migrationBuilder.Sql(@"
                UPDATE ""SupportTicketAttachments"" a
                SET ""SupportTicketId"" = m.""Id""
                FROM ""SupportMessages"" m
                WHERE m.""SupportTicketId"" = a.""SupportTicketId""
                  AND a.""UserId"" = m.""UserId"";
            ");

            migrationBuilder.RenameColumn(
                name: "SupportTicketId",
                table: "SupportTicketAttachments",
                newName: "SupportMessageId");

            migrationBuilder.RenameIndex(
                name: "IX_SupportTicketAttachments_SupportTicketId_UserId",
                table: "SupportTicketAttachments",
                newName: "IX_SupportTicketAttachments_SupportMessageId_UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTicketAttachments_SupportMessages_SupportMessageId_U~",
                table: "SupportTicketAttachments",
                columns: new[] { "SupportMessageId", "UserId" },
                principalTable: "SupportMessages",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Cascade);

            // ---- Step 5: drop Message last — every row's body is now safely copied
            // into SupportMessages before the source column disappears. ----
            migrationBuilder.DropColumn(
                name: "Message",
                table: "SupportTickets");
        }

        /// <summary>
        /// Best-effort down for a pre-launch beta: reverses the shape and moves data back
        /// as best it can, but is NOT lossless — a ticket with more than one message keeps
        /// only its first message's body, and any second-or-later message (and its
        /// attachments) is discarded along with the SupportMessages table. Must not throw.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Message",
                table: "SupportTickets",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: false,
                defaultValue: "");

            // Move each ticket's first message body back onto SupportTickets.Message.
            migrationBuilder.Sql(@"
                UPDATE ""SupportTickets"" t SET ""Message"" = first_msg.""Body""
                FROM (
                    SELECT DISTINCT ON (""SupportTicketId"") ""SupportTicketId"", ""Body""
                    FROM ""SupportMessages""
                    ORDER BY ""SupportTicketId"", ""CreatedAt"" ASC
                ) AS first_msg
                WHERE first_msg.""SupportTicketId"" = t.""Id"";
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTicketAttachments_SupportMessages_SupportMessageId_U~",
                table: "SupportTicketAttachments");

            // Repoint surviving attachments back to their message's ticket. Attachments on
            // any message other than a ticket's first are orphaned by this — no ticket
            // column exists to hold more than one attachment origin, so they get pointed
            // at the ticket anyway (best effort) rather than left dangling.
            migrationBuilder.Sql(@"
                UPDATE ""SupportTicketAttachments"" a
                SET ""SupportMessageId"" = m.""SupportTicketId""
                FROM ""SupportMessages"" m
                WHERE m.""Id"" = a.""SupportMessageId"";
            ");

            migrationBuilder.RenameColumn(
                name: "SupportMessageId",
                table: "SupportTicketAttachments",
                newName: "SupportTicketId");

            migrationBuilder.RenameIndex(
                name: "IX_SupportTicketAttachments_SupportMessageId_UserId",
                table: "SupportTicketAttachments",
                newName: "IX_SupportTicketAttachments_SupportTicketId_UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTicketAttachments_SupportTickets_SupportTicketId_Use~",
                table: "SupportTicketAttachments",
                columns: new[] { "SupportTicketId", "UserId" },
                principalTable: "SupportTickets",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS user_isolation ON ""SupportMessages"";
                ALTER TABLE ""SupportMessages"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""SupportMessages"" DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "SupportMessages");

            migrationBuilder.DropColumn(
                name: "ExternalRef",
                table: "SupportTickets");

            // Restore the 4-value status ordinals (best-effort inverse of the Up() remap;
            // OnHold=2 has no old-schema equivalent and folds into Resolved).
            migrationBuilder.Sql(@"
                UPDATE ""SupportTickets"" SET ""Status"" = CASE ""Status""
                    WHEN 0 THEN 0   -- Open    -> Open
                    WHEN 1 THEN 0   -- Pending -> Open (no old equivalent; folds to Open)
                    WHEN 2 THEN 2   -- OnHold  -> Resolved (no old equivalent; folds to Resolved)
                    WHEN 3 THEN 2   -- Solved  -> Resolved
                    WHEN 4 THEN 3   -- Closed  -> Closed
                    ELSE ""Status"" END;
            ");
        }
    }
}
