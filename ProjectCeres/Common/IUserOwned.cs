namespace ProjectCeres.Common;

/// <summary>
/// Implemented by every entity that belongs to exactly one user. System/lookup
/// entities (Currency, AccountType, etc.) and shared rows (system Categories with
/// IsSystem = true) do NOT implement this interface.
/// </summary>
public interface IUserOwned
{
    Guid UserId { get; }
}

/// <summary>
/// For entities where some rows are user-owned and others are shared (currently only
/// Category, where IsSystem = true rows are shared across all users). UserId is nullable
/// for these entities.
/// </summary>
public interface IOptionallyUserOwned
{
    Guid? UserId { get; }
}
