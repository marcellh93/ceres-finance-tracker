using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// A file attached to a support ticket — typically a screenshot of whatever broke.
/// Mirrors <see cref="TransactionAttachment"/>: stored on the filesystem, never as a
/// BLOB, with the original <see cref="FileName"/> kept separate from the
/// system-generated <see cref="StoredPath"/> so no user-supplied string ever reaches
/// the filesystem.
/// </summary>
public class SupportTicketAttachment : IUserOwned
{
    public Guid Id { get; set; }
    public Guid SupportMessageId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Original name, for display only. Never used to build a path.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>System-generated relative path. Extension comes from the DETECTED mime, never the upload.</summary>
    public string StoredPath { get; set; } = string.Empty;

    /// <summary>Mime type as detected by magic-byte inspection, not as claimed by the client.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }

    public SupportMessage SupportMessage { get; set; } = null!;
}
