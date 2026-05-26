using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 9.3 follow-up — the AddEmailConfirmationTokens migration (20260522214903)
    /// created the table but did NOT install the user_isolation RLS policy; the entity
    /// was also missing from <c>UserOwnedTables.All</c>, so the Stage 7.5 migration loop
    /// never covered it. Result: cross-user verification-token visibility under
    /// <c>ceres_app</c>. Browser-tested 2026-05-26; psql confirmed
    /// <c>rowsecurity = false</c> on the table.
    ///
    /// This migration brings the table to parity with the other token tables
    /// (PasswordResetTokens, LockoutUnlockTokens, EmailChangeTokens). The same SQL
    /// shape as the Stage 7.5 <c>AddRowLevelSecurityPolicies</c> migration applies:
    /// ENABLE + FORCE + DROP-IF-EXISTS + CREATE POLICY user_isolation with the
    /// fail-closed NULLIF guard against DISCARD ALL.
    /// </summary>
    public partial class EnableRlsOnEmailConfirmationTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""EmailConfirmationTokens"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""EmailConfirmationTokens"" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS user_isolation ON ""EmailConfirmationTokens"";
                CREATE POLICY user_isolation ON ""EmailConfirmationTokens""
                  USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
                  WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS user_isolation ON ""EmailConfirmationTokens"";
                ALTER TABLE ""EmailConfirmationTokens"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""EmailConfirmationTokens"" DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
