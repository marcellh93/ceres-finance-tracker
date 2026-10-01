using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Unit tests for AccountBalanceCalculator's pure functions — no database needed.
/// Supersedes BalanceCalculationTests.cs, which tested the same formula via a
/// hand-copied local helper rather than the real production code.
/// </summary>
public class AccountBalanceCalculatorTests
{
    private static Category IncomeCategory(bool isSystem = false) => new()
    {
        Id = Guid.NewGuid(),
        IsSystem = isSystem,
        CategoryType = new CategoryType { Id = 1, Name = "Income" }
    };

    private static Category ExpenseCategory() => new()
    {
        Id = Guid.NewGuid(),
        IsSystem = false,
        CategoryType = new CategoryType { Id = 2, Name = "Expense" }
    };

    private static Transaction Tx(decimal amount, Category category) => new()
    {
        Id = Guid.NewGuid(),
        Amount = amount,
        Category = category
    };

    private static Account AssetAccount(Guid id) => new()
    {
        Id = id,
        AccountType = new AccountType { Id = 1, Name = "Asset" }
    };

    private static Account LiabilityAccount(Guid id) => new()
    {
        Id = id,
        AccountType = new AccountType { Id = 2, Name = "Liability" }
    };

    // -------------------------------------------------------------------------
    // SignedAmount
    // -------------------------------------------------------------------------

    [Fact]
    public void SignedAmount_AssetAccount_IncomeAdds()
    {
        var tx = Tx(100m, IncomeCategory());
        AccountBalanceCalculator.SignedAmount(tx, isLiability: false).Should().Be(100m);
    }

    [Fact]
    public void SignedAmount_AssetAccount_ExpenseSubtracts()
    {
        var tx = Tx(100m, ExpenseCategory());
        AccountBalanceCalculator.SignedAmount(tx, isLiability: false).Should().Be(-100m);
    }

    [Fact]
    public void SignedAmount_LiabilityAccount_ExpenseAdds()
    {
        var tx = Tx(100m, ExpenseCategory());
        AccountBalanceCalculator.SignedAmount(tx, isLiability: true).Should().Be(100m);
    }

    [Fact]
    public void SignedAmount_LiabilityAccount_IncomeSubtracts()
    {
        var tx = Tx(100m, IncomeCategory());
        AccountBalanceCalculator.SignedAmount(tx, isLiability: true).Should().Be(-100m);
    }

    [Fact]
    public void SignedAmount_SystemCategory_AlwaysAdds_RegardlessOfAccountType()
    {
        var tx = Tx(500m, IncomeCategory(isSystem: true));
        AccountBalanceCalculator.SignedAmount(tx, isLiability: false).Should().Be(500m);
        AccountBalanceCalculator.SignedAmount(tx, isLiability: true).Should().Be(500m);
    }

    // -------------------------------------------------------------------------
    // ComputeBalance
    // -------------------------------------------------------------------------

    [Fact]
    public void ComputeBalance_AssetAccount_SumsTransactionsOnly_WhenNoTransfersOrPayments()
    {
        var accountId = Guid.NewGuid();
        var account = AssetAccount(accountId);
        var transactions = new[] { Tx(1000m, IncomeCategory()), Tx(300m, ExpenseCategory()) };

        var balance = AccountBalanceCalculator.ComputeBalance(
            account, transactions, transfers: [], liabilityPayments: []);

        balance.Should().Be(700m);
    }

    [Fact]
    public void ComputeBalance_LiabilityFundedEntirelyByATransfer_IncludesTheTransfer()
    {
        // The exact bug class this stage exists to eliminate: a liability account
        // whose entire balance comes from a Transfer (never a Transaction) must not
        // be silently treated as zero.
        var assetId = Guid.NewGuid();
        var liabilityId = Guid.NewGuid();
        var liability = LiabilityAccount(liabilityId);
        var transfer = new Transfer { Id = Guid.NewGuid(), SourceAccountId = assetId, DestAccountId = liabilityId, Amount = 608.03m };

        var balance = AccountBalanceCalculator.ComputeBalance(
            liability, transactions: [], transfers: [transfer], liabilityPayments: []);

        balance.Should().Be(608.03m, "the transferred-in amount is the liability's ENTIRE balance — it must not be zero");
    }

    [Fact]
    public void ComputeBalance_AssetFundingATransfer_SubtractsTheTransfer()
    {
        var assetId = Guid.NewGuid();
        var liabilityId = Guid.NewGuid();
        var asset = AssetAccount(assetId);
        var transfer = new Transfer { Id = Guid.NewGuid(), SourceAccountId = assetId, DestAccountId = liabilityId, Amount = 608.03m };

        var balance = AccountBalanceCalculator.ComputeBalance(
            asset, transactions: [Tx(1000m, IncomeCategory())], transfers: [transfer], liabilityPayments: []);

        balance.Should().Be(1000m - 608.03m);
    }

    [Fact]
    public void ComputeBalance_LiabilityPayment_ReducesBothLegs()
    {
        var assetId = Guid.NewGuid();
        var liabilityId = Guid.NewGuid();
        var payment = new LiabilityPayment { Id = Guid.NewGuid(), AssetAccountId = assetId, LiabilityAccountId = liabilityId, Amount = 200m };

        var assetBalance = AccountBalanceCalculator.ComputeBalance(
            AssetAccount(assetId), transactions: [Tx(1000m, IncomeCategory())], transfers: [], liabilityPayments: [payment]);
        var liabilityBalance = AccountBalanceCalculator.ComputeBalance(
            LiabilityAccount(liabilityId), transactions: [Tx(1000m, ExpenseCategory())], transfers: [], liabilityPayments: [payment]);

        assetBalance.Should().Be(1000m - 200m, "a payment leaving the asset account reduces its balance");
        liabilityBalance.Should().Be(1000m - 200m, "a payment against the liability reduces the debt owed");
    }

    [Fact]
    public void ComputeBalance_RowsTouchingOtherAccounts_AreIgnored()
    {
        // ComputeBalance must only react to rows whose Source/Dest or Asset/Liability
        // id matches the account passed in — a row between two OTHER accounts must
        // not affect this one, even if it's in the same input collection.
        var thisAccountId = Guid.NewGuid();
        var otherAccountA = Guid.NewGuid();
        var otherAccountB = Guid.NewGuid();
        var account = AssetAccount(thisAccountId);
        var unrelatedTransfer = new Transfer { Id = Guid.NewGuid(), SourceAccountId = otherAccountA, DestAccountId = otherAccountB, Amount = 999m };
        var unrelatedPayment = new LiabilityPayment { Id = Guid.NewGuid(), AssetAccountId = otherAccountA, LiabilityAccountId = otherAccountB, Amount = 999m };

        var balance = AccountBalanceCalculator.ComputeBalance(
            account, transactions: [Tx(100m, IncomeCategory())], transfers: [unrelatedTransfer], liabilityPayments: [unrelatedPayment]);

        balance.Should().Be(100m);
    }
}
