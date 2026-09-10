using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel4")]
public class AttachmentsApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");
    private readonly Guid _intruderUserId = Guid.NewGuid();

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _accountIds = [];
    private readonly List<Guid> _transactionIds = [];
    private readonly List<Guid> _transferIds = [];
    private readonly List<Guid> _attachmentIds = [];
    private readonly List<Guid> _transferAttachmentIds = [];

    public AttachmentsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_attachmentIds.Count > 0)         await db.TransactionAttachments.Where(a => _attachmentIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_transferAttachmentIds.Count > 0) await db.TransferAttachments.Where(a => _transferAttachmentIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_transactionIds.Count > 0)        await db.Transactions.Where(t => _transactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_transferIds.Count > 0)           await db.Transfers.Where(t => _transferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_accountIds.Count > 0)            await db.Accounts.Where(a => _accountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private static MultipartFormDataContent SmallPng()
    {
        byte[] png = [
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
            0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,0x89,
            0x00,0x00,0x00,0x0D,0x49,0x44,0x41,0x54,
            0x78,0x9C,0x62,0x00,0x01,0x00,0x00,0x05,
            0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,
            0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,0x60,0x82
        ];
        var content = new MultipartFormDataContent();
        var bc = new ByteArrayContent(png);
        bc.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(bc, "file", "tiny.png");
        return content;
    }

    private async Task<Guid> SeedSentinelTransactionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Att-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 16), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        db.Accounts.Add(account); db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        _accountIds.Add(account.Id); _transactionIds.Add(tx.Id);
        return tx.Id;
    }

    private async Task<(Guid TransferId, Guid SourceId, Guid DestId)> SeedSentinelTransferAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var src  = new Account { Id = Guid.NewGuid(), Name = $"Att-Src-{Guid.NewGuid():N}",  AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var dst  = new Account { Id = Guid.NewGuid(), Name = $"Att-Dst-{Guid.NewGuid():N}",  AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var trf  = new Transfer { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 16), Amount = 50m, SourceAccountId = src.Id, DestAccountId = dst.Id };
        db.Accounts.Add(src); db.Accounts.Add(dst); db.Transfers.Add(trf);
        await db.SaveChangesAsync();
        _accountIds.Add(src.Id); _accountIds.Add(dst.Id); _transferIds.Add(trf.Id);
        return (trf.Id, src.Id, dst.Id);
    }

    private async Task<Guid> UploadTxAttachmentAsync(Guid txId)
    {
        var res = await _client.PostAsync($"/api/transactions/{txId}/attachments", SmallPng());
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        _attachmentIds.Add(id);
        return id;
    }

    // -------------------------------------------------------------------------
    // Download (new endpoint)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Download_transaction_attachment_streams_with_content_disposition()
    {
        var txId = await SeedSentinelTransactionAsync();
        var attId = await UploadTxAttachmentAsync(txId);

        var res = await _client.GetAsync($"/api/attachments/transactions/{attId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        res.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        res.Content.Headers.ContentDisposition!.FileName.Should().Contain("tiny.png");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Download_transaction_attachment_returns_404_when_unknown_id()
    {
        var res = await _client.GetAsync($"/api/attachments/transactions/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_transaction_attachment_returns_404_for_intruder_attachment()
    {
        // Seed an attachment whose parent transaction belongs to another user.
        var intruderAccountId = Guid.NewGuid();
        var intruderTxId = Guid.NewGuid();
        var intruderAttId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Accounts.Add(new Account { Id = intruderAccountId, Name = "intruder-acct", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = _intruderUserId });
            db.Transactions.Add(new Transaction { Id = intruderTxId, UserId = _intruderUserId, Date = new DateOnly(2026, 4, 1), Amount = 1m, AccountId = intruderAccountId, CategoryId = HousingCategoryId });
            db.TransactionAttachments.Add(new TransactionAttachment
            {
                Id = intruderAttId,
                TransactionId = intruderTxId,
                // Must match the parent transaction's owner: the FK is composite on
                // (TransactionId, UserId) since 2026-08-24, so a row whose UserId differs
                // from its parent's is no longer representable. Without this the
                // interceptor would stamp the CURRENT user and the insert would fail.
                UserId = _intruderUserId,
                FileName = "secret.png",
                StoredPath = "uploads/intruder/never-served.png",
                ContentType = "image/png",
                FileSizeBytes = 100,
                UploadedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            var res = await _client.GetAsync($"/api/attachments/transactions/{intruderAttId}");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.TransactionAttachments.Where(a => a.Id == intruderAttId).ExecuteDeleteAsync();
            await db.Transactions.Where(t => t.Id == intruderTxId).ExecuteDeleteAsync();
            await db.Accounts.Where(a => a.Id == intruderAccountId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Download_transfer_attachment_streams_with_content_disposition()
    {
        var (trfId, _, _) = await SeedSentinelTransferAsync();

        var upload = await _client.PostAsync($"/api/transfers/{trfId}/attachments", SmallPng());
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await upload.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var attId = body.GetProperty("id").GetGuid();
        _transferAttachmentIds.Add(attId);

        var res = await _client.GetAsync($"/api/attachments/transfers/{attId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    [Fact]
    public async Task Download_transfer_attachment_returns_404_for_intruder_attachment()
    {
        var srcId = Guid.NewGuid();
        var dstId = Guid.NewGuid();
        var trfId = Guid.NewGuid();
        var attId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Accounts.Add(new Account { Id = srcId, Name = "intruder-trf-src", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = _intruderUserId });
            db.Accounts.Add(new Account { Id = dstId, Name = "intruder-trf-dst", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = _intruderUserId });
            db.Transfers.Add(new Transfer { Id = trfId, UserId = _intruderUserId, Date = new DateOnly(2026, 4, 1), Amount = 1m, SourceAccountId = srcId, DestAccountId = dstId });
            db.TransferAttachments.Add(new TransferAttachment
            {
                Id = attId, TransferId = trfId,
                // Must match the parent transfer's owner — see the transaction case above.
                UserId = _intruderUserId,
                FileName = "secret.png", StoredPath = "uploads/transfers/intruder/never-served.png",
                ContentType = "image/png", FileSizeBytes = 100, UploadedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        try
        {
            var res = await _client.GetAsync($"/api/attachments/transfers/{attId}");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.TransferAttachments.Where(a => a.Id == attId).ExecuteDeleteAsync();
            await db.Transfers.Where(t => t.Id == trfId).ExecuteDeleteAsync();
            await db.Accounts.Where(a => a.Id == srcId || a.Id == dstId).ExecuteDeleteAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Existing Upload/Delete endpoints — confirm IDOR fix is in place
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Upload_to_intruder_transaction_returns_404()
    {
        var intruderAccountId = Guid.NewGuid();
        var intruderTxId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Accounts.Add(new Account { Id = intruderAccountId, Name = "intruder-up", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = _intruderUserId });
            db.Transactions.Add(new Transaction { Id = intruderTxId, UserId = _intruderUserId, Date = new DateOnly(2026, 4, 1), Amount = 1m, AccountId = intruderAccountId, CategoryId = HousingCategoryId });
            await db.SaveChangesAsync();
        }
        try
        {
            // Existing TransactionsApi controller pre-checks with GetByIdForEditAsync (now ownership-filtered)
            // and returns NotFound before invoking the upload service.
            var res = await _client.PostAsync($"/api/transactions/{intruderTxId}/attachments", SmallPng());
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Transactions.Where(t => t.Id == intruderTxId).ExecuteDeleteAsync();
            await db.Accounts.Where(a => a.Id == intruderAccountId).ExecuteDeleteAsync();
        }
    }

    // Uploading onto someone else's transaction must look like the transaction does not
    // exist, per security-model.md § IDOR.
    //
    // Verified 2026-08-24 that the controller's own existence check already returns 404
    // here, so the composite FK's 23503 is unreachable on this path — removing the
    // ForeignKeyViolationException guard does NOT make this test fail. The guard is
    // defence in depth for a future caller that reaches the service directly; this test
    // pins the OBSERVABLE contract (404, and no constraint name in the body) rather than
    // the mechanism that produces it.
    [Fact]
    public async Task Upload_onto_another_users_transaction_returns_404_not_422()
    {
        var intruderAccountId = Guid.NewGuid();
        var intruderTxId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Accounts.Add(new Account { Id = intruderAccountId, Name = "intruder-upload", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = _intruderUserId });
            db.Transactions.Add(new Transaction { Id = intruderTxId, UserId = _intruderUserId, Date = new DateOnly(2026, 4, 1), Amount = 1m, AccountId = intruderAccountId, CategoryId = HousingCategoryId });
            await db.SaveChangesAsync();
        }

        try
        {
            using var content = new MultipartFormDataContent();
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(file, "file", "probe.png");

            var res = await _client.PostAsync($"/api/transactions/{intruderTxId}/attachments", content);

            res.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "a transaction the caller cannot see must appear not to exist, rather than " +
                "returning a 422 that names the foreign-key constraint");

            var body = await res.Content.ReadAsStringAsync();
            body.Should().NotContain("FK_TransactionAttachments",
                "the response must never carry an internal constraint name");
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.TransactionAttachments.IgnoreQueryFilters().Where(a => a.TransactionId == intruderTxId).ExecuteDeleteAsync();
            await db.Transactions.IgnoreQueryFilters().Where(t => t.Id == intruderTxId).ExecuteDeleteAsync();
            await db.Accounts.IgnoreQueryFilters().Where(a => a.Id == intruderAccountId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Delete_intruder_transaction_attachment_returns_404()
    {
        var intruderAccountId = Guid.NewGuid();
        var intruderTxId = Guid.NewGuid();
        var intruderAttId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Accounts.Add(new Account { Id = intruderAccountId, Name = "intruder-del", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = _intruderUserId });
            db.Transactions.Add(new Transaction { Id = intruderTxId, UserId = _intruderUserId, Date = new DateOnly(2026, 4, 1), Amount = 1m, AccountId = intruderAccountId, CategoryId = HousingCategoryId });
            db.TransactionAttachments.Add(new TransactionAttachment
            {
                Id = intruderAttId, TransactionId = intruderTxId,
                // Must match the parent transaction's owner — see the download case above.
                UserId = _intruderUserId,
                FileName = "do-not-delete.png", StoredPath = "uploads/intruder/keep.png",
                ContentType = "image/png", FileSizeBytes = 100, UploadedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            var res = await _client.DeleteAsync($"/api/transactions/attachments/{intruderAttId}");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Confirm the intruder row is still in the database. IgnoreQueryFilters is
            // required: the row carries the INTRUDER's UserId (the composite FK has
            // required that since 2026-08-24), so the per-user filter correctly hides it
            // from this scope. Without the bypass this asserts the filter works, not that
            // the delete was refused.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.TransactionAttachments.IgnoreQueryFilters()
                .AnyAsync(a => a.Id == intruderAttId)).Should().BeTrue();
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.TransactionAttachments.Where(a => a.Id == intruderAttId).ExecuteDeleteAsync();
            await db.Transactions.Where(t => t.Id == intruderTxId).ExecuteDeleteAsync();
            await db.Accounts.Where(a => a.Id == intruderAccountId).ExecuteDeleteAsync();
        }
    }
}
