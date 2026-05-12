namespace ProjectCeres.Common;

/// <summary>
/// Implemented by every entity that belongs to exactly one user. System/lookup
/// entities (Currency, AccountType, etc.) do NOT implement this interface.
/// </summary>
public interface IUserOwned
{
    Guid UserId { get; }
}
