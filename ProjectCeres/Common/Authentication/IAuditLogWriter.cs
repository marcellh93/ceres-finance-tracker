using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public interface IAuditLogWriter
{
    Task RecordAsync(
        Guid userId,
        AuditLogAction action,
        string? entityType = null,
        Guid? entityId = null,
        CancellationToken ct = default);
}
