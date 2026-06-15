namespace ProjectCeres.Common.Authentication;

/// <summary>
/// TTL configuration for <see cref="LockoutCache"/>. The per-email entry's
/// lifetime is governed by the <c>until</c> value passed to
/// <see cref="LockoutCache.SetLockoutEnd(string, DateTimeOffset)"/>
/// (cache TTL = <c>until - UtcNow</c>), so no separate per-email knob is
/// needed here. <see cref="IpPointerTtl"/> stays because the per-IP pointer's
/// lifetime is independent of any specific lockout — it tracks "the most
/// recent lockout from this IP" and is bounded by the rate-limit window.
///
/// Not bound to configuration; production uses the defaults. Tests override
/// via <c>services.PostConfigure&lt;LockoutCacheOptions&gt;(...)</c> to avoid
/// wall-clock waits.
/// </summary>
public sealed class LockoutCacheOptions
{
    /// <summary>TTL for per-IP "the last lockout from this IP was for email X at time T" pointer.
    /// Default = 60 seconds, matching the <c>AuthLoginByIp</c> rate-limit window so the
    /// pointer is only honored within one rate-limit cycle.</summary>
    public TimeSpan IpPointerTtl { get; set; } = TimeSpan.FromSeconds(60);
}
