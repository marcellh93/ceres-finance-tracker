using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class RenameToImportProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "CsvImportProfiles",
                newName: "ImportProfiles");

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "ImportProfiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Csv");

            migrationBuilder.AddColumn<string>(
                name: "SheetName",
                table: "ImportProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SheetName", table: "ImportProfiles");
            migrationBuilder.DropColumn(name: "Format",    table: "ImportProfiles");

            migrationBuilder.RenameTable(
                name: "ImportProfiles",
                newName: "CsvImportProfiles");
        }
    }
}
