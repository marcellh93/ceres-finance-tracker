using FluentAssertions;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for TransactionExportService against the real project_ceres_test database.
/// Each test rolls back — no data persists.
///
/// Seed data used:
///   AccountTypeId 1 = Asset, CurrencyId 1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
/// </summary>
[Collection("IntegrationTests")]
public class TransactionExportServiceTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture   _fixture = new();
    private TransactionExportService _service = null!;
    private Guid                     _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _service = new TransactionExportService(_fixture.Db);

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Export Test {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        _fixture.Db.Accounts.Add(account);
        await _fixture.Db.SaveChangesAsync();
        _accountId = account.Id;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private Transaction MakeTx(DateOnly date, decimal amount = 50m, string? desc = null) => new()
    {
        Id          = Guid.NewGuid(),
        AccountId   = _accountId,
        CategoryId  = HousingCategoryId,
        Date        = date,
        Amount      = amount,
        Description = desc,
        CreatedAt   = DateTime.UtcNow
    };

    [Fact]
    public async Task ExportAsync_NoFilter_ReturnsAllTransactions()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _fixture.Db.Transactions.AddRange(
            MakeTx(today, 10m),
            MakeTx(today, 20m),
            MakeTx(today, 30m),
            MakeTx(today, 40m),
            MakeTx(today, 50m));
        await _fixture.Db.SaveChangesAsync();

        var rows = await _service.ExportAsync();

        rows.Count.Should().Be(5);
    }

    [Fact]
    public async Task ExportAsync_AccountIdFilter_ReturnsOnlyMatchingAccount()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var other = new Account
        {
            Id = Guid.NewGuid(), Name = $"Other {Guid.NewGuid():N}",
            AccountTypeId = 1, CurrencyId = 1, IsActive = true
        };
        _fixture.Db.Accounts.Add(other);
        await _fixture.Db.SaveChangesAsync();

        _fixture.Db.Transactions.AddRange(
            MakeTx(today, 10m),
            MakeTx(today, 20m));
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), AccountId = other.Id,
            CategoryId = HousingCategoryId, Date = today,
            Amount = 99m, CreatedAt = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var rows = await _service.ExportAsync(accountId: _accountId);

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.Account != null);
    }

    [Fact]
    public async Task ExportAsync_DateRangeFilter_ReturnsOnlyInRangeRows()
    {
        var jan1  = new DateOnly(2025, 1, 1);
        var jan15 = new DateOnly(2025, 1, 15);
        var feb1  = new DateOnly(2025, 2, 1);

        _fixture.Db.Transactions.AddRange(
            MakeTx(jan1,  10m),
            MakeTx(jan15, 20m),
            MakeTx(feb1,  30m));
        await _fixture.Db.SaveChangesAsync();

        var rows = await _service.ExportAsync(
            from: new DateOnly(2025, 1, 1),
            to:   new DateOnly(2025, 1, 31));

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.Date <= new DateOnly(2025, 1, 31));
    }

    [Fact]
    public async Task ExportAsync_NoMatchingTransactions_ReturnsEmptyList()
    {
        var rows = await _service.ExportAsync(
            from: new DateOnly(1900, 1, 1),
            to:   new DateOnly(1900, 12, 31));

        rows.Should().BeEmpty();
    }
}
