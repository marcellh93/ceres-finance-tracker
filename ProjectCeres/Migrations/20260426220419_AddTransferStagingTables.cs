using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferStagingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportStagedTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RawAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RawDescription = table.Column<string>(type: "text", nullable: true),
                    CandidateTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportStagedTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportStagedTransfers_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportStagedTransfers_Transactions_CandidateTransactionId",
                        column: x => x.CandidateTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImportTransferExclusions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DescriptionPattern = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportTransferExclusions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportStagedTransfers_AccountId",
                table: "ImportStagedTransfers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportStagedTransfers_CandidateTransactionId",
                table: "ImportStagedTransfers",
                column: "CandidateTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportTransferExclusions_DescriptionPattern",
                table: "ImportTransferExclusions",
                column: "DescriptionPattern",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportStagedTransfers");

            migrationBuilder.DropTable(
                name: "ImportTransferExclusions");
        }
    }
}
