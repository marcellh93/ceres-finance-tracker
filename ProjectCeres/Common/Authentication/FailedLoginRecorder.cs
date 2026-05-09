using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class FailedLoginRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;

    // Use IServiceScopeFactory so each RecordAsync call gets a fresh, isolated
    // DbContext scope. When PasswordSignInAsync races under concurrency, Identity's
    // EF UserStore catches a DbUpdateConcurrencyException internally and returns
    // IdentityResult.Failed — but the shared request-scoped DbContext is left with
    // the stale AspNetUsers entity still tracked. A subsequent SaveChangesAsync on
    // that same context would re-attempt the failed update and throw again, silently
    // swallowing the FailedLoginAttempt insert. Creating a private scope here avoids
    // the contaminated context entirely.
    public FailedLoginRecorder(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RecordAsync(
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

    private static string? TruncateAndNormalize(string? input, int max)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var normalized = input.Trim().ToLowerInvariant();
        return normalized.Length > max ? normalized[..max] : normalized;
    }

    private static string Truncate(string input, int max)
        => input.Length > max ? input[..max] : input;
}
