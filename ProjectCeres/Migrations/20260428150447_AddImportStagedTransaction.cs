using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddImportStagedTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportStagedTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RawAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RawDescription = table.Column<string>(type: "text", nullable: true),
                    MatchedTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportStagedTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportStagedTransactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportStagedTransactions_Transactions_MatchedTransactionId",
                        column: x => x.MatchedTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportStagedTransactions_AccountId",
                table: "ImportStagedTransactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportStagedTransactions_MatchedTransactionId",
                table: "ImportStagedTransactions",
                column: "MatchedTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportStagedTransactions");
        }
    }
}
