using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel2")]
public class CurrenciesApiTests : IntegrationTestBase<Bucket2Factory>, IAsyncLifetime
{
    private const int Cop = 4;

    private readonly Bucket2Factory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _intruderAccountIds = [];

    public CurrenciesApiTests(Bucket2Factory factory, Bucket2Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_intruderAccountIds.Count == 0) return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Accounts.Where(a => _intruderAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GetCurrencies_returns_seed_data()
    {
        var response = await _client.GetAsync("/api/currencies");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"code\":\"EUR\"");
        body.Should().Contain("\"code\":\"USD\"");
    }

    [Fact]
    public async Task GetCurrencies_inUse_returns_the_callers_currencies_in_the_same_shape()
    {
        var response = await _client.GetAsync("/api/currencies?inUse=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"code\":\"EUR\"", "the test user's baseline accounts and default currency are EUR");
        body.Should().Contain("\"symbol\":");
    }

    [Fact]
    public async Task GetCurrencies_inUse_ignores_another_users_accounts()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var intruder = new Account
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Name = $"intruder-{Guid.NewGuid():N}",
                AccountTypeId = 1,
                CurrencyId = Cop,
                IsActive = true,
            };
            db.Accounts.Add(intruder);
            _intruderAccountIds.Add(intruder.Id);
            await db.SaveChangesAsync();
        }

        var body = await (await _client.GetAsync("/api/currencies?inUse=true")).Content.ReadAsStringAsync();

        body.Should().NotContain("\"code\":\"COP\"", "another user's account must not widen this user's currency list");
    }
}
