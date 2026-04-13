using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CategoryTypeId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    LifestyleTag = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_CategoryTypes_CategoryTypeId",
                        column: x => x.CategoryTypeId,
                        principalTable: "CategoryTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AccountTypeId = table.Column<int>(type: "integer", nullable: false),
                    CurrencyId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_AccountTypes_AccountTypeId",
                        column: x => x.AccountTypeId,
                        principalTable: "AccountTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Accounts_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrencyId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NumberFormat = table.Column<string>(type: "text", nullable: false),
                    DateFormat = table.Column<string>(type: "text", nullable: false),
                    DateSeparator = table.Column<string>(type: "text", nullable: false),
                    DefaultCurrencyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Settings_Currencies_DefaultCurrencyId",
                        column: x => x.DefaultCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CategoryBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyId = table.Column<int>(type: "integer", nullable: false),
                    LimitAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryBudgets_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CategoryBudgets_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Frequency = table.Column<string>(type: "text", nullable: false),
                    DayOfPeriod = table.Column<int>(type: "integer", nullable: true),
                    NextDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringTransactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringTransactions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SavedReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ReportTypeId = table.Column<int>(type: "integer", nullable: false),
                    DateFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    DateTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrencyId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedReports_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SavedReports_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SavedReports_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SavedReports_ReportTypes_ReportTypeId",
                        column: x => x.ReportTypeId,
                        principalTable: "ReportTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transfers_Accounts_DestAccountId",
                        column: x => x.DestAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Accounts_SourceAccountId",
                        column: x => x.SourceAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    StoredPath = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionAttachments_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AccountTypes",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Asset" },
                    { 2, "Liability" }
                });

            migrationBuilder.InsertData(
                table: "CategoryTypes",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Income" },
                    { 2, "Expense" }
                });

            migrationBuilder.InsertData(
                table: "Currencies",
                columns: new[] { "Id", "Code", "Name", "Symbol" },
                values: new object[,]
                {
                    { 1, "EUR", "Euro", "€" },
                    { 2, "USD", "US Dollar", "$" },
                    { 3, "GBP", "British Pound", "£" },
                    { 4, "COP", "Colombian Peso", "$" },
                    { 5, "ARS", "Argentine Peso", "$" },
                    { 6, "VED", "Venezuelan Bolívar Digital", "Bs.D" }
                });

            migrationBuilder.InsertData(
                table: "ReportTypes",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Net Worth Statement" },
                    { 2, "Income & Expense Summary" },
                    { 3, "Expense Breakdown by Category" },
                    { 4, "Transaction History" }
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "AccountTypeId", "CurrencyId", "Description", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), 1, 1, null, true, "Cash" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), 1, 1, null, true, "Checking Account" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), 1, 1, null, true, "Savings Account" },
                    { new Guid("10000000-0000-0000-0000-000000000004"), 2, 1, null, true, "Credit Card" }
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "CategoryTypeId", "IsActive", "IsSystem", "LifestyleTag", "Name" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), 1, true, true, null, "Opening Balance" },
                    { new Guid("20000000-0000-0000-0000-000000000002"), 1, true, false, null, "Salary" },
                    { new Guid("20000000-0000-0000-0000-000000000003"), 1, true, false, null, "Freelance Income" },
                    { new Guid("20000000-0000-0000-0000-000000000004"), 1, true, false, null, "Rental Income" },
                    { new Guid("20000000-0000-0000-0000-000000000005"), 1, true, false, null, "Investment Income" },
                    { new Guid("20000000-0000-0000-0000-000000000006"), 1, true, false, null, "Business Income" },
                    { new Guid("20000000-0000-0000-0000-000000000007"), 1, true, false, null, "Other Income" },
                    { new Guid("20000000-0000-0000-0000-000000000008"), 2, true, false, "Needs", "Housing / Rent" },
                    { new Guid("20000000-0000-0000-0000-000000000009"), 2, true, false, "Needs", "Utilities" },
                    { new Guid("20000000-0000-0000-0000-000000000010"), 2, true, false, "Needs", "Groceries" },
                    { new Guid("20000000-0000-0000-0000-000000000011"), 2, true, false, "Needs", "Transport" },
                    { new Guid("20000000-0000-0000-0000-000000000012"), 2, true, false, "Needs", "Fuel" },
                    { new Guid("20000000-0000-0000-0000-000000000013"), 2, true, false, "Needs", "Healthcare" },
                    { new Guid("20000000-0000-0000-0000-000000000014"), 2, true, false, "Needs", "Insurance" },
                    { new Guid("20000000-0000-0000-0000-000000000015"), 2, true, false, "Wants", "Subscriptions" },
                    { new Guid("20000000-0000-0000-0000-000000000016"), 2, true, false, "Wants", "Dining Out" },
                    { new Guid("20000000-0000-0000-0000-000000000017"), 2, true, false, "Wants", "Entertainment" },
                    { new Guid("20000000-0000-0000-0000-000000000018"), 2, true, false, "Wants", "Clothing" },
                    { new Guid("20000000-0000-0000-0000-000000000019"), 2, true, false, "Needs", "Personal Care" },
                    { new Guid("20000000-0000-0000-0000-000000000020"), 2, true, false, "Needs", "Education" },
                    { new Guid("20000000-0000-0000-0000-000000000021"), 2, true, false, "Wants", "Travel" },
                    { new Guid("20000000-0000-0000-0000-000000000022"), 2, true, false, "Needs", "Home & Garden" },
                    { new Guid("20000000-0000-0000-0000-000000000023"), 2, true, false, "Wants", "Gifts & Donations" },
                    { new Guid("20000000-0000-0000-0000-000000000024"), 2, true, false, null, "Other Expenses" }
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "DateFormat", "DateSeparator", "DefaultCurrencyId", "NumberFormat" },
                values: new object[] { 1, "DD/MM/YYYY", "/", 1, "comma_decimal" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_AccountTypeId",
                table: "Accounts",
                column: "AccountTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CurrencyId",
                table: "Accounts",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CurrencyId",
                table: "Budgets",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_CategoryTypeId",
                table: "Categories",
                column: "CategoryTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryBudgets_CategoryId",
                table: "CategoryBudgets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryBudgets_CurrencyId",
                table: "CategoryBudgets",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_AccountId",
                table: "RecurringTransactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_CategoryId",
                table: "RecurringTransactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedReports_AccountId",
                table: "SavedReports",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedReports_CategoryId",
                table: "SavedReports",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedReports_CurrencyId",
                table: "SavedReports",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedReports_ReportTypeId",
                table: "SavedReports",
                column: "ReportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_DefaultCurrencyId",
                table: "Settings",
                column: "DefaultCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAttachments_TransactionId",
                table: "TransactionAttachments",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BudgetId",
                table: "Transactions",
                column: "BudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_DestAccountId",
                table: "Transfers",
                column: "DestAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_SourceAccountId",
                table: "Transfers",
                column: "SourceAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryBudgets");

            migrationBuilder.DropTable(
                name: "RecurringTransactions");

            migrationBuilder.DropTable(
                name: "SavedReports");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TransactionAttachments");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropTable(
                name: "ReportTypes");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "AccountTypes");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "CategoryTypes");
        }
    }
}
