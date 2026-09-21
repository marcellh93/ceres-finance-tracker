using ProjectCeres.Common;

namespace ProjectCeres.Models;

public enum ExportJobStatus { Pending = 0, Processing = 1, Ready = 2, Failed = 3 }
public enum ExportFormat { Zip = 0 }

public sealed class ExportJob : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ExportJobStatus Status { get; set; } = ExportJobStatus.Pending;
    public ExportFormat Format { get; set; } = ExportFormat.Zip;
    public DateTime RequestedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public int FailureCount { get; set; }
    public string? StoredPath { get; set; }
    // HMAC-SHA256(serverSecret, rawToken); unique. Empty until the worker builds the ZIP.
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
    public DateTime? EmailedAt { get; set; }
}
