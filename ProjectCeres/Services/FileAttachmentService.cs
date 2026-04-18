using MimeDetective;
using MimeDetective.Definitions;
using MimeDetective.Engine;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public class FileAttachmentService(AppDbContext db, IWebHostEnvironment env) : IFileAttachmentService
{
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
        var fullPath     = Path.Combine(env.ContentRootPath, relativePath);

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
            UploadedAt    = DateTime.UtcNow
        };

        db.TransactionAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return attachment;
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> GetAsync(Guid attachmentId)
    {
        var attachment = await db.TransactionAttachments.FindAsync(attachmentId)
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var fullPath = Path.Combine(env.ContentRootPath, attachment.StoredPath);
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
        var attachment = await db.TransactionAttachments.FindAsync(attachmentId)
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var fullPath = Path.Combine(env.ContentRootPath, attachment.StoredPath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        db.TransactionAttachments.Remove(attachment);
        await db.SaveChangesAsync();
    }

    private static string DetectMime(byte[] bytes)
    {
        var results = Inspector.Inspect(bytes);
        var top = results.ByMimeType().FirstOrDefault();
        return top?.MimeType ?? "application/octet-stream";
    }
}
