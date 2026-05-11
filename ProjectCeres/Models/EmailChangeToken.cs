namespace ProjectCeres.Models;

public enum EmailChangeTokenPurpose
{
    VerifyNew = 1,
    RevokeOld = 2,
}

/// <summary>
/// One row per pending or recently-consumed email-change token. A single
/// /email-change/request issues two rows sharing the same UserId and NewEmail —
/// one Purpose=VerifyNew (30-min, clicked by the new-address owner) and one
/// Purpose=RevokeOld (7-day, clicked by the old-address owner). Confirm or
/// revoke consumes both siblings atomically.
///
/// TokenHash is Argon2id-hashed (per-row salt). ConsumedAt is set synchronously
/// inside the same DB transaction as the Identity update or revoke.
/// </summary>
public sealed class EmailChangeToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public EmailChangeTokenPurpose Purpose { get; set; }
    public string NewEmail { get; set; } = "";
    // HMAC-SHA256(serverSecret, rawToken). Unique index ensures /confirm and /revoke
    // locate the matching row in O(1) regardless of how many candidates exist
    // (Stage 6.15 — closes the Argon2id-amplification DoS on the verify path).
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
