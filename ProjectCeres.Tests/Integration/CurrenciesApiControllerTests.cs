using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Controllers.Api;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// The currency list the UI offers. Seeded currencies: 1 EUR, 2 USD, 3 GBP, 4 COP, 5 ARS, 6 VED.
/// The sentinel test user owns EUR accounts only and has EUR as its default currency.
/// </summary>
[Collection("TestDbFixtureTests")]
public class CurrenciesApiControllerTests : IAsyncLifetime
{
    private static readonly Guid Sentinel = new("00000000-0000-0000-0000-000000000001");
    private const int Usd = 2, Gbp = 3;

    private readonly TestDbFixture _fixture = new();
    private CurrenciesApiController _controller = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        var user = new FakeCurrentUserAccessor(Sentinel);
        _controller = new CurrenciesApiController(_fixture.Db, user, new SettingsService(_fixture.Db, user));
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<string[]> CodesAsync(bool inUse)
    {
        var result = await _controller.Get(inUse);
        var list = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<IEnumerable<CurrencyDto>>().Subject;
        return list.Select(c => c.Code).ToArray();
    }

    private async Task AddAccountAsync(int currencyId, Guid owner, bool isActive = true)
    {
        _fixture.Db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            Name = $"cur-{Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId = currencyId,
            IsActive = isActive,
        });
        await _fixture.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Without_the_flag_every_currency_is_listed()
    {
        (await CodesAsync(inUse: false)).Should().BeEquivalentTo("EUR", "USD", "GBP", "COP", "ARS", "VED");
    }

    [Fact]
    public async Task InUse_lists_only_currencies_the_user_has_accounts_in()
    {
        await AddAccountAsync(Usd, Sentinel);

        var codes = await CodesAsync(inUse: true);

        codes.Should().BeEquivalentTo("EUR", "USD");
        codes.Should().NotContain(new[] { "GBP", "COP", "ARS", "VED" });
    }

    [Fact]
    public async Task InUse_still_lists_a_currency_whose_only_accounts_are_archived()
    {
        await AddAccountAsync(Usd, Sentinel, isActive: false);

        (await CodesAsync(inUse: true)).Should().Contain("USD");
    }

    [Fact]
    public async Task InUse_always_includes_the_default_currency_even_with_no_account_in_it()
    {
        var settings = await _fixture.Db.Settings.FirstAsync(s => s.UserId == Sentinel);
        settings.DefaultCurrencyId = Gbp;
        await _fixture.Db.SaveChangesAsync();

        (await CodesAsync(inUse: true)).Should().Contain("GBP", "reports default to it, so the filter must be able to show it");
    }
}
