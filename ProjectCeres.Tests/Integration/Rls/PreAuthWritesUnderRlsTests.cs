using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
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
