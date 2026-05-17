using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 9.1.5.h — Backfill every normalized Identity column to match
    /// <c>LowercaseLookupNormalizer</c>'s output. The columns covered:
    /// <c>AspNetUsers.NormalizedEmail</c>, <c>AspNetUsers.NormalizedUserName</c>,
    /// <c>AspNetRoles.NormalizedName</c>.
    ///
    /// Idempotent: each <c>UPDATE</c> filters out rows whose value already equals
    /// its own lowercased version, so re-runs are no-ops. NULL rows are preserved.
    ///
    /// <c>AspNetRoles</c> is empty at the time of this migration (no <c>RoleManager</c>
    /// usage), but is included so the migration's contract becomes "all normalized
    /// identity columns are kept in sync with the active normalizer" — future
    /// commits introducing roles inherit the protection automatically.
    ///
    /// Closes the silent-login-break incident from commit <c>4b35911</c>: that commit
    /// swapped <c>UpperInvariantLookupNormalizer</c> for <c>LowercaseLookupNormalizer</c>
    /// without a backfill, leaving pre-existing uppercase rows unmatchable by
    /// <c>FindByEmailAsync</c>/<c>FindByNameAsync</c>. See spec
    /// <c>docs/superpowers/specs/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-design.md</c>.
    /// </summary>
    public partial class BackfillIdentityNormalizedToLowercase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill NormalizedEmail to match LowercaseLookupNormalizer output.
            // Idempotent — the WHERE filter is false for rows already lowercase, so
            // a re-run on a fully-lowercase table is a no-op (0 rows affected).
            migrationBuilder.Sql(@"
                UPDATE ""AspNetUsers""
                SET ""NormalizedEmail"" = LOWER(""NormalizedEmail"")
                WHERE ""NormalizedEmail"" IS NOT NULL
                  AND ""NormalizedEmail"" <> LOWER(""NormalizedEmail"");
            ");

            migrationBuilder.Sql(@"
                UPDATE ""AspNetUsers""
                SET ""NormalizedUserName"" = LOWER(""NormalizedUserName"")
                WHERE ""NormalizedUserName"" IS NOT NULL
                  AND ""NormalizedUserName"" <> LOWER(""NormalizedUserName"");
            ");

            // AspNetRoles is currently empty (no roles seeded, no RoleManager usage
            // beyond a dead constructor injection in ApplicationUserClaimsPrincipalFactory).
            // Including this UPDATE now is a no-op against zero rows, but the migration's
            // contract becomes "every normalized identity column matches the active
            // normalizer's output" — so a future commit that introduces roles inherits
            // the protection automatically, without needing a same-commit follow-up
            // migration that someone has to remember to add.
            migrationBuilder.Sql(@"
                UPDATE ""AspNetRoles""
                SET ""NormalizedName"" = LOWER(""NormalizedName"")
                WHERE ""NormalizedName"" IS NOT NULL
                  AND ""NormalizedName"" <> LOWER(""NormalizedName"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting this migration would restore the broken state — uppercase
            // rows that the LowercaseLookupNormalizer cannot match. The correct
            // recovery if a rollback is needed is to ALSO revert the
            // LowercaseLookupNormalizer DI registration (back to UpperInvariantLookupNormalizer)
            // in the same operation, which is outside EF migrations' scope. Throwing
            // here forces the engineer to make that choice explicitly rather than
            // silently producing an unauthenticatable database.
            throw new NotSupportedException(
                "Backfill migration cannot be reverted: doing so would restore the " +
                "broken pre-4b35911 state where AspNetUsers.NormalizedEmail/NormalizedUserName " +
                "uppercase rows are unmatchable by LowercaseLookupNormalizer. To roll back, " +
                "revert both this migration AND the LowercaseLookupNormalizer DI registration " +
                "(Program.cs ~line 139) in a coordinated change."
            );
        }
    }
}
