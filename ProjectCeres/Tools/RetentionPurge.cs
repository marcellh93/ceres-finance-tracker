using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Data;

namespace ProjectCeres.Tools;

/// <summary>
/// GDPR retention purges — four FLAT cross-tenant DELETEs through AdminDbContext
/// (ceres_admin, BYPASSRLS), mirroring SweepSessions. NOT per-user fan-outs, so no
/// IUserJobRunner/BackgroundJobScope. AppDbContext/ceres_app would RLS-scope the
/// DELETE to nobody. Invoked by cron via `dotnet run -- --run-retention-purge`.
/// Retention periods are fixed by Stage 13 decision (planning-resolved 2026-09-19).
/// </summary>
[RequiresAdminContext]
public static class RetentionPurge
{
    public static readonly TimeSpan AuditLogHorizon = TimeSpan.FromDays(365);      // 12 months
    public static readonly TimeSpan FailedLoginHorizon = TimeSpan.FromDays(365);   // 1 year
    public static readonly TimeSpan SoftDeleteHorizon = TimeSpan.FromDays(90);     // 90 days

    /// <summary>Deletes AuditLog rows older than 12 months. Returns rows deleted.</summary>
    public static async Task<int> PurgeAuditLogAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - AuditLogHorizon;
        // IgnoreQueryFilters: cross-tenant by design — spans all users (allow-listed).
        return await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.OccurredAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Deletes FailedLoginAttempt rows older than 1 year. Returns rows deleted.</summary>
    public static async Task<int> PurgeFailedLoginsAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - FailedLoginHorizon;
        // FailedLoginAttempt carries no RLS filter, but IgnoreQueryFilters is harmless
        // and keeps the pattern uniform with the IUserOwned purges.
        return await db.FailedLoginAttempts
            .IgnoreQueryFilters()
            .Where(f => f.OccurredAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Hard-deletes SavedReport rows soft-deleted more than 90 days ago. Returns rows deleted.</summary>
    public static async Task<int> PurgeSavedReportsAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - SoftDeleteHorizon;
        return await db.SavedReports
            .IgnoreQueryFilters()
            .Where(r => r.DeletedAt != null && r.DeletedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Hard-deletes ImportProfile rows soft-deleted more than 90 days ago (all formats). Returns rows deleted.</summary>
    public static async Task<int> PurgeImportProfilesAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - SoftDeleteHorizon;
        return await db.ImportProfiles
            .IgnoreQueryFilters()
            .Where(p => p.DeletedAt != null && p.DeletedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
