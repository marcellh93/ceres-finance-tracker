using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddUncategorizedSystemCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "CategoryTypeId", "IsActive", "IsSystem", "LifestyleTag", "Name" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000025"), 1, true, true, null, "Uncategorized Income" },
                    { new Guid("20000000-0000-0000-0000-000000000026"), 2, true, true, null, "Uncategorized Expense" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000025"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000026"));
        }
    }
}
