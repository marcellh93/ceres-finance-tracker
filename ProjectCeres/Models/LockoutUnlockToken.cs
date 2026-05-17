using ProjectCeres.Common;

namespace ProjectCeres.Models;

public sealed class LockoutUnlockToken : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    // HMAC-SHA256(serverSecret, rawToken). Unique index ensures /lockout-unlock
    // locates the matching row in O(1) regardless of how many candidates exist
    // (Stage 9.1.5.a — extends the Stage 6.15 pattern to the third token sibling).
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
