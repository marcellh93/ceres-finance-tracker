using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToImportEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportTransferExclusions_DescriptionPattern",
                table: "ImportTransferExclusions");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ImportTransferExclusions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ImportStagedTransfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ImportStagedTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ImportProfiles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Pre-auth backfill: stamp existing rows with the sentinel UserId so they
            // belong to the bootstrap user, matching SingleUserAccessor.UserId.
            const string Sentinel = "'00000000-0000-0000-0000-000000000001'";
            migrationBuilder.Sql($"UPDATE \"ImportProfiles\"             SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"ImportStagedTransactions\"   SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"ImportStagedTransfers\"      SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"ImportTransferExclusions\"   SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateIndex(
                name: "IX_ImportTransferExclusions_UserId",
                table: "ImportTransferExclusions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportTransferExclusions_UserId_DescriptionPattern",
                table: "ImportTransferExclusions",
                columns: new[] { "UserId", "DescriptionPattern" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportStagedTransfers_UserId",
                table: "ImportStagedTransfers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportStagedTransactions_UserId",
                table: "ImportStagedTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportProfiles_UserId",
                table: "ImportProfiles",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportTransferExclusions_UserId",
                table: "ImportTransferExclusions");

            migrationBuilder.DropIndex(
                name: "IX_ImportTransferExclusions_UserId_DescriptionPattern",
                table: "ImportTransferExclusions");

            migrationBuilder.DropIndex(
                name: "IX_ImportStagedTransfers_UserId",
                table: "ImportStagedTransfers");

            migrationBuilder.DropIndex(
                name: "IX_ImportStagedTransactions_UserId",
                table: "ImportStagedTransactions");

            migrationBuilder.DropIndex(
                name: "IX_ImportProfiles_UserId",
                table: "ImportProfiles");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ImportTransferExclusions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ImportStagedTransfers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ImportStagedTransactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ImportProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_ImportTransferExclusions_DescriptionPattern",
                table: "ImportTransferExclusions",
                column: "DescriptionPattern",
                unique: true);
        }
    }
}
