using FluentAssertions;
using ProjectCeres.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for FileAttachmentService transfer methods against the real project_ceres_test database.
/// Each test rolls back via TestDbFixture — no test data persists between tests.
/// File I/O uses a per-test temp directory that is cleaned up on dispose.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
/// </summary>
[Collection("IntegrationTests")]
public class TransferAttachmentServiceTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private FileAttachmentService _service = null!;
    private string _tempRoot = null!;
    private Guid _transferId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        _tempRoot = Path.Combine(Path.GetTempPath(), $"ceres-transfer-attach-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_tempRoot);

        _service = new FileAttachmentService(_fixture.Db, env.Object, new SingleUserAccessor());

        // Two accounts needed for a valid transfer.
        var source = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Transfer Source {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        var dest = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Transfer Dest {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        _fixture.Db.Accounts.AddRange(source, dest);

        var transfer = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 50m,
            SourceAccountId = source.Id,
            DestAccountId   = dest.Id,
            CreatedAt       = DateTime.UtcNow
        };
        _fixture.Db.Transfers.Add(transfer);
        await _fixture.Db.SaveChangesAsync();
        _transferId = transfer.Id;
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

    private static byte[] MinimalJpegBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0,
        0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01,
        0x00,
        0x00, 0x01, 0x00, 0x01,
        0x00, 0x00,
        0xFF, 0xD9
    ];

    private static IFormFile MakeFormFile(byte[] bytes, string fileName, string contentType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType
        };

    // -------------------------------------------------------------------------
    // UploadForTransferAsync — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UploadForTransferAsync_PersistsAttachmentRecord_AndWritesFileToDisk()
    {
        var bytes = MinimalJpegBytes();
        var file  = MakeFormFile(bytes, "receipt.jpg", "image/jpeg");

        var attachment = await _service.UploadForTransferAsync(_transferId, file);

        var reloaded = await _fixture.Db.TransferAttachments.FindAsync(attachment.Id);
        reloaded.Should().NotBeNull();
        reloaded!.TransferId.Should().Be(_transferId);
        reloaded.FileName.Should().Be("receipt.jpg");
        reloaded.ContentType.Should().Be("image/jpeg");
        reloaded.FileSizeBytes.Should().Be(bytes.Length);

        var fullPath = Path.Combine(_tempRoot, reloaded.StoredPath);
        File.Exists(fullPath).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // GetTransferAttachmentAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTransferAttachmentAsync_ReturnsFileDataAndMetadata()
    {
        var bytes      = MinimalJpegBytes();
        var file       = MakeFormFile(bytes, "invoice.jpg", "image/jpeg");
        var attachment = await _service.UploadForTransferAsync(_transferId, file);

        var (data, contentType, fileName) = await _service.GetTransferAttachmentAsync(attachment.Id);

        data.Should().BeEquivalentTo(bytes);
        contentType.Should().Be("image/jpeg");
        fileName.Should().Be("invoice.jpg");
    }

    // -------------------------------------------------------------------------
    // DeleteTransferAttachmentAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteTransferAttachmentAsync_RemovesDbRecord_AndDeletesFileFromDisk()
    {
        var bytes      = MinimalJpegBytes();
        var file       = MakeFormFile(bytes, "receipt.jpg", "image/jpeg");
        var attachment = await _service.UploadForTransferAsync(_transferId, file);
        var fullPath   = Path.Combine(_tempRoot, attachment.StoredPath);

        await _service.DeleteTransferAttachmentAsync(attachment.Id);

        var reloaded = await _fixture.Db.TransferAttachments.FindAsync(attachment.Id);
        reloaded.Should().BeNull();
        File.Exists(fullPath).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // UploadForTransferAsync — spoofed file type rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UploadForTransferAsync_ThrowsWhenFileTypeIsNotAllowed()
    {
        // ZIP magic bytes disguised as a JPEG filename.
        byte[] zipBytes = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
        var file = MakeFormFile(zipBytes, "receipt.jpg", "image/jpeg");

        var act = async () => await _service.UploadForTransferAsync(_transferId, file);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not allowed*");
    }
}
