using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public class ExportJobService(
    AppDbContext db,
    ICurrentUserAccessor user,
    TimeProvider timeProvider,
    IAuditLogWriter auditLog) : IExportJobService
{
    public async Task<ExportJob> CreateOrGetPendingAsync(CancellationToken ct)
    {
        var existing = await db.ExportJobs
            .Owned(user)
            .Where(j => j.Status == ExportJobStatus.Pending || j.Status == ExportJobStatus.Processing)
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var job = new ExportJob
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            Status = ExportJobStatus.Pending,
            Format = ExportFormat.Zip,
            RequestedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.ExportJobs.Add(job);
        await db.SaveChangesAsync(ct);

        // GDPR accountability: record the request itself (not a dedupe-return).
        await auditLog.RecordAsync(user.UserId, AuditLogAction.DataExportRequested,
            entityType: nameof(ExportJob), entityId: job.Id, ct: ct);
        return job;
    }

    public async Task<ExportJob?> FindOwnByTokenAsync(byte[] tokenLookup, CancellationToken ct) =>
        await db.ExportJobs.Owned(user).FirstOrDefaultAsync(j => j.TokenLookup == tokenLookup, ct);

    public async Task MarkConsumedAsync(ExportJob job, CancellationToken ct)
    {
        job.ConsumedAt = timeProvider.GetUtcNow().UtcDateTime;
        db.ExportJobs.Update(job);
        await db.SaveChangesAsync(ct);
    }
}
