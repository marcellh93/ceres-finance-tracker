using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <inheritdoc />
    public partial class AddFailedLoginAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FailedLoginAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailAttempted = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedLoginAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailedLoginAttempts_EmailAttempted_OccurredAt",
                table: "FailedLoginAttempts",
                columns: new[] { "EmailAttempted", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FailedLoginAttempts_IpAddress_OccurredAt",
                table: "FailedLoginAttempts",
                columns: new[] { "IpAddress", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FailedLoginAttempts_OccurredAt",
                table: "FailedLoginAttempts",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FailedLoginAttempts");
        }
    }
}
