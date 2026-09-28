using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Tests.Integration;
using Reqnroll.Microsoft.Extensions.DependencyInjection;

namespace ProjectCeres.Specs.Support;

/// <summary>
/// Auth-enabled WAF pinned to the BDD project's own serial DB (project_ceres_test_specs
/// under clones; legacy under the N=1 fallback), so scenarios reuse the real harness while
/// preserving §12.18 per-run isolation. Support-ticket flows exercise the real auth
/// pipeline, hence AuthTestWebApplicationFactory rather than the plain factory.
///
/// Overrides SweepOnDispose (found during Stage 13.9 Task 6b, 2026-09-27): the base
/// TestWebApplicationFactory.SweepOnDispose defaults false because most ad-hoc factories
/// are constructed mid-run inside a test — sweeping there would delete a user a still-
/// running sibling test needs (see the base class's own doc comment). This factory is
/// registered [ScenarioDependencies]-singleton (SpecsDependencies.Register below), so
/// exactly one instance exists for the whole ProjectCeres.Specs run and it is disposed
/// only at process teardown — the same "disposed once, after every scenario has
/// finished" guarantee SweepingTestWebApplicationFactory relies on for
/// ProjectCeres.Tests's bucket collections. Without this, every abandoned BDD-step user
/// (e.g. ErasureSteps' erasure-spec-*@erasure-spec-test.local rows) was never swept,
/// unlike its ProjectCeres.Tests sibling.
/// </summary>
public sealed class SpecsAuthFactory : AuthTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("SpecsTests");
    protected override bool SweepOnDispose => true;
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
