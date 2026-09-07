using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels.Sessions;

namespace ProjectCeres.Services;

public class SessionService(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ISessionService
{
    public async Task<IReadOnlyList<SessionDto>> GetActiveAsync(Guid currentSessionId)
    {
        var userId = currentUser.UserId;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ephemeralCutoff = now - SessionConstants.EphemeralSlidingWindow;
        var persistentCutoff = now - SessionConstants.PersistentLifetime;

        return await db.UserSessions
            .Where(s => s.UserId == userId
                && s.RevokedAt == null
                && (s.IsPersistent
                    ? s.LastUsedAt > persistentCutoff
                    : s.LastUsedAt > ephemeralCutoff))
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionDto(
                s.Id,
                s.CreatedAt,
                s.LastUsedAt,
                s.IpCreatedAt,
                s.UserAgent,
                s.Id == currentSessionId,
                s.IsIpAnchored))
            .ToArrayAsync();
    }

    public async Task<Result> TryRevokeAsync(Guid sessionId)
    {
        var userId = currentUser.UserId;

        var session = await db.UserSessions
            .Where(s => s.UserId == userId && s.Id == sessionId)
            .SingleOrDefaultAsync();

        if (session is null) return Result.Fail("NOT_FOUND", "Session not found.");

        session.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result> TrySetIpAnchorAsync(Guid sessionId, bool anchored)
    {
        var userId = currentUser.UserId;

        // Scoped to the caller, like TryRevokeAsync: another user's (or unknown) session
        // is NOT_FOUND, never a leak. A revoked session cannot be anchored — the toggle
        // only appears on live rows, and re-anchoring a dead session is meaningless.
        var session = await db.UserSessions
            .Where(s => s.UserId == userId && s.Id == sessionId && s.RevokedAt == null)
            .SingleOrDefaultAsync();

        if (session is null) return Result.Fail("NOT_FOUND", "Session not found.");

        session.IsIpAnchored = anchored;
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result> TryBlockIpAsync(string? ipAddress, string? callerIpAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return Result.Fail("VALIDATION_ERROR", "IpAddress is required.");
        }

        // Self-lockout guard. UserBlockedIpMiddleware 403s every authenticated
        // request from a blocked IP — including the login that would undo it —
        // and there is no unblock endpoint, so this is unrecoverable in-app.
        if (!string.IsNullOrWhiteSpace(callerIpAddress)
            && string.Equals(ipAddress, callerIpAddress, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail(
                "SELF_LOCKOUT",
                "You cannot block the address you are currently connected from — "
                + "it would lock you out of your own account. Revoke the individual "
                + "sessions instead.");
        }

        var userId = currentUser.UserId;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // One transaction: ExecuteUpdateAsync issues its own statement immediately,
        // so without this the bulk-revoke can commit while the insert fails.
        await using var tx = await db.Database.BeginTransactionAsync();

        var alreadyBlocked = await db.UserBlockedIps
            .AnyAsync(b => b.UserId == userId && b.IpAddress == ipAddress);

        if (!alreadyBlocked)
        {
            db.UserBlockedIps.Add(new UserBlockedIp
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                IpAddress = ipAddress,
                BlockedAt = now,
            });

            try
            {
                await db.SaveChangesAsync();
            }
            catch (UniqueConstraintViolationException)
            {
                // Concurrent block of the same IP — the row exists, which is the
                // desired end state. Same pattern as SettingsService.
                foreach (var entry in db.ChangeTracker.Entries<UserBlockedIp>().ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        await db.UserSessions
            .Where(s => s.UserId == userId && s.IpCreatedAt == ipAddress && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));

        await tx.CommitAsync();
        return Result.Ok();
    }

    public async Task<IReadOnlyList<BlockedIpDto>> GetBlockedIpsAsync()
    {
        var userId = currentUser.UserId;
        return await db.UserBlockedIps
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.BlockedAt)
            .Select(b => new BlockedIpDto(b.IpAddress, b.BlockedAt))
            .ToArrayAsync();
    }

    public async Task<Result> TryUnblockIpAsync(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return Result.Fail("VALIDATION_ERROR", "IpAddress is required.");
        }

        var userId = currentUser.UserId;

        // Scoped to the caller: an unknown IP or another user's block matches nothing
        // and returns NOT_FOUND (the same IDOR-safe shape as TryRevokeAsync). We do not
        // touch UserSessions — unblocking restores future access; it does not resurrect
        // the sessions the block revoked (those stay revoked, as a revoke should).
        var deleted = await db.UserBlockedIps
            .Where(b => b.UserId == userId && b.IpAddress == ipAddress)
            .ExecuteDeleteAsync();

        return deleted > 0
            ? Result.Ok()
            : Result.Fail("NOT_FOUND", "No block found for that address.");
    }
}
