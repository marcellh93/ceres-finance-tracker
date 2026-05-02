using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToUserOwnedEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Transfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Settings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "SavedReports",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "RecurringTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "LiabilityPayments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "CategoryBudgets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Budgets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Accounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Pre-auth backfill: stamp every existing user-created row with the sentinel
            // UserId. EF's AddColumn default of Guid.Empty would otherwise leave real user
            // data (non-seeded rows) with a UserId that does not match SingleUserAccessor.
            // System categories (IsSystem = true) keep their NULL UserId.
            const string Sentinel = "'00000000-0000-0000-0000-000000000001'";
            migrationBuilder.Sql($"UPDATE \"Accounts\"              SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"Budgets\"               SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"CategoryBudgets\"       SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"LiabilityPayments\"     SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"RecurringTransactions\" SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"SavedReports\"          SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"Settings\"              SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"Transactions\"          SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"Transfers\"             SET \"UserId\" = {Sentinel} WHERE \"UserId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql($"UPDATE \"Categories\"            SET \"UserId\" = {Sentinel} WHERE \"UserId\" IS NULL AND \"IsSystem\" = false;");

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                column: "UserId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000002"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000003"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000004"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000005"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000006"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000007"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000008"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000009"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000010"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000011"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000012"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000013"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000014"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000015"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000016"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000017"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000018"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000019"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000020"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000021"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000022"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000023"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000024"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000025"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000026"),
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "UserId",
                value: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_UserId",
                table: "Transfers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_UserId",
                table: "Settings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedReports_UserId",
                table: "SavedReports",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_UserId",
                table: "RecurringTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LiabilityPayments_UserId",
                table: "LiabilityPayments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryBudgets_UserId",
                table: "CategoryBudgets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId",
                table: "Categories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId",
                table: "Budgets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId",
                table: "Accounts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transfers_UserId",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Settings_UserId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_SavedReports_UserId",
                table: "SavedReports");

            migrationBuilder.DropIndex(
                name: "IX_RecurringTransactions_UserId",
                table: "RecurringTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LiabilityPayments_UserId",
                table: "LiabilityPayments");

            migrationBuilder.DropIndex(
                name: "IX_CategoryBudgets_UserId",
                table: "CategoryBudgets");

            migrationBuilder.DropIndex(
                name: "IX_Categories_UserId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_UserId",
                table: "Budgets");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_UserId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "SavedReports");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "RecurringTransactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "LiabilityPayments");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "CategoryBudgets");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Accounts");
        }
    }
}
