using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// In-memory hint cache used by <c>OnRejected</c> to surface ACCOUNT_LOCKED_OUT
/// instead of RATE_LIMITED when the per-IP rate-limit fires AFTER the account
/// has already been locked. Two entry shapes:
/// <list type="bullet">
///   <item><c>lockout:{normalizedEmail}</c> → <see cref="DateTimeOffset"/> of LockoutEnd. TTL = <see cref="LockoutCacheOptions.EntryTtl"/>.</item>
///   <item><c>last-login-email:{ip}</c> → (email, lockedAt). TTL = <see cref="LockoutCacheOptions.IpPointerTtl"/>. OnRejected only honors the pointer if lockedAt is within the IpPointerTtl window.</item>
/// </list>
/// Memory-only reads in OnRejected — no DB query — so the DoS-amplification
/// concern in security-model.md § lockout is preserved.
///
/// Single-host. Multi-host migration is tracked under Stage 16 alongside the
/// _loginLocks semaphore (see planning-phase3.md Stage 16 entry).
/// </summary>
public sealed class LockoutCache
{
    private const string EntryKeyPrefix = "lockout:";
    private const string PointerKeyPrefix = "last-login-email:";

    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<LockoutCacheOptions> _options;

    public LockoutCache(IMemoryCache cache, IServiceScopeFactory scopeFactory, IOptions<LockoutCacheOptions> options)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public bool TryGetLockoutEnd(string email, out DateTimeOffset lockoutEnd)
    {
        var key = EntryKey(email);
        if (key is null) { lockoutEnd = default; return false; }
        return _cache.TryGetValue(key, out lockoutEnd);
    }

    public void SetLockoutEnd(string email, DateTimeOffset until)
    {
        var key = EntryKey(email);
        if (key is null) return;
        _cache.Set(key, until, _options.Value.EntryTtl);
    }

    public bool TryGetLastLockedEmailForIp(string ip, out string normalizedEmail, out DateTimeOffset lockedAt)
    {
        normalizedEmail = string.Empty;
        lockedAt = default;
        if (string.IsNullOrWhiteSpace(ip)) return false;
        if (!_cache.TryGetValue<PointerEntry>(PointerKeyPrefix + ip, out var entry) || entry is null)
            return false;

        // Honor the pointer only if lockedAt is within IpPointerTtl. The IMemoryCache absolute
        // expiry already enforces this, but the explicit check defends against clock skew or
        // any future caller that reads the entry via a non-expiring path.
        if (DateTimeOffset.UtcNow - entry.LockedAt > _options.Value.IpPointerTtl) return false;
        normalizedEmail = entry.NormalizedEmail;
        lockedAt = entry.LockedAt;
        return true;
    }

    public void SetLastLockedEmailForIp(string ip, string email)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        var normalized = Normalize(email);
        if (string.IsNullOrEmpty(normalized)) return;
        _cache.Set(
            PointerKeyPrefix + ip,
            new PointerEntry(normalized, DateTimeOffset.UtcNow),
            _options.Value.IpPointerTtl);
    }

    public void Remove(string email)
    {
        var key = EntryKey(email);
        if (key is not null) _cache.Remove(key);
    }

    private string? EntryKey(string email)
    {
        var normalized = Normalize(email);
        return string.IsNullOrEmpty(normalized) ? null : EntryKeyPrefix + normalized;
    }

    // ILookupNormalizer is scoped; resolve per-call from a short-lived scope so this
    // singleton stays valid under scope validation. The default UpperInvariantLookupNormalizer
    // is stateless, so per-call resolution is cheap.
    private string? Normalize(string email)
    {
        using var scope = _scopeFactory.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();
        return normalizer.NormalizeEmail(email);
    }

    private sealed record PointerEntry(string NormalizedEmail, DateTimeOffset LockedAt);
}
