using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// D3's 30-day re-registration cooling-off, tracked separately from
/// ApplicationUser.Email because ErasureExecutor anonymises that column.
/// AuthController.Register checks the fingerprint against un-expired holds.
/// See docs/superpowers/specs/2026-09-25-stage-13-9-right-to-erasure-design.md § 8 step 5.
/// </summary>
public sealed class ErasedEmailHold : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    // HMAC-SHA256(serverSecret, normalized original email) via TokenLookupHasher.ComputeLookup.
    public byte[] EmailFingerprint { get; set; } = Array.Empty<byte>();
    public DateTime ErasedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
