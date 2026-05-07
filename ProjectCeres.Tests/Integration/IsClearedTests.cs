using FluentAssertions;
using ProjectCeres.Common;
using Microsoft.EntityFrameworkCore;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for IsCleared behaviour on Transaction and Transfer.
///
/// Seed data used:
///   AccountTypeId 1 = Asset, CurrencyId 1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary (Income)
/// </summary>
[Collection("IntegrationTests")]
public class IsClearedTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId = new("20000000-0000-0000-0000-000000000002");

    private readonly TestDbFixture _fixture = new();
    private TransactionService _txService = null!;
    private TransferService _trService = null!;
    private AccountService _accountService = null!;
    private Guid _accountId;
    private Guid _account2Id;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db, new SingleUserAccessor());
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, _accountService, new SingleUserAccessor());
        var attachmentService = new Mock<IFileAttachmentService>().Object;
        _txService = new TransactionService(_fixture.Db, _accountService, liabilityPaymentService, attachmentService, new SingleUserAccessor());
        _trService = new TransferService(_fixture.Db, _accountService, new SingleUserAccessor());

        var a1 = new Account { Id = Guid.NewGuid(), Name = $"IsCleared A1 {Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var a2 = new Account { Id = Guid.NewGuid(), Name = $"IsCleared A2 {Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        _fixture.Db.Accounts.AddRange(a1, a2);
        await _fixture.Db.SaveChangesAsync();
        _accountId  = a1.Id;
        _account2Id = a2.Id;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Transaction — new records default to IsCleared = false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateTransaction_DefaultsIsCleared_ToFalse()
    {
        var id = await _txService.CreateAsync(new TransactionCreateViewModel
        {
            TransactionType = "Regular",
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 50m,
            AccountId       = _accountId,
            CategoryId      = SalaryCategoryId
        });

        var saved = await _fixture.Db.Transactions.FindAsync(id);
        saved!.IsCleared.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Transaction — MarkClearedAsync sets IsCleared = true
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarkClearedAsync_Transaction_SetsIsClearedTrue()
    {
        var tx = new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            AccountId  = _accountId,
            CategoryId = SalaryCategoryId,
            IsCleared  = false,
            CreatedAt  = DateTime.UtcNow
        };
        _fixture.Db.Transactions.Add(tx);
        await _fixture.Db.SaveChangesAsync();

        await _txService.MarkClearedAsync(tx.Id, cleared: true);

        var reloaded = await _fixture.Db.Transactions.FindAsync(tx.Id);
        reloaded!.IsCleared.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Transaction — MarkClearedAsync can toggle back to false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarkClearedAsync_Transaction_CanToggleBackToFalse()
    {
        var tx = new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            AccountId  = _accountId,
            CategoryId = SalaryCategoryId,
            IsCleared  = true,
            CreatedAt  = DateTime.UtcNow
        };
        _fixture.Db.Transactions.Add(tx);
        await _fixture.Db.SaveChangesAsync();

        await _txService.MarkClearedAsync(tx.Id, cleared: false);

        var reloaded = await _fixture.Db.Transactions.FindAsync(tx.Id);
        reloaded!.IsCleared.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Transaction — BulkMarkClearedAsync marks all in range
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BulkMarkClearedAsync_MarksAllTransactionsInDateRange()
    {
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var inRange1 = new Transaction { Id = Guid.NewGuid(), Date = today,             Amount = 10m, AccountId = _accountId, CategoryId = SalaryCategoryId, IsCleared = false, CreatedAt = DateTime.UtcNow };
        var inRange2 = new Transaction { Id = Guid.NewGuid(), Date = today.AddDays(-1), Amount = 20m, AccountId = _accountId, CategoryId = SalaryCategoryId, IsCleared = false, CreatedAt = DateTime.UtcNow };
        var outRange  = new Transaction { Id = Guid.NewGuid(), Date = today.AddDays(-10), Amount = 30m, AccountId = _accountId, CategoryId = SalaryCategoryId, IsCleared = false, CreatedAt = DateTime.UtcNow };
        _fixture.Db.Transactions.AddRange(inRange1, inRange2, outRange);
        await _fixture.Db.SaveChangesAsync();

        await _txService.BulkMarkClearedAsync(from: today.AddDays(-2), to: today, accountId: _accountId);

        // Clear change tracker to force reload from database after bulk update
        _fixture.Db.ChangeTracker.Clear();

        var r1 = await _fixture.Db.Transactions.FindAsync(inRange1.Id);
        var r2 = await _fixture.Db.Transactions.FindAsync(inRange2.Id);
        var r3 = await _fixture.Db.Transactions.FindAsync(outRange.Id);

        r1!.IsCleared.Should().BeTrue();
        r2!.IsCleared.Should().BeTrue();
        r3!.IsCleared.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Transfer — new records default to IsCleared = false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateTransfer_DefaultsIsCleared_ToFalse()
    {
        var transfer = await _trService.CreateAsync(new TransferCreateViewModel
        {
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 200m,
            SourceAccountId = _accountId,
            DestAccountId   = _account2Id
        });

        var saved = await _fixture.Db.Transfers.FindAsync(transfer.Id);
        saved!.IsCleared.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateTransfer_PersistsIsClearedFromVm(bool isCleared)
    {
        var transfer = await _trService.CreateAsync(new TransferCreateViewModel
        {
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 200m,
            SourceAccountId = _accountId,
            DestAccountId   = _account2Id,
            IsCleared       = isCleared
        });

        var saved = await _fixture.Db.Transfers.FindAsync(transfer.Id);
        saved!.IsCleared.Should().Be(isCleared);
    }

    // -------------------------------------------------------------------------
    // Transfer — MarkClearedAsync sets IsCleared = true
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarkClearedAsync_Transfer_SetsIsClearedTrue()
    {
        var transfer = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 150m,
            SourceAccountId = _accountId,
            DestAccountId   = _account2Id,
            IsCleared       = false,
            CreatedAt       = DateTime.UtcNow
        };
        _fixture.Db.Transfers.Add(transfer);
        await _fixture.Db.SaveChangesAsync();

        await _trService.MarkClearedAsync(transfer.Id, cleared: true);

        var reloaded = await _fixture.Db.Transfers.FindAsync(transfer.Id);
        reloaded!.IsCleared.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Transfer — MarkClearedAsync can toggle back to false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarkClearedAsync_Transfer_CanToggleBackToFalse()
    {
        var transfer = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 150m,
            SourceAccountId = _accountId,
            DestAccountId   = _account2Id,
            IsCleared       = true,
            CreatedAt       = DateTime.UtcNow
        };
        _fixture.Db.Transfers.Add(transfer);
        await _fixture.Db.SaveChangesAsync();

        await _trService.MarkClearedAsync(transfer.Id, cleared: false);

        var reloaded = await _fixture.Db.Transfers.FindAsync(transfer.Id);
        reloaded!.IsCleared.Should().BeFalse();
    }
}
