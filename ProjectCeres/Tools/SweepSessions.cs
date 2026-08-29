using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;

namespace ProjectCeres.Tools;

/// <summary>
/// Retention sweep for UserSession rows. A FLAT cross-tenant DELETE through
/// AdminDbContext (ceres_admin, BYPASSRLS) — not a per-user fan-out, so it does
/// NOT use IUserJobRunner/BackgroundJobScope. AppDbContext/ceres_app would scope
/// the DELETE to one user via RLS and delete nothing. Invoked by cron via
/// `dotnet run -- --sweep-sessions`, mirroring SeedDevUser.
/// </summary>
[RequiresAdminContext]
public static class SweepSessions
{
    /// <summary>Deletes rows past the 90-day retention horizon. Returns rows deleted.</summary>
    public static async Task<int> DeleteExpiredAsync(
        AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now - SessionConstants.RetentionHorizon;

        // IgnoreQueryFilters: cross-tenant by design — the sweep spans all users.
        // Stage 10 architecture test allow-lists this file.
        return await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => (s.RevokedAt != null && s.RevokedAt < cutoff)
                     || (s.RevokedAt == null && !s.IsPersistent && s.LastUsedAt < cutoff))
            .ExecuteDeleteAsync(ct);
    }

    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AdminDbContext>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var logger = sp.GetRequiredService<ILogger<Program>>();

        var deleted = await DeleteExpiredAsync(db, clock, CancellationToken.None);
        logger.LogInformation("Session sweep deleted {Count} expired UserSession rows.", deleted);
        return 0;
    }
}
