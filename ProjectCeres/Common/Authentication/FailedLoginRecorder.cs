using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public class FailedLoginRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILookupNormalizer _normalizer;

    // Use IServiceScopeFactory so each RecordAsync call gets a fresh, isolated
    // DbContext scope. When PasswordSignInAsync races under concurrency, Identity's
    // EF UserStore catches a DbUpdateConcurrencyException internally and returns
    // IdentityResult.Failed — but the shared request-scoped DbContext is left with
    // the stale AspNetUsers entity still tracked. A subsequent SaveChangesAsync on
    // that same context would re-attempt the failed update and throw again, silently
    // swallowing the FailedLoginAttempt insert. Creating a private scope here avoids
    // the contaminated context entirely.
    public FailedLoginRecorder(IServiceScopeFactory scopeFactory, ILookupNormalizer normalizer)
    {
        _scopeFactory = scopeFactory;
        _normalizer = normalizer;
    }

    public virtual async Task RecordAsync(
        string? emailAttempted,
        Guid? userId,
        FailedLoginReason reason,
        string ipAddress,
        string userAgent,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = new FailedLoginAttempt
        {
            Id = Guid.NewGuid(),
            EmailAttempted = TruncateAndNormalize(emailAttempted, 256),
            UserId = userId,
            IpAddress = string.IsNullOrEmpty(ipAddress) ? "unknown" : ipAddress,
            UserAgent = Truncate(userAgent ?? "", 512),
            Reason = reason,
            OccurredAt = DateTime.UtcNow,
        };
        db.FailedLoginAttempts.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    private string? TruncateAndNormalize(string? input, int max)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        // Stage 9.1.5.b §4.6: route through ILookupNormalizer (the same normalizer
        // UserManager.NormalizeEmail uses, and that LockoutCache uses) so all three
        // sites produce identical email keys. The DI-registered LowercaseLookupNormalizer
        // preserves the prior lowercase semantics. Falls back to a trim-only path if
        // the normalizer returns null for any reason.
        var normalized = _normalizer.NormalizeEmail(input.Trim()) ?? input.Trim();
        return normalized.Length > max ? normalized[..max] : normalized;
    }

    private static string Truncate(string input, int max)
        => input.Length > max ? input[..max] : input;
}
