using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tools;

/// <summary>
/// Stage 13.b inactivity sweep. 12mo no login → one warning email; 18mo → anonymise.
/// Flat cross-tenant find (AdminDbContext, BYPASSRLS) + per-user anonymise loop, the
/// RetentionPurge/ErasureWorker pattern. Skips Sealed/Erased accounts (never collides
/// with an in-flight erasure). Invoked via `dotnet run -- --run-inactivity-sweep`.
/// </summary>
[RequiresAdminContext]
public static class InactivitySweep
{
    public static readonly TimeSpan WarningHorizon = TimeSpan.FromDays(365);     // 12 months
    public static readonly TimeSpan AnonymiseHorizon = TimeSpan.FromDays(548);   // 18 months
    private const int MonthsUntilAnonymise = 18;

    public static async Task<(int Warned, int Anonymised)> ProcessAsync(
        AdminDbContext db,
        UserAnonymiser anonymiser,
        IInactivityWarningNotificationService warner,
        ErasurePseudonym pseudonym,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var warnCutoff = now - WarningHorizon;
        var anonCutoff = now - AnonymiseHorizon;

        // Anonymise group first (older horizon). Cross-tenant by design — allow-listed.
        var toAnonymise = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SealedAt == null && u.ErasedAt == null && u.LastLoginAt <= anonCutoff)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var anonymised = 0;
        foreach (var userId in toAnonymise)
        {
            // Re-check immediately before acting: a user could log in or seal an erasure
            // between the query and this iteration (ErasureWorker's defense-in-depth re-read).
            var current = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.SealedAt, u.ErasedAt, u.LastLoginAt })
                .FirstOrDefaultAsync(ct);
            if (current is null || current.SealedAt != null || current.ErasedAt != null
                || current.LastLoginAt > anonCutoff) continue;

            try
            {
                var token = pseudonym.Compute(userId);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await anonymiser.AnonymiseAsync(userId, token, ct);
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = AuditLogAction.InactivityAnonymised, // CK_AuditLog_EntityPair: EntityType/EntityId left null together
                    OccurredAt = clock.GetUtcNow().UtcDateTime,
                    IpAddress = "unknown",
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                anonymised++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inactivity anonymise failed for {UserId}; continuing.", userId);
                db.ChangeTracker.Clear();
            }
        }

        // Warning group: inactive >= 12mo and < 18mo and not yet warned.
        var toWarn = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SealedAt == null && u.ErasedAt == null
                && u.LastLoginAt <= warnCutoff && u.LastLoginAt > anonCutoff
                && u.InactivityWarningSentAt == null)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var warned = 0;
        foreach (var userId in toWarn)
        {
            try
            {
                var sent = await warner.NotifyAsync(userId, MonthsUntilAnonymise, ct);
                if (!sent) continue; // stays eligible next run

                await db.Users.IgnoreQueryFilters()
                    .Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.InactivityWarningSentAt, now), ct);
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = AuditLogAction.InactivityWarningSent,
                    OccurredAt = clock.GetUtcNow().UtcDateTime,
                    IpAddress = "unknown",
                });
                await db.SaveChangesAsync(ct);
                warned++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inactivity warning failed for {UserId}; continuing.", userId);
                db.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("Inactivity sweep: {Warned} warned, {Anonymised} anonymised.", warned, anonymised);
        return (warned, anonymised);
    }

    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await ProcessAsync(
            sp.GetRequiredService<AdminDbContext>(),
            sp.GetRequiredService<UserAnonymiser>(),
            sp.GetRequiredService<IInactivityWarningNotificationService>(),
            sp.GetRequiredService<ErasurePseudonym>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<Program>>(),
            CancellationToken.None);
        return 0;
    }
}
