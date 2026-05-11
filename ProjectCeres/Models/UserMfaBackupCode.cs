using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// One row per MFA backup code. Codes are Argon2id-hashed (PHC string in
/// CodeHash). Single-use: UsedAt is set on first successful verify.
/// Regeneration deletes all existing rows for the user.
/// </summary>
public sealed class UserMfaBackupCode : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? UsedFromIp { get; set; }
}
