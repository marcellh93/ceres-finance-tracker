using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Sibling of AuthTestWebApplicationFactory used by Stage 6b.2 rate-limit tests.
/// Inherits the real auth pipeline + the no-op rate-limiter PostConfigure from
/// the base class, then RE-REGISTERS the production sliding-window policies via
/// ConfigureTestServices (which runs after ConfigureServices/Configure/PostConfigure).
///
/// Tests that use this factory MUST live in their own xUnit collection
/// ("RateLimitTests") so the in-memory partition state does not leak across test
/// classes that share IntegrationTests.
/// </summary>
public sealed class RateLimitedAuthTestWebApplicationFactory : AuthTestWebApplicationFactory
{
    // No test-window compression: each test that needs a fresh limiter state
    // builds a derived inner host via WithFreshRateLimiter() below, which gives
    // an empty middleware partition without sleeping. The window stays at production
    // 60s; the fresh-host pattern is the community-recommended alternative when
    // System.Threading.RateLimiting cannot accept TimeProvider
    // (https://github.com/dotnet/runtime/issues/52079).
    // See project memory project_test_suite_performance.md.

    /// <summary>
    /// Returns a derived factory with a brand-new inner WebHost — and therefore a
    /// brand-new <see cref="Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware"/>
    /// holding fresh, empty rate-limit partition state. Use this at the start of any
    /// test in the RateLimitTests collection that depends on the limiter starting
    /// fresh. Replaces the prior pattern of `Task.Delay(70s)` waiting for the
    /// production-scale window to expire.
    /// </summary>
    public WebApplicationFactory<Program> WithFreshRateLimiter() =>
        this.WithWebHostBuilder(_ => { });

    /// <summary>
    /// Returns a derived factory with a brand-new inner WebHost AND the
    /// AuthLoginByIp rate-limit policy reconfigured to use a 1-second sliding
    /// window. Use this ONLY for tests whose semantics require the window to
    /// actually expire (e.g. <c>Login_LimiterResetsAfterWindow</c>), so they can
    /// wait ~1.1s instead of 70s. Other policies stay at production 60s.
    /// </summary>
    public WebApplicationFactory<Program> WithShortLoginWindow() =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(opts =>
                {
                    var type = opts.GetType();
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    var policyMap = type.GetProperty("PolicyMap", flags)!.GetValue(opts)!;
                    var removeFromPolicy = policyMap.GetType().GetMethod("Remove", new[] { typeof(string) })!;
                    removeFromPolicy.Invoke(policyMap, new object[] { AuthRateLimitPolicies.AuthLoginByIp });

                    opts.AddPolicy(AuthRateLimitPolicies.AuthLoginByIp, httpContext =>
                    {
                        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromSeconds(1),
                            SegmentsPerWindow = 2,
                            QueueLimit = 0,
                        });
                    });
                });
            }));

    /// <summary>
    /// 1.1s delay matching the short login window in <see cref="WithShortLoginWindow"/>.
    /// </summary>
    public static readonly TimeSpan ShortLoginWindowClearDelay = TimeSpan.FromMilliseconds(1100);

    /// <summary>
    /// Returns a derived factory whose AuthLoginByIp policy uses a 5-second window
    /// (instead of production 60s OR the 1s in <see cref="WithShortLoginWindow"/>).
    /// 5s is the smallest window that comfortably accommodates an 11-request test
    /// burst (~1-3s of HTTP + Argon2id overhead) while still being 12× faster than
    /// production. Use ONLY for tests that need the window LARGER than test
    /// execution but SHORTER than production — e.g. the sliding-window-boundary
    /// attack scenario.
    /// </summary>
    public WebApplicationFactory<Program> WithMediumLoginWindow() =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(opts =>
                {
                    var type = opts.GetType();
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    var policyMap = type.GetProperty("PolicyMap", flags)!.GetValue(opts)!;
                    var removeFromPolicy = policyMap.GetType().GetMethod("Remove", new[] { typeof(string) })!;
                    removeFromPolicy.Invoke(policyMap, new object[] { AuthRateLimitPolicies.AuthLoginByIp });

                    opts.AddPolicy(AuthRateLimitPolicies.AuthLoginByIp, httpContext =>
                    {
                        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromSeconds(5),
                            SegmentsPerWindow = 4,
                            QueueLimit = 0,
                        });
                    });
                });
            }));

    /// <summary>
    /// Returns a derived factory with a fresh, empty <see cref="IMemoryCache"/> singleton.
    /// Use this for per-email rate-limit tests so each test starts with a clean bucket
    /// regardless of prior test state.
    /// </summary>
    public WebApplicationFactory<Program> WithFreshMemoryCache() =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMemoryCache>();
                services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
            }));

    /// <summary>
    /// Evicts all entries from the shared <see cref="IMemoryCache"/>, simulating an elapsed
    /// time window without waiting. Calls <c>Compact(1.0)</c> which flushes 100% of entries.
    /// Call on a derived factory returned from <see cref="WithFreshMemoryCache"/> to reset
    /// the per-email bucket mid-test.
    /// </summary>
    public static void ResetMemoryCache(WebApplicationFactory<Program> factory) =>
        ((MemoryCache)factory.Services.GetRequiredService<IMemoryCache>()).Compact(1.0);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<RateLimiterOptions>(opts =>
            {
                var type = opts.GetType();
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                var policyMapProp     = type.GetProperty("PolicyMap",          flags)!;
                var unactivatedProp   = type.GetProperty("UnactivatedPolicyMap", flags)!;

                var policyMap      = policyMapProp.GetValue(opts)!;
                var unactivatedMap = unactivatedProp.GetValue(opts)!;

                var removeFromPolicy      = policyMap.GetType()     .GetMethod("Remove", new[] { typeof(string) })!;
                var removeFromUnactivated = unactivatedMap.GetType().GetMethod("Remove", new[] { typeof(string) })!;

                foreach (var name in new[]
                {
                    AuthRateLimitPolicies.AuthLoginByIp,
                    AuthRateLimitPolicies.AuthTotpByUser,
                    AuthRateLimitPolicies.AuthCsrfByIp,
                    AuthRateLimitPolicies.AuthMfaByUser,
                    AuthRateLimitPolicies.AuthReauthByUser,
                })
                {
                    removeFromPolicy.Invoke(policyMap, new object[] { name });
                    removeFromUnactivated.Invoke(unactivatedMap, new object[] { name });
                }

                opts.AddPolicy(AuthRateLimitPolicies.AuthLoginByIp, httpContext =>
                {
                    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(60),
                        SegmentsPerWindow = 4,
                        QueueLimit = 0,
                    });
                });

                // TotpByUserPartitioner is internal to ProjectCeres and not accessible
                // from the test assembly. Replicate the partition logic as a lambda —
                // functionally identical to the production typed partitioner.
                opts.AddPolicy(AuthRateLimitPolicies.AuthTotpByUser, httpContext =>
                {
                    var task = httpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
                    task.Wait();
                    var userId = task.Result.Principal?.Identity?.Name
                              ?? AuthRateLimitPolicies.AnonymousTotpPartition;

                    return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(60),
                        SegmentsPerWindow = 4,
                        QueueLimit = 0,
                    });
                });

                opts.AddPolicy(AuthRateLimitPolicies.AuthCsrfByIp, httpContext =>
                {
                    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromSeconds(60),
                        SegmentsPerWindow = 4,
                        QueueLimit = 0,
                    });
                });

                opts.AddPolicy(AuthRateLimitPolicies.AuthMfaByUser, httpContext =>
                {
                    var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? "anonymous-mfa";
                    return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(60),
                        SegmentsPerWindow = 4,
                        QueueLimit = 0,
                    });
                });

                opts.AddPolicy(AuthRateLimitPolicies.AuthReauthByUser, httpContext =>
                {
                    // Rate limiter runs BEFORE UseAuthentication, so httpContext.User is empty here.
                    // Explicitly authenticate against the application cookie scheme to resolve the
                    // current user id for per-user partitioning. Mirrors the pattern in
                    // TotpByUserPartitioner (which authenticates against TwoFactorUserIdScheme).
                    var task = httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                    task.Wait();
                    var userId = task.Result.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? "anonymous-reauth";
                    return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(60),
                        SegmentsPerWindow = 4,
                        QueueLimit = 0,
                    });
                });
            });
        });
    }
}

[CollectionDefinition("RateLimitTests", DisableParallelization = true)]
public class RateLimitTestsCollection
    : ICollectionFixture<RateLimitedAuthTestWebApplicationFactory>
{ }

/// <summary>
/// Separate collection for MFA-specific rate-limit tests. Uses its own
/// RateLimitedAuthTestWebApplicationFactory instance so the in-process login/TOTP
/// partition state from RateLimitedAuthEndpointTests does not bleed into
/// MfaRegenerateRateLimitTests.
/// </summary>
[CollectionDefinition("MfaRateLimitTests", DisableParallelization = true)]
public class MfaRateLimitTestsCollection
    : ICollectionFixture<RateLimitedAuthTestWebApplicationFactory>
{ }
