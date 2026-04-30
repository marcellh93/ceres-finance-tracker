using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class TransactionAttachmentsApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededAttachmentIds = [];

    public TransactionAttachmentsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededAttachmentIds.Count > 0)  await db.TransactionAttachments.Where(a => _seededAttachmentIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_seededTransactionIds.Count > 0) await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedTransactionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Att-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 16), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        db.Accounts.Add(account); db.Transactions.Add(tx);
        _seededAccountIds.Add(account.Id); _seededTransactionIds.Add(tx.Id);
        await db.SaveChangesAsync();
        return tx.Id;
    }

    private static MultipartFormDataContent SmallPng()
    {
        // A 1x1 PNG (89 50 4E 47 0D 0A 1A 0A …), enough to satisfy magic-byte validation.
        byte[] png = [
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
            0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,0x89,
            0x00,0x00,0x00,0x0D,0x49,0x44,0x41,0x54,
            0x78,0x9C,0x62,0x00,0x01,0x00,0x00,0x05,
            0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,
            0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,
            0x60,0x82
        ];
        var content = new MultipartFormDataContent();
        var bc = new ByteArrayContent(png);
        bc.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(bc, "file", "tiny.png");
        return content;
    }

    [Fact]
    public async Task Post_Returns201_AndReturnsAttachmentMetadata()
    {
        var txId = await SeedTransactionAsync();

        var response = await _client.PostAsync($"/api/transactions/{txId}/attachments", SmallPng());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("fileName").GetString().Should().Be("tiny.png");
        body.GetProperty("contentType").GetString().Should().Be("image/png");
        var attId = body.GetProperty("id").GetGuid();
        _seededAttachmentIds.Add(attId);
    }

    [Fact]
    public async Task Post_Returns404_WhenTransactionMissing()
    {
        var response = await _client.PostAsync($"/api/transactions/{Guid.NewGuid()}/attachments", SmallPng());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns204_AndRemovesAttachment()
    {
        var txId = await SeedTransactionAsync();

        var post = await _client.PostAsync($"/api/transactions/{txId}/attachments", SmallPng());
        var posted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var attId = posted.GetProperty("id").GetGuid();

        var del = await _client.DeleteAsync($"/api/transactions/attachments/{attId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.TransactionAttachments.FindAsync(attId)).Should().BeNull();
    }
}
