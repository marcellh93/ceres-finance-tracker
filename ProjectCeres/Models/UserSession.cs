using ProjectCeres.Common;

namespace ProjectCeres.Models;

public sealed class UserSession : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? PersistentTokenHash { get; set; }
    public string IpCreatedAt { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsPersistent { get; set; }
    public bool UsedBackupCodeAtLogin { get; set; }

    /// <summary>
    /// When true, this session is rejected on any request whose source IP differs from
    /// <see cref="IpCreatedAt"/> (exact match). Opt-in per session; defends a stolen
    /// session cookie replayed from another network. On mismatch the validator signs the
    /// user out, so recovery is a fresh login.
    /// </summary>
    public bool IsIpAnchored { get; set; }
}
