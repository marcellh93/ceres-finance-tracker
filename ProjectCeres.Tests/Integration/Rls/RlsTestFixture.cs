using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 7.5 / ADR-0068 — dedicated fixture for tests that exercise the wall
/// directly. Unlike <see cref="TestDbFixture"/> (which the WAF rewires onto the
/// admin role to keep legacy tests working), this fixture connects as the real
/// <c>ceres_app</c> role with the <see cref="RowLevelSecurityInterceptor"/>
/// active — so RLS policies fire on every command and the tests assert what the
/// production runtime actually sees.
///
/// <para>
/// Seeds two users (<see cref="UserA"/>, <see cref="UserB"/>) via the admin role
/// so the rows are visible across the fixture's two app-bound contexts. Cleanup
/// drops the seeded users (and their FK-cascaded rows) via the admin role too.
/// </para>
/// </summary>
public sealed class RlsTestFixture : IAsyncLifetime
{
    public Guid UserA { get; } = Guid.NewGuid();
    public Guid UserB { get; } = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        // Seed both users via the admin role so the runtime app role can see them.
        await using var admin = CreateAdminContext();
        admin.Users.Add(new Models.ApplicationUser { Id = UserA, UserName = $"a-{UserA:N}@rls-test.local", Email = $"a-{UserA:N}@rls-test.local", EmailConfirmed = true });
        admin.Users.Add(new Models.ApplicationUser { Id = UserB, UserName = $"b-{UserB:N}@rls-test.local", Email = $"b-{UserB:N}@rls-test.local", EmailConfirmed = true });
        await admin.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var admin = CreateAdminContext();
        await admin.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == UserA || u.Id == UserB)
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// Construct an <see cref="AppDbContext"/> bound to the runtime <c>ceres_app</c>
    /// role (NOBYPASSRLS), with the <see cref="RowLevelSecurityInterceptor"/> wired
    /// to set the GUC for <paramref name="actingAs"/> on every connection open.
    /// </summary>
    public AppDbContext CreateAppContext(Guid actingAs)
    {
        // Stage 7.6.7 / ADR-0073: interceptor takes UserContext via the accessor — no
        // tagger param. FakeCurrentUserAccessor wraps the Guid in UserContext.Resolved
        // (or Uninitialized if Guid.Empty), so the interceptor's switch fires the right
        // branch automatically.
        var accessor = new FakeCurrentUserAccessor(actingAs);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestDbFixture.AppConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(accessor))
            .AddInterceptors(new RowLevelSecurityInterceptor(
                accessor, NullLogger<RowLevelSecurityInterceptor>.Instance))
            .Options;
        return new AppDbContext(options, accessor);
    }

    /// <summary>
    /// Construct an <see cref="AdminDbContext"/> for cross-tenant seeding / cleanup
    /// / parity-query work. BYPASSRLS so policies are not checked.
    /// </summary>
    public AdminDbContext CreateAdminContext()
    {
        var accessor = new FakeCurrentUserAccessor(UserA); // any non-empty Guid is fine
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(TestDbFixture.AdminConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(accessor))
            .Options;
        return new AdminDbContext(options, accessor);
    }

    /// <summary>
    /// Open a raw Npgsql connection as the runtime <c>ceres_app</c> role and set
    /// the GUC <c>app.current_user_ref</c> to <paramref name="actingAs"/>. Useful
    /// for the FromSqlRaw bypass tests (Group 1) and direct INSERT 42501 tests
    /// (Group 2), which need to talk to Postgres without going through EF.
    /// </summary>
    public async Task<NpgsqlConnection> OpenAppConnectionAsync(Guid actingAs)
    {
        var conn = new NpgsqlConnection(TestDbFixture.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT set_config('app.current_user_ref', '{actingAs:D}', false)";
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

}

[CollectionDefinition("RlsTests")]
public class RlsCollection : ICollectionFixture<RlsTestFixture> { }
