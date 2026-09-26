using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Right-to-erasure request/cancel per Stage 13.9 Task 5. RequestAsync runs as the
/// authenticated caller (RLS-scoped) — mirrors ExportJobService's dedupe pattern, plus
/// sealing the account. CancelAsync is a PRE-AUTH token confirm (the caller is sealed,
/// so cannot authenticate) — mirrors LockoutUnlockService.ConfirmAsync: admin-context
/// lookup by CancelTokenLookup, then Argon2id verify, then flip Status + un-seal.
///
/// SealedAt lives on ApplicationUser (AspNetUsers), which carries no RLS policy — both
/// RequestAsync (own row) and CancelAsync (admin, pre-auth) update it via plain EF,
/// same as the SessionRevocationValidator seal-check read.
/// </summary>
[PreAuthScope]
[RequiresAdminContext]
public class ErasureService(
    AppDbContext db,
    AdminDbContext admin,
    ICurrentUserAccessor user,
    TimeProvider timeProvider,
    ErasureTokenGenerator tokens,
    TokenLookupHasher lookupHasher,
    Argon2idPasswordHasher argon,
    IAuditLogWriter auditLog) : IErasureService
{
    public static readonly TimeSpan CancelWindow = TimeSpan.FromHours(72);

    public async Task<(ErasureRequest request, string rawCancelToken)> RequestAsync(CancellationToken ct)
    {
        var existing = await db.ErasureRequests
            .Owned(user)
            .Where(r => r.Status == ErasureStatus.Sealed)
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return (existing, "");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rawToken = tokens.Generate();

        var request = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            Status = ErasureStatus.Sealed,
            RequestedAt = now,
            ExecuteAfter = now + CancelWindow,
            CancelTokenLookup = lookupHasher.ComputeLookup(rawToken),
            CancelTokenHash = tokens.Hash(rawToken),
        };
        db.ErasureRequests.Add(request);
        await db.SaveChangesAsync(ct);

        // Seal via the ADMIN context: the runtime ceres_app role has NO grants on
        // AspNetUsers (Identity manages users through the admin/migrator role), so an
        // ExecuteUpdate through the app context cannot write SealedAt. CancelAsync
        // already un-seals via admin for the same reason.
        await admin.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == user.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SealedAt, now), ct);

        await auditLog.RecordAsync(user.UserId, AuditLogAction.GdprErasureRequested,
            entityType: nameof(ErasureRequest), entityId: request.Id, ct: ct);

        return (request, rawToken);
    }

    [RlsBypassJustified("CER-1301")]
    public async Task<ErasureCancelOutcome> CancelAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            // Constant-time: pad the empty-token path with one Argon2 verify so it costs
            // the same as a real match, matching LockoutUnlockService.ConfirmAsync.
            argon.RunDummyHash();
            return new ErasureCancelOutcome.NotFound();
        }

        var lookup = lookupHasher.ComputeLookup(rawToken);
        // Pre-auth lookup via AdminDbContext (BYPASSRLS) — the caller is sealed and
        // cannot authenticate, so userId is not yet known. Stage 10 allow-lists this file.
        var candidate = await admin.ErasureRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.CancelTokenLookup == lookup, ct);

        if (candidate is null || !tokens.Verify(rawToken, candidate.CancelTokenHash))
        {
            // Constant-time: even with no matching row, run one Argon2 verify so timing
            // doesn't reveal "no such token" vs "token found but wrong secret" — the
            // enumeration oracle LockoutUnlockService.ConfirmAsync guards against.
            if (candidate is null) argon.RunDummyHash();
            return new ErasureCancelOutcome.NotFound();
        }

        if (candidate.Status != ErasureStatus.Sealed)
        {
            return new ErasureCancelOutcome.Gone();
        }

        await using var rlsScope = await db.BeginPreAuthUserScopeAsync(candidate.UserId, ct);

        // Re-read inside the scope; another caller may have already cancelled/completed it.
        var current = await admin.ErasureRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == candidate.Id, ct);
        if (current is null)
        {
            return new ErasureCancelOutcome.NotFound();
        }
        if (current.Status != ErasureStatus.Sealed)
        {
            return new ErasureCancelOutcome.Gone();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        await db.ErasureRequests
            .IgnoreQueryFilters()
            .Where(r => r.Id == candidate.Id)
            .ExecuteUpdateExactlyAsync(s => s
                .SetProperty(r => r.Status, ErasureStatus.Cancelled)
                .SetProperty(r => r.CancelledAt, now), ct: ct);

        // Cross-tenant by design: pre-auth, admin context. AspNetUsers carries no RLS policy.
        await admin.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == candidate.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SealedAt, (DateTime?)null), ct);

        // Audit the undo, mirroring EmailChangeService.RevokeAsync — "who un-sealed this
        // account and when" is exactly the trail Stage 13's erasure audit exists to hold.
        await auditLog.RecordAsync(candidate.UserId, AuditLogAction.GdprErasureCancelled,
            entityType: nameof(ErasureRequest), entityId: candidate.Id, ct: ct);

        await rlsScope.CommitAsync(ct);

        return new ErasureCancelOutcome.Cancelled();
    }
}
