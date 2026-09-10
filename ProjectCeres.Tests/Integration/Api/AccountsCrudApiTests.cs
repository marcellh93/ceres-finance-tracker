using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel2")]
public class AccountsCrudApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _createdAccountIds = [];

    public AccountsCrudApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdAccountIds.Count == 0) return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Transactions.Where(t => _createdAccountIds.Contains(t.AccountId)).ExecuteDeleteAsync();
        await db.Accounts.Where(a => _createdAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Get_lists_active_accounts_with_balance()
    {
        var res = await _client.GetAsync("/api/accounts");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Should().NotBeEmpty();
        rows!.Should().OnlyContain(r => r.GetProperty("isActive").GetBoolean());
        rows!.First().TryGetProperty("balance", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_creates_asset_account_with_opening_balance()
    {
        var res = await _client.PostAsJsonAsync("/api/accounts", new
        {
            name                 = "Brokerage",
            accountTypeId        = 1,
            currencyId           = 1,
            description          = "Test",
            openingBalance       = 1500m,
            openingBalanceDate   = "2026-01-01",
            excludeFromSpendable = false
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        _createdAccountIds.Add(id);
        dto.GetProperty("name").GetString().Should().Be("Brokerage");
        dto.GetProperty("balance").GetDecimal().Should().Be(1500m);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Accounts.FindAsync(id);
        saved!.UserId.Should().Be(new Guid("00000000-0000-0000-0000-000000000001"));
    }

    [Fact]
    public async Task Post_returns_422_on_invalid_account_type()
    {
        var res = await _client.PostAsJsonAsync("/api/accounts", new
        {
            name               = "Bad",
            accountTypeId      = 999,
            currencyId         = 1,
            openingBalance     = 0m,
            openingBalanceDate = "2026-01-01"
        });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_ACCOUNT_TYPE");
    }

    [Fact]
    public async Task Post_returns_422_on_amortising_liability_without_interest_rate()
    {
        var res = await _client.PostAsJsonAsync("/api/accounts", new
        {
            name                   = "Mortgage",
            accountTypeId          = 2,
            currencyId             = 1,
            openingBalance         = 0m,
            openingBalanceDate     = "2026-01-01",
            liabilityRepaymentType = "Amortising"
        });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_LIABILITY_REPAYMENT");
    }

    [Fact]
    public async Task Patch_updates_account_fields()
    {
        var create = await _client.PostAsJsonAsync("/api/accounts", new
        {
            name               = "ToEdit",
            accountTypeId      = 1,
            currencyId         = 1,
            openingBalance     = 0m,
            openingBalanceDate = "2026-01-01"
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdAccountIds.Add(id);

        var res = await _client.PatchAsJsonAsync($"/api/accounts/{id}", new
        {
            name                 = "Edited",
            description          = "Updated",
            openingBalance       = 100m,
            openingBalanceDate   = "2026-02-01",
            excludeFromSpendable = false
        });
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{id}");
        detail.GetProperty("name").GetString().Should().Be("Edited");
        detail.GetProperty("description").GetString().Should().Be("Updated");
        detail.GetProperty("openingBalance").GetDecimal().Should().Be(100m);
    }

    [Fact]
    public async Task Patch_returns_404_when_unknown_id()
    {
        var res = await _client.PatchAsJsonAsync($"/api/accounts/{Guid.NewGuid()}", new
        {
            name               = "x",
            openingBalance     = 0m,
            openingBalanceDate = "2026-01-01"
        });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Archive_deactivates_account()
    {
        var create = await _client.PostAsJsonAsync("/api/accounts", new
        {
            name               = "Disposable",
            accountTypeId      = 1,
            currencyId         = 1,
            openingBalance     = 0m,
            openingBalanceDate = "2026-01-01"
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdAccountIds.Add(id);

        var res = await _client.PatchAsync($"/api/accounts/{id}/archive", null);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.Accounts.FindAsync(id);
        fresh!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Ledger_returns_entries_for_account()
    {
        var create = await _client.PostAsJsonAsync("/api/accounts", new
        {
            name               = "Ledgered",
            accountTypeId      = 1,
            currencyId         = 1,
            openingBalance     = 200m,
            openingBalanceDate = "2026-01-01"
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdAccountIds.Add(id);

        var res = await _client.GetAsync($"/api/accounts/{id}/ledger");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("accountName").GetString().Should().Be("Ledgered");
        dto.GetProperty("entries").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Ledger_returns_404_when_unknown_id()
    {
        var res = await _client.GetAsync($"/api/accounts/{Guid.NewGuid()}/ledger");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_returns_404_when_unknown_id()
    {
        var res = await _client.GetAsync($"/api/accounts/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AccountTypes_returns_seeded_types()
    {
        var res = await _client.GetAsync("/api/account-types");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("name").GetString()).Should().BeEquivalentTo(["Asset", "Liability"]);
    }

    [Fact]
    public async Task Get_returns_HasTransactions_false_for_account_with_no_movements()
    {
        var create = await _client.PostAsJsonAsync("/api/accounts", new
        {
            Name                   = $"Empty-{Guid.NewGuid():N}",
            AccountTypeId          = 1,
            CurrencyId             = 1,
            Description            = (string?)null,
            OpeningBalance         = 0m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.UtcNow),
            LiabilityRepaymentType = (string?)null,
            InterestRate           = (decimal?)null,
            ExcludeFromSpendable   = false,
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        _createdAccountIds.Add(id);

        var res = await _client.GetAsync("/api/accounts");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        var row = rows!.First(r => r.GetProperty("id").GetGuid() == id);
        row.GetProperty("hasTransactions").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Get_returns_HasTransactions_true_for_account_with_opening_balance()
    {
        var create = await _client.PostAsJsonAsync("/api/accounts", new
        {
            Name                   = $"Funded-{Guid.NewGuid():N}",
            AccountTypeId          = 1,
            CurrencyId             = 1,
            Description            = (string?)null,
            OpeningBalance         = 100m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.UtcNow),
            LiabilityRepaymentType = (string?)null,
            InterestRate           = (decimal?)null,
            ExcludeFromSpendable   = false,
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        _createdAccountIds.Add(id);

        var res = await _client.GetAsync("/api/accounts");
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        var row = rows!.First(r => r.GetProperty("id").GetGuid() == id);
        row.GetProperty("hasTransactions").GetBoolean().Should().BeTrue();
    }
}
