using ProjectCeres.Common;

namespace ProjectCeres.Models;

public sealed class LockoutUnlockToken : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
