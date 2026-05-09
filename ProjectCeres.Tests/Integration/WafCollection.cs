using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Tests.Integration.Authentication;

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

        builder.ConfigureServices(services =>
        {
            // Replace the HttpClient-backed HIBP checker with the deterministic stub
            // so password-validation tests do not hit api.pwnedpasswords.com.
            var existing = services.Where(d => d.ServiceType == typeof(IBreachedPasswordChecker)).ToList();
            foreach (var d in existing) services.Remove(d);
            services.AddSingleton<IBreachedPasswordChecker, HibpStubBreachedPasswordChecker>();
        });
    }
}

/// <summary>
/// All integration tests — both TestDbFixture-based and WebApplicationFactory-based —
/// share a single xUnit collection. xUnit runs all classes in a collection sequentially
/// on one thread, eliminating races between concurrent writes to project_ceres_test.
/// </summary>
[CollectionDefinition("IntegrationTests")]
public class IntegrationCollection : ICollectionFixture<TestWebApplicationFactory> { }
