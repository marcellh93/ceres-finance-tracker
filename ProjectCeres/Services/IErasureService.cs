using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IErasureService
{
    /// <summary>
    /// Seals the caller's account (ApplicationUser.SealedAt) and creates a Sealed
    /// ErasureRequest, generating a raw cancel token (returned for the email link —
    /// only its lookup+hash are persisted). If the caller already has a Sealed
    /// request, returns it unchanged (dedupe) with an empty raw token — the original
    /// email already carries the live token; a dedupe-return cannot re-mint it.
    /// </summary>
    Task<(ErasureRequest request, string rawCancelToken)> RequestAsync(CancellationToken ct);

    /// <summary>
    /// Pre-auth token confirm (the caller is sealed, so cannot authenticate normally).
    /// Un-seals the account and marks the request Cancelled on a valid, still-Sealed match.
    /// </summary>
    Task<ErasureCancelOutcome> CancelAsync(string rawToken, CancellationToken ct);
}
