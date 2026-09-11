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

[Collection("IntegrationParallel2")]
public class TransferAttachmentsApiTests : IntegrationTestBase<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededAttachmentIds = [];

    public TransferAttachmentsApiTests(TestWebApplicationFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededAttachmentIds.Count > 0) await db.TransferAttachments.Where(a => _seededAttachmentIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_seededTransferIds.Count > 0)   await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)    await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedTransferAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceAccount = new Account { Id = Guid.NewGuid(), Name = $"Transfer-Src-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var destAccount = new Account { Id = Guid.NewGuid(), Name = $"Transfer-Dst-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var transfer = new Transfer { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 16), Amount = 100m, SourceAccountId = sourceAccount.Id, DestAccountId = destAccount.Id };
        db.Accounts.Add(sourceAccount);
        db.Accounts.Add(destAccount);
        db.Transfers.Add(transfer);
        _seededAccountIds.Add(sourceAccount.Id);
        _seededAccountIds.Add(destAccount.Id);
        _seededTransferIds.Add(transfer.Id);
        await db.SaveChangesAsync();
        return transfer.Id;
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
        var trId = await SeedTransferAsync();

        var response = await _client.PostAsync($"/api/transfers/{trId}/attachments", SmallPng());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("fileName").GetString().Should().Be("tiny.png");
        body.GetProperty("contentType").GetString().Should().Be("image/png");
        var attId = body.GetProperty("id").GetGuid();
        _seededAttachmentIds.Add(attId);
    }

    [Fact]
    public async Task Post_Returns404_WhenTransferMissing()
    {
        var response = await _client.PostAsync($"/api/transfers/{Guid.NewGuid()}/attachments", SmallPng());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns204_AndRemovesAttachment()
    {
        var trId = await SeedTransferAsync();

        var post = await _client.PostAsync($"/api/transfers/{trId}/attachments", SmallPng());
        var posted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var attId = posted.GetProperty("id").GetGuid();

        var del = await _client.DeleteAsync($"/api/transfers/attachments/{attId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.TransferAttachments.FindAsync(attId)).Should().BeNull();
    }
}
