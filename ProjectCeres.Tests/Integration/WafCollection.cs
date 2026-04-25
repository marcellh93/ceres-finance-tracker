using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Custom factory that always routes WAF tests to project_ceres_test, never to the
/// dev database. Any test class that receives this factory from the collection fixture
/// is guaranteed to hit the test database regardless of appsettings.json.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnectionString);
    }
}

/// <summary>
/// All integration tests — both TestDbFixture-based and WebApplicationFactory-based —
/// share a single xUnit collection. xUnit runs all classes in a collection sequentially
/// on one thread, eliminating races between concurrent writes to project_ceres_test.
/// </summary>
[CollectionDefinition("IntegrationTests")]
public class IntegrationCollection : ICollectionFixture<TestWebApplicationFactory> { }
