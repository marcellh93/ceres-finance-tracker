using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Tests.Integration;
using Reqnroll.Microsoft.Extensions.DependencyInjection;

namespace ProjectCeres.Specs.Support;

/// <summary>
/// Auth-enabled WAF pinned to the BDD project's own serial DB (project_ceres_test_specs
/// under clones; legacy under the N=1 fallback), so scenarios reuse the real harness while
/// preserving §12.18 per-run isolation. Support-ticket flows exercise the real auth
/// pipeline, hence AuthTestWebApplicationFactory rather than the plain factory.
/// </summary>
public sealed class SpecsAuthFactory : AuthTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("SpecsTests");
}

public static class SpecsDependencies
{
    [ScenarioDependencies]
    public static IServiceCollection Register()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SpecsAuthFactory>();
        return services;
    }
}
