using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 9.1.5.a — extends the Stage 6.15 <c>TokenLookup</c> pattern to
    /// <c>LockoutUnlockTokens</c> so <c>ConfirmAsync</c> can locate a row in O(1) instead
    /// of running Argon2id against every unconsumed candidate. Closes the suite-wide CPU
    /// saturation that caused 9.1.5.a's shifting auth-tier test flakes.
    ///
    /// Existing rows cannot be HMACed (the raw token is gone) so the backfill writes
    /// a synthetic placeholder (<c>md5(TokenHash || Id::text)</c>) and stamps
    /// <c>ConsumedAt = NOW()</c> in the SAME statement — every pre-9.1.5.a token is
    /// invalidated by the migration. Acceptable because Phase 3 hosted-beta has one
    /// real user and no production lockout-unlock tokens worth preserving. Mirrors
    /// <c>20260511155005_AddTokenLookup.cs</c> exactly. See spec
    /// docs/superpowers/specs/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-design.md § 3.3.
    /// </summary>
    public partial class AddLockoutUnlockTokenLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "TokenLookup",
                table: "LockoutUnlockTokens",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.Sql(@"
                UPDATE ""LockoutUnlockTokens""
                SET ""ConsumedAt"" = NOW(),
                    ""TokenLookup"" = decode(md5(""TokenHash"" || ""Id""::text), 'hex')
                WHERE ""ConsumedAt"" IS NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_LockoutUnlockTokens_TokenLookup",
                table: "LockoutUnlockTokens",
                column: "TokenLookup",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LockoutUnlockTokens_TokenLookup",
                table: "LockoutUnlockTokens");

            migrationBuilder.DropColumn(
                name: "TokenLookup",
                table: "LockoutUnlockTokens");
        }
    }
}
