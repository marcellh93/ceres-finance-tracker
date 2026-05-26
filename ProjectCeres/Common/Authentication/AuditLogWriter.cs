using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

[PreAuthScope]
public class AuditLogWriter : IAuditLogWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    // Mirrors FailedLoginRecorder's fresh-scope discipline. Each RecordAsync
    // call resolves its own AppDbContext from a private scope to insulate
    // the writer from a request DbContext that may have been left holding
    // a stale tracked entity after an Identity-internal concurrency race.
    public AuditLogWriter(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
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
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
            IpAddress = string.IsNullOrEmpty(ip) ? "unknown" : ip,
        };

        // Stage 9.6.1 (2026-05-18) — AuditLogs is RLS-protected (user-owned
        // table). This writer creates its own DI scope per call (fresh
        // DbContext + fresh connection), so it doesn't inherit any GUC the
        // caller's PreAuthUserScope might have set. Wrap the write in our
        // own PreAuthUserScope so the user_isolation policy passes
        // regardless of whether the caller is pre-auth or authed.
        await using var rlsScope = await db.BeginPreAuthUserScopeAsync(userId, ct);
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(ct);
        await rlsScope.CommitAsync(ct);
    }
}
