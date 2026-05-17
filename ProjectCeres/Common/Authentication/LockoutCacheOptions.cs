namespace ProjectCeres.Common.Authentication;

/// <summary>
/// TTL configuration for <see cref="LockoutCache"/>. Defaults match production
/// (15-minute lockout window for per-email entries, 60-second pointer expiry
/// for per-IP entries — aligned with the AuthLoginByIp sliding window). Tests
/// shorten both via PostConfigure to avoid wall-clock waits.
/// </summary>
public sealed class LockoutCacheOptions
{
    /// <summary>TTL for per-email "this account is locked until X" entries.
    /// Default = 15 minutes, matching Identity's <c>DefaultLockoutTimeSpan</c>.</summary>
    public TimeSpan EntryTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>TTL for per-IP "the last lockout from this IP was for email X at time T" pointer.
    /// Default = 60 seconds, matching the <c>AuthLoginByIp</c> rate-limit window so the
    /// pointer is only honored within one rate-limit cycle.</summary>
    public TimeSpan IpPointerTtl { get; set; } = TimeSpan.FromSeconds(60);
}
