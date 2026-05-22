using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// One row per active or recently-consumed email-confirmation token (Stage 9.3).
/// TokenHash Argon2id-hashed; TokenLookup HMAC-SHA256 indexed for O(1) lookup.
/// Single-use: ConsumedAt set in the same transaction as EmailConfirmed = true.
public sealed class EmailConfirmationToken : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
