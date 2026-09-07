using ProjectCeres.Common;
using ProjectCeres.ViewModels.Sessions;

namespace ProjectCeres.Services;

/// <summary>Active-session listing, revocation, and per-user IP blocking.</summary>
public interface ISessionService
{
    Task<IReadOnlyList<SessionDto>> GetActiveAsync(Guid currentSessionId);

    /// <summary>Revokes one of the caller's own sessions. Fails NOT_FOUND for an
    /// unknown id or another user's session (IDOR guard).</summary>
    Task<Result> TryRevokeAsync(Guid sessionId);

    /// <summary>
    /// Toggles IP-anchoring on one of the caller's own live sessions. An anchored session
    /// is rejected by <c>SessionRevocationValidator</c> on any request whose source IP
    /// differs from the session's origin IP (exact match) — defending a stolen cookie
    /// replayed from another network. Fails NOT_FOUND for an unknown id, another user's
    /// session, or an already-revoked one (IDOR-safe, scoped to the caller like every
    /// other method here).
    /// </summary>
    Task<Result> TrySetIpAnchorAsync(Guid sessionId, bool anchored);

    /// <summary>
    /// Blocks an IP and revokes the caller's sessions created from it. Idempotent —
    /// re-blocking an already-blocked IP succeeds. Refuses SELF_LOCKOUT when the
    /// target matches <paramref name="callerIpAddress"/>: UserBlockedIpMiddleware
    /// 403s every authenticated request from a blocked IP, so blocking your own IP
    /// would be unrecoverable but for <see cref="TryUnblockIpAsync"/>.
    /// </summary>
    Task<Result> TryBlockIpAsync(string? ipAddress, string? callerIpAddress);

    /// <summary>Lists the caller's currently-blocked IPs, newest block first.
    /// A block the user cannot see is a block they cannot reverse (Stage 12.5.4).</summary>
    Task<IReadOnlyList<BlockedIpDto>> GetBlockedIpsAsync();

    /// <summary>
    /// Removes the caller's block on <paramref name="ipAddress"/> so requests from it
    /// are no longer 403'd by UserBlockedIpMiddleware. Fails NOT_FOUND when the caller
    /// has no block on that address (an unknown IP or another user's block — IDOR-safe,
    /// scoped to the caller like every other method here). Idempotent from the user's
    /// point of view: the end state is "not blocked".
    /// </summary>
    Task<Result> TryUnblockIpAsync(string? ipAddress);
}
