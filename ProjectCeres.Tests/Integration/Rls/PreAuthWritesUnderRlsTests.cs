using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Common;
using ProjectCeres.Tests.Integration.Infrastructure;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 9.6.1 (2026-05-18) — regression tests for the pre-auth RLS GUC bug.
///
/// <para>
/// <b>Background.</b> The default integration-test factory (<c>WafCollection</c>)
/// overrides <c>ApplicationConnection</c> to point at <c>ceres_admin</c>
/// (BYPASSRLS). That means the existing <c>TotpReplayGuardTests</c> /
/// <c>PasswordReset*Tests</c> / <c>LockoutUnlock*Tests</c> never actually
/// exercised RLS — they ran with the policy silently inert. The production
/// bug only surfaced when the SPA hit the dev DB on the <c>ceres_app</c>
/// connection (NOBYPASSRLS).
/// </para>
///
/// <para>
/// <b>What this test does.</b> Builds an <see cref="AppDbContext"/> bound
/// directly to the <c>ceres_app</c> connection string (per
/// <see cref="TestDbFixture.AppConnectionString"/>), installs the
/// <see cref="RowLevelSecurityInterceptor"/> with a
/// <see cref="UserContext.PreAuth"/> accessor — exactly mirroring the live
/// runtime where <c>[PreAuthCallSite("Auth.LoginTotp")]</c> resolves to
/// <c>PreAuth</c>. Then invokes <see cref="TotpReplayGuard.TryAcceptAsync"/>.
/// Without the <see cref="PreAuthRlsScope.SetPreAuthUserGucAsync"/> fix in
/// the guard, this test fails with
/// <c>RlsPolicyViolationException</c> (42501).
/// </para>
///
/// <para>
/// This test deliberately bypasses the <c>WafCollection</c> /
/// <c>AuthTestWebApplicationFactory</c> infrastructure because those classes
/// route every connection through <c>ceres_admin</c>. Reusing them here
/// would let the bug recur silently — the test must build its own context
/// against <c>ceres_app</c> to be load-bearing.
/// </para>
/// </summary>
public class PreAuthWritesUnderRlsTests
{
    [Fact]
    public async Task TotpReplayGuard_TryAcceptAsync_succeeds_under_PreAuth_context_against_ceres_app_connection()
    {
        await MigrationFixture.EnsureMigratedAsync();

        // Unique per-run userId so parallel test execution doesn't see a leftover
        // row from a previous run on the same shared test DB. The cleanup at the
        // end is best-effort; if it fails the row stays orphaned, but the next
        // run is unaffected because the Guid is fresh.
        var userId = Guid.NewGuid();
        await using var ctx = BuildAppContextWithPreAuthInterceptor(userId);

        var guard = new TotpReplayGuard(ctx, new Argon2idPasswordHasher(
            Microsoft.Extensions.Options.Options.Create(
                new ProjectCeres.Common.Authentication.Argon2idOptions
                {
                    MemorySizeKb = 8192, Iterations = 1, Parallelism = 1,
                })));

        // Pre-fix this throws RlsPolicyViolationException on the INSERT.
        var firstAccept = await guard.TryAcceptAsync(userId, "654987", CancellationToken.None);
        firstAccept.Should().BeTrue("first acceptance of a code under PreAuth context must succeed once the GUC is set per-request");

        // Cleanup through a fresh PreAuth-scoped context for the same user; the
        // scope's transaction means SET LOCAL persists through the DELETE.
        await using var cleanupCtx = BuildAppContextWithPreAuthInterceptor(userId);
        await using var cleanupScope = await cleanupCtx.BeginPreAuthUserScopeAsync(userId);
        await cleanupCtx.TotpReplayEntries.IgnoreQueryFilters()
            .Where(e => e.UserId == userId)
            .ExecuteDeleteAsync();
        await cleanupScope.CommitAsync();
    }

    [Fact]
    public async Task PasswordResetTokens_select_under_PreAuth_returns_null_until_PreAuthUserScope_opens()
    {
        // Stage 9.6.1 (2026-05-19) — pins the root cause that
        // PasswordResetService.ConfirmAsync was suffering from: under a
        // [PreAuthCallSite] context, the RowLevelSecurityInterceptor RESETs
        // app.current_user_ref on connection open. A SELECT against the
        // ceres_app connection therefore filters every PasswordResetToken row
        // via the user_isolation policy (returns null) regardless of whether
        // the row exists. The fix is two-part:
        //   1) read the candidate token via AdminDbContext (BYPASSRLS), and
        //   2) wrap all subsequent writes in a PreAuthUserScope.
        // This test pins both behaviors so a future refactor can't re-introduce
        // the bug by routing the initial lookup through ceres_app.
        await MigrationFixture.EnsureMigratedAsync();
        var userId = Guid.NewGuid();

        // Seed a token via the admin context (BYPASSRLS).
        await using (var admin = AdminContextFactory.Create())
        {
            admin.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenLookup = Guid.NewGuid().ToByteArray(),
                TokenHash = "x",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            });
            await admin.SaveChangesAsync();
        }

        try
        {
            // (1) ceres_app + PreAuth + no PreAuthUserScope → row filtered.
            await using (var app = BuildAppContextWithPreAuthInterceptor(userId))
            {
                var filtered = await app.PasswordResetTokens
                    .IgnoreQueryFilters()
                    .Where(t => t.UserId == userId)
                    .FirstOrDefaultAsync();

                filtered.Should().BeNull(
                    "without app.current_user_ref set, the user_isolation RLS policy filters every row " +
                    "— this is precisely the failure mode ConfirmAsync was hitting on the production ceres_app connection");
            }

            // (2) Admin context (BYPASSRLS) sees the row — this is the path
            //     ConfirmAsync's initial lookup uses post-fix. IgnoreQueryFilters
            //     because AdminDbContext shares AppDbContext's global per-user
            //     EF filter; ceres_admin bypasses RLS at the DB level but the
            //     EF filter still applies, and ConfirmAsync's real call site
            //     explicitly pairs admin reads with IgnoreQueryFilters.
            await using (var admin = AdminContextFactory.Create())
            {
                var visible = await admin.PasswordResetTokens
                    .IgnoreQueryFilters()
                    .Where(t => t.UserId == userId)
                    .FirstOrDefaultAsync();

                visible.Should().NotBeNull(
                    "AdminDbContext binds to ceres_admin (BYPASSRLS), so the initial token lookup in " +
                    "ConfirmAsync — which runs before match.UserId is known — sees the row");
            }

            // (3) ceres_app + PreAuth + PreAuthUserScope keyed to the row's
            //     UserId → row visible. This is the path the writes take
            //     after the admin lookup resolves match.UserId.
            await using (var app = BuildAppContextWithPreAuthInterceptor(userId))
            {
                await using var scope = await app.BeginPreAuthUserScopeAsync(userId);
                var scoped = await app.PasswordResetTokens
                    .IgnoreQueryFilters()
                    .Where(t => t.UserId == userId)
                    .FirstOrDefaultAsync();

                scoped.Should().NotBeNull(
                    "once PreAuthUserScope sets app.current_user_ref via SET LOCAL, the user_isolation policy " +
                    "lets the row through and ConfirmAsync's subsequent UPDATE/INSERT writes can proceed");
                await scope.CommitAsync();
            }
        }
        finally
        {
            await using var admin = AdminContextFactory.Create();
            await admin.PasswordResetTokens
                .Where(t => t.UserId == userId)
                .ExecuteDeleteAsync();
        }
    }

    private static AppDbContext BuildAppContextWithPreAuthInterceptor(Guid userId)
    {
        // The CurrentUserAccessor for AppDbContext returns PreAuth (matching the
        // runtime path where [PreAuthCallSite] is set on the action). The
        // interceptor will see PreAuth and RESET the GUC on connection open —
        // exactly what production does. TotpReplayGuard's own SetPreAuthUserGucAsync
        // call is what then re-sets the GUC for the upcoming write.
        var dbAccessor = new FakeCurrentUserAccessor(new UserContext.PreAuth("Auth.LoginTotp"));

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var interceptorLogger = loggerFactory.CreateLogger<RowLevelSecurityInterceptor>();
        var interceptor = new RowLevelSecurityInterceptor(dbAccessor, interceptorLogger);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestDbFixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options, dbAccessor);
    }
}
