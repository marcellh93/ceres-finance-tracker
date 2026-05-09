using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaBackupCodesAndReplayPrevention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TotpReplayEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotpReplayEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserMfaBackupCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedFromIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMfaBackupCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TotpReplayEntries_AcceptedAt",
                table: "TotpReplayEntries",
                column: "AcceptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TotpReplayEntries_UserId",
                table: "TotpReplayEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMfaBackupCodes_UserId",
                table: "UserMfaBackupCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMfaBackupCodes_UserId_Unused",
                table: "UserMfaBackupCodes",
                columns: new[] { "UserId", "UsedAt" },
                filter: "\"UsedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TotpReplayEntries");

            migrationBuilder.DropTable(
                name: "UserMfaBackupCodes");
        }
    }
}
