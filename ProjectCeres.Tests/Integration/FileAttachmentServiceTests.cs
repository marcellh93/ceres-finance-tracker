using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for FileAttachmentService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
/// File I/O uses a per-test temp directory that is cleaned up on dispose.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary (Income, non-system)
/// </summary>
[Collection("TestDbFixtureTests")]
public class FileAttachmentServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId = new("20000000-0000-0000-0000-000000000002");

    private readonly TestDbFixture _fixture = new();
    private FileAttachmentService _service = null!;
    private AccountService _accountService = null!;
    private string _tempRoot = null!;
    private Guid _transactionId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);

        _tempRoot = Path.Combine(Path.GetTempPath(), $"ceres-attach-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_tempRoot);

        _service = new FileAttachmentService(_fixture.Db, env.Object, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);

        // Create a real transaction to attach files to.
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Attach Test Account {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        _fixture.Db.Accounts.Add(account);

        var transaction = new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            AccountId  = account.Id,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        };
        _fixture.Db.Transactions.Add(transaction);
        await _fixture.Db.SaveChangesAsync();
        _transactionId = transaction.Id;
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Returns a minimal valid 1×1 JPEG (magic bytes + minimal structure).
    private static byte[] MinimalJpegBytes()
    {
        // SOI + APP0 marker + EOF marker — enough for magic-byte detection.
        return
        [
            0xFF, 0xD8, 0xFF, 0xE0,  // SOI + APP0 marker
            0x00, 0x10,              // APP0 length (16 bytes)
            0x4A, 0x46, 0x49, 0x46, 0x00, // "JFIF\0"
            0x01, 0x01,              // version 1.1
            0x00,                    // aspect ratio units
            0x00, 0x01, 0x00, 0x01, // X/Y density
            0x00, 0x00,              // thumbnail dimensions
            0xFF, 0xD9               // EOI
        ];
    }

    private static byte[] PdfMagicBytes() =>
        [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A]; // "%PDF-1.4\n"

    private static IFormFile MakeFormFile(byte[] bytes, string fileName, string contentType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType
        };

    // -------------------------------------------------------------------------
    // UploadAsync — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UploadAsync_PersistsAttachmentRecord_AndWritesFileToDisk()
    {
        var bytes = MinimalJpegBytes();
        var file  = MakeFormFile(bytes, "receipt.jpg", "image/jpeg");

        var attachment = await _service.UploadAsync(_transactionId, file);

        // DB record
        var reloaded = await _fixture.Db.TransactionAttachments.FindAsync(attachment.Id);
        reloaded.Should().NotBeNull();
        reloaded!.TransactionId.Should().Be(_transactionId);
        reloaded.FileName.Should().Be("receipt.jpg");
        reloaded.ContentType.Should().Be("image/jpeg");
        reloaded.FileSizeBytes.Should().Be(bytes.Length);

        // File on disk
        var fullPath = Path.Combine(_tempRoot, reloaded.StoredPath);
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_AcceptsPdfFiles()
    {
        var bytes = PdfMagicBytes();
        var file  = MakeFormFile(bytes, "invoice.pdf", "application/pdf");

        var act = async () => await _service.UploadAsync(_transactionId, file);

        // PDFs are in the allowed list — should not throw.
        await act.Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // UploadAsync — validation guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UploadAsync_ThrowsWhenFileIsEmpty()
    {
        var file = MakeFormFile([], "empty.jpg", "image/jpeg");

        var act = async () => await _service.UploadAsync(_transactionId, file);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task UploadAsync_ThrowsWhenFileSizeExceedsLimit()
    {
        // 11 MB — over the 10 MB cap.
        var bytes = new byte[11 * 1024 * 1024];
        // Write JPEG magic bytes so the size check runs before MIME detection.
        MinimalJpegBytes().CopyTo(bytes, 0);
        var file = MakeFormFile(bytes, "huge.jpg", "image/jpeg");

        var act = async () => await _service.UploadAsync(_transactionId, file);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*10 MB*");
    }

    [Fact]
    public async Task UploadAsync_ThrowsWhenFileTypeIsNotAllowed()
    {
        // ZIP magic bytes — not in the allowed list.
        byte[] zipBytes = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
        var file = MakeFormFile(zipBytes, "archive.zip", "application/zip");

        var act = async () => await _service.UploadAsync(_transactionId, file);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not allowed*");
    }

    [Fact]
    public async Task UploadAsync_ThrowsWhenTransactionAlreadyHasTenAttachments()
    {
        // Insert 10 attachment records directly — bypassing disk I/O.
        for (var i = 0; i < 10; i++)
        {
            _fixture.Db.TransactionAttachments.Add(new TransactionAttachment
            {
                Id            = Guid.NewGuid(),
                TransactionId = _transactionId,
                FileName      = $"file{i}.jpg",
                StoredPath    = $"uploads/{_transactionId}/{Guid.NewGuid()}.jpg",
                ContentType   = "image/jpeg",
                FileSizeBytes = 1024,
                UploadedAt    = DateTime.UtcNow
            });
        }
        await _fixture.Db.SaveChangesAsync();

        var file = MakeFormFile(MinimalJpegBytes(), "eleventh.jpg", "image/jpeg");

        var act = async () => await _service.UploadAsync(_transactionId, file);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*10*");
    }

    // -------------------------------------------------------------------------
    // GetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ReturnsFileDataAndMetadata()
    {
        var bytes      = MinimalJpegBytes();
        var file       = MakeFormFile(bytes, "receipt.jpg", "image/jpeg");
        var attachment = await _service.UploadAsync(_transactionId, file);

        var (data, contentType, fileName) = await _service.GetAsync(attachment.Id);

        data.Should().BeEquivalentTo(bytes);
        contentType.Should().Be("image/jpeg");
        fileName.Should().Be("receipt.jpg");
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenAttachmentNotFound()
    {
        var act = async () => await _service.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenFileIsMissingFromDisk()
    {
        var bytes      = MinimalJpegBytes();
        var file       = MakeFormFile(bytes, "receipt.jpg", "image/jpeg");
        var attachment = await _service.UploadAsync(_transactionId, file);

        // Delete the file from disk to simulate a missing file scenario.
        var fullPath = Path.Combine(_tempRoot, attachment.StoredPath);
        File.Delete(fullPath);

        var act = async () => await _service.GetAsync(attachment.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found on disk*");
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_RemovesDbRecord_AndDeletesFileFromDisk()
    {
        var bytes      = MinimalJpegBytes();
        var file       = MakeFormFile(bytes, "receipt.jpg", "image/jpeg");
        var attachment = await _service.UploadAsync(_transactionId, file);
        var fullPath   = Path.Combine(_tempRoot, attachment.StoredPath);

        await _service.DeleteAsync(attachment.Id);

        var reloaded = await _fixture.Db.TransactionAttachments.FindAsync(attachment.Id);
        reloaded.Should().BeNull();
        File.Exists(fullPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenAttachmentNotFound()
    {
        var act = async () => await _service.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_RemovesDbRecord_EvenWhenFileAlreadyMissingFromDisk()
    {
        var bytes      = MinimalJpegBytes();
        var file       = MakeFormFile(bytes, "receipt.jpg", "image/jpeg");
        var attachment = await _service.UploadAsync(_transactionId, file);

        // Pre-delete the file from disk.
        File.Delete(Path.Combine(_tempRoot, attachment.StoredPath));

        // DeleteAsync should still remove the DB record without throwing.
        await _service.DeleteAsync(attachment.Id);

        var reloaded = await _fixture.Db.TransactionAttachments.FindAsync(attachment.Id);
        reloaded.Should().BeNull();
    }
}
