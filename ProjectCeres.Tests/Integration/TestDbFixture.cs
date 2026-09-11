using Microsoft.EntityFrameworkCore.Storage;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Tests.Common;
using ProjectCeres.Tests.Integration.Infrastructure;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Shared setup for integration tests. Each test class that uses this fixture gets
/// a fresh AppDbContext connected to <c>project_ceres_test</c>. Each test is wrapped
/// in a transaction that rolls back on dispose — no test leaves data behind.
///
/// <para>
/// Stage 7.5 / ADR-0068: connects as <c>ceres_app</c> (NOBYPASSRLS) by default so
/// integration tests exercise the same code path the running app does. Tests that
/// need to read across users (the admin path) opt into the admin connection by
/// calling <see cref="CreateAdminContext"/>; schema migrations always run as
/// <c>ceres_migrator</c>.
/// </para>
///
/// <para>
/// Stage 7.6.6: this class is now a thin coordinator. Migration, App-context, and
/// Admin-context construction live in the three single-responsibility helpers under
/// <c>Infrastructure/</c>. The connection-string constants stay here because
/// external callers (`SettingsServiceTests`, `PrivilegeLeakStartupCheckTests`,
/// `Rls/RlsTestFixture`, `Rls/Group4_BackstopTests`) reference them by qualified
/// name; <see cref="SentinelUserId"/> is exposed for the same reason.
/// </para>
/// </summary>
public class TestDbFixture : IAsyncDisposable
{
    // Stage 12.18 — routed through TestDatabaseRouter so this fixture targets the
    // TestDbFixtureTests serial collection's own database (falls back to the legacy
    // shared DB when CERES_TEST_DB_CLONES is unset). All TestDbFixture-based classes
    // share this one database name regardless of which collection they run in — safe
    // because TestDbFixture rollback-isolates every test via its own transaction.
    internal static string DatabaseName => TestDatabaseRouter.DatabaseForCollection("TestDbFixtureTests");

    internal static string AppConnectionString => TestDatabaseRouter.ConnectionsFor(DatabaseName).app;

    internal static string AdminConnectionString => TestDatabaseRouter.ConnectionsFor(DatabaseName).admin;

    internal static string MigratorConnectionString => TestDatabaseRouter.ConnectionsFor(DatabaseName).migrator;

    internal static readonly Guid SentinelUserId = new("00000000-0000-0000-0000-000000000001");

    public AppDbContext Db { get; }
    private IDbContextTransaction? _transaction;

    public TestDbFixture()
    {
        Db = AppContextFactory.Create(new FakeCurrentUserAccessor(SentinelUserId));
    }

    /// <summary>
    /// Call at the start of each test to migrate the schema (idempotent) and open a
    /// transaction that will be rolled back when the test finishes.
    /// </summary>
    public async Task InitAsync()
    {
        await MigrationFixture.EnsureMigratedAsync();
        _transaction = await Db.Database.BeginTransactionAsync();
    }

    /// <summary>
    /// Construct a fresh <see cref="AdminDbContext"/> (<c>ceres_admin</c>, BYPASSRLS)
    /// for tests that need to read across users — e.g. asserting that an admin path
    /// sees all rows. Not wrapped in the fixture's transaction; admin reads should be
    /// observational.
    /// </summary>
    public AdminDbContext CreateAdminContext(ICurrentUserAccessor? user = null)
        => AdminContextFactory.Create(user);

    /// <summary>
    /// Construct a fresh <see cref="AppDbContext"/> (<c>ceres_app</c>, NOBYPASSRLS,
    /// RLS-bound) under an arbitrary user. Useful for cross-user IDOR + RLS
    /// integration tests.
    /// </summary>
    public AppDbContext CreateAppContext(ICurrentUserAccessor user)
        => AppContextFactory.Create(user);

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.RollbackAsync();

        await Db.DisposeAsync();
    }
}

// Stage 12.18 (Task 5b-1): these classes each construct their own `new TestDbFixture()`
// (a static shared connection + transaction-rollback isolation) and cannot join the
// DB-per-bucket parallel model. DisableParallelization=true is required, not optional:
// per xUnit's own contract, a collection opted out of parallelism "is guaranteed not to
// run concurrently with any other test" — matching the RateLimitTests/MfaRateLimitTests/
// AppRoleTests precedent. Without it this collection would still run concurrently with
// the IntegrationParallelK buckets, which is exactly the race this move exists to avoid.
[CollectionDefinition("TestDbFixtureTests", DisableParallelization = true)]
public class TestDbFixtureTestsCollection { }
