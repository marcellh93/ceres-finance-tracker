using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 6.15 — Adds the HMAC-SHA256-derived <c>TokenLookup</c> column + unique index
    /// to <c>PasswordResetTokens</c> and <c>EmailChangeTokens</c> so /confirm and /revoke
    /// can locate a row in O(1) instead of running Argon2id against every unconsumed
    /// candidate. Closes the Argon2id-amplification DoS on the verify path.
    ///
    /// Existing rows cannot be HMACed (the raw token is gone) so the backfill writes
    /// a synthetic placeholder (<c>md5(TokenHash || Id::text)</c>) and stamps
    /// <c>ConsumedAt = NOW()</c> in the SAME statement — every pre-6.15 token is
    /// invalidated by the migration, which is acceptable because the affected
    /// populations are dev/test only (Phase 3 has not launched). See spec
    /// docs/superpowers/specs/2026-05-11-stage-6-15-token-lookup-design.md § 3.3.
    /// </summary>
    public partial class AddTokenLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "TokenLookup",
                table: "PasswordResetTokens",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "TokenLookup",
                table: "EmailChangeTokens",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0]);

            // Backfill existing rows so the unique index can be created. Pre-6.15 raw
            // tokens cannot be recomputed; write a per-row placeholder via md5 and mark
            // every row consumed in the SAME statement so no legacy row can match a
            // real verify call after the migration completes.
            migrationBuilder.Sql(@"
                UPDATE ""PasswordResetTokens""
                SET ""ConsumedAt"" = NOW(),
                    ""TokenLookup"" = decode(md5(""TokenHash"" || ""Id""::text), 'hex')
                WHERE ""ConsumedAt"" IS NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""EmailChangeTokens""
                SET ""ConsumedAt"" = NOW(),
                    ""TokenLookup"" = decode(md5(""TokenHash"" || ""Id""::text), 'hex')
                WHERE ""ConsumedAt"" IS NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenLookup",
                table: "PasswordResetTokens",
                column: "TokenLookup",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailChangeTokens_TokenLookup",
                table: "EmailChangeTokens",
                column: "TokenLookup",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_TokenLookup",
                table: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_EmailChangeTokens_TokenLookup",
                table: "EmailChangeTokens");

            migrationBuilder.DropColumn(
                name: "TokenLookup",
                table: "PasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "TokenLookup",
                table: "EmailChangeTokens");
        }
    }
}
