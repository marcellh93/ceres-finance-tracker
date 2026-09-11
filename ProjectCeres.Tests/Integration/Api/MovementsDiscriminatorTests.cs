using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel4")]
public class MovementsDiscriminatorTests : IntegrationTestBase<Bucket4Factory>, IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly Bucket4Factory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededTransferIds = [];

    public MovementsDiscriminatorTests(Bucket4Factory factory, Bucket4Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0) await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededTransferIds.Count > 0)    await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Get_ReturnsTransaction_WhenIdIsTransaction()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"D-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 25), Amount = 5m, AccountId = account.Id, CategoryId = HousingCategoryId };
        db.Accounts.Add(account); db.Transactions.Add(tx); _seededAccountIds.Add(account.Id); _seededTransactionIds.Add(tx.Id);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/movements/{tx.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("movementType").GetString().Should().Be("Transaction");
        body.GetProperty("id").GetGuid().Should().Be(tx.Id);
    }

    [Fact]
    public async Task Get_Returns404_WhenIdMissing()
    {
        var response = await _client.GetAsync($"/api/movements/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
