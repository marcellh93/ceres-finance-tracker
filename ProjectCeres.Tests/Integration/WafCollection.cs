using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Custom factory that always routes WAF tests to project_ceres_test, never to the
/// dev database. Any test class that receives this factory from the collection fixture
/// is guaranteed to hit the test database regardless of appsettings.json.
///
/// Auth-test classes (ProjectCeres.Tests.Integration.Authentication.*) exercise the
/// real Stage 6a auth pipeline. All other API integration tests rely on the
/// TestAuthenticationHandler installed below — it auto-authenticates every request
/// as the sentinel user, preserving their pre-Stage-6a contract without rewriting
/// each test for explicit login + cookie management.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=postgres;Password=postgres";

    /// <summary>
    /// When true (default), the factory installs the TestAuthenticationHandler as the
    /// default scheme and drops the global antiforgery filter. Pre-Stage-6a CRUD tests
    /// rely on this so they continue to work without login boilerplate.
    ///
    /// Stage 6a auth tests inherit the factory and override this to false to exercise
    /// the real Identity + cookie + CSRF + global fallback policy pipeline.
    /// </summary>
    protected virtual bool UseTestAuthHandler => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnectionString);

        builder.ConfigureServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(IBreachedPasswordChecker)).ToList();
            foreach (var d in existing) services.Remove(d);
            services.AddSingleton<IBreachedPasswordChecker, HibpStubBreachedPasswordChecker>();

            if (!UseTestAuthHandler) return;

            // ===== Pre-Stage-6a-test bypass =====
            // Rebind ICurrentUserAccessor to the sentinel for non-auth tests.
            var current = services.Where(d => d.ServiceType == typeof(ICurrentUserAccessor)).ToList();
            foreach (var d in current) services.Remove(d);
            services.AddScoped<ICurrentUserAccessor, SingleUserAccessor>();

            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });

            services.PostConfigure<AuthorizationOptions>(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthenticationHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build();
                options.FallbackPolicy = options.DefaultPolicy;
            });

            services.PostConfigure<MvcOptions>(options =>
            {
                var antiforgery = options.Filters
                    .OfType<AutoValidateAntiforgeryTokenAttribute>()
                    .ToList();
                foreach (var f in antiforgery) options.Filters.Remove(f);
            });
        });
    }
}

/// <summary>
/// Sibling factory used by Stage 6a auth integration tests. Inherits everything from
/// TestWebApplicationFactory but disables the test auth handler so the real Identity +
/// cookie + CSRF + global fallback policy pipeline runs end-to-end.
/// </summary>
public class AuthTestWebApplicationFactory : TestWebApplicationFactory
{
    protected override bool UseTestAuthHandler => false;
}

/// <summary>
/// All integration tests — both TestDbFixture-based and WebApplicationFactory-based —
/// share a single xUnit collection. xUnit runs all classes in a collection sequentially
/// on one thread, eliminating races between concurrent writes to project_ceres_test.
/// </summary>
[CollectionDefinition("IntegrationTests")]
public class IntegrationCollection
    : ICollectionFixture<TestWebApplicationFactory>,
      ICollectionFixture<AuthTestWebApplicationFactory>
{ }
