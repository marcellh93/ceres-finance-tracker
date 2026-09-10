using FluentAssertions;
using ProjectCeres.Tests.Integration;
using Xunit;

public class TestDatabaseRouterTests
{
    [Fact]
    public void ConnectionsFor_builds_the_three_role_strings_for_a_db()
    {
        var (app, admin, migrator) = TestDatabaseRouter.ConnectionsFor("project_ceres_test_2");
        app.Should().Be("Host=localhost;Database=project_ceres_test_2;Username=ceres_app;Password=ceres_app_dev_password");
        admin.Should().Be("Host=localhost;Database=project_ceres_test_2;Username=ceres_admin;Password=ceres_admin_dev_password");
        migrator.Should().Be("Host=localhost;Database=project_ceres_test_2;Username=ceres_migrator;Password=ceres_migrator_dev_password");
    }

    [Fact]
    public void DatabaseForCollection_maps_bucket_to_numbered_db_when_clones_enabled()
    {
        using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", "4");
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1").Should().Be("project_ceres_test_1");
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4").Should().Be("project_ceres_test_4");
    }

    [Fact]
    public void DatabaseForCollection_falls_back_to_legacy_db_when_no_clone_env()
    {
        using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", null);
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1").Should().Be("project_ceres_test");
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4").Should().Be("project_ceres_test");
    }

    [Fact]
    public void DatabaseForCollection_gives_each_serial_collection_its_own_db()
    {
        using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", "4");
        TestDatabaseRouter.DatabaseForCollection("RateLimitTests").Should().Be("project_ceres_test_ratelimit");
        TestDatabaseRouter.DatabaseForCollection("AppRoleTests").Should().Be("project_ceres_test_approle");
        TestDatabaseRouter.DatabaseForCollection("RlsTests").Should().Be("project_ceres_test_rls");
        TestDatabaseRouter.DatabaseForCollection("MfaRateLimitTests").Should().Be("project_ceres_test_mfaratelimit");
    }

    [Fact]
    public void DatabaseForCollection_serial_collections_ignore_clone_env_fallback_to_legacy_when_off()
    {
        using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", null);
        TestDatabaseRouter.DatabaseForCollection("RateLimitTests").Should().Be("project_ceres_test");
    }
}

// Test helper: restore an env var on dispose.
public sealed class EnvVarScope : IDisposable
{
    private readonly string _name;
    private readonly string? _prev;
    public EnvVarScope(string name, string? value)
    {
        _name = name;
        _prev = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
    public void Dispose() => Environment.SetEnvironmentVariable(_name, _prev);
}
