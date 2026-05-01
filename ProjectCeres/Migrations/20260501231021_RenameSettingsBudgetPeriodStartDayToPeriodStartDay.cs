using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class RenameSettingsBudgetPeriodStartDayToPeriodStartDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BudgetPeriodStartDay",
                table: "Settings",
                newName: "PeriodStartDay");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PeriodStartDay",
                table: "Settings",
                newName: "BudgetPeriodStartDay");
        }
    }
}
