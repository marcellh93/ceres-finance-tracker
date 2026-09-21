using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Profile;

/// <summary>
/// DataExportBuilder against the real project_ceres_test database, reading through
/// AdminDbContext (BYPASSRLS) the same way the cron worker will — a fresh user id
/// seeded directly via the admin connection (not the fixture's rolled-back
/// transaction, which a separate connection cannot see).
/// </summary>
[Collection("TestDbFixtureTests")]
public class DataExportBuilderTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private readonly Guid _userId = Guid.NewGuid();
    private string _contentRoot = null!;
    private string _outputDir = null!;
    private DataExportBuilder _builder = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        _contentRoot = Path.Combine(Path.GetTempPath(), $"ceres-export-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        _outputDir = Path.Combine(Path.GetTempPath(), $"ceres-export-out-{Guid.NewGuid():N}");

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_contentRoot);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Users.Add(new ApplicationUser
            {
                Id = _userId,
                UserName = $"export-{_userId:N}@example.com",
                NormalizedUserName = $"EXPORT-{_userId:N}@EXAMPLE.COM",
                Email = $"export-{_userId:N}@example.com",
                NormalizedEmail = $"EXPORT-{_userId:N}@EXAMPLE.COM",
            });

            admin.Settings.Add(new Settings
            {
                UserId = _userId,
                NumberFormat = "en-US",
                DateFormat = "yyyy-MM-dd",
                DefaultCurrencyId = 1,
                PeriodStartDay = 1,
                Language = "en",
            });

            var account = new Account
            {
                Id = Guid.NewGuid(),
                Name = "Export Test Account",
                AccountTypeId = 1,
                CurrencyId = 1,
                IsActive = true,
                UserId = _userId,
            };
            admin.Accounts.Add(account);

            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = "Export Test Category",
                CategoryTypeId = 1,
                IsActive = true,
                UserId = _userId,
            };
            admin.Categories.Add(category);

            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                Date = DateOnly.FromDateTime(DateTime.Today),
                Amount = 42.50m,
                Description = "=CMD()",
                AccountId = account.Id,
                CategoryId = category.Id,
                CreatedAt = DateTime.UtcNow,
                UserId = _userId,
            };
            admin.Transactions.Add(transaction);

            // Attachment file on disk under _contentRoot, mirroring FileAttachmentService's layout.
            var relativePath = Path.Combine("uploads", transaction.Id.ToString(), "receipt.jpg");
            var fullPath = Path.Combine(_contentRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var fileBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0xFF, 0xD9 };
            await File.WriteAllBytesAsync(fullPath, fileBytes);

            admin.TransactionAttachments.Add(new TransactionAttachment
            {
                Id = Guid.NewGuid(),
                TransactionId = transaction.Id,
                UserId = _userId,
                FileName = "receipt.jpg",
                StoredPath = relativePath,
                ContentType = "image/jpeg",
                FileSizeBytes = fileBytes.Length,
                UploadedAt = DateTime.UtcNow,
            });

            await admin.SaveChangesAsync();
        }

        var adminForBuilder = _fixture.CreateAdminContext();
        _builder = new DataExportBuilder(adminForBuilder, env.Object);
    }

    public async Task DisposeAsync()
    {
        await using (var admin = _fixture.CreateAdminContext())
        {
            await admin.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
            await admin.TransactionAttachments.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
            await admin.Transactions.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
            await admin.Categories.IgnoreQueryFilters().Where(c => c.UserId == _userId).ExecuteDeleteAsync();
            await admin.Accounts.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
            await admin.Settings.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
            await admin.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
        }

        await _fixture.DisposeAsync();

        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true);
    }

    [Fact]
    public async Task BuildAsync_zip_contains_a_csv_per_user_content_entity_plus_profile_and_manifest()
    {
        var zipPath = await _builder.BuildAsync(_userId, _outputDir, CancellationToken.None);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);

        names.Should().Contain("Accounts.csv");
        names.Should().Contain("Transactions.csv");
        names.Should().Contain("profile.csv");
        names.Should().Contain("manifest.txt");
    }

    [Fact]
    public async Task BuildAsync_does_not_export_security_log_tables()
    {
        // Seed a REAL security-log row for this user with a distinctive marker, so the
        // test proves the export LAYER never surfaces seeded security data — not merely
        // that the table-name filter excludes the file (that's the unit test's job).
        const string sentinelIp = "203.0.113.199-audit-sentinel";
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                Action = default,
                OccurredAt = DateTime.UtcNow,
                IpAddress = sentinelIp,
            });
            await admin.SaveChangesAsync();
        }

        var zipPath = await _builder.BuildAsync(_userId, _outputDir, CancellationToken.None);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        names.Should().NotContain(n => n.Contains("AuditLog", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(n => n.Contains("FailedLoginAttempt", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(n => n.Contains("UserSession", StringComparison.OrdinalIgnoreCase));

        // The seeded audit row's marker must appear NOWHERE in the archive bytes.
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var content = await reader.ReadToEndAsync();
            content.Should().NotContain(sentinelIp,
                $"the export layer must not leak seeded security-log data (entry {entry.FullName})");
        }
    }

    [Fact]
    public async Task BuildAsync_escapes_a_formula_injection_description_in_transactions_csv()
    {
        var zipPath = await _builder.BuildAsync(_userId, _outputDir, CancellationToken.None);

        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("Transactions.csv")!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        content.Should().NotContain(",=CMD()", "a raw leading '=' would let a spreadsheet execute it as a formula");
        content.Should().Contain("'=CMD()");
    }

    [Fact]
    public async Task BuildAsync_each_csv_begins_with_the_utf8_bom()
    {
        var zipPath = await _builder.BuildAsync(_userId, _outputDir, CancellationToken.None);

        using var zip = ZipFile.OpenRead(zipPath);
        var csvEntries = zip.Entries.Where(e => e.FullName.EndsWith(".csv", StringComparison.Ordinal));

        csvEntries.Should().NotBeEmpty();
        foreach (var entry in csvEntries)
        {
            using var stream = entry.Open();
            var buffer = new byte[3];
            var read = await stream.ReadAsync(buffer.AsMemory(0, 3));
            read.Should().Be(3, $"{entry.FullName} should have at least 3 bytes for the BOM");
            buffer.Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF }, $"{entry.FullName} must begin with the UTF-8 BOM");
        }
    }

    [Fact]
    public async Task BuildAsync_copies_transaction_attachment_bytes_into_attachments_folder()
    {
        var zipPath = await _builder.BuildAsync(_userId, _outputDir, CancellationToken.None);

        using var zip = ZipFile.OpenRead(zipPath);
        var attachmentEntry = zip.Entries.SingleOrDefault(e => e.FullName.StartsWith("attachments/", StringComparison.Ordinal));

        attachmentEntry.Should().NotBeNull("the seeded TransactionAttachment's file should be copied into the ZIP");
        using var stream = attachmentEntry!.Open();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        bytes.Should().StartWith(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "the copied file should be the seeded JPEG bytes");
    }
}
