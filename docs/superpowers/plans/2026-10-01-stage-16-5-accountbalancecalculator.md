# AccountBalanceCalculator Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace 8 independently-duplicated account-balance/net-worth computations with calls to one shared, pure, static calculator.

**Architecture:** A new static class `AccountBalanceCalculator` exposes two pure functions — `SignedAmount` (the sign of one transaction) and `ComputeBalance` (an account's full balance from pre-loaded Transactions/Transfers/LiabilityPayments). It owns no database access, no DI registration, and no account-selection logic — callers query and filter however their own purpose requires, then pass the resulting in-memory collections in. All 8 call sites are migrated to call it, one at a time, each verified against its own existing (or newly-written) tests before moving to the next.

**Tech Stack:** .NET 10, EF Core (Npgsql), xUnit + FluentAssertions, the project's `TestDbFixture`/`FakeCurrentUserAccessor` integration-test harness.

**Spec:** `docs/superpowers/specs/2026-10-01-stage-16-5-accountbalancecalculator-design.md`

## Global Constraints

- `AccountBalanceCalculator` is a `public static class` with no constructor, no `DbContext` parameter, no `async` methods, and no DI registration in `Program.cs` (spec D1).
- `ComputeBalance` and `SignedAmount` never filter or select which accounts matter — they operate only on data the caller already selected and loaded (spec D4). Do not add an `IQueryable` overload or a default account filter.
- `ComputeBalance` takes the *raw*, unsplit `IEnumerable<Transfer>`/`IEnumerable<LiabilityPayment>` rows touching one account (both directions mixed together) and splits them internally by `SourceAccountId`/`DestAccountId` and `AssetAccountId`/`LiabilityAccountId` (spec D2).
- File location: `ProjectCeres/Services/AccountBalanceCalculator.cs`, flat — not under `Services/Reports/`, not a new `Services/Accounts/` subfolder (spec D5).
- Do not change any site's account-selection logic, output shape, or external behavior beyond swapping its inline arithmetic for calculator calls (spec Non-goals). This is a refactor; a test whose asserted value changes as a result of this plan's edits (other than test-setup mechanics) is a bug in the task, not an accepted side effect.
- Keep all 5 existing per-site Transfer-regression tests — do not delete them (spec D7). Only `ProjectCeres.Tests/Unit/BalanceCalculationTests.cs` is deleted (spec D8).
- Per `docs/testing.md` § Rules: before editing any test file, state which of the three cases applies (new test / contract intentionally changed / expected value was wrong). Every test edit in this plan is case "new test" (pinning current behavior before refactor) or "contract intentionally changed" (test-setup mechanics only, e.g. a helper call replacing inline arithmetic) — never "expected value was wrong," since this plan changes no externally-observable behavior.
- Run the full `dotnet build` + full `dotnet test` once, at the end of Task 9 — not after every single-site migration task (project convention; avoids the full-suite DB reseed cost documented in `docs/testing.md`). Each individual task instead runs only its own affected test file(s) via `dotnet test --filter`.
- No `Co-Authored-By` trailer in any commit (CLAUDE.md).

---

## Task 1: Create `AccountBalanceCalculator` + its unit test file

**Files:**
- Create: `ProjectCeres/Services/AccountBalanceCalculator.cs`
- Create: `ProjectCeres.Tests/Unit/AccountBalanceCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `public static decimal SignedAmount(Transaction transaction, bool isLiability)`
  - `public static decimal ComputeBalance(Account account, IEnumerable<Transaction> transactions, IEnumerable<Transfer> transfers, IEnumerable<LiabilityPayment> liabilityPayments)` — `transfers` and `liabilityPayments` are the full set of rows touching `account.Id` in EITHER direction; the method splits them internally.

The exact formula to port (from `ProjectCeres/Services/AccountService.cs:80-140`, the reference implementation every other site's existing fix comments cite):

```csharp
// Sign logic (from AccountService.GetBalanceAsync lines 102-108):
//   System categories (e.g. Opening Balance) are a neutral starting point — always add.
//   Assets:      income adds, expense subtracts.
//   Liabilities: expense adds (increases what you owe), income subtracts (e.g. refund).
if (t.Category.IsSystem) return t.Amount;
bool isIncome = t.Category.CategoryType.Name == "Income";
bool addsToBalance = isLiability ? !isIncome : isIncome;
return addsToBalance ? t.Amount : -t.Amount;

// Liability payments reduce the balance on both sides (lines 110-124):
//   Asset account:     money leaves  → subtract the payment amount
//   Liability account: debt reduces  → subtract the payment amount
balance -= paymentsOut;  // where AssetAccountId == account.Id
balance -= paymentsIn;   // where LiabilityAccountId == account.Id

// Transfers (lines 126-137):
balance += transfersIn;   // where DestAccountId == account.Id
balance -= transfersOut;  // where SourceAccountId == account.Id
```

- [ ] **Step 1: Write `AccountBalanceCalculator.cs`**

```csharp
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Shared account-balance arithmetic. Pure functions over pre-loaded data —
/// no DbContext, no account-selection logic. Callers query and filter
/// however their own purpose requires (single account, all active accounts,
/// Asset-only, per-currency-grouped, etc.) and pass the resulting
/// collections in. See docs/superpowers/specs/2026-10-01-stage-16-5-accountbalancecalculator-design.md.
/// </summary>
public static class AccountBalanceCalculator
{
    /// <summary>
    /// The signed contribution of one transaction to its account's balance.
    /// System categories (e.g. Opening Balance) are a neutral starting point
    /// and always add. For regular transactions: on an asset account, income
    /// adds and expense subtracts; on a liability account, expense adds
    /// (increases what you owe) and income subtracts (e.g. a refund).
    /// </summary>
    public static decimal SignedAmount(Transaction transaction, bool isLiability)
    {
        if (transaction.Category.IsSystem) return transaction.Amount;
        bool isIncome = transaction.Category.CategoryType.Name == "Income";
        bool addsToBalance = isLiability ? !isIncome : isIncome;
        return addsToBalance ? transaction.Amount : -transaction.Amount;
    }

    /// <summary>
    /// An account's full balance: summed Transactions (via <see cref="SignedAmount"/>),
    /// minus LiabilityPayments on either leg, plus/minus Transfers in either direction.
    /// <paramref name="transfers"/> and <paramref name="liabilityPayments"/> must contain
    /// every row touching <paramref name="account"/> in EITHER direction — this method
    /// splits them internally by SourceAccountId/DestAccountId and
    /// AssetAccountId/LiabilityAccountId.
    /// </summary>
    public static decimal ComputeBalance(
        Account account,
        IEnumerable<Transaction> transactions,
        IEnumerable<Transfer> transfers,
        IEnumerable<LiabilityPayment> liabilityPayments)
    {
        bool isLiability = account.AccountType.Name == "Liability";

        decimal balance = transactions.Sum(t => SignedAmount(t, isLiability));

        foreach (var payment in liabilityPayments)
        {
            if (payment.AssetAccountId == account.Id) balance -= payment.Amount;
            if (payment.LiabilityAccountId == account.Id) balance -= payment.Amount;
        }

        foreach (var transfer in transfers)
        {
            if (transfer.DestAccountId == account.Id) balance += transfer.Amount;
            if (transfer.SourceAccountId == account.Id) balance -= transfer.Amount;
        }

        return balance;
    }
}
```

- [ ] **Step 2: Write the unit test file**

This is case "new test" per `docs/testing.md` § Rules — pinning the formula's behavior before any call site is migrated to use it.

```csharp
using FluentAssertions;
using ProjectCeres.Models;

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
```

- [ ] **Step 3: Run the new tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AccountBalanceCalculatorTests"`
Expected: 9 passed, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Services/AccountBalanceCalculator.cs ProjectCeres.Tests/Unit/AccountBalanceCalculatorTests.cs
git commit -m "feat(16.5): add AccountBalanceCalculator, no call sites migrated yet

Pure static functions (SignedAmount, ComputeBalance) over pre-loaded
data -- no DbContext, no account-selection logic. 9 unit tests pin the
formula directly, independent of any database or call site."
```

---

## Task 2: Migrate `AccountService.GetBalanceAsync` + create its test file

**Files:**
- Modify: `ProjectCeres/Services/AccountService.cs:80-140`
- Create: `ProjectCeres.Tests/Integration/AccountServiceTests.cs`

**Interfaces:**
- Consumes: `AccountBalanceCalculator.ComputeBalance` (Task 1).

No test file currently exists for `GetBalanceAsync` or `GetLedgerAsync` (verified: `grep -rln "GetBalanceAsync|GetLedgerAsync" ProjectCeres.Tests/` finds only a comment mention in `ReportServiceTests.cs` and the soon-to-be-deleted `BalanceCalculationTests.cs`, neither of which calls the real method). This task creates that file and writes a pinning test *before* changing the production code, per TDD.

- [ ] **Step 1: Write the failing test — pin current `GetBalanceAsync` behavior**

Case "new test" per `docs/testing.md` § Rules.

```csharp
using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for AccountService.GetBalanceAsync/GetLedgerAsync against the
/// real project_ceres_test database. Each test rolls back its transaction — no test
/// data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   AccountTypeId 2 = Liability
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary  (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
/// </summary>
[Collection("TestDbFixtureTests")]
public class AccountServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private AccountService _service = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _service = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateAssetAccountAsync(decimal openingBalance = 0m) =>
        (await _service.TryCreateAsync(new CreateAccountRequest(
            Name: $"Asset {Guid.NewGuid():N}",
            AccountTypeId: 1,
            CurrencyId: 1,
            Description: null,
            OpeningBalance: openingBalance,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    private async Task<Guid> CreateLiabilityAccountAsync(decimal openingBalance = 0m) =>
        (await _service.TryCreateAsync(new CreateAccountRequest(
            Name: $"Liability {Guid.NewGuid():N}",
            AccountTypeId: 2,
            CurrencyId: 1,
            Description: null,
            OpeningBalance: openingBalance,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    private void AddTransaction(Guid accountId, Guid categoryId, decimal amount)
    {
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today),
            Amount = amount, AccountId = accountId, CategoryId = categoryId, CreatedAt = DateTime.UtcNow
        });
    }

    private void AddTransfer(Guid sourceAccountId, Guid destAccountId, decimal amount)
    {
        _fixture.Db.Transfers.Add(new Transfer
        {
            Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today),
            Amount = amount, SourceAccountId = sourceAccountId, DestAccountId = destAccountId, CreatedAt = DateTime.UtcNow
        });
    }

    private void AddLiabilityPayment(Guid assetAccountId, Guid liabilityAccountId, decimal amount)
    {
        _fixture.Db.LiabilityPayments.Add(new LiabilityPayment
        {
            Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today),
            Amount = amount, AssetAccountId = assetAccountId, LiabilityAccountId = liabilityAccountId, CreatedAt = DateTime.UtcNow
        });
    }

    // -------------------------------------------------------------------------
    // GetBalanceAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBalanceAsync_SumsTransactionsBySignedCategory()
    {
        var accountId = await CreateAssetAccountAsync();
        AddTransaction(accountId, SalaryCategoryId, 2000m);
        AddTransaction(accountId, HousingCategoryId, 500m);
        await _fixture.Db.SaveChangesAsync();

        (await _service.GetBalanceAsync(accountId)).Should().Be(1500m);
    }

    [Fact]
    public async Task GetBalanceAsync_LiabilityFundedEntirelyByATransfer_IncludesTheTransfer()
    {
        var assetId = await CreateAssetAccountAsync(openingBalance: 1000m);
        var liabilityId = await CreateLiabilityAccountAsync(openingBalance: 0m);
        AddTransfer(assetId, liabilityId, 608.03m);
        await _fixture.Db.SaveChangesAsync();

        (await _service.GetBalanceAsync(liabilityId)).Should().Be(608.03m,
            "the transferred-in amount is the liability's ENTIRE balance — it must not be zero");
    }

    [Fact]
    public async Task GetBalanceAsync_LiabilityPayment_ReducesBothLegs()
    {
        var assetId = await CreateAssetAccountAsync(openingBalance: 1000m);
        var liabilityId = await CreateLiabilityAccountAsync(openingBalance: 500m);
        AddLiabilityPayment(assetId, liabilityId, 200m);
        await _fixture.Db.SaveChangesAsync();

        (await _service.GetBalanceAsync(assetId)).Should().Be(800m);
        (await _service.GetBalanceAsync(liabilityId)).Should().Be(300m);
    }

    [Fact]
    public async Task GetBalanceAsync_UnknownAccountId_ReturnsZero()
    {
        (await _service.GetBalanceAsync(Guid.NewGuid())).Should().Be(0m);
    }
}
```

- [ ] **Step 2: Run the tests to verify they pass against the CURRENT (pre-migration) implementation**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AccountServiceTests"`
Expected: 4 passed, 0 failed. (These pin the existing, already-correct behavior — they must pass BEFORE the migration, proving the migration changes nothing observable.)

- [ ] **Step 3: Commit the test file on its own**

```bash
git add ProjectCeres.Tests/Integration/AccountServiceTests.cs
git commit -m "test(16.5): pin AccountService.GetBalanceAsync's current behavior

No test file existed for GetBalanceAsync/GetLedgerAsync before this.
Pinning current (already-correct) behavior before migrating it to call
the new AccountBalanceCalculator, so the migration's test run proves
no observable change."
```

- [ ] **Step 4: Migrate `GetBalanceAsync` to call the calculator**

In `ProjectCeres/Services/AccountService.cs`, replace lines 96-139 (everything from `bool isLiability = ...` through `return balance;`) with:

```csharp
        var transfers = await db.Transfers
            .Owned(user)
            .Where(t => t.SourceAccountId == id || t.DestAccountId == id)
            .ToListAsync();

        var liabilityPayments = await db.LiabilityPayments
            .Owned(user)
            .Where(p => p.AssetAccountId == id || p.LiabilityAccountId == id)
            .ToListAsync();

        return AccountBalanceCalculator.ComputeBalance(account, transactions, transfers, liabilityPayments);
```

This collapses the four separate `LiabilityPayments`/`Transfers` queries (lines 113-134 of the original) into two queries that each fetch both directions in one round trip — `ComputeBalance` does the direction split that used to live in four separate `.Where(...).SumAsync(...)` calls.

- [ ] **Step 5: Run the pinning tests again to verify the migration changed nothing observable**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AccountServiceTests"`
Expected: 4 passed, 0 failed — identical result to Step 2, now against the migrated implementation.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/AccountService.cs
git commit -m "refactor(16.5): migrate GetBalanceAsync to AccountBalanceCalculator

Site 1 of 8. Reference implementation migrated first, to prove the
calculator reproduces it exactly. Also collapses 4 separate
LiabilityPayments/Transfers queries into 2 (one per table, both
directions), since ComputeBalance does the direction split internally."
```

---

## Task 3: Migrate `AccountService.GetLedgerAsync`

**Files:**
- Modify: `ProjectCeres/Services/AccountService.cs:142-270`
- Modify: `ProjectCeres.Tests/Integration/AccountServiceTests.cs`

**Interfaces:**
- Consumes: `AccountBalanceCalculator.SignedAmount` (Task 1).

- [ ] **Step 1: Add a failing pinning test for `GetLedgerAsync`**

Case "new test." Append to `AccountServiceTests.cs`:

```csharp
    // -------------------------------------------------------------------------
    // GetLedgerAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetLedgerAsync_ComputesRunningBalanceAcrossTransactionsTransfersAndPayments()
    {
        var assetId = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();
        AddTransaction(assetId, SalaryCategoryId, 1000m);
        AddTransfer(assetId, liabilityId, 300m);
        AddLiabilityPayment(assetId, liabilityId, 50m);
        await _fixture.Db.SaveChangesAsync();

        var ledger = await _service.GetLedgerAsync(assetId);

        ledger.Should().NotBeNull();
        ledger!.Entries.Should().HaveCount(3);
        ledger.Entries.Sum(e => e.SignedAmount).Should().Be(1000m - 300m - 50m);
        ledger.Entries.OrderBy(e => e.CreatedAt).Last().RunningBalance.Should().Be(1000m - 300m - 50m);
    }

    [Fact]
    public async Task GetLedgerAsync_UnknownAccountId_ReturnsNull()
    {
        (await _service.GetLedgerAsync(Guid.NewGuid())).Should().BeNull();
    }
```

- [ ] **Step 2: Run to verify both pass against the current implementation**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~GetLedgerAsync"`
Expected: 2 passed, 0 failed.

- [ ] **Step 3: Commit the test additions on their own**

```bash
git add ProjectCeres.Tests/Integration/AccountServiceTests.cs
git commit -m "test(16.5): pin AccountService.GetLedgerAsync's current behavior"
```

- [ ] **Step 4: Migrate `GetLedgerAsync`'s per-row sign logic**

In `ProjectCeres/Services/AccountService.cs`, inside the `txEntries` projection (the block starting at the original line 161, `var txEntries = transactions.Select(t => ...)`), replace the inline sign computation:

```csharp
            else
            {
                bool isIncome      = t.Category.CategoryType.Name == "Income";
                bool addsToBalance = isLiability ? !isIncome : isIncome;
                signed    = addsToBalance ? t.Amount : -t.Amount;
                entryType = "Transaction";
            }
```

with:

```csharp
            else
            {
                signed    = AccountBalanceCalculator.SignedAmount(t, isLiability);
                entryType = "Transaction";
            }
```

The `t.Category.IsSystem` branch above this `else` is unchanged (it already matches `SignedAmount`'s own `IsSystem` short-circuit, so `signed = t.Amount` for system categories stays as-is — only the regular-transaction branch is replaced).

- [ ] **Step 5: Run the ledger tests again to verify no observable change**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~GetLedgerAsync"`
Expected: 2 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/AccountService.cs
git commit -m "refactor(16.5): migrate GetLedgerAsync to AccountBalanceCalculator.SignedAmount

Site 2 of 8. Per-row sign logic now calls the same SignedAmount that
ComputeBalance uses internally, instead of a third hand-copied version
of the same formula."
```

---

## Task 4: Migrate `DashboardService.GetRunwayAsync`

**Files:**
- Modify: `ProjectCeres/Services/DashboardService.cs:350-437`

**Interfaces:**
- Consumes: `AccountBalanceCalculator.ComputeBalance` (Task 1).

Existing test coverage: `ProjectCeres.Tests/Integration/DashboardServiceTests.cs` — `GetHealthSnapshotAsync_Runway_ReturnsCorrectValue_GivenKnownExpensesAndNetWorth`, `GetHealthSnapshotAsync_Runway_ReturnsNull_WhenNoExpensesInLast6Months`, `GetHealthSnapshotAsync_Runway_IncludesLiabilityBalanceFundedEntirelyByATransfer` (added commit `352e3af7`). No new test needed — this migration must not change any of their asserted values.

- [ ] **Step 1: Run the existing runway tests to confirm the current (post-2026-10-01-fix) baseline**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~GetHealthSnapshotAsync_Runway"`
Expected: 3 passed, 0 failed.

- [ ] **Step 2: Migrate the per-account loop**

In `ProjectCeres/Services/DashboardService.cs`, inside `GetRunwayAsync`, replace the `foreach (var account in accounts)` block's balance computation:

```csharp
        foreach (var account in accounts)
        {
            bool isLiability = account.AccountType.Name == "Liability";

            var balance = account.Transactions.Sum(t =>
            {
                if (t.Category.IsSystem) return t.Amount;
                bool isIncome    = t.Category.CategoryType.Name == "Income";
                bool addsBalance = isLiability ? !isIncome : isIncome;
                return addsBalance ? t.Amount : -t.Amount;
            });

            balance -= paymentsByAsset.GetValueOrDefault(account.Id);
            balance -= paymentsByLiability.GetValueOrDefault(account.Id);

            balance += transfersInByAccount.GetValueOrDefault(account.Id);
            balance -= transfersOutByAccount.GetValueOrDefault(account.Id);

            if (!isLiability)
                totalAssets += balance;
            else
                totalLiabilities += balance;
        }
```

with:

```csharp
        foreach (var account in accounts)
        {
            bool isLiability = account.AccountType.Name == "Liability";

            var accountTransfers = transfers.Where(t => t.SourceAccountId == account.Id || t.DestAccountId == account.Id);
            var accountPayments  = liabilityPayments.Where(p => p.AssetAccountId == account.Id || p.LiabilityAccountId == account.Id);
            var balance = AccountBalanceCalculator.ComputeBalance(account, account.Transactions, accountTransfers, accountPayments);

            if (!isLiability)
                totalAssets += balance;
            else
                totalLiabilities += balance;
        }
```

This also removes the now-unused `paymentsByAsset`/`paymentsByLiability`/`transfersInByAccount`/`transfersOutByAccount` dictionary construction above the loop (added 2026-10-01, commit `352e3af7`) — delete those four `GroupBy`/`ToDictionary` statements, since `ComputeBalance` now does the per-account filtering directly from the flat `transfers`/`liabilityPayments` lists.

- [ ] **Step 3: Run the runway tests again to verify no observable change**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~GetHealthSnapshotAsync_Runway"`
Expected: 3 passed, 0 failed — identical result to Step 1.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Services/DashboardService.cs
git commit -m "refactor(16.5): migrate GetRunwayAsync to AccountBalanceCalculator

Site 3 of 8. Also removes the per-account dictionary construction added
2026-10-01 (352e3af7) -- ComputeBalance filters the flat transfer/payment
lists per account internally, so the dictionaries are no longer needed."
```

---

## Task 5: Migrate `DashboardService.GetSpendableBalanceAsync`

**Files:**
- Modify: `ProjectCeres/Services/DashboardService.cs:155-297`

**Interfaces:**
- Consumes: `AccountBalanceCalculator.ComputeBalance` (Task 1).

Existing test coverage: `DashboardServiceTests.cs` has multiple `GetHealthSnapshotAsync_AvailableToday_*`/`GetHealthSnapshotAsync_SafeToSpend_*` tests exercising this method (including `GetHealthSnapshotAsync_AvailableToday_ExcludesTransfersOutToExcludedAccounts`, confirmed by research to already cover Transfer handling here). No new test needed.

- [ ] **Step 1: Run the existing spendable-balance tests to confirm the baseline**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~GetHealthSnapshotAsync_AvailableToday|FullyQualifiedName~GetHealthSnapshotAsync_SafeToSpend"`
Expected: all passing (exact count depends on current test file state — record the count, it must be identical after migration).

- [ ] **Step 2: Add the missing `AccountType` include**

`ComputeBalance` reads `account.AccountType.Name` to decide the liability sign flip. This method's account query filters on `a.AccountType.Name == "Asset"` directly in SQL (translated to a join) but never `.Include()`s the navigation property, so the returned entities currently have `AccountType == null`. In `ProjectCeres/Services/DashboardService.cs`, inside `GetSpendableBalanceAsync`, replace:

```csharp
        // Load active, non-excluded asset accounts with their transactions and category types.
        var accounts = await db.Accounts
            .Owned(user)
            .Where(a => a.IsActive
                     && a.CurrencyId == currencyId
                     && a.AccountType.Name == "Asset"
                     && !a.ExcludeFromSpendable)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
            .ToListAsync();
```

with:

```csharp
        // Load active, non-excluded asset accounts with their transactions and category types.
        var accounts = await db.Accounts
            .Owned(user)
            .Where(a => a.IsActive
                     && a.CurrencyId == currencyId
                     && a.AccountType.Name == "Asset"
                     && !a.ExcludeFromSpendable)
            .Include(a => a.AccountType)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
            .ToListAsync();
```

- [ ] **Step 3: Migrate the per-account loop**

Replace:

```csharp
        // Derive liquid balance per account (same sign logic as AccountService).
        decimal liquid = 0m;
        foreach (var account in accounts)
        {
            liquid += account.Transactions.Sum(t =>
            {
                if (t.Category.IsSystem) return t.Amount;
                bool isIncome = t.Category.CategoryType.Name == "Income";
                return isIncome ? t.Amount : -t.Amount;
            });
        }

        // Subtract liability payments that source from these asset accounts.
        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var liabilityPayments = await db.LiabilityPayments
            .Owned(user)
            .AsNoTracking()
            .Where(p => accountIds.Contains(p.AssetAccountId))
            .ToListAsync();
        liquid -= liabilityPayments.Sum(p => p.Amount);

        // Include transfers (cross-boundary transfers must be counted to match displayed balances).
        var transfersIn = await db.Transfers
            .Owned(user)
            .Where(t => accountIds.Contains(t.DestAccountId))
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var transfersOut = await db.Transfers
            .Owned(user)
            .Where(t => accountIds.Contains(t.SourceAccountId))
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        liquid += transfersIn;
        liquid -= transfersOut;
```

with:

```csharp
        // These accounts are all Asset accounts (filtered above), so isLiability is
        // always false here — ComputeBalance's liability-leg handling is a no-op for
        // every row in this loop, which matches this method's pre-existing behavior of
        // only subtracting LiabilityPayments on the ASSET side (never crediting the
        // liability side back into "liquid cash").
        var accountIds = accounts.Select(a => a.Id).ToHashSet();

        var liabilityPayments = await db.LiabilityPayments
            .Owned(user)
            .AsNoTracking()
            .Where(p => accountIds.Contains(p.AssetAccountId))
            .ToListAsync();

        var transfers = await db.Transfers
            .Owned(user)
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.SourceAccountId) || accountIds.Contains(t.DestAccountId))
            .ToListAsync();

        decimal liquid = 0m;
        foreach (var account in accounts)
        {
            var accountTransfers = transfers.Where(t => t.SourceAccountId == account.Id || t.DestAccountId == account.Id);
            var accountPayments  = liabilityPayments.Where(p => p.AssetAccountId == account.Id);
            liquid += AccountBalanceCalculator.ComputeBalance(account, account.Transactions, accountTransfers, accountPayments);
        }
```

**Important — preserve the existing asymmetry exactly:** the original code only ever queries `LiabilityPayments` where `AssetAccountId` is in this account set (never `LiabilityAccountId` — these are Asset-only accounts, so a payment's *liability* leg is never one of them). `ComputeBalance`'s liability-leg check (`payment.LiabilityAccountId == account.Id`) will therefore simply never match for any row in this method's `liabilityPayments` list, since none of those rows have a `LiabilityAccountId` equal to an account in `accountIds` — the migrated code's behavior is identical to the original. Do not broaden the `LiabilityPayments` query to include both legs; that would change this method's output.

- [ ] **Step 5: Run the spendable-balance tests again to verify no observable change**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~GetHealthSnapshotAsync_AvailableToday|FullyQualifiedName~GetHealthSnapshotAsync_SafeToSpend"`
Expected: identical pass count to Step 1.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/DashboardService.cs
git commit -m "refactor(16.5): migrate GetSpendableBalanceAsync to AccountBalanceCalculator

Site 4 of 8. Adds the missing AccountType include this method's query
never had (ComputeBalance reads account.AccountType.Name; the query
filtered on it via a SQL join without materializing the navigation
property). Preserves the existing Asset-only LiabilityPayments query
(only the AssetAccountId leg, never LiabilityAccountId) exactly --
ComputeBalance's liability-leg check is a structural no-op here since
every account in this method's scope is an Asset account."
```

---

## Task 6: Migrate `ReportService.GetNetWorthAsync`

**Files:**
- Modify: `ProjectCeres/Services/ReportService.cs:10-95`

**Interfaces:**
- Consumes: `AccountBalanceCalculator.ComputeBalance` (Task 1).

Existing test coverage: `ProjectCeres.Tests/Integration/ReportServiceTests.cs` — `GetNetWorthAsync_ReturnsEntry_WithCorrectAssetAndLiabilityTotals`, `GetNetWorthAsync_GroupsByCurrency`, `GetNetWorthAsync_IncludesLiabilityBalanceFundedEntirelyByATransfer`.

- [ ] **Step 1: Run the existing net-worth tests to confirm the baseline**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ReportServiceTests.GetNetWorthAsync"`
Expected: 3 passed, 0 failed.

- [ ] **Step 2: Migrate the per-account loop**

In `ProjectCeres/Services/ReportService.cs`, replace:

```csharp
        var paymentsByAsset = liabilityPayments
            .GroupBy(p => p.AssetAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var paymentsByLiability = liabilityPayments
            .GroupBy(p => p.LiabilityAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        // A Transfer moves money between two of the user's own accounts and never
        // touches Transactions — an account funded entirely by a transfer (e.g. an
        // opening-balance transfer into a new liability) has no Transactions to sum
        // above and would otherwise contribute nothing here, matching
        // AccountService.GetBalanceAsync's own transfersIn/transfersOut terms.
        var transfers = await db.Transfers
            .Owned(user)
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.SourceAccountId) || accountIds.Contains(t.DestAccountId))
            .ToListAsync();

        var transfersOutByAccount = transfers
            .GroupBy(t => t.SourceAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var transfersInByAccount = transfers
            .GroupBy(t => t.DestAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var grouped = accounts
            .GroupBy(a => new { a.Currency.Code, a.Currency.Symbol, a.CurrencyId })
            .Select(g =>
            {
                decimal assets = 0;
                decimal liabilities = 0;

                foreach (var account in g)
                {
                    bool isLiability = account.AccountType.Name == "Liability";

                    var balance = account.Transactions.Sum(t =>
                    {
                        if (t.Category.IsSystem) return t.Amount;
                        bool isIncome = t.Category.CategoryType.Name == "Income";
                        bool addsToBalance = isLiability ? !isIncome : isIncome;
                        return addsToBalance ? t.Amount : -t.Amount;
                    });

                    balance -= paymentsByAsset.GetValueOrDefault(account.Id);
                    balance -= paymentsByLiability.GetValueOrDefault(account.Id);

                    balance += transfersInByAccount.GetValueOrDefault(account.Id);
                    balance -= transfersOutByAccount.GetValueOrDefault(account.Id);

                    if (!isLiability)
                        assets += balance;
                    else
                        liabilities += balance;
                }

                return new NetWorthEntry(g.Key.Code, g.Key.Symbol, assets, liabilities, assets - liabilities);
            })
            .ToList();
```

with:

```csharp
        // A Transfer moves money between two of the user's own accounts and never
        // touches Transactions — an account funded entirely by a transfer (e.g. an
        // opening-balance transfer into a new liability) has no Transactions to sum
        // above and would otherwise contribute nothing here, matching
        // AccountService.GetBalanceAsync's own transfersIn/transfersOut terms.
        var transfers = await db.Transfers
            .Owned(user)
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.SourceAccountId) || accountIds.Contains(t.DestAccountId))
            .ToListAsync();

        var grouped = accounts
            .GroupBy(a => new { a.Currency.Code, a.Currency.Symbol, a.CurrencyId })
            .Select(g =>
            {
                decimal assets = 0;
                decimal liabilities = 0;

                foreach (var account in g)
                {
                    var accountTransfers = transfers.Where(t => t.SourceAccountId == account.Id || t.DestAccountId == account.Id);
                    var accountPayments  = liabilityPayments.Where(p => p.AssetAccountId == account.Id || p.LiabilityAccountId == account.Id);
                    var balance = AccountBalanceCalculator.ComputeBalance(account, account.Transactions, accountTransfers, accountPayments);

                    if (account.AccountType.Name != "Liability")
                        assets += balance;
                    else
                        liabilities += balance;
                }

                return new NetWorthEntry(g.Key.Code, g.Key.Symbol, assets, liabilities, assets - liabilities);
            })
            .ToList();
```

This removes the `paymentsByAsset`/`paymentsByLiability`/`transfersOutByAccount`/`transfersInByAccount` dictionary construction — `ComputeBalance` now filters per-account directly from the flat `liabilityPayments`/`transfers` lists (both kept, only their downstream `GroupBy`/`ToDictionary` calls are removed).

- [ ] **Step 3: Run the net-worth tests again to verify no observable change**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ReportServiceTests.GetNetWorthAsync"`
Expected: 3 passed, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Services/ReportService.cs
git commit -m "refactor(16.5): migrate GetNetWorthAsync to AccountBalanceCalculator

Site 5 of 8."
```

---

## Task 7: Migrate `NetWorthGenerator`

**Files:**
- Modify: `ProjectCeres/Services/Reports/NetWorthGenerator.cs`

**Interfaces:**
- Consumes: `AccountBalanceCalculator.ComputeBalance` (Task 1).

Research confirmed `NetWorthGenerator.cs` is line-for-line identical to `ReportService.GetNetWorthAsync` in the account-balance section (verified by direct comparison of both files).

Existing test coverage: `ProjectCeres.Tests/Integration/ReportGeneratorTests.cs` — `NetWorth_ReturnsAssetAndLiabilityTotalsPerCurrency`, `NetWorth_IncludesLiabilityBalanceFundedEntirelyByATransfer`.

- [ ] **Step 1: Run the existing tests to confirm the baseline**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ReportGeneratorTests.NetWorth_"`
Expected: 2 passed, 0 failed (this filter must NOT also match `NetWorthOverTime_*` tests — if it does, narrow to `FullyQualifiedName~NetWorth_ReturnsAssetAndLiabilityTotalsPerCurrency|FullyQualifiedName~NetWorth_IncludesLiabilityBalanceFundedEntirelyByATransfer`).

- [ ] **Step 2: Migrate the per-account loop**

In `ProjectCeres/Services/Reports/NetWorthGenerator.cs`, replace:

```csharp
        var paymentsByAsset = liabilityPayments
            .GroupBy(p => p.AssetAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var paymentsByLiability = liabilityPayments
            .GroupBy(p => p.LiabilityAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        // A Transfer moves money between two of the user's own accounts and never
        // touches Transactions — an account funded entirely by a transfer (e.g. an
        // opening-balance transfer into a new liability) has no Transactions to sum
        // above and would otherwise contribute nothing here, matching
        // AccountService.GetBalanceAsync's own transfersIn/transfersOut terms.
        var transfers = await db.Transfers
            .Owned(user)
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.SourceAccountId) || accountIds.Contains(t.DestAccountId))
            .ToListAsync();

        var transfersOutByAccount = transfers
            .GroupBy(t => t.SourceAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var transfersInByAccount = transfers
            .GroupBy(t => t.DestAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var grouped = accounts
            .GroupBy(a => new { a.Currency.Code, a.Currency.Symbol, a.CurrencyId })
            .Select(g =>
            {
                decimal assets      = 0;
                decimal liabilities = 0;

                foreach (var account in g)
                {
                    bool isLiability = account.AccountType.Name == "Liability";

                    var balance = account.Transactions.Sum(t =>
                    {
                        if (t.Category.IsSystem) return t.Amount;
                        bool isIncome = t.Category.CategoryType.Name == "Income";
                        bool addsToBalance = isLiability ? !isIncome : isIncome;
                        return addsToBalance ? t.Amount : -t.Amount;
                    });

                    balance -= paymentsByAsset.GetValueOrDefault(account.Id);
                    balance -= paymentsByLiability.GetValueOrDefault(account.Id);

                    balance += transfersInByAccount.GetValueOrDefault(account.Id);
                    balance -= transfersOutByAccount.GetValueOrDefault(account.Id);

                    if (!isLiability)
                        assets += balance;
                    else
                        liabilities += balance;
                }

                return new NetWorthEntry(g.Key.Code, g.Key.Symbol, assets, liabilities, assets - liabilities);
            })
            .ToList();
```

with:

```csharp
        // A Transfer moves money between two of the user's own accounts and never
        // touches Transactions — an account funded entirely by a transfer (e.g. an
        // opening-balance transfer into a new liability) has no Transactions to sum
        // above and would otherwise contribute nothing here, matching
        // AccountService.GetBalanceAsync's own transfersIn/transfersOut terms.
        var transfers = await db.Transfers
            .Owned(user)
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.SourceAccountId) || accountIds.Contains(t.DestAccountId))
            .ToListAsync();

        var grouped = accounts
            .GroupBy(a => new { a.Currency.Code, a.Currency.Symbol, a.CurrencyId })
            .Select(g =>
            {
                decimal assets      = 0;
                decimal liabilities = 0;

                foreach (var account in g)
                {
                    var accountTransfers = transfers.Where(t => t.SourceAccountId == account.Id || t.DestAccountId == account.Id);
                    var accountPayments  = liabilityPayments.Where(p => p.AssetAccountId == account.Id || p.LiabilityAccountId == account.Id);
                    var balance = AccountBalanceCalculator.ComputeBalance(account, account.Transactions, accountTransfers, accountPayments);

                    if (account.AccountType.Name != "Liability")
                        assets += balance;
                    else
                        liabilities += balance;
                }

                return new NetWorthEntry(g.Key.Code, g.Key.Symbol, assets, liabilities, assets - liabilities);
            })
            .ToList();
```

- [ ] **Step 3: Run the tests again to verify no observable change**

Run: same filter as Step 1.
Expected: 2 passed, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Services/Reports/NetWorthGenerator.cs
git commit -m "refactor(16.5): migrate NetWorthGenerator to AccountBalanceCalculator

Site 6 of 8."
```

---

## Task 8: Migrate `NetWorthOverTimeReportGenerator`

**Files:**
- Modify: `ProjectCeres/Services/Reports/NetWorthOverTimeReportGenerator.cs`

**Interfaces:**
- Consumes: `AccountBalanceCalculator.ComputeBalance` (Task 1).

This site and Task 9's site both re-filter by month inside a loop (12 iterations for the dashboard trend; as many months as the report's date range covers here). `ComputeBalance` accepts any pre-filtered slice, so the per-month date filtering (`.Where(t => t.Date <= monthEnd)` etc.) stays exactly as-is — only the per-account balance computation inside each month's iteration changes.

Existing test coverage: `ProjectCeres.Tests/Integration/ReportGeneratorTests.cs` — `NetWorthOverTime_ReturnsMonthlyEquitySnapshots`, `NetWorthOverTime_IncludesLiabilityAccounts`, `NetWorthOverTime_IncludesLiabilityBalanceFundedEntirelyByATransfer`, `NetWorthOverTime_FiltersByCurrency`, `NetWorthOverTime_SnapshotsAreCumulative`, `NetWorthOverTime_ExcludesAccountFlaggedExcludeFromReports_ButIncludesArchivedAccountNotSoFlagged`.

- [ ] **Step 1: Run the existing tests to confirm the baseline**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ReportGeneratorTests.NetWorthOverTime_"`
Expected: 6 passed, 0 failed.

- [ ] **Step 2: Migrate the per-account, per-month loop**

In `ProjectCeres/Services/Reports/NetWorthOverTimeReportGenerator.cs`, inside the `rows = months.Select(m => { ... })` projection, replace:

```csharp
            var paymentsUpToMonth = liabilityPayments.Where(p => p.Date <= snapshotEnd).ToList();
            var paymentsByAsset = paymentsUpToMonth
                .GroupBy(p => p.AssetAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
            var paymentsByLiability = paymentsUpToMonth
                .GroupBy(p => p.LiabilityAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            var transfersUpToMonth = transfers.Where(t => t.Date <= snapshotEnd).ToList();
            var transfersOutByAccount = transfersUpToMonth
                .GroupBy(t => t.SourceAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
            var transfersInByAccount = transfersUpToMonth
                .GroupBy(t => t.DestAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

            foreach (var account in accounts)
            {
                bool isLiability = account.AccountType.Name == "Liability";

                var balance = account.Transactions
                    .Where(t => t.Date <= snapshotEnd)
                    .Sum(t =>
                    {
                        if (t.Category.IsSystem) return t.Amount;
                        bool isIncome = t.Category.CategoryType.Name == "Income";
                        bool addsToBalance = isLiability ? !isIncome : isIncome;
                        return addsToBalance ? t.Amount : -t.Amount;
                    });

                balance -= paymentsByAsset.GetValueOrDefault(account.Id);
                balance -= paymentsByLiability.GetValueOrDefault(account.Id);

                balance += transfersInByAccount.GetValueOrDefault(account.Id);
                balance -= transfersOutByAccount.GetValueOrDefault(account.Id);

                if (!isLiability)
                    assets += balance;
                else
                    liabilities += balance;
            }
```

with:

```csharp
            var paymentsUpToMonth = liabilityPayments.Where(p => p.Date <= snapshotEnd).ToList();
            var transfersUpToMonth = transfers.Where(t => t.Date <= snapshotEnd).ToList();

            foreach (var account in accounts)
            {
                var transactionsUpToMonth = account.Transactions.Where(t => t.Date <= snapshotEnd);
                var accountTransfers = transfersUpToMonth.Where(t => t.SourceAccountId == account.Id || t.DestAccountId == account.Id);
                var accountPayments  = paymentsUpToMonth.Where(p => p.AssetAccountId == account.Id || p.LiabilityAccountId == account.Id);
                var balance = AccountBalanceCalculator.ComputeBalance(account, transactionsUpToMonth, accountTransfers, accountPayments);

                if (account.AccountType.Name != "Liability")
                    assets += balance;
                else
                    liabilities += balance;
            }
```

This removes the per-month `paymentsByAsset`/`paymentsByLiability`/`transfersInByAccount`/`transfersOutByAccount` dictionary construction — `ComputeBalance` now filters per-account directly from the already-date-filtered `paymentsUpToMonth`/`transfersUpToMonth` flat lists, which are kept (only their downstream `GroupBy`/`ToDictionary` calls are removed).

- [ ] **Step 3: Run the tests again to verify no observable change**

Run: same filter as Step 1.
Expected: 6 passed, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Services/Reports/NetWorthOverTimeReportGenerator.cs
git commit -m "refactor(16.5): migrate NetWorthOverTimeReportGenerator to AccountBalanceCalculator

Site 7 of 8. Per-month date filtering on Transactions/Transfers/
LiabilityPayments is unchanged -- ComputeBalance accepts any
pre-filtered slice, so only the per-account arithmetic inside each
month's loop iteration changes."
```

---

## Task 9: Migrate `DashboardApiController.GetNetWorthTrend`, delete `BalanceCalculationTests.cs`, update roadmap

**Files:**
- Modify: `ProjectCeres/Controllers/Api/DashboardApiController.cs:146-243`
- Delete: `ProjectCeres.Tests/Unit/BalanceCalculationTests.cs`
- Modify: `docs/roadmap-phase-three.md`

**Interfaces:**
- Consumes: `AccountBalanceCalculator.ComputeBalance` (Task 1).

Existing test coverage: `ProjectCeres.Tests/Integration/DashboardApiTests.cs` — `GetNetWorthTrend_Returns200_WithWrappedShape_And12MonthWindow`, `GetNetWorthTrend_IncludesLiabilityBalanceFundedEntirelyByATransfer`.

- [ ] **Step 1: Run the existing trend tests to confirm the baseline**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~GetNetWorthTrend"`
Expected: 2 passed, 0 failed.

- [ ] **Step 2: Migrate the per-account, per-month loop**

In `ProjectCeres/Controllers/Api/DashboardApiController.cs`'s `GetNetWorthTrend()`, inside the `for (int i = 11; i >= 0; i--)` loop's `foreach (var account in accounts)` block, replace:

```csharp
            foreach (var account in accounts)
            {
                bool isLiability = account.AccountType.Name == "Liability";

                var balance = account.Transactions
                    .Where(t => t.Date <= monthEnd)
                    .Sum(t =>
                    {
                        if (t.Category.IsSystem) return t.Amount;
                        bool isIncome = t.Category.CategoryType.Name == "Income";
                        bool addsToBalance = isLiability ? !isIncome : isIncome;
                        return addsToBalance ? t.Amount : -t.Amount;
                    });

                balance -= paymentsByAsset.GetValueOrDefault(account.Id);
                balance -= paymentsByLiability.GetValueOrDefault(account.Id);

                balance += transfersInByAccount.GetValueOrDefault(account.Id);
                balance -= transfersOutByAccount.GetValueOrDefault(account.Id);

                if (!isLiability) assets += balance;
                else liabilities += balance;
            }
```

with:

```csharp
            foreach (var account in accounts)
            {
                var transactionsUpToMonth = account.Transactions.Where(t => t.Date <= monthEnd);
                var accountTransfers = transfersUpToMonth.Where(t => t.SourceAccountId == account.Id || t.DestAccountId == account.Id);
                var accountPayments  = paymentsUpToMonth.Where(p => p.AssetAccountId == account.Id || p.LiabilityAccountId == account.Id);
                var balance = AccountBalanceCalculator.ComputeBalance(account, transactionsUpToMonth, accountTransfers, accountPayments);

                if (account.AccountType.Name != "Liability") assets += balance;
                else liabilities += balance;
            }
```

Remove the now-unused `paymentsByAsset`/`paymentsByLiability`/`transfersInByAccount`/`transfersOutByAccount` dictionary construction inside the month loop (the four `GroupBy`/`ToDictionary` lines built from `paymentsUpToMonth`/`transfersUpToMonth` each iteration) — `ComputeBalance` now filters per-account directly from those same `paymentsUpToMonth`/`transfersUpToMonth` flat lists, which stay as-is.

- [ ] **Step 3: Run the trend tests again to verify no observable change**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~GetNetWorthTrend"`
Expected: 2 passed, 0 failed.

- [ ] **Step 4: Delete the superseded test file**

```bash
git rm ProjectCeres.Tests/Unit/BalanceCalculationTests.cs
```

- [ ] **Step 5: Run the full build + full test suite once**

Run: `dotnet build ProjectCeres.sln`
Expected: 0 errors.

Run: `dotnet test ProjectCeres.sln`
Expected: all green. (This is the one full-suite run for the whole plan, per the Global Constraints section — every individual task above used a targeted `--filter` run instead.)

- [ ] **Step 6: Update `docs/roadmap-phase-three.md`'s Stage 16.5 section**

Read the current Stage 16.5 section first (`grep -n "Stage 16.5" -A 15 docs/roadmap-phase-three.md`) to get its exact current line numbers, since Task-9-prior commits in this plan don't touch this file and its line numbers are stable from the state after commit `352e3af7`. Make these edits:

- Tick checklist item 1 (`Extract AccountBalanceCalculator...`) to `[x]`, with a short resolution note naming this plan's spec and the 8 migrated sites.
- Tick checklist item 3 (`Once extracted, add one test on the calculator itself...`) to `[x]`, with a resolution note citing `AccountBalanceCalculatorTests.cs` (9 tests) and explicitly stating that all 5 pre-existing per-site integration tripwires were KEPT (per spec D7) — correcting the ambiguity the roadmap's own "instead of" wording created, now that this plan has resolved it.
- Update the tripwire line to note that each of the 5 (now also) proves calculator-wiring correctness at its site, not just pre-extraction regression coverage (per spec D7).
- Check whether every item under the `## Stage 16.5` heading is now ticked; if so, this stage is fully closed — add or confirm a `**Status: ✅ Closed**` line at the top of the section (match whatever closed-stage convention the surrounding roadmap entries use — check a nearby already-closed stage's header format before writing this line, since the plan should match existing convention rather than invent a new one).

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Controllers/Api/DashboardApiController.cs ProjectCeres.Tests/Unit/BalanceCalculationTests.cs docs/roadmap-phase-three.md
git commit -m "refactor(16.5): migrate GetNetWorthTrend, close out the stage

Site 8 of 8 -- all duplicated balance-calculation implementations now
call the shared AccountBalanceCalculator. Deletes BalanceCalculationTests.cs
(superseded by AccountBalanceCalculatorTests.cs, which tests the real
production code instead of a hand-copied local helper). Ticks the
roadmap's remaining Stage 16.5 checklist items and corrects its
ambiguous test-strategy wording."
```

---

## Verification

After Task 9 completes, confirm:

- `grep -n "paymentsByAsset\|paymentsByLiability\|transfersInByAccount\|transfersOutByAccount" ProjectCeres/Services/ ProjectCeres/Controllers/ -r` returns no matches — every site's local dictionary-based direction-splitting has been removed, since `AccountBalanceCalculator.ComputeBalance` now does that splitting internally.
- `grep -rn "isIncome ? t.Amount : -t.Amount\|addsToBalance ? t.Amount : -t.Amount\|addsBalance ? t.Amount : -t.Amount" ProjectCeres/Services/ ProjectCeres/Controllers/ -r` returns no matches outside `AccountBalanceCalculator.cs` itself — the sign formula exists in exactly one place.
- `dotnet build ProjectCeres.sln` — 0 errors, 0 new warnings.
- `dotnet test ProjectCeres.sln` — all green.
- `docs/roadmap-phase-three.md`'s Stage 16.5 section has no remaining `[ ]` items.
