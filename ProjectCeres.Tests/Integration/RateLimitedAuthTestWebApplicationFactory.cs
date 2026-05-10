using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
            });
        });
    }
}

[CollectionDefinition("RateLimitTests", DisableParallelization = true)]
public class RateLimitTestsCollection
    : ICollectionFixture<RateLimitedAuthTestWebApplicationFactory>
{ }
