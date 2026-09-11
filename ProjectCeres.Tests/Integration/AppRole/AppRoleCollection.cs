using Npgsql;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 9.5d. Boots the ceres_app-wired DualContextWebApplicationFactory for the AppRole
/// suite AND fails the whole collection fast (roadmap condition D1 / spec decision D5) if
/// ceres_app secretly holds BYPASSRLS — otherwise every AppRole test would pass falsely.
/// </summary>
public sealed class AppRoleFixture : IAsyncLifetime
{
    // Stage 12.18: this collection's OWN database, not TestDbFixture's (that one resolves to
    // project_ceres_test_txfixture). Under clones, raw connections opened via TestDbFixture here
    // would split-brain against Factory/AppRoleFixture-seeded data, which lives on _approle.
    internal static string DatabaseName => TestDatabaseRouter.DatabaseForCollection("AppRoleTests");
    internal static string AppConnectionString => TestDatabaseRouter.ConnectionsFor(DatabaseName).app;
    internal static string MigratorConnectionString => TestDatabaseRouter.ConnectionsFor(DatabaseName).migrator;

    public DualContextWebApplicationFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        // Stage 12.18: pin Factory to this collection's own DB before anything touches
        // Factory.Services (UseDatabase must run before the host builds). Without this,
        // DualContextWebApplicationFactory falls back to InitDbName (the legacy DB) even
        // though AppConnectionString/MigratorConnectionString above resolve to _approle —
        // a split-brain in the opposite direction of the one this file fixed.
        Factory.UseDatabase(DatabaseName);

        await using var conn = new NpgsqlConnection(AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rolbypassrls FROM pg_roles WHERE rolname = 'ceres_app'";
        var result = await cmd.ExecuteScalarAsync();
        var bypass = result is bool b && b;
        if (bypass)
            throw new InvalidOperationException(
                "ceres_app has BYPASSRLS — the AppRole suite would pass falsely. " +
                "Fix scripts/setup-postgres-roles.sql (ceres_app must be NOBYPASSRLS).");
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}

// DisableParallelization: these RLS-correctness tests don't need to run in parallel, and
// their concurrent HTTP+DB load destabilizes the timing-sensitive RateLimitTests under full-
// suite contention (9.5d flake-check). Matches the RateLimitTests/MfaRateLimitTests precedent.
[CollectionDefinition("AppRoleTests", DisableParallelization = true)]
public class AppRoleTestsCollection : ICollectionFixture<AppRoleFixture> { }
