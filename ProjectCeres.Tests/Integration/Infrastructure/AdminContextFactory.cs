using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Infrastructure;

/// <summary>
/// Stage 7.6.6 — single-responsibility builder for <see cref="AdminDbContext"/>
/// instances bound to the <c>ceres_admin</c> role (BYPASSRLS). For tests that
/// need to read across users — e.g. asserting an admin path sees all rows.
/// Extracted from the prior omnibus <c>TestDbFixture</c>.
///
/// <para>
/// Admin reads are observational: no transaction wrapper, no cleanup. Tests that
/// mutate via the admin context are responsible for cleaning up.
/// </para>
/// </summary>
internal static class AdminContextFactory
{
    public static AdminDbContext Create(ICurrentUserAccessor? user = null)
    {
        var accessor = user ?? new FakeCurrentUserAccessor(TestDbFixture.SentinelUserId);
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(TestDbFixture.AdminConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(accessor))
            .Options;
        return new AdminDbContext(options, accessor);
    }
}
