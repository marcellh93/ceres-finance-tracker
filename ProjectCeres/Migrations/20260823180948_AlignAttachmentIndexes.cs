using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 12.5 — brings the model and the database into agreement on the attachment
    /// indexes.
    ///
    /// ScopeAttachmentFkToOwner created IX_SupportTicketAttachments_UserId directly in
    /// SQL without a matching HasIndex in the entity config, so the index existed in the
    /// database and in no model or snapshot. MigrationDriftTests compares model to
    /// snapshot and therefore could not see it — a future scaffolded migration would have
    /// been generated without knowledge of it. Found by the Stage 12.5 review.
    ///
    /// Also drops IX_SupportTicketAttachments_SupportTicketId: the composite FK index
    /// (SupportTicketId, UserId) already serves every query its leading column would, so
    /// it was pure write-path cost.
    ///
    /// CreateIndex is guarded with IF NOT EXISTS because the UserId index is already
    /// present on every database that applied ScopeAttachmentFkToOwner — this migration
    /// exists to make the MODEL match, not to change the schema there.
    /// </summary>
    public partial class AlignAttachmentIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportTicketAttachments_SupportTicketId",
                table: "SupportTicketAttachments");

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_SupportTicketAttachments_UserId""
                    ON ""SupportTicketAttachments"" (""UserId"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportTicketAttachments_UserId",
                table: "SupportTicketAttachments");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_SupportTicketId",
                table: "SupportTicketAttachments",
                column: "SupportTicketId");
        }
    }
}
