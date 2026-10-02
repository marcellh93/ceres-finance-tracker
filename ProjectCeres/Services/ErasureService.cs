using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
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
    IAuditLogWriter auditLog,
    IEmailComposer composer,
    IEmailService email,
    IEmailRecipientResolver recipients,
    ILanguageResolver languages,
    IOptions<EmailOptions> emailOptions,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ErasureService> logger) : IErasureService
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

        await SendInitiatedEmailAsync(user.UserId, rawToken, ct);

        return (request, rawToken);
    }

    /// <summary>
    /// Fires once, at request time, with the cancel link — per design spec § 6 (not
    /// from the worker, which only executes 72h later). A failed send is logged but
    /// never rolls back the seal/request creation, mirroring ExportJobWorker.SendReadyEmailAsync.
    /// </summary>
    private async Task SendInitiatedEmailAsync(Guid userId, string rawToken, CancellationToken ct)
    {
        try
        {
            var cancelUrl = $"{CancelUrlBase().TrimEnd('/')}/erasure/cancel#token={rawToken}";

            var recipient = await recipients.ResolveAsync(userId, ct);
            var culture = await languages.ResolveForUserAsync(userId, ct);
            var msg = composer.Compose(EmailTemplateKey.GdprErasureInitiated, culture, cancelUrl)
                with { To = recipient };
            await email.SendAsync(msg, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send GdprErasureInitiated email for user {UserId}.", userId);
        }
    }

    // Configured origin wins; otherwise the incoming request's scheme+host, matching
    // SupportAdminApiController.SupportUrlBase() — RequestAsync always runs inside the
    // authenticated caller's own HTTP request (never a background job), so the request
    // Host is trustworthy here the same way it is there. Email:PublicBaseUrl is unset by
    // design in dev/test (Program.cs's Production-only startup check), so this fallback
    // is the normal path outside Production, not an edge case.
    private string CancelUrlBase()
    {
        var configured = emailOptions.Value.PublicBaseUrl;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var ctx = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("RequestAsync must run inside an HTTP request.");
        return $"{ctx.Request.Scheme}://{ctx.Request.Host}";
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
