using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for the same-currency transfer rule enforced by TransferService.
/// Uses the real project_ceres_test database. Each test rolls back its transaction.
///
/// Seed data IDs used:
///   CurrencyId 1 = EUR, CurrencyId 2 = USD
///   AccountTypeId 1 = Asset
/// </summary>
[Collection("IntegrationTests")]
public class TransferValidationTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private TransferService _service = null!;
    private AccountService _accountService = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        _service = new TransferService(_fixture.Db, _accountService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Account> CreateAccountAsync(int currencyId)
    {
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Test Account {Guid.NewGuid():N}",
            AccountTypeId = 1, // Asset (seeded)
            CurrencyId    = currencyId,
            IsActive      = true
        };
        _fixture.Db.Accounts.Add(account);
        await _fixture.Db.SaveChangesAsync();
        return account;
    }

    private static TransferCreateViewModel CreateVm(Guid sourceId, Guid destId, DateOnly? date = null) =>
        new()
        {
            Date            = date ?? DateOnly.FromDateTime(DateTime.Today),
            Amount          = 100m,
            SourceAccountId = sourceId,
            DestAccountId   = destId,
            Description     = null
        };

    private static TransferEditViewModel EditVm(Guid id, Guid sourceId, Guid destId, DateOnly? date = null) =>
        new()
        {
            Id              = id,
            Date            = date ?? DateOnly.FromDateTime(DateTime.Today),
            Amount          = 100m,
            SourceAccountId = sourceId,
            DestAccountId   = destId,
            Description     = null
        };

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transfer_BetweenSameCurrencyAccounts_Succeeds()
    {
        var source = await CreateAccountAsync(currencyId: 1); // EUR
        var dest   = await CreateAccountAsync(currencyId: 1); // EUR

        var vm = CreateVm(source.Id, dest.Id);
        vm.Amount = 250m;
        var transfer = await _service.CreateAsync(vm);

        transfer.Should().NotBeNull();
        transfer.Amount.Should().Be(250m);
        transfer.SourceAccountId.Should().Be(source.Id);
        transfer.DestAccountId.Should().Be(dest.Id);
    }

    [Fact]
    public async Task Transfer_BetweenDifferentCurrencyAccounts_Throws()
    {
        var source = await CreateAccountAsync(currencyId: 1); // EUR
        var dest   = await CreateAccountAsync(currencyId: 2); // USD

        var act = async () => await _service.CreateAsync(CreateVm(source.Id, dest.Id));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*same currency*");
    }

    [Fact]
    public async Task UpdateTransfer_ChangingToDifferentCurrencyAccounts_Throws()
    {
        var eurA = await CreateAccountAsync(currencyId: 1); // EUR
        var eurB = await CreateAccountAsync(currencyId: 1); // EUR
        var usd  = await CreateAccountAsync(currencyId: 2); // USD

        // Create valid same-currency transfer first.
        var transfer = await _service.CreateAsync(CreateVm(eurA.Id, eurB.Id));

        // Attempt to edit it to cross-currency — should be rejected.
        var act = async () => await _service.UpdateAsync(EditVm(transfer.Id, eurA.Id, usd.Id));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*same currency*");
    }
}
