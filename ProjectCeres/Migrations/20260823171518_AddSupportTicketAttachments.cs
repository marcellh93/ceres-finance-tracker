using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 12.5 — SupportTicketAttachment table plus its RLS policy, in one migration,
    /// for the same reason as AddSupportTickets: the entity implements IUserOwned, so
    /// ParityTests and RlsParityStartupCheck both fail the moment it exists without a
    /// matching user_isolation policy.
    ///
    /// It could not share the ticket's migration — that one had already run.
    /// </summary>
    public partial class AddSupportTicketAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportTicketAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportTicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StoredPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTicketAttachments_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_SupportTicketId",
                table: "SupportTicketAttachments",
                column: "SupportTicketId");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SupportTicketAttachments"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""SupportTicketAttachments"" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS user_isolation ON ""SupportTicketAttachments"";
                CREATE POLICY user_isolation ON ""SupportTicketAttachments""
                  USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
                  WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS user_isolation ON ""SupportTicketAttachments"";
                ALTER TABLE ""SupportTicketAttachments"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""SupportTicketAttachments"" DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "SupportTicketAttachments");
        }
    }
}
