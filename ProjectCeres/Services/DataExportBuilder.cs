using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Helpers;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Assembles one user's GDPR data export: a CSV per <see cref="UserContentEntities"/>
/// table, a profile.csv, the user's attachment files, and a manifest — zipped to
/// <c>outputDir</c>. Runs in the cron worker (Stage 13.8 Task 6), which acts for a
/// specific user but has no HTTP principal, so it reads through
/// <see cref="AdminDbContext"/> (BYPASSRLS) filtered explicitly by
/// <c>IgnoreQueryFilters().Where(UserId == userId)</c> — the SweepSessions
/// cross-user pattern, not the RLS-scoped path Task 4's ExportJobService uses.
/// </summary>
public sealed class DataExportBuilder(AdminDbContext db, IWebHostEnvironment env)
{
    private readonly string _root = env.ContentRootPath;

    public async Task<string> BuildAsync(Guid userId, string outputDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);
        var zipPath = Path.Combine(outputDir, $"export-{userId:N}.zip");
        var manifestEntries = new List<string>();

        using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            foreach (var table in UserContentEntities.List(db.Model))
            {
                var fileName = $"{table.PostgresTableName}.csv";
                var bytes = await ExportTableCsvAsync(table.EntityType, userId, ct);
                await WriteZipEntryAsync(zip, fileName, bytes, ct);
                manifestEntries.Add(fileName);
            }

            var profileBytes = await BuildProfileCsvAsync(userId, ct);
            await WriteZipEntryAsync(zip, "profile.csv", profileBytes, ct);
            manifestEntries.Add("profile.csv");

            var attachmentEntries = await CopyAttachmentsAsync(zip, userId, ct);
            manifestEntries.AddRange(attachmentEntries);

            var manifestBytes = BuildManifest(manifestEntries);
            await WriteZipEntryAsync(zip, "manifest.txt", manifestBytes, ct);
        }

        return zipPath;
    }

    private static async Task WriteZipEntryAsync(ZipArchive zip, string entryName, byte[] bytes, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(bytes, ct);
    }

    /// <summary>
    /// Generic per-entity export: reads that user's rows cross-tenant, then renders
    /// every non-shadow CLR scalar property as a CSV column via reflection — the
    /// entity list is data-driven (Task 3), so the renderer must not hand-type columns.
    /// </summary>
    private async Task<byte[]> ExportTableCsvAsync(Type entityType, Guid userId, CancellationToken ct)
    {
        var method = typeof(DataExportBuilder)
            .GetMethod(nameof(QueryOwnedRowsAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entityType);
        dynamic task = method.Invoke(this, [userId, ct])!;
        var rows = (System.Collections.IEnumerable)await task;

        var efEntityType = db.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"No EF model entry for {entityType.Name}.");
        var columns = ScalarColumns(efEntityType);

        var lines = new List<string> { string.Join(",", columns.Select(c => c.Name)) };
        foreach (var row in rows)
        {
            var values = columns.Select(c => CsvFormattingHelper.Csv(FormatValue(c.Property.GetValue(row))));
            lines.Add(string.Join(",", values));
        }

        return CsvFormattingHelper.CsvBytes(lines);
    }

    /// <summary>
    /// Cross-tenant by design: the worker acts for one user with no HTTP principal,
    /// so it bypasses RLS and filters explicitly. Allow-listed in ArchitectureTests.
    /// </summary>
    private async Task<List<TEntity>> QueryOwnedRowsAsync<TEntity>(Guid userId, CancellationToken ct)
        where TEntity : class, IUserOwned
        => await db.Set<TEntity>()
            .IgnoreQueryFilters()
            .Where(e => e.UserId == userId)
            .ToListAsync(ct);

    private static List<(string Name, PropertyInfo Property)> ScalarColumns(IEntityType efEntityType) =>
        efEntityType.GetProperties()
            .Where(p => !p.IsShadowProperty())
            .Select(p => (p.Name, Property: p.PropertyInfo!))
            .Where(c => c.Property is not null)
            .ToList();

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private async Task<byte[]> BuildProfileCsvAsync(Guid userId, CancellationToken ct)
    {
        // Cross-tenant read of AspNetUsers — same justified pattern as
        // EmailRecipientResolver, here run for a background-job target user.
        var appUser = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.UserName, u.Email })
            .FirstOrDefaultAsync(ct);

        var settings = await db.Settings
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId)
            .FirstOrDefaultAsync(ct);

        var lines = new List<string> { "Field,Value" };
        lines.Add($"UserName,{CsvFormattingHelper.Csv(appUser?.UserName ?? "")}");
        lines.Add($"Email,{CsvFormattingHelper.Csv(appUser?.Email ?? "")}");
        lines.Add($"NumberFormat,{CsvFormattingHelper.Csv(settings?.NumberFormat ?? "")}");
        lines.Add($"DateFormat,{CsvFormattingHelper.Csv(settings?.DateFormat ?? "")}");
        lines.Add($"PeriodStartDay,{settings?.PeriodStartDay.ToString(CultureInfo.InvariantCulture) ?? ""}");
        lines.Add($"Language,{CsvFormattingHelper.Csv(settings?.Language ?? "")}");

        return CsvFormattingHelper.CsvBytes(lines);
    }

    /// <summary>
    /// Copies every attachment family's files under an <c>attachments/</c> ZIP folder,
    /// resolving <c>StoredPath</c> against the same <c>_root</c> FileAttachmentService
    /// writes to. Returns the manifest entry names.
    /// </summary>
    private async Task<List<string>> CopyAttachmentsAsync(ZipArchive zip, Guid userId, CancellationToken ct)
    {
        var entries = new List<string>();

        var transactionAttachments = await db.TransactionAttachments
            .IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.StoredPath, a.FileName })
            .ToListAsync(ct);
        foreach (var a in transactionAttachments)
            entries.Add(await CopyOneAttachmentAsync(zip, a.StoredPath, $"attachments/{a.Id}_{a.FileName}", ct));

        var transferAttachments = await db.TransferAttachments
            .IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.StoredPath, a.FileName })
            .ToListAsync(ct);
        foreach (var a in transferAttachments)
            entries.Add(await CopyOneAttachmentAsync(zip, a.StoredPath, $"attachments/{a.Id}_{a.FileName}", ct));

        var supportAttachments = await db.SupportTicketAttachments
            .IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.StoredPath, a.FileName })
            .ToListAsync(ct);
        foreach (var a in supportAttachments)
            entries.Add(await CopyOneAttachmentAsync(zip, a.StoredPath, $"attachments/{a.Id}_{a.FileName}", ct));

        return entries.Where(e => e.Length > 0).ToList();
    }

    private async Task<string> CopyOneAttachmentAsync(ZipArchive zip, string storedPath, string entryName, CancellationToken ct)
    {
        var fullPath = Path.Combine(_root, storedPath);
        if (!File.Exists(fullPath)) return "";

        var bytes = await File.ReadAllBytesAsync(fullPath, ct);
        await WriteZipEntryAsync(zip, entryName, bytes, ct);
        return entryName;
    }

    private static byte[] BuildManifest(List<string> entries)
    {
        var lines = new List<string> { $"Generated: {DateTimeOffset.UtcNow:O}", "" };
        lines.AddRange(entries);
        return System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));
    }
}
