using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDateSeparator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateSeparator",
                table: "Settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateSeparator",
                table: "Settings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DateSeparator",
                value: "/");
        }
    }
}
