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
    private const int  MaxFilesPerTransaction = 10;

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
        if (file.Length == 0)
            throw new InvalidOperationException("Uploaded file is empty.");
        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException("File exceeds the 10 MB limit.");

        // Ownership gate: parent transaction must belong to the current user.
        var parentExists = await db.Transactions.Owned(user).AnyAsync(t => t.Id == transactionId);
        if (!parentExists)
            throw new InvalidOperationException($"Transaction {transactionId} not found.");

        var existingCount = await db.TransactionAttachments
            .CountAsync(a => a.TransactionId == transactionId);
        if (existingCount >= MaxFilesPerTransaction)
            throw new InvalidOperationException($"A transaction may not have more than {MaxFilesPerTransaction} attachments.");

        // Read file bytes for magic-byte inspection.
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var detectedMime = DetectMime(bytes);
        if (!AllowedMimeTypes.TryGetValue(detectedMime, out var extension))
            throw new InvalidOperationException($"File type not allowed. Accepted types: JPEG, PNG, GIF, WebP, PDF.");

        // Build a safe stored path — no user-supplied values touch the filesystem.
        var relativePath = Path.Combine("uploads", transactionId.ToString(), $"{Guid.NewGuid()}{extension}");
        var fullPath     = Path.Combine(_root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes);

        var attachment = new TransactionAttachment
        {
            Id            = Guid.NewGuid(),
            TransactionId = transactionId,
            FileName      = file.FileName,   // stored for display only
            StoredPath    = relativePath,
            ContentType   = detectedMime,
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

        var fullPath = Path.Combine(_root, attachment.StoredPath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException("Attachment file not found on disk.");

        var bytes = await File.ReadAllBytesAsync(fullPath);

        // Re-verify MIME at serve time.
        var detectedMime = DetectMime(bytes);
        if (!AllowedMimeTypes.ContainsKey(detectedMime))
            throw new InvalidOperationException("Attachment failed MIME verification at serve time.");

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
        if (file.Length == 0)
            throw new InvalidOperationException("Uploaded file is empty.");
        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException("File exceeds the 10 MB limit.");

        var parentExists = await db.Transfers.Owned(user).AnyAsync(t => t.Id == transferId);
        if (!parentExists)
            throw new InvalidOperationException($"Transfer {transferId} not found.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var detectedMime = DetectMime(bytes);
        if (!AllowedMimeTypes.TryGetValue(detectedMime, out var extension))
            throw new InvalidOperationException($"File type not allowed. Accepted types: JPEG, PNG, GIF, WebP, PDF.");

        var relativePath = Path.Combine("uploads", "transfers", transferId.ToString(), $"{Guid.NewGuid()}{extension}");
        var fullPath     = Path.Combine(_root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes);

        var attachment = new TransferAttachment
        {
            Id            = Guid.NewGuid(),
            TransferId    = transferId,
            FileName      = file.FileName,
            StoredPath    = relativePath,
            ContentType   = detectedMime,
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

        var fullPath = Path.Combine(_root, attachment.StoredPath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException("Attachment file not found on disk.");

        var bytes = await File.ReadAllBytesAsync(fullPath);

        var detectedMime = DetectMime(bytes);
        if (!AllowedMimeTypes.ContainsKey(detectedMime))
            throw new InvalidOperationException("Attachment failed MIME verification at serve time.");

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

    private static string DetectMime(byte[] bytes)
    {
        var results = Inspector.Inspect(bytes);
        var top = results.ByMimeType().FirstOrDefault();
        return top?.MimeType ?? "application/octet-stream";
    }
}
