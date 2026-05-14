using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration.Infrastructure;

/// <summary>
/// Stage 7.6.6 — single-responsibility builder for <see cref="AppDbContext"/>
/// instances bound to the <c>ceres_app</c> role (NOBYPASSRLS) with the production
/// interceptor stack wired so RLS policies apply on every read/write.
/// Extracted from the prior omnibus <c>TestDbFixture</c>.
///
/// <para>
/// Per ADR-0068, integration tests connect as <c>ceres_app</c> by default so they
/// exercise the same code path the running app does. Cross-user / admin reads opt
/// into <see cref="AdminContextFactory"/>.
/// </para>
/// </summary>
internal static class AppContextFactory
{
    public static AppDbContext Create(ICurrentUserAccessor user)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestDbFixture.AppConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(user))
            .AddInterceptors(new RowLevelSecurityInterceptor(
                user, new StubPreAuthTagger(), NullLogger<RowLevelSecurityInterceptor>.Instance))
            .Options;
        return new AppDbContext(options, user);
    }

    /// <summary>
    /// Stub tagger that never reports pre-auth — fine for the base fixture where every
    /// test has a real user. The dedicated RLS test fixture (Group 4 backstop tests)
    /// uses a richer fake that flips the return value.
    /// </summary>
    private sealed class StubPreAuthTagger : IPreAuthCallSiteTagger
    {
        public bool IsLegitimatePreAuth() => false;
    }
}
