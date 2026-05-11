using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// One row per active or recently-consumed password reset token.
/// TokenHash is Argon2id-hashed (per-row salt). Single-use: ConsumedAt is
/// set synchronously inside the same DB transaction as the password write.
/// MfaVerifiedAt is set after the TOTP step succeeds for MFA-enabled users.
/// TokenLookup is HMAC-SHA256(serverSecret, rawToken) and carries a unique
/// index so /confirm finds the row in O(1) instead of running Argon2id over
/// every candidate (Stage 6.15).
/// </summary>
public sealed class PasswordResetToken : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? MfaVerifiedAt { get; set; }
}
