namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Single source of truth for which Postgres database a test collection uses,
/// and the three role-scoped connection strings for a database name.
///
/// Stage 12.18: the integration suite splits into N parallel bucket collections,
/// each pinned to its own database, so the connection-scoped RLS GUC
/// (app.current_user_ref) can never leak across buckets — a distinct DB name
/// yields a distinct Npgsql connection pool. When CERES_TEST_DB_CLONES is unset
/// (a plain local `dotnet test` with no provisioned clones), every collection
/// resolves the single legacy project_ceres_test and xUnit serializes them —
/// today's behaviour.
/// </summary>
public static class TestDatabaseRouter
{
    public const string LegacyDatabase = "project_ceres_test";

    private static readonly Dictionary<string, string> SerialCollectionDatabases = new()
    {
        ["RateLimitTests"] = "project_ceres_test_ratelimit",
        ["MfaRateLimitTests"] = "project_ceres_test_mfaratelimit",
        ["AppRoleTests"] = "project_ceres_test_approle",
        ["RlsTests"] = "project_ceres_test_rls",
        ["TestDbFixtureTests"] = "project_ceres_test_txfixture",
    };

    /// <summary>Number of parallel bucket databases; 1 (fallback) when unset.</summary>
    public static int CloneCount
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("CERES_TEST_DB_CLONES");
            return int.TryParse(raw, out var n) && n >= 1 ? n : 1;
        }
    }

    public static string DatabaseForCollection(string collectionName)
    {
        // Fallback: no clones provisioned → one shared legacy DB (xUnit serializes).
        if (CloneCount <= 1) return LegacyDatabase;

        if (SerialCollectionDatabases.TryGetValue(collectionName, out var serialDb))
            return serialDb;

        // IntegrationParallelK → project_ceres_test_K
        const string prefix = "IntegrationParallel";
        if (collectionName.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(collectionName[prefix.Length..], out var k))
        {
            return $"{LegacyDatabase}_{k}";
        }

        return LegacyDatabase;
    }

    public static (string app, string admin, string migrator) ConnectionsFor(string databaseName)
    {
        string Conn(string role) =>
            $"Host=localhost;Database={databaseName};Username={role};Password={role}_dev_password";
        return (Conn("ceres_app"), Conn("ceres_admin"), Conn("ceres_migrator"));
    }
}
