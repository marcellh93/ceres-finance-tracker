using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Runs the §8 erasure sequence for one user: anonymise-and-retain the statutory
/// financial tables, redact-and-retain support correspondence, hard-delete
/// everything else, delete the outstanding export ZIP, anonymise the account
/// identity, and write the completion audit row. Stage 13.9 Task 6.
///
/// Cross-tenant by design — acts for an arbitrary erased user with no HTTP
/// principal, same AdminDbContext + IgnoreQueryFilters().Where(UserId==) pattern
/// as ExportJobWorker/DataExportBuilder. All DB work, including the completion
/// audit row, is one atomic transaction — a mid-run crash rolls it back to the
/// pre-run state. The two physical file deletions (support attachment, export
/// ZIP) are irreversible side effects outside that transaction: each runs after
/// its row mutation is already saved, so a rollback after either point leaves a
/// harmless "row exists, file gone" state, never "file gone, row also gone" —
/// the deliberate ordering from fix-loop round 1. A re-run against an
/// already-Completed request is a no-op (top-of-method Sealed check); a re-run
/// against a Sealed request whose prior pass actually finished the DB work but
/// crashed before the status flip is safe to repeat (each lane is check-then-act)
/// and the final status-flip guard prevents a second completion audit row. Full
/// crash-mid-transaction resume/retry orchestration is Task 10's ErasureWorker,
/// not this class.
/// </summary>
[RequiresAdminContext]
public class ErasureExecutor(
    AdminDbContext db,
    ErasurePseudonym pseudonym,
    UserAnonymiser anonymiser,
    TimeProvider timeProvider,
    ILookupNormalizer normalizer,
    TokenLookupHasher lookupHasher,
    ILogger<ErasureExecutor> logger)
{
    public async Task ExecuteAsync(Guid userId, CancellationToken ct)
    {
        var request = await db.ErasureRequests
            .IgnoreQueryFilters()
            .Where(r => r.UserId == userId && r.Status == ErasureStatus.Sealed)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (request is null) return; // cancelled/already-completed job — nothing to do (idempotent no-op)

        logger.LogInformation("Erasure starting for user {UserId}, request {RequestId}", userId, request.Id);

        var token = pseudonym.Compute(userId);

        // Whole sequence is one transaction: a thrown exception anywhere below rolls
        // everything back when `await using` disposes the uncommitted transaction —
        // no explicit rollback needed. Standard EF Core DML transaction behavior;
        // no DDL is involved.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            await RecordEmailHoldAsync(userId, ct); // reads the REAL email — must run BEFORE the anonymiser scrubs identity
            await anonymiser.AnonymiseAsync(userId, token, ct);

            // Stage 6.14 GDPR-on-erasure: historical audit rows keep UserId as a pseudonym
            // but must not retain a real IP. Runs after the completion write below — that
            // row's IpAddress is "unknown" (no HttpContext in this background path), never
            // a real address, so rewriting before or after it makes no observable difference.
            await db.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IpAddress, "erased"), ct);

            // Guard the status flip: if some other path already completed/cancelled this
            // request (race, or a resumed call that got past the top check before a prior
            // pass's transaction committed), this affects 0 rows instead of re-flipping it.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var flipped = await db.ErasureRequests
                .IgnoreQueryFilters()
                .Where(r => r.Id == request.Id && r.Status == ErasureStatus.Sealed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ErasureStatus.Completed)
                    .SetProperty(r => r.CompletedAt, now), ct);

            // A prior pass can have completed all the DB work and even written its audit
            // row, then crashed before this flip landed, leaving Status back at Sealed for
            // a legitimate resumed re-run — the flip guard alone allows that resumed call
            // to flip Sealed->Completed a second time. Check for an existing completion
            // row too, so a genuine second full pass over already-processed data can't
            // double the "exactly one audit row" guarantee.
            var alreadyRecorded = await db.AuditLogs.IgnoreQueryFilters()
                .AnyAsync(a => a.UserId == userId && a.Action == AuditLogAction.GdprErasureCompleted, ct);

            if (flipped > 0 && !alreadyRecorded)
            {
                // IAuditLogWriter opens its own scope + AppDbContext + transaction, so a
                // call through it here would commit outside this method's transaction —
                // a false completion record if the tx below then failed to commit. Insert
                // directly on `db` (AdminDbContext, BYPASSRLS — no PreAuthUserScope needed)
                // so this row is genuinely part of the one ambient transaction, mirroring
                // the direct db.AuditLogs write used for the IpAddress rewrite above.
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = AuditLogAction.GdprErasureCompleted,
                    EntityType = token,
                    EntityId = request.Id,
                    OccurredAt = timeProvider.GetUtcNow().UtcDateTime,
                    IpAddress = "unknown",
                });
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erasure failed for user {UserId}", userId);
            throw;
        }

        logger.LogInformation("Erasure completed for user {UserId}", userId);
    }

    /// <summary>
    /// D3's 30-day re-registration hold (spec § 8 step 5). Reads the user's REAL
    /// email BEFORE <see cref="UserAnonymiser"/>'s account scrub overwrites it — that step is
    /// a raw <c>ExecuteUpdateAsync</c>, so the real value is never otherwise loaded.
    /// Upserts by fingerprint: a second erasure of the same email (post-expiry
    /// re-registration, re-erasure) would otherwise collide on the unique index.
    /// </summary>
    private async Task RecordEmailHoldAsync(Guid userId, CancellationToken ct)
    {
        var email = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(email)) return;

        var normalized = normalizer.NormalizeEmail(email) ?? email;
        var fingerprint = lookupHasher.ComputeLookup(normalized);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddDays(30);

        var existing = await db.ErasedEmailHolds.IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.EmailFingerprint == fingerprint, ct);
        if (existing is not null)
        {
            await db.ErasedEmailHolds.IgnoreQueryFilters()
                .Where(h => h.Id == existing.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(h => h.UserId, userId)
                    .SetProperty(h => h.ErasedAt, now)
                    .SetProperty(h => h.ExpiresAt, expiresAt), ct);
            return;
        }

        db.ErasedEmailHolds.Add(new ErasedEmailHold
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EmailFingerprint = fingerprint,
            ErasedAt = now,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(ct);
    }
}
