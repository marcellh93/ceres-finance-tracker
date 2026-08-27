using MimeDetective;
using MimeDetective.Definitions;
using MimeDetective.Engine;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public class FileAttachmentService : IFileAttachmentService
{
    private readonly AppDbContext db;
    private readonly ICurrentUserAccessor user;
    private readonly string _root;
    private readonly TimeProvider _timeProvider;

    public FileAttachmentService(
        AppDbContext db,
        IWebHostEnvironment env,
        ICurrentUserAccessor user,
        TimeProvider timeProvider,
        IOptions<FileAttachmentOptions>? options = null)
    {
        this.db   = db;
        this.user = user;
        _root     = options?.Value.RootPath ?? env.ContentRootPath;
        _timeProvider = timeProvider;
    }

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int  MaxFilesPerParent = 10;

    // security-model.md § File Access Control: "enforce a maximum total file storage per
    // user (e.g. 500 MB for Phase 3 beta) ... Without a quota, a single user can exhaust
    // server disk space and take down the application for all users."
    private const long MaxTotalBytesPerUser = 500L * 1024 * 1024;

    // MIME types that are allowed. Extension is derived from this list — never from user input.
    private static readonly Dictionary<string, string> AllowedMimeTypes = new()
    {
        ["image/jpeg"]       = ".jpg",
        ["image/png"]        = ".png",
        ["image/gif"]        = ".gif",
        ["image/webp"]       = ".webp",
        ["application/pdf"]  = ".pdf"
    };

    // Build inspector scoped to images + PDF only — rejects everything else by design.
    private static readonly IContentInspector Inspector = new ContentInspectorBuilder
    {
        Definitions = DefaultDefinitions.FileTypes.Images.All()
            .AddRange(DefaultDefinitions.FileTypes.Documents.PDF())
    }.Build();

    public async Task ValidateAsync(IFormFile file)
    {
        if (file.Length == 0)
            throw new InvalidOperationException("Uploaded file is empty.");
        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException("File exceeds the 10 MB limit.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var detectedMime = DetectMime(ms.ToArray());
        if (!AllowedMimeTypes.ContainsKey(detectedMime))
            throw new InvalidOperationException($"File type not allowed. Accepted types: JPEG, PNG, GIF, WebP, PDF.");
    }

    public async Task<TransactionAttachment> UploadAsync(Guid transactionId, IFormFile file)
    {
        // Ownership gate: parent transaction must belong to the current user.
        var parentExists = await db.Transactions.Owned(user).AnyAsync(t => t.Id == transactionId);
        if (!parentExists)
            throw new InvalidOperationException($"Transaction {transactionId} not found.");

        var existingCount = await db.TransactionAttachments
            .CountAsync(a => a.TransactionId == transactionId);
        if (existingCount >= MaxFilesPerParent)
            throw new InvalidOperationException($"A transaction may not have more than {MaxFilesPerParent} attachments.");

        var (bytes, mime, relativePath) =
            await ValidateAndStoreAsync(file, "uploads", transactionId.ToString());

        var attachment = new TransactionAttachment
        {
            Id            = Guid.NewGuid(),
            TransactionId = transactionId,
            FileName      = file.FileName,   // stored for display only
            StoredPath    = relativePath,
            ContentType   = mime,
            FileSizeBytes = bytes.Length,
            UploadedAt    = _timeProvider.GetUtcNow().UtcDateTime
        };

        db.TransactionAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return attachment;
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> GetAsync(Guid attachmentId)
    {
        var attachment = await db.TransactionAttachments
            .Where(a => a.Id == attachmentId && a.Transaction.UserId == user.UserId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var bytes = await ReadVerifiedAsync(attachment.StoredPath);
        return (bytes, attachment.ContentType, attachment.FileName);
    }

    public async Task DeleteAsync(Guid attachmentId)
    {
        var attachment = await db.TransactionAttachments
            .Where(a => a.Id == attachmentId && a.Transaction.UserId == user.UserId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var fullPath = Path.Combine(_root, attachment.StoredPath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        db.TransactionAttachments.Remove(attachment);
        await db.SaveChangesAsync();
    }

    public async Task<TransferAttachment> UploadForTransferAsync(Guid transferId, IFormFile file)
    {
        var parentExists = await db.Transfers.Owned(user).AnyAsync(t => t.Id == transferId);
        if (!parentExists)
            throw new InvalidOperationException($"Transfer {transferId} not found.");

        var (bytes, mime, relativePath) =
            await ValidateAndStoreAsync(file, "uploads", "transfers", transferId.ToString());

        var attachment = new TransferAttachment
        {
            Id            = Guid.NewGuid(),
            TransferId    = transferId,
            FileName      = file.FileName,
            StoredPath    = relativePath,
            ContentType   = mime,
            FileSizeBytes = bytes.Length,
            UploadedAt    = _timeProvider.GetUtcNow().UtcDateTime
        };

        db.TransferAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return attachment;
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> GetTransferAttachmentAsync(Guid attachmentId)
    {
        var attachment = await db.TransferAttachments
            .Where(a => a.Id == attachmentId && a.Transfer.UserId == user.UserId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var bytes = await ReadVerifiedAsync(attachment.StoredPath);
        return (bytes, attachment.ContentType, attachment.FileName);
    }

    public async Task DeleteTransferAttachmentAsync(Guid attachmentId)
    {
        var attachment = await db.TransferAttachments
            .Where(a => a.Id == attachmentId && a.Transfer.UserId == user.UserId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var fullPath = Path.Combine(_root, attachment.StoredPath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        db.TransferAttachments.Remove(attachment);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Attaches a file to one of the current user's support tickets.
    ///
    /// Unlike the transaction and transfer paths, this one sets <c>UserId</c> on the row
    /// explicitly. SupportTicketAttachments carries a COMPOSITE foreign key
    /// (SupportTicketId, UserId) against the ticket's alternate key, added after a review
    /// found the single-column shape allowed a cross-tenant destructive write: Postgres
    /// runs FK checks and ON DELETE CASCADE through a referential-integrity trigger that
    /// RLS does not apply to. Leaving UserId unset does not silently mis-scope the row —
    /// the insert fails outright.
    /// </summary>
    public async Task<SupportTicketAttachment> UploadForSupportTicketAsync(Guid supportTicketId, IFormFile file)
    {
        var parentExists = await db.SupportTickets.Owned(user).AnyAsync(t => t.Id == supportTicketId);
        if (!parentExists)
            throw new InvalidOperationException($"Support ticket {supportTicketId} not found.");

        var existingCount = await db.SupportTicketAttachments
            .CountAsync(a => a.SupportTicketId == supportTicketId);
        if (existingCount >= MaxFilesPerParent)
            throw new InvalidOperationException($"A support ticket may not have more than {MaxFilesPerParent} attachments.");

        var (bytes, mime, relativePath) =
            await ValidateAndStoreAsync(file, "uploads", "support", supportTicketId.ToString());

        var attachment = new SupportTicketAttachment
        {
            Id              = Guid.NewGuid(),
            SupportTicketId = supportTicketId,
            UserId          = user.UserId,
            FileName        = file.FileName,
            StoredPath      = relativePath,
            ContentType     = mime,
            FileSizeBytes   = bytes.Length,
            UploadedAt      = _timeProvider.GetUtcNow().UtcDateTime
        };

        db.SupportTicketAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return attachment;
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> GetSupportTicketAttachmentAsync(Guid attachmentId)
    {
        var attachment = await db.SupportTicketAttachments
            .Where(a => a.Id == attachmentId && a.SupportTicket.UserId == user.UserId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var bytes = await ReadVerifiedAsync(attachment.StoredPath);
        return (bytes, attachment.ContentType, attachment.FileName);
    }

    /// <summary>
    /// Deletes the file THEN the row. A database cascade never runs application code, so
    /// deleting a ticket directly would strand its files on disk forever — every
    /// ticket-delete path must come through here (roadmap § 12.5, GDPR erasure Stage 13).
    /// </summary>
    public async Task DeleteSupportTicketAttachmentAsync(Guid attachmentId)
    {
        var attachment = await db.SupportTicketAttachments
            .Where(a => a.Id == attachmentId && a.SupportTicket.UserId == user.UserId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var fullPath = Path.Combine(_root, attachment.StoredPath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        db.SupportTicketAttachments.Remove(attachment);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Validate, magic-byte inspect, and write one upload to disk. Returns everything the
    /// caller needs to build its own attachment row.
    ///
    /// The three attachment families (transaction, transfer, support ticket) differ only in
    /// their parent check, their storage folder, and the row type. Everything security-
    /// relevant — the size limit, the magic-byte inspection, the extension coming from the
    /// DETECTED mime rather than the filename — is here, once, so a fix cannot land on one
    /// family and miss the others.
    /// </summary>
    private async Task<(byte[] Bytes, string Mime, string RelativePath)> ValidateAndStoreAsync(
        IFormFile file, params string[] folderSegments)
    {
        if (file.Length == 0)
            throw new InvalidOperationException("Uploaded file is empty.");
        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException("File exceeds the 10 MB limit.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var detectedMime = DetectMime(bytes);
        if (!AllowedMimeTypes.TryGetValue(detectedMime, out var extension))
            throw new InvalidOperationException($"File type not allowed. Accepted types: JPEG, PNG, GIF, WebP, PDF.");

        // Quota is checked HERE — after validation, before the write — so it covers all
        // three attachment families at once and cannot be forgotten by a fourth. The
        // per-parent cap above does not bound a user's total: 10 files x 10 MB is only a
        // per-ticket ceiling, and nothing limits how many parents they create.
        await EnsureWithinStorageQuotaAsync(bytes.LongLength);

        var relativePath = Path.Combine([.. folderSegments, $"{Guid.NewGuid()}{extension}"]);
        var fullPath = Path.Combine(_root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes);

        return (bytes, detectedMime, relativePath);
    }

    /// <summary>
    /// Refuses a write that would take the current user past their total storage
    /// allowance, counting every attachment family they own.
    /// </summary>
    private async Task EnsureWithinStorageQuotaAsync(long incomingBytes)
    {
        var used = await db.TransactionAttachments.Where(a => a.Transaction.UserId == user.UserId)
                       .SumAsync(a => (long?)a.FileSizeBytes) ?? 0L;
        used += await db.TransferAttachments.Where(a => a.Transfer.UserId == user.UserId)
                    .SumAsync(a => (long?)a.FileSizeBytes) ?? 0L;
        used += await db.SupportTicketAttachments.Where(a => a.UserId == user.UserId)
                    .SumAsync(a => (long?)a.FileSizeBytes) ?? 0L;

        if (used + incomingBytes > MaxTotalBytesPerUser)
        {
            var limitMb = MaxTotalBytesPerUser / (1024 * 1024);
            var usedMb = used / (1024 * 1024);
            throw new InvalidOperationException(
                $"Storage limit reached. Attachments may total {limitMb} MB per account; "
                + $"you are using {usedMb} MB. Delete an attachment to free space.");
        }
    }

    /// <summary>Reads an attachment off disk and re-verifies its magic bytes at serve time.</summary>
    private async Task<byte[]> ReadVerifiedAsync(string storedPath)
    {
        var fullPath = Path.Combine(_root, storedPath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException("Attachment file not found on disk.");

        var bytes = await File.ReadAllBytesAsync(fullPath);

        if (!AllowedMimeTypes.ContainsKey(DetectMime(bytes)))
            throw new InvalidOperationException("Attachment failed MIME verification at serve time.");

        return bytes;
    }

    private static string DetectMime(byte[] bytes)
    {
        var results = Inspector.Inspect(bytes);
        var top = results.ByMimeType().FirstOrDefault();
        return top?.MimeType ?? "application/octet-stream";
    }
}
