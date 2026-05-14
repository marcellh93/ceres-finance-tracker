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
        // Stage 7.6.7 / ADR-0073: the interceptor switches on UserContext directly. The
        // base fixture binds tests to a real user (FakeCurrentUserAccessor wraps in
        // UserContext.Resolved), so the interceptor sees Resolved and issues set_config.
        // Tests that exercise other UserContext cases use the dedicated RLS fixture.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestDbFixture.AppConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(user))
            .AddInterceptors(new RowLevelSecurityInterceptor(
                user, NullLogger<RowLevelSecurityInterceptor>.Instance))
            .Options;
        return new AppDbContext(options, user);
    }
}
