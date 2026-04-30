using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class TransactionsCrudApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededAttachmentIds = [];

    public TransactionsCrudApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededAttachmentIds.Count > 0)
            await db.TransactionAttachments.Where(a => _seededAttachmentIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_seededTransactionIds.Count > 0)
            await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid AccountId, Guid TransactionId)> SeedTransactionAsync(decimal amount = 25.50m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"TxCrud-Acct-{Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);

        var tx = new Transaction
        {
            Id          = Guid.NewGuid(),
            Date        = new DateOnly(2026, 4, 15),
            Amount      = amount,
            AccountId   = account.Id,
            CategoryId  = HousingCategoryId,
            Description = "Existing tx",
            IsCleared   = false
        };
        db.Transactions.Add(tx);
        _seededTransactionIds.Add(tx.Id);

        await db.SaveChangesAsync();
        return (account.Id, tx.Id);
    }

    [Fact]
    public async Task Get_Returns200_WithEditDto()
    {
        var (accountId, txId) = await SeedTransactionAsync();

        var response = await _client.GetAsync($"/api/transactions/{txId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(txId);
        body.GetProperty("amount").GetDecimal().Should().Be(25.50m);
        body.GetProperty("accountId").GetGuid().Should().Be(accountId);
        body.GetProperty("categoryId").GetGuid().Should().Be(HousingCategoryId);
        body.GetProperty("isCleared").GetBoolean().Should().BeFalse();
        body.GetProperty("attachments").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Returns404_WhenIdMissing()
    {
        var response = await _client.GetAsync($"/api/transactions/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Returns200_WithAttachmentMetadata_WhenAttachmentsExist()
    {
        var (_, txId) = await SeedTransactionAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attachment = new TransactionAttachment
        {
            Id            = Guid.NewGuid(),
            TransactionId = txId,
            FileName      = "receipt.pdf",
            StoredPath    = "/tmp/test-receipt.pdf",
            FileSizeBytes = 4096,
            ContentType   = "application/pdf",
            UploadedAt    = DateTime.UtcNow
        };
        db.TransactionAttachments.Add(attachment);
        await db.SaveChangesAsync();
        _seededAttachmentIds.Add(attachment.Id);

        var response = await _client.GetAsync($"/api/transactions/{txId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var attachments = body.GetProperty("attachments").EnumerateArray().ToList();
        attachments.Should().HaveCount(1);
        attachments[0].GetProperty("fileName").GetString().Should().Be("receipt.pdf");
        attachments[0].GetProperty("sizeBytes").GetInt64().Should().Be(4096);
        attachments[0].GetProperty("contentType").GetString().Should().Be("application/pdf");
    }
}
