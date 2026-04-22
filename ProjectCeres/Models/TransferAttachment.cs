namespace ProjectCeres.Models;

public class TransferAttachment
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }

    public Transfer Transfer { get; set; } = null!;
}
