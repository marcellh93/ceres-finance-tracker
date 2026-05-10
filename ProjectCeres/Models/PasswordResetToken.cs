namespace ProjectCeres.Models;

/// <summary>
/// One row per active or recently-consumed password reset token.
/// TokenHash is Argon2id-hashed (per-row salt). Single-use: ConsumedAt is
/// set synchronously inside the same DB transaction as the password write.
/// MfaVerifiedAt is set after the TOTP step succeeds for MFA-enabled users.
/// </summary>
public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? MfaVerifiedAt { get; set; }
}
