using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddExportJobTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "ExportJobs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "ExportJobs");
        }
    }
}
