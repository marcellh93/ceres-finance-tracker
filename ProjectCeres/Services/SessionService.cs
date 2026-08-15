using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
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

        return await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionDto(
                s.Id,
                s.CreatedAt,
                s.LastUsedAt,
                s.IpCreatedAt,
                s.UserAgent,
                s.Id == currentSessionId))
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

    public async Task<Result> TryBlockIpAsync(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return Result.Fail("VALIDATION_ERROR", "IpAddress is required.");
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
}
