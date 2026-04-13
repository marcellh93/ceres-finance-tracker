namespace ProjectCeres.Models;

public class TransactionAttachment
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }

    public Transaction Transaction { get; set; } = null!;
}
