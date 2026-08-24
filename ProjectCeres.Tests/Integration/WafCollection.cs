using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
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
    // Stage 7.5 / ADR-0068 — three role-scoped connection strings into project_ceres_test.
    // The hosted app resolves ApplicationConnection (ceres_app) at request time; admin
    // services + IUserJobRunner resolve AdminConnection (ceres_admin / BYPASSRLS); the
    // fixture itself uses MigratorConnection for schema setup.
    private const string AppConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=ceres_app;Password=ceres_app_dev_password";

    private const string AdminConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=ceres_admin;Password=ceres_admin_dev_password";

    private const string MigratorConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=ceres_migrator;Password=ceres_migrator_dev_password";

    // Per-factory upload root keeps WAF-based tests from leaking files into the SUT
    // project root (ProjectCeres/uploads/). Cleaned up in Dispose(bool).
    private readonly string _uploadsRoot =
        Path.Combine(Path.GetTempPath(), $"ceres-waf-{Guid.NewGuid():N}");


    /// <summary>
    /// When true (default), the factory installs the TestAuthenticationHandler as the
    /// default scheme and drops the global antiforgery filter. Pre-Stage-6a CRUD tests
    /// rely on this so they continue to work without login boilerplate.
    ///
    /// Stage 6a auth tests inherit the factory and override this to false to exercise
    /// the real Identity + cookie + CSRF + global fallback policy pipeline.
    /// </summary>
    protected virtual bool UseTestAuthHandler => true;

    /// <summary>
    /// When true, the WAF wires AppDbContext to the RLS-active ceres_app role instead of
    /// the BYPASSRLS ceres_admin role. Default false preserves the legacy admin routing all
    /// existing tests rely on (D7). DualContextWebApplicationFactory overrides it to true.
    /// </summary>
    protected virtual bool UseAppRoleConnection => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Stage 7.5 — skip the privilege-leak startup probe inside the WAF. The check
        // is unnecessary per-test (the connection strings are fixed in this fixture)
        // and would add a round-trip per WAF construction.
        builder.UseSetting("Stage75:SkipPrivilegeLeakCheck", "true");

        // Stage 7.5 — point the WAF's AppDbContext at the ADMIN connection string so
        // legacy integration tests that resolve AppDbContext from DI and do cross-user
        // cleanup (`IgnoreQueryFilters().Where(...).ExecuteDeleteAsync()`) continue to
        // work after Postgres RLS turns on. The RLS-bound app role would block those
        // deletes with `new row violates row-level security policy`. The dedicated
        // RlsTestFixture (under Integration/Rls/) connects as the real ceres_app to
        // exercise the wall directly — that's where Stage 7.5 test coverage lives.
        builder.UseSetting("ConnectionStrings:ApplicationConnection",
            UseAppRoleConnection ? AppConnectionString : AdminConnectionString);
        builder.UseSetting("ConnectionStrings:AdminConnection",       AdminConnectionString);
        builder.UseSetting("ConnectionStrings:MigrationConnection",   MigratorConnectionString);
        builder.UseSetting("FileAttachments:RootPath", _uploadsRoot);

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

                var removeFromPolicy     = policyMap.GetType()     .GetMethod("Remove", [typeof(string)])!;
                var removeFromUnactivated = unactivatedMap.GetType().GetMethod("Remove", [typeof(string)])!;

                foreach (var name in new[]
                {
                    AuthRateLimitPolicies.AuthLoginByIp,
                    AuthRateLimitPolicies.AuthTotpByUser,
                    AuthRateLimitPolicies.AuthCsrfByIp,
                    AuthRateLimitPolicies.AuthMfaByUser,
                    AuthRateLimitPolicies.AuthReauthByUser,
                    AuthRateLimitPolicies.EmailByUser,
                    AuthRateLimitPolicies.EmailByIp,
                })
                {
                    removeFromPolicy.Invoke(policyMap,         [name]);
                    removeFromUnactivated.Invoke(unactivatedMap, [name]);
                    opts.AddPolicy(name,
                        _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
                }

                // Stage 8d. The production EmailByIp bucket is wired as the
                // RateLimiterOptions.GlobalLimiter (gated by [ApplyEmailIpRateLimit])
                // because EnableRateLimitingAttribute is AllowMultiple=false. Replace it
                // with a no-op so non-rate-limit-test classes don't trip it on burst.
                opts.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter
                    .Create<Microsoft.AspNetCore.Http.HttpContext, string>(
                        _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
            });

            if (!UseTestAuthHandler) return;

            // ===== Pre-Stage-6a-test bypass =====
            // Rebind ICurrentUserAccessor to the sentinel for non-auth tests. Stage 7
            // Task 17 deleted SingleUserAccessor; the replacement test double takes the
            // sentinel Guid as a constructor argument, so the registration is now a
            // singleton-instance binding instead of a type binding.
            var current = services.Where(d => d.ServiceType == typeof(ICurrentUserAccessor)).ToList();
            foreach (var d in current) services.Remove(d);
            services.AddScoped<ICurrentUserAccessor>(_ =>
                new Common.FakeCurrentUserAccessor(
                    new Guid("00000000-0000-0000-0000-000000000001")));

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

    /// <summary>
    /// Whether disposing this factory sweeps abandoned test users. FALSE by default,
    /// because tests construct ad-hoc factories mid-run (five in ArchitectureTests alone)
    /// and disposing one of those must not delete users other tests are still using —
    /// which is exactly what happened: the sweep ran on an ArchitectureTests factory and
    /// deleted the user AuditLogIntegrationTests had just registered, failing it.
    ///
    /// Only the shared collection fixture opts in, via SweepingTestWebApplicationFactory,
    /// because it alone is disposed after every test in the collection has finished.
    /// </summary>
    protected virtual bool SweepOnDispose => false;

    protected override void Dispose(bool disposing)
    {
        if (disposing && SweepOnDispose) SweepAbandonedTestUsers();

        base.Dispose(disposing);
        if (disposing && Directory.Exists(_uploadsRoot))
        {
            try { Directory.Delete(_uploadsRoot, recursive: true); }
            catch { /* best-effort cleanup; never mask test failures */ }
        }
    }

    /// <summary>
    /// Backstop for the shared test database: deletes test users the suite abandoned.
    ///
    /// Of 69 test files that create users, 22 call UserOwnedCleanup.PurgeUserAsync and
    /// far fewer delete the user itself, so users accumulate — 579 of them by 2026-08-24,
    /// holding 14,352 seeded categories between them and pushing the suite from ~3:30 to
    /// over 11 minutes.
    ///
    /// Running it here — once, when the collection tears down — fixes the class rather
    /// than editing 47 files that would drift again. Best-effort by design: a cleanup
    /// failure must never turn a green run red, so it swallows and reports rather than
    /// throwing.
    /// </summary>
    private void SweepAbandonedTestUsers()
    {
        try
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deleted = UserOwnedCleanup.SweepAbandonedTestUsersAsync(db).GetAwaiter().GetResult();
            if (deleted > 0)
            {
                Console.WriteLine($"[test-db sweep] removed {deleted} abandoned test user(s) and their rows.");
            }
        }
        catch (Exception ex)
        {
            // Never mask a test result. A failed sweep only means the next run starts
            // with more rows, which the run-tests tooling surfaces separately.
            Console.WriteLine($"[test-db sweep] skipped: {ex.GetType().Name}: {ex.Message}");
        }
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
    /// Replace a scoped service by type (TImpl must derive from / implement TService).
    /// Used when the production registration is scoped (e.g. CategorySeedService) and
    /// promoting it to singleton would change behaviour.
    /// </summary>
    public WebApplicationFactory<Program> WithReplacedScopedService<TService, TImpl>()
        where TService : class
        where TImpl : class, TService =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TService>();
                services.AddScoped<TService, TImpl>();
            }));

    /// <summary>
    /// Returns a derived factory whose configuration binds
    /// <c>Email:Resend:WebhookSecret</c> to <paramref name="secret"/>. Used by the
    /// Stage 8e Resend webhook tests to inject a deterministic signing secret without
    /// touching user-secrets or the production environment.
    /// </summary>
    public WebApplicationFactory<Program> WithWebhookSecret(string secret) =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Resend:WebhookSecret"] = secret,
                })));

    /// <summary>
    /// Returns a derived factory that appends every log message to <paramref name="sink"/>.
    /// Chain with <see cref="WithReplacedService{T}"/> to combine effects.
    /// </summary>
    public WebApplicationFactory<Program> WithCapturedLogger(List<string> sink) =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ILoggerProvider>(new InMemoryLoggerProvider(sink))));

    /// <summary>
    /// Returns a derived factory in which every <see cref="Argon2idPasswordHasher"/>
    /// invocation increments the singleton <see cref="Argon2idCallCounter"/> exposed
    /// via the out parameter. Used by constant-time-defence tests to assert that
    /// two HTTP branches perform the same number of Argon2id operations — a
    /// deterministic replacement for wall-clock timing assertions.
    ///
    /// Both production DI registrations are replaced (the <see cref="IPasswordHasher{T}"/>
    /// interface for Identity's UserManager AND the concrete <see cref="Argon2idPasswordHasher"/>
    /// for services that call `RunDummyHash` directly), so every Argon2id call site
    /// is observed.
    ///
    /// Usage:
    /// <code>
    /// await using var factory = _factory.WithArgon2idCounter(out var counter);
    /// counter.Reset();
    /// await PostKnownBranch();
    /// var knownCount = counter.Count;
    /// counter.Reset();
    /// await PostUnknownBranch();
    /// var unknownCount = counter.Count;
    /// unknownCount.Should().Be(knownCount);
    /// </code>
    /// </summary>
    public WebApplicationFactory<Program> WithArgon2idCounter(out Common.Argon2idCallCounter counter)
    {
        var c = new Common.Argon2idCallCounter();
        counter = c;
        return this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => ConfigureCountingHasher(services, c)));
    }

    /// <summary>
    /// Combined hook for tests that need BOTH a replaced singleton service AND the
    /// Argon2id counter. Necessary because both <see cref="WithReplacedService{T}"/>
    /// and <see cref="WithArgon2idCounter"/> return the base
    /// <see cref="WebApplicationFactory{Program}"/> (the configuration methods don't
    /// live on the derived type), so they can't be fluently chained on this factory.
    /// </summary>
    public WebApplicationFactory<Program> WithReplacedServiceAndArgon2idCounter<T>(
        T replacement, out Common.Argon2idCallCounter counter) where T : class
    {
        var c = new Common.Argon2idCallCounter();
        counter = c;
        return this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<T>();
                services.AddSingleton(replacement);
                ConfigureCountingHasher(services, c);
            }));
    }

    private static void ConfigureCountingHasher(IServiceCollection services, Common.Argon2idCallCounter counter)
    {
        services.RemoveAll<IPasswordHasher<ApplicationUser>>();
        services.RemoveAll<Argon2idPasswordHasher>();
        services.AddSingleton(counter);
        services.AddScoped<Argon2idPasswordHasher,
                           Common.CountingArgon2idPasswordHasher>();
        services.AddScoped<IPasswordHasher<ApplicationUser>>(sp =>
            sp.GetRequiredService<Argon2idPasswordHasher>());
    }
}

/// <summary>
/// All integration tests — both TestDbFixture-based and WebApplicationFactory-based —
/// share a single xUnit collection. xUnit runs all classes in a collection sequentially
/// on one thread, eliminating races between concurrent writes to project_ceres_test.
/// </summary>
[CollectionDefinition("IntegrationTests")]
public class IntegrationCollection
    : ICollectionFixture<SweepingTestWebApplicationFactory>,
      ICollectionFixture<TestWebApplicationFactory>,
      ICollectionFixture<AuthTestWebApplicationFactory>
{ }

/// <summary>
/// The one factory that sweeps abandoned test users on teardown.
///
/// xUnit disposes collection fixtures after every test in the collection has run, so
/// this is the only disposal point where deleting test users cannot pull the ground out
/// from under a test still executing. Ad-hoc factories built inside a test must NOT
/// sweep — see TestWebApplicationFactory.SweepOnDispose.
///
/// It exists purely for its teardown; no test needs to inject it.
/// </summary>
public class SweepingTestWebApplicationFactory : TestWebApplicationFactory
{
    protected override bool SweepOnDispose => true;
}
