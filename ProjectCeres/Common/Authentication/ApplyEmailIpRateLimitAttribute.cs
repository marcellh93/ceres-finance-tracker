namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Marker attribute applied to controller actions that should be protected by the
/// EmailByIp 10/hr/IP backstop. The rate-limit policy is enforced by a
/// <see cref="System.Threading.RateLimiting.PartitionedRateLimiter{TResource}"/>
/// registered on <c>RateLimiterOptions.GlobalLimiter</c> — that global limiter checks
/// every request's endpoint metadata for this attribute and returns
/// <c>RateLimitPartition.GetNoLimiter</c> when absent.
///
/// We use a marker attribute + <c>GlobalLimiter</c> rather than a second
/// <c>[EnableRateLimiting]</c> on the same action because
/// <c>EnableRateLimitingAttribute</c> is declared with <c>AllowMultiple = false</c>;
/// stacking it does not compose two policies on a single endpoint.
/// Stage 8d.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ApplyEmailIpRateLimitAttribute : Attribute
{
}
