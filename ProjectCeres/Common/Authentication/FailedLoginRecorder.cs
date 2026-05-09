using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class FailedLoginRecorder
{
    private readonly AppDbContext _db;

    public FailedLoginRecorder(AppDbContext db) => _db = db;

    public async Task RecordAsync(
        string? emailAttempted,
        Guid? userId,
        FailedLoginReason reason,
        string ipAddress,
        string userAgent,
        CancellationToken ct = default)
    {
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
        _db.FailedLoginAttempts.Add(entry);
        await _db.SaveChangesAsync(ct);
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
