using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSentinelSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stage 7 Task 17 — schema-only migration. The deleted HasData declarations would
            // ordinarily emit DeleteData() calls for the seeded Account/Category/Settings rows,
            // but those rows now hold real Phase 1/2 personal-finance data tagged with the
            // sentinel UUID. Task 15's RemapSentinelToFirstUser migration owns the eventual
            // remap onto the first real user; THIS migration must not delete the rows or the
            // remap target disappears. The model-snapshot change recorded by this migration's
            // Designer file is the entire effect.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: the Up() method made no schema or data changes, so there is nothing to undo.
        }
    }
}
