using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ProjectCeres.Common;

/// <summary>
/// Defense-in-depth: stamps UserId on every IUserOwned entity at SaveChanges time
/// when the value is still default(Guid). Service code is the primary line of
/// defense (call sites should be explicit about ownership), but this interceptor
/// catches missed stamps before they reach the database.
///
/// Rules:
/// - IUserOwned (non-nullable UserId): default(Guid) → set to current user's id.
/// - IOptionallyUserOwned (nullable UserId): null → leave alone (system rows). Set,
///   non-empty values are also left alone.
/// - Modified entities: never overwrite an existing UserId — that would let a write
///   silently change ownership.
/// </summary>
public sealed class UserOwnershipInterceptor(ICurrentUserAccessor user) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContextEventData eventData)
    {
        if (eventData.Context is null) return;
        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added) continue;
            if (entry.Entity is IUserOwned owned && owned.UserId == default)
            {
                entry.Property(nameof(IUserOwned.UserId)).CurrentValue = user.UserId;
            }
            // IOptionallyUserOwned (Category): leave null intact; only stamp if value
            // is default(Guid) (which is a meaningful zero-Guid, not "unset"). System
            // rows pass UserId = null and stay null.
        }
    }
}
