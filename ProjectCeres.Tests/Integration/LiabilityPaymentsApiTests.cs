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
public class LiabilityPaymentsApiTests : IntegrationTestBase<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededPaymentIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public LiabilityPaymentsApiTests(TestWebApplicationFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededPaymentIds.Count > 0)
        {
            var rows = await db.LiabilityPayments.Where(p => _seededPaymentIds.Contains(p.Id)).ToListAsync();
            db.LiabilityPayments.RemoveRange(rows);
        }
        if (_seededAccountIds.Count > 0)
        {
            var rows = await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ToListAsync();
            db.Accounts.RemoveRange(rows);
        }
        await db.SaveChangesAsync();
    }

    private Account SeedAccount(AppDbContext db, string name, int accountTypeId = 1, int currencyId = 1)
    {
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = name,
            AccountTypeId = accountTypeId,
            CurrencyId    = currencyId,
            IsActive      = true
        };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);
        return account;
    }

    [Fact]
    public async Task Post_Returns201_AndCreatesLiabilityPayment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assetAccount     = SeedAccount(db, "LP-Asset",     accountTypeId: 1);
        var liabilityAccount = SeedAccount(db, "LP-Liability", accountTypeId: 2);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/liability-payments", new
        {
            date               = "2026-04-15",
            amount             = 200.00m,
            assetAccountId     = assetAccount.Id,
            liabilityAccountId = liabilityAccount.Id,
            description        = "Loan payment"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("id", out var idProp).Should().BeTrue();
        var newId = idProp.GetGuid();
        _seededPaymentIds.Add(newId);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await verifyDb.LiabilityPayments.FindAsync(newId);
        saved.Should().NotBeNull();
        saved!.Amount.Should().Be(200.00m);
        saved.Description.Should().Be("Loan payment");
    }

    [Fact]
    public async Task Post_Returns422_WhenAmountIsZero()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assetAccount     = SeedAccount(db, "LP-Asset-Bad",     accountTypeId: 1);
        var liabilityAccount = SeedAccount(db, "LP-Liability-Bad", accountTypeId: 2);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/liability-payments", new
        {
            date               = "2026-04-15",
            amount             = 0m,
            assetAccountId     = assetAccount.Id,
            liabilityAccountId = liabilityAccount.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
