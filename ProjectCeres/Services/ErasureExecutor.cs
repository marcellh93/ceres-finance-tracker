using Microsoft.EntityFrameworkCore;
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
/// as ExportJobWorker/DataExportBuilder. Each lane is check-then-act so a mid-run
/// crash followed by a re-run (Task 10's ErasureWorker retry) completes cleanly
/// without double-processing or throwing on an already-empty lane.
/// </summary>
[RequiresAdminContext]
public class ErasureExecutor(
    AdminDbContext db,
    ErasurePseudonym pseudonym,
    IAuditLogWriter auditLog,
    IWebHostEnvironment env,
    TimeProvider timeProvider,
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

        var token = pseudonym.Compute(userId);

        var lanes = ErasureLanes.Classify(db.Model);

        await AnonymiseStatutoryAsync(userId, token, ct);
        await RedactSupportAsync(userId, ct);
        await PurgeAsync(userId, lanes.Purge, ct);
        await DeleteExportJobAsync(userId, ct);
        await AnonymiseAccountAsync(userId, token, ct);

        await auditLog.RecordAsync(
            userId: userId,
            action: AuditLogAction.GdprErasureCompleted,
            entityType: token,
            entityId: null,
            ct: ct);

        // Stage 6.14 GDPR-on-erasure: historical audit rows keep UserId as a pseudonym
        // but must not retain a real IP. Runs after the completion write above — that
        // row's IpAddress is "unknown" (no HttpContext in this background path), never
        // a real address, so rewriting before or after it makes no observable difference.
        await db.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IpAddress, "erased"), ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await db.ErasureRequests
            .IgnoreQueryFilters()
            .Where(r => r.Id == request.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ErasureStatus.Completed)
                .SetProperty(r => r.CompletedAt, now), ct);
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
    /// Step 2 — redact-retain support (B3a). Bodies are redacted via
    /// <see cref="IdentifierRedactor"/>; attachment files are deleted from disk (no
    /// legal duty to keep them) and their rows redacted, not removed — the thread
    /// survives as de-identified knowledge.
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

        var attachments = await db.SupportTicketAttachments.IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);
        foreach (var attachment in attachments)
        {
            var fullPath = Path.Combine(_root, attachment.StoredPath);
            if (File.Exists(fullPath)) File.Delete(fullPath);

            attachment.FileName = "[removed]";
            // StoredPath is non-nullable string; the path is now dead, so empty it —
            // GetAsync/DeleteAsync both go through FileAttachmentService, which is
            // RLS-scoped and can no longer reach an erased user's rows anyway.
            attachment.StoredPath = "";
        }
        if (attachments.Count > 0) await db.SaveChangesAsync(ct);
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

    /// <summary>Step 4 — delete any outstanding ExportJob ZIP + row. Naturally
    /// idempotent: File.Exists guards the delete, and an absent row is a no-op.</summary>
    private async Task DeleteExportJobAsync(Guid userId, CancellationToken ct)
    {
        var jobs = await db.ExportJobs.IgnoreQueryFilters()
            .Where(j => j.UserId == userId)
            .ToListAsync(ct);
        foreach (var job in jobs)
        {
            if (!string.IsNullOrEmpty(job.StoredPath))
            {
                var fullPath = Path.Combine(_root, job.StoredPath);
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
        }

        await db.ExportJobs.IgnoreQueryFilters()
            .Where(j => j.UserId == userId)
            .ExecuteDeleteAsync(ct);
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
