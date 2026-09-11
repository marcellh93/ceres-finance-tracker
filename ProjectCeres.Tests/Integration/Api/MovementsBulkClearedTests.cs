using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel1")]
public class MovementsBulkClearedTests : IntegrationTestBase<TestWebApplicationFactory>, IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededLiabilityPaymentIds = [];

    public MovementsBulkClearedTests(TestWebApplicationFactory factory, Bucket1Database bucketDb) : base(factory, bucketDb)
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
        if (_seededTransferIds.Count > 0) await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededLiabilityPaymentIds.Count > 0) await db.LiabilityPayments.Where(p => _seededLiabilityPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Post_MarksMatchingTransactionsCleared()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"BC-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx1 = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 5), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        var tx2 = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 8), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        db.Accounts.Add(account); db.Transactions.AddRange(tx1, tx2);
        _seededAccountIds.Add(account.Id); _seededTransactionIds.AddRange([tx1.Id, tx2.Id]);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/movements/bulk-cleared", new
        {
            from = "2026-04-01",
            to   = "2026-04-30",
            accountId = account.Id,
            type = "transaction"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cleared").GetInt32().Should().Be(2);

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.Transactions.FindAsync(tx1.Id))!.IsCleared.Should().BeTrue();
        (await verifyDb.Transactions.FindAsync(tx2.Id))!.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task Post_Returns400_WhenInvalidType()
    {
        var response = await _client.PostAsJsonAsync("/api/movements/bulk-cleared", new
        {
            from = "2026-04-01",
            to   = "2026-04-30",
            type = "bogus"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_NoTypeFilter_SumsAcrossAllThreeServices()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed: 1 asset account, 1 second asset account (for transfers), 1 liability.
        var asset1 = new Account { Id = Guid.NewGuid(), Name = $"BC-Mix-A1-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var asset2 = new Account { Id = Guid.NewGuid(), Name = $"BC-Mix-A2-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var liab   = new Account { Id = Guid.NewGuid(), Name = $"BC-Mix-L-{Guid.NewGuid():N}",  AccountTypeId = 2, CurrencyId = 1, IsActive = true };
        db.Accounts.AddRange(asset1, asset2, liab);

        // 1 uncleared transaction, 1 uncleared transfer, 1 uncleared liability payment in range.
        var tx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 5, 5), Amount = 1m, AccountId = asset1.Id, CategoryId = HousingCategoryId };
        var tr = new Transfer    { Id = Guid.NewGuid(), Date = new DateOnly(2026, 5, 6), Amount = 2m, SourceAccountId = asset1.Id, DestAccountId = asset2.Id };
        var lp = new LiabilityPayment { Id = Guid.NewGuid(), Date = new DateOnly(2026, 5, 7), Amount = 3m, AssetAccountId = asset1.Id, LiabilityAccountId = liab.Id };
        db.Transactions.Add(tx);
        db.Transfers.Add(tr);
        db.LiabilityPayments.Add(lp);
        await db.SaveChangesAsync();

        _seededAccountIds.AddRange([asset1.Id, asset2.Id, liab.Id]);
        _seededTransactionIds.Add(tx.Id);
        _seededTransferIds.Add(tr.Id);
        _seededLiabilityPaymentIds.Add(lp.Id);

        var response = await _client.PostAsJsonAsync("/api/movements/bulk-cleared", new
        {
            from = "2026-05-01",
            to   = "2026-05-31"
            // no type → all types
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cleared").GetInt32().Should().Be(3);

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.Transactions.FindAsync(tx.Id))!.IsCleared.Should().BeTrue();
        (await verifyDb.Transfers.FindAsync(tr.Id))!.IsCleared.Should().BeTrue();
        (await verifyDb.LiabilityPayments.FindAsync(lp.Id))!.IsCleared.Should().BeTrue();
    }
}
