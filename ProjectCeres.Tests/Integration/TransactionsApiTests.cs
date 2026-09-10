using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationParallel4")]
public class TransactionsApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public TransactionsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0)
        {
            var rows = await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ToListAsync();
            db.Transactions.RemoveRange(rows);
        }
        if (_seededAccountIds.Count > 0)
        {
            var rows = await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ToListAsync();
            db.Accounts.RemoveRange(rows);
        }
        await db.SaveChangesAsync();
    }

    private Account SeedAssetAccount(AppDbContext db, string name)
    {
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = name,
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);
        return account;
    }

    [Fact]
    public async Task Post_Returns201_AndCreatesTransaction()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = SeedAssetAccount(db, "Checking-Tx");
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            date = "2026-04-15",
            amount = 25.50m,
            accountId = account.Id,
            categoryId = HousingCategoryId,
            description = "Test rent"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("id", out var idProp).Should().BeTrue();
        var newId = idProp.GetGuid();
        _seededTransactionIds.Add(newId);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await verifyDb.Transactions.FindAsync(newId);
        saved.Should().NotBeNull();
        saved!.Amount.Should().Be(25.50m);
        saved.Description.Should().Be("Test rent");
    }

    [Fact]
    public async Task Post_Returns422_WhenAmountIsZero()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = SeedAssetAccount(db, "Checking-Tx-Bad");
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            date = "2026-04-15",
            amount = 0m,
            accountId = account.Id,
            categoryId = HousingCategoryId
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Post_Returns422_WhenAccountIdMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            date = "2026-04-15",
            amount = 10m,
            categoryId = HousingCategoryId
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
