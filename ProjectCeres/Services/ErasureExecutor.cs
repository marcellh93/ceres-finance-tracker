using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common;
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
/// as ExportJobWorker/DataExportBuilder. The whole sequence runs inside one DB
/// transaction, so a mid-run crash rolls back to the pre-run state rather than
/// leaving the user half-erased. A re-run against an already-Completed request is
/// a no-op (top-of-method Sealed check); a re-run against a Sealed request whose
/// prior pass actually finished the DB work but crashed before the status flip is
/// safe to repeat (each lane is check-then-act) and the final status-flip guard
/// prevents a second completion audit row. Full crash-mid-transaction resume/retry
/// orchestration is Task 10's ErasureWorker, not this class.
/// </summary>
[RequiresAdminContext]
public class ErasureExecutor(
    AdminDbContext db,
    ErasurePseudonym pseudonym,
    IAuditLogWriter auditLog,
    IWebHostEnvironment env,
    TimeProvider timeProvider,
    ILookupNormalizer normalizer,
    TokenLookupHasher lookupHasher,
    ILogger<ErasureExecutor> logger,
    IOptions<FileAttachmentOptions>? options = null)
{
    // Mirrors DataExportBuilder/FileAttachmentService: attachment StoredPath is
    // relative to this root, never absolute.
    private readonly string _root = options?.Value.RootPath ?? env.ContentRootPath;

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

        var lanes = ErasureLanes.Classify(db.Model);

        // Whole sequence is one transaction: a thrown exception anywhere below rolls
        // everything back when `await using` disposes the uncommitted transaction —
        // no explicit rollback needed. Standard EF Core DML transaction behavior;
        // no DDL is involved.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await AnonymiseStatutoryAsync(userId, token, ct);
        await RedactSupportAsync(userId, ct);
        await PurgeAsync(userId, lanes.Purge, ct);
        await DeleteExportJobAsync(userId, ct);
        await RecordEmailHoldAsync(userId, ct);
        await AnonymiseAccountAsync(userId, token, ct);

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
            // CK_AuditLog_EntityPair (and AuditLogWriter's own guard) require
            // EntityType/EntityId to be both set or both null — entityId is the
            // completed ErasureRequest's id, the only real entity here.
            await auditLog.RecordAsync(
                userId: userId,
                action: AuditLogAction.GdprErasureCompleted,
                entityType: token,
                entityId: request.Id,
                ct: ct);
        }

        await tx.CommitAsync(ct);

        logger.LogInformation("Erasure completed for user {UserId}", userId);
    }

    /// <summary>
    /// Step 1 — anonymise-and-retain. Statutory rows keep their FKs and row count
    /// (Código de Comercio / LGT retention); only user-authored identity text is
    /// replaced. Re-running against an already-anonymised row reproduces the same
    /// deterministic token, so this step is idempotent by construction.
    /// </summary>
    private async Task AnonymiseStatutoryAsync(Guid userId, string token, CancellationToken ct)
    {
        await db.Accounts.IgnoreQueryFilters().Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Name, $"Erased {token}")
                .SetProperty(a => a.Description, (string?)null), ct);

        await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, $"Erased {token}"), ct);

        await db.Transactions.IgnoreQueryFilters().Where(t => t.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Description, (string?)null), ct);

        await db.Transfers.IgnoreQueryFilters().Where(t => t.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Description, (string?)null), ct);

        await db.LiabilityPayments.IgnoreQueryFilters().Where(t => t.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Description, (string?)null), ct);

        // Ruling 2: these files ARE the evidentiary record — leave StoredPath/ContentType/
        // FileSizeBytes untouched. Only the user-authored FileName is PII-bearing metadata.
        await db.TransactionAttachments.IgnoreQueryFilters().Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FileName, $"Erased {token}"), ct);

        await db.TransferAttachments.IgnoreQueryFilters().Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FileName, $"Erased {token}"), ct);
    }

    /// <summary>
    /// Step 2 — redact-retain support (B3a). Bodies and ticket subjects are redacted
    /// via <see cref="IdentifierRedactor"/>; attachment files are deleted from disk
    /// (no legal duty to keep them) and their rows redacted, not removed — the
    /// thread survives as de-identified knowledge.
    /// </summary>
    private async Task RedactSupportAsync(Guid userId, CancellationToken ct)
    {
        var appUser = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.UserName })
            .FirstOrDefaultAsync(ct);
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: appUser?.Email ?? "",
            DisplayName: appUser?.UserName ?? "",
            AccountTokens: []);

        var messages = await db.SupportMessages.IgnoreQueryFilters()
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);
        foreach (var message in messages)
        {
            message.Body = IdentifierRedactor.Redact(message.Body, known);
        }
        if (messages.Count > 0) await db.SaveChangesAsync(ct);

        var tickets = await db.SupportTickets.IgnoreQueryFilters()
            .Where(t => t.UserId == userId)
            .ToListAsync(ct);
        foreach (var ticket in tickets)
        {
            ticket.Subject = IdentifierRedactor.Redact(ticket.Subject, known);
        }
        if (tickets.Count > 0) await db.SaveChangesAsync(ct);

        // Row mutation is queued and saved BEFORE the physical unlink: a crash
        // between the two leaves a retained file with an already-redacted row
        // (recoverable) rather than a deleted file with a still-PII-bearing row.
        var attachments = await db.SupportTicketAttachments.IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);
        var pathsToDelete = new List<string>(attachments.Count);
        foreach (var attachment in attachments)
        {
            pathsToDelete.Add(Path.Combine(_root, attachment.StoredPath));

            attachment.FileName = "[removed]";
            // StoredPath is non-nullable string; the path is now dead, so empty it —
            // GetAsync/DeleteAsync both go through FileAttachmentService, which is
            // RLS-scoped and can no longer reach an erased user's rows anyway.
            attachment.StoredPath = "";
        }
        if (attachments.Count > 0) await db.SaveChangesAsync(ct);

        foreach (var fullPath in pathsToDelete)
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
    }

    /// <summary>Step 3 — hard-delete every remaining user-content table. 0 rows in a
    /// lane the user never touched is a legitimate outcome, not an error.</summary>
    private async Task PurgeAsync(Guid userId, IReadOnlyList<string> purgeTables, CancellationToken ct)
    {
        // Transaction.BudgetId → Budget is DeleteBehavior.Restrict, and Transaction is
        // statutory (retained) while Budget is purge-lane — sever the FK first or the
        // Budget delete below violates the constraint.
        await db.Transactions.IgnoreQueryFilters().Where(t => t.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BudgetId, (Guid?)null), ct);

        foreach (var table in purgeTables)
        {
            await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{table}\" WHERE \"UserId\" = {{0}}", [userId], ct);
        }
    }

    /// <summary>Step 4 — delete any outstanding ExportJob row + ZIP. Naturally
    /// idempotent: File.Exists guards the delete, and an absent row is a no-op.
    /// Rows are deleted before their files are unlinked — if the row-delete
    /// throws, no files are touched yet; if it succeeds, the files are orphans of
    /// an already-deleted row, which is the safe state even if the unlink below
    /// itself later fails.</summary>
    private async Task DeleteExportJobAsync(Guid userId, CancellationToken ct)
    {
        var jobs = await db.ExportJobs.IgnoreQueryFilters()
            .Where(j => j.UserId == userId)
            .ToListAsync(ct);

        await db.ExportJobs.IgnoreQueryFilters()
            .Where(j => j.UserId == userId)
            .ExecuteDeleteAsync(ct);

        foreach (var job in jobs)
        {
            if (!string.IsNullOrEmpty(job.StoredPath))
            {
                var fullPath = Path.Combine(_root, job.StoredPath);
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
        }
    }

    /// <summary>
    /// D3's 30-day re-registration hold (spec § 8 step 5). Reads the user's REAL
    /// email BEFORE <see cref="AnonymiseAccountAsync"/> overwrites it — that step is
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

    /// <summary>
    /// Step 5 — set ErasedAt, keep SealedAt, anonymise Identity's PII-bearing
    /// columns. Ruling 3 token shapes satisfy Identity's unique indexes and the
    /// NormalizedX ASP.NET Identity convention (ToUpperInvariant of the raw value).
    /// </summary>
    private async Task AnonymiseAccountAsync(Guid userId, string token, CancellationToken ct)
    {
        var email = $"erased-{token}@erased.invalid";
        var userName = email;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Email, email)
                .SetProperty(u => u.NormalizedEmail, email.ToUpperInvariant())
                .SetProperty(u => u.UserName, userName)
                .SetProperty(u => u.NormalizedUserName, userName.ToUpperInvariant())
                .SetProperty(u => u.PhoneNumber, (string?)null)
                .SetProperty(u => u.ErasedAt, now), ct);
    }
}
