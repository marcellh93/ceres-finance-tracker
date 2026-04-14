using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddLiabilityPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiabilityPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AssetAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LiabilityAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiabilityPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiabilityPayments_Accounts_AssetAccountId",
                        column: x => x.AssetAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiabilityPayments_Accounts_LiabilityAccountId",
                        column: x => x.LiabilityAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiabilityPayments_AssetAccountId",
                table: "LiabilityPayments",
                column: "AssetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LiabilityPayments_LiabilityAccountId",
                table: "LiabilityPayments",
                column: "LiabilityAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiabilityPayments");
        }
    }
}
