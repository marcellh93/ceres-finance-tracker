using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public class AuditLogWriter : IAuditLogWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Mirrors FailedLoginRecorder's fresh-scope discipline. Each RecordAsync
    // call resolves its own AppDbContext from a private scope to insulate
    // the writer from a request DbContext that may have been left holding
    // a stale tracked entity after an Identity-internal concurrency race.
    public AuditLogWriter(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public virtual async Task RecordAsync(
        Guid userId,
        AuditLogAction action,
        string? entityType = null,
        Guid? entityId = null,
        CancellationToken ct = default)
    {
        if ((entityType is null) != (entityId is null))
            throw new ArgumentException(
                "EntityType and EntityId must both be set or both be null.",
                nameof(entityType));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAt = DateTime.UtcNow,
            IpAddress = string.IsNullOrEmpty(ip) ? "unknown" : ip,
        };

        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
