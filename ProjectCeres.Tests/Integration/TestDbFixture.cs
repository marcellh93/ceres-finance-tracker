using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Shared setup for integration tests. Each test class that uses this fixture gets
/// a fresh AppDbContext connected to project_ceres_test. Each test is wrapped in a
/// transaction that rolls back on dispose — no test leaves data behind.
///
/// Stage 7.5 / ADR-0068: connects as <c>ceres_app</c> (NOBYPASSRLS) by default so
/// integration tests exercise the same code path the running app does. Tests that
/// need to read across users (the admin path) opt into the admin connection by
/// calling <see cref="CreateAdminContext"/>; schema migrations always run as
/// <c>ceres_migrator</c>.
/// </summary>
public class TestDbFixture : IAsyncDisposable
{
    internal const string AppConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=ceres_app;Password=ceres_app_dev_password";

    internal const string AdminConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=ceres_admin;Password=ceres_admin_dev_password";

    internal const string MigratorConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=ceres_migrator;Password=ceres_migrator_dev_password";

    private static readonly Guid SentinelUserId = new("00000000-0000-0000-0000-000000000001");

    public AppDbContext Db { get; }
    private IDbContextTransaction? _transaction;

    public TestDbFixture()
    {
        Db = CreateAppContext(new FakeCurrentUserAccessor(SentinelUserId));
    }

    /// <summary>
    /// Call at the start of each test to migrate the schema (idempotent) and open a
    /// transaction that will be rolled back when the test finishes.
    /// </summary>
    public async Task InitAsync()
    {
        // Migrations run as ceres_migrator (DDL + BYPASSRLS). Once the schema is up,
        // the test runs against ceres_app via the Db property — the same role the
        // production app uses, so RLS policies apply to test reads/writes.
        await using var migrator = CreateMigratorContext();
        await migrator.Database.MigrateAsync();

        _transaction = await Db.Database.BeginTransactionAsync();
    }

    /// <summary>
    /// Construct a fresh AdminDbContext (ceres_admin, BYPASSRLS) for tests that
    /// need to read across users — e.g. asserting that an admin path sees all rows.
    /// Not wrapped in the fixture's transaction; admin reads should be observational.
    /// </summary>
    public AdminDbContext CreateAdminContext(ICurrentUserAccessor? user = null)
    {
        var accessor = user ?? new FakeCurrentUserAccessor(SentinelUserId);
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(AdminConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(accessor))
            .Options;
        return new AdminDbContext(options, accessor);
    }

    /// <summary>
    /// Construct a fresh AppDbContext (ceres_app, NOBYPASSRLS, RLS-bound) under
    /// an arbitrary user. Useful for cross-user IDOR + RLS integration tests.
    /// </summary>
    public AppDbContext CreateAppContext(ICurrentUserAccessor user)
    {
        var options = BuildAppOptions(user);
        return new AppDbContext(options, user);
    }

    private static DbContextOptions<AppDbContext> BuildAppOptions(ICurrentUserAccessor user)
    {
        // Stage 7.5 / ADR-0068 — the AppDbContext talks to Postgres as ceres_app
        // (NOBYPASSRLS). Without the RowLevelSecurityInterceptor, every WRITE against
        // an RLS-bound table fails with "new row violates row-level security policy"
        // because the GUC app.current_user_ref is unset.
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(AppConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(user))
            .AddInterceptors(new RowLevelSecurityInterceptor(
                user, new StubPreAuthTagger(), NullLogger<RowLevelSecurityInterceptor>.Instance))
            .Options;
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

    private static AppDbContext CreateMigratorContext()
    {
        var accessor = new FakeCurrentUserAccessor(SentinelUserId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(MigratorConnectionString)
            .Options;
        return new AppDbContext(options, accessor);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.RollbackAsync();

        await Db.DisposeAsync();
    }
}
