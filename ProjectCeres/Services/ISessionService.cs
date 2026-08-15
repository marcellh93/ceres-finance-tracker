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

    /// <summary>Blocks an IP and revokes the caller's sessions created from it.
    /// Idempotent — re-blocking an already-blocked IP succeeds.</summary>
    Task<Result> TryBlockIpAsync(string? ipAddress);
}
