using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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

            // Stage 6b.2: replace the rate limiter with no-ops so existing 6a/6b.1
            // integration tests don't trip the limiter on burst. Tests that need to
            // verify the real limiter use RateLimitedAuthTestWebApplicationFactory.
            //
            // Implementation note: RateLimiterOptions stores lambda-based policies in
            // PolicyMap and DI-activated typed policies in UnactivatedPolicyMap. Both are
            // internal properties, so we clear them via reflection before re-registering
            // the policy names as GetNoLimiter partitions. PostConfigure runs after all
            // Configure callbacks (including the production AddRateLimiter call), so the
            // keys already exist when we get here — we must remove before re-adding.
            services.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(opts =>
            {
                var type = opts.GetType();
                var flags = System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic
                          | System.Reflection.BindingFlags.Instance;

                var policyMapProp     = type.GetProperty("PolicyMap",          flags)!;
                var unactivatedProp   = type.GetProperty("UnactivatedPolicyMap", flags)!;

                var policyMap      = policyMapProp.GetValue(opts)!;
                var unactivatedMap = unactivatedProp.GetValue(opts)!;

                var removeFromPolicy     = policyMap.GetType()     .GetMethod("Remove", new[] { typeof(string) })!;
                var removeFromUnactivated = unactivatedMap.GetType().GetMethod("Remove", new[] { typeof(string) })!;

                foreach (var name in new[]
                {
                    AuthRateLimitPolicies.AuthLoginByIp,
                    AuthRateLimitPolicies.AuthTotpByUser,
                    AuthRateLimitPolicies.AuthCsrfByIp,
                    AuthRateLimitPolicies.AuthMfaByUser,
                })
                {
                    removeFromPolicy.Invoke(policyMap,         new object[] { name });
                    removeFromUnactivated.Invoke(unactivatedMap, new object[] { name });
                    opts.AddPolicy(name,
                        _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
                }
            });

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

    /// <summary>
    /// Returns a derived factory in which <typeparamref name="T"/> is replaced with
    /// <paramref name="replacement"/>. The returned factory is disposable; tests should
    /// <c>await using</c> it so the inner WebHost is torn down after the test.
    /// </summary>
    public WebApplicationFactory<Program> WithReplacedService<T>(T replacement) where T : class =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<T>();
                services.AddSingleton(replacement);
            }));

    /// <summary>
    /// Returns a derived factory that appends every log message to <paramref name="sink"/>.
    /// Chain with <see cref="WithReplacedService{T}"/> to combine effects.
    /// </summary>
    public WebApplicationFactory<Program> WithCapturedLogger(List<string> sink) =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ILoggerProvider>(new InMemoryLoggerProvider(sink))));
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
