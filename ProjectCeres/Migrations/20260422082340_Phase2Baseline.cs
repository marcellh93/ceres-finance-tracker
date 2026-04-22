using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCleared",
                table: "Transfers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCleared",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedAmount",
                table: "RecurringTransactions",
                type: "numeric(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AddColumn<string>(
                name: "ReminderBehaviour",
                table: "RecurringTransactions",
                type: "text",
                nullable: false,
                defaultValue: "SnapToCalendarDay");

            migrationBuilder.AddColumn<string>(
                name: "GoalType",
                table: "Budgets",
                type: "text",
                nullable: false,
                defaultValue: "Spending");

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedAccountId",
                table: "Budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromSpendable",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "InterestRate",
                table: "Accounts",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiabilityRepaymentType",
                table: "Accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CsvImportProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ColumnMappings = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CsvImportProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransferAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    StoredPath = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferAttachments_Transfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "Transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                columns: new[] { "ExcludeFromSpendable", "InterestRate", "LiabilityRepaymentType" },
                values: new object[] { false, null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"),
                columns: new[] { "ExcludeFromSpendable", "InterestRate", "LiabilityRepaymentType" },
                values: new object[] { false, null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"),
                columns: new[] { "ExcludeFromSpendable", "InterestRate", "LiabilityRepaymentType" },
                values: new object[] { false, null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"),
                columns: new[] { "ExcludeFromSpendable", "InterestRate", "LiabilityRepaymentType" },
                values: new object[] { false, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_LinkedAccountId",
                table: "Budgets",
                column: "LinkedAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferAttachments_TransferId",
                table: "TransferAttachments",
                column: "TransferId");

            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_Accounts_LinkedAccountId",
                table: "Budgets",
                column: "LinkedAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Budgets_Accounts_LinkedAccountId",
                table: "Budgets");

            migrationBuilder.DropTable(
                name: "CsvImportProfiles");

            migrationBuilder.DropTable(
                name: "TransferAttachments");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_LinkedAccountId",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "IsCleared",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "IsCleared",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReminderBehaviour",
                table: "RecurringTransactions");

            migrationBuilder.DropColumn(
                name: "GoalType",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "LinkedAccountId",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "ExcludeFromSpendable",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "InterestRate",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "LiabilityRepaymentType",
                table: "Accounts");

            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedAmount",
                table: "RecurringTransactions",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldNullable: true);
        }
    }
}
