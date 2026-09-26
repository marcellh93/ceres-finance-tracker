using ProjectCeres.Common;

namespace ProjectCeres.Models;

public enum ErasureStatus { Sealed = 0, Cancelled = 1, Completed = 2 }

public sealed class ErasureRequest : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ErasureStatus Status { get; set; } = ErasureStatus.Sealed;
    public DateTime RequestedAt { get; set; }
    public DateTime ExecuteAfter { get; set; }   // RequestedAt + 72h
    // HMAC-SHA256(serverSecret, rawToken); unique. Lets the cancel link find the row.
    public byte[] CancelTokenLookup { get; set; } = Array.Empty<byte>();
    // Argon2id hash of the raw token, verified after CancelTokenLookup narrows to one
    // candidate — mirrors ExportJob.TokenHash and the 4 sibling token tables.
    public string CancelTokenHash { get; set; } = "";
    public DateTime? CancelledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
