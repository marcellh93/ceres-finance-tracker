using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// One row per TOTP code accepted within the 2-minute replay window. Argon2id-hashed
/// (per-row salt). On every accept call we (a) verify the new code does not match any
/// existing row in the window, then (b) insert the new row + opportunistic-purge old
/// rows. Purge happens inline on every call instead of as a scheduled job.
/// </summary>
public sealed class TotpReplayEntry : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime AcceptedAt { get; set; }
}
