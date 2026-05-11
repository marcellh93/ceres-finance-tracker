# Stage 6 Close-Out — Balance Calculator Extraction + Unit Test Anchoring

**Date:** 2026-05-11
**Stage:** Phase 3, Stage 6 close-out
**Trigger:** Roadmap action point added 2026-05-11 to `docs/roadmap-phase-three.md` "Stage 6 close-out documentation" block.
**Author:** drafted after a coverage-discussion deep-dive prompted by the user 2026-05-11.

## 1. Scope

In scope:
- Extract the duplicated balance + direction-inference formula into one static helper.
- Replace three production call sites with calls to the helper.
- Rewire `BalanceCalculationTests` to target the production helper instead of a local mirror.
- Add ~8 edge-case unit tests covering scenarios the existing 12 don't.
- Decide deliberately how to resolve the `NetWorthGenerator` `LiabilityPayments` omission — bug fix OR explanatory comment.

Out of scope (separate follow-ups, tracked in `docs/planning-phase3.md` at execution time):
- Extracting `TransferService` validation predicates (same-currency, source ≠ destination) into a `TransferPolicies` static class.
- Unit-testing `DashboardService.GetDashboardDataAsync` prior-period comparison logic.
- Unit-testing `ReportService.GetExpenseBreakdownAsync` grouping logic.
- Unit-testing `BudgetVsActualReportGenerator` variance arithmetic.

## 2. Problem

The balance + direction-inference formula is duplicated verbatim across four locations:

| File | Lines | Adjustments applied at call site |
|---|---|---|
| `ProjectCeres/Services/AccountService.cs` | 74–80 | `LiabilityPayments` (subtract both `paymentsOut` and `paymentsIn`) + `Transfers` (`+ transfersIn − transfersOut`) |
| `ProjectCeres/Services/ReportService.cs` | 52–58 | `LiabilityPayments` (via `paymentsByAsset` + `paymentsByLiability` dictionary lookups) |
| `ProjectCeres/Services/Reports/NetWorthGenerator.cs` | 33–39 | **None** — does NOT load or subtract `LiabilityPayments`, inconsistent with the other two |
| `ProjectCeres.Tests/Unit/BalanceCalculationTests.cs` | 15–22 | N/A (test mirror) |

The shared core in all four:

```csharp
transactions.Sum(t =>
{
    if (t.Category.IsSystem) return t.Amount;
    bool isIncome = t.Category.CategoryType.Name == "Income";
    bool addsToBalance = isLiability ? !isIncome : isIncome;
    return addsToBalance ? t.Amount : -t.Amount;
});
```

Two risks the duplication creates:

1. **Test mirror fails its job.** `BalanceCalculationTests` has its own `CalculateBalance(...)` helper that mirrors the formula. The 12 tests pin the *mirrored* formula's behavior, not the production sites'. If any one production site drifted, all 12 tests would stay green.

2. **Drift has already happened.** `NetWorthGenerator.GenerateAsync` does not apply `LiabilityPayments` while the other two sites do. This is the kind of bug the mirror-based test approach is incapable of catching. Either the omission is a bug (most likely — net worth should include debt payments) or it's intentional and the reasoning is undocumented.

## 3. Solution

### 3.1 New helper

File: `ProjectCeres/Services/AccountBalanceCalculator.cs` (new)

```csharp
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public static class AccountBalanceCalculator
{
    // Sums a transaction stream into a running balance, applying direction inference
    // from Category → CategoryType per the data-model rule that Transaction.Amount is
    // always positive.
    //
    // System categories (e.g. opening balance) always add — they are a neutral
    // starting point regardless of account type.
    //
    // Liability payments and inter-account transfers are NOT covered here — they live
    // outside the transaction stream and are applied by callers that need them.
    public static decimal SumTransactions(IEnumerable<Transaction> transactions, bool isLiability) =>
        transactions.Sum(t =>
        {
            if (t.Category.IsSystem) return t.Amount;
            bool isIncome = t.Category.CategoryType.Name == "Income";
            bool addsToBalance = isLiability ? !isIncome : isIncome;
            return addsToBalance ? t.Amount : -t.Amount;
        });
}
```

The helper deliberately covers only the transaction-sum portion. `LiabilityPayments` and `Transfers` adjustments stay at each call site because the three sites differ in which adjustments they apply. Pulling those into the helper would force a wider rewrite (different query shapes, different DbContext access) for no payoff — the duplication risk lives in the formula, not the adjustments.

### 3.2 Call site replacements

| File | Replace lines | With |
|---|---|---|
| `ProjectCeres/Services/AccountService.cs` | 74–80 (the `transactions.Sum(t => …)` lambda) | `decimal balance = AccountBalanceCalculator.SumTransactions(transactions, isLiability);` |
| `ProjectCeres/Services/ReportService.cs` | 52–58 (the `account.Transactions.Sum(t => …)` lambda) | `var balance = AccountBalanceCalculator.SumTransactions(account.Transactions, isLiability);` |
| `ProjectCeres/Services/Reports/NetWorthGenerator.cs` | 33–39 (same shape) | `var balance = AccountBalanceCalculator.SumTransactions(account.Transactions, isLiability);` |

Surrounding code (account loading, `LiabilityPayments` subtraction, transfer adjustments, currency grouping, `NetWorthEntry` construction) stays put.

### 3.3 Resolve the `NetWorthGenerator` inconsistency

Before merging the refactor, investigate `NetWorthGenerator`'s call path:

1. Find every callsite of `NetWorthGenerator` (likely via `ReportGeneratorFactory` → controller / SavedReport runner).
2. Determine whether the report path is supposed to include or exclude liability payments.

Then choose:

- **(a) Bug fix.** Add the same `LiabilityPayments` loading + dictionary lookup that `ReportService.GetNetWorthAsync` uses (`ReportService.cs:28-39` for the load, `:60-61` for the subtraction). Add or update an integration test asserting the corrected behavior.
- **(b) Intentional.** Add a one-line code comment in `NetWorthGenerator.GenerateAsync` explaining why this path excludes `LiabilityPayments`. If the reasoning is non-trivial (e.g. "this is consumed by the historical-comparison report which uses a different debt aggregation"), also note it in `docs/planning-resolved.md` so the decision is grep-able later.

Default position absent investigation: this is option (a), a bug. The other two sites apply the same adjustment for the same purpose (debt payments reduce both sides of net worth).

### 3.4 Test rewiring

File: `ProjectCeres.Tests/Unit/BalanceCalculationTests.cs`

1. Delete the local `CalculateBalance` helper (lines 15–22).
2. Update the 12 existing `[Fact]`s to call `AccountBalanceCalculator.SumTransactions(...)` instead of the local helper. The call sites are at lines 62, 69, 76, 86, 92, 98, 106, 116, 123, 130, 137, 144.
3. Keep the `OpeningBalance`, `Income`, `Expense` factory helpers (lines 24–53) — they're test fixtures, not formula mirrors.
4. Update the class-level summary comment (lines 6–11) to drop the "Mirrors the formula…" line and replace with "Verifies `AccountBalanceCalculator.SumTransactions` against the documented direction-inference rules."

### 3.5 New edge-case tests

Append to `BalanceCalculationTests.cs`. Target eight new `[Fact]`s:

| Test name | Scenario | Why it's worth pinning |
|---|---|---|
| `OpeningBalance_ZeroAmount_DoesNotAffectBalance` | `OpeningBalance(0m)` over empty otherwise | Defensive — confirms zero-amount opening row is a no-op. |
| `Asset_OpeningBalanceFollowedByIncomeAndExpenses_NetsCorrectly` | `[OpeningBalance(500), Income(200), Expense(150)] → 550` | The 12 existing tests don't combine opening + regular on asset. |
| `Liability_RefundExceedsCharges_ReturnsNegativeBalance` | `[Expense(50), Income(200)] isLiability:true → -150` | Overpaid credit-card scenario. Existing tests cap at refund ≤ charges. |
| `Asset_MultipleOpeningBalances_AllAdd` | `[OpeningBalance(100), OpeningBalance(50)] → 150` | Defensive against data quirks (the app should only ever create one, but the formula must be tolerant). |
| `Asset_LargeTransactionCount_PreservesPrecision` | 1,000 alternating `Income(0.01m)` / `Expense(0.01m)` → 0 | Decimal arithmetic sanity check — `decimal` is exact, but pin it. |
| `Liability_OnlyIncomeNoExpenses_ReturnsNegativeBalance` | `[Income(300)] isLiability:true → -300` | Inverse of `Asset_SingleIncomeTransaction_ReturnsPositiveBalance`. |
| `MixedSystemAndRegularCategories_SystemAlwaysAdds_OnLiability` | `[OpeningBalance(200), Expense(50), Income(30)] isLiability:true → 220` | Pins the `IsSystem` short-circuit on liability — current tests cover the system path on liability alone and the regular path mixed, but not both interleaved. |
| `SystemCategory_DoesNotConsultCategoryType` | A `IsSystem=true` transaction whose `CategoryType.Name` is `"Expense"` still adds | Pins that the `IsSystem` check short-circuits BEFORE the income/expense branch. Today's test factories happen to set `IsSystem` rows to `Name = "Income"`, so this branch isn't pinned. |

Total: 12 existing + 8 new = 20 tests in `BalanceCalculationTests`.

## 4. Files modified

- `ProjectCeres/Services/AccountBalanceCalculator.cs` — new
- `ProjectCeres/Services/AccountService.cs` — replace lambda at 74–80
- `ProjectCeres/Services/ReportService.cs` — replace lambda at 52–58
- `ProjectCeres/Services/Reports/NetWorthGenerator.cs` — replace lambda at 33–39; conditionally add `LiabilityPayments` handling depending on §3.3 decision
- `ProjectCeres.Tests/Unit/BalanceCalculationTests.cs` — delete local helper, rewire 12 tests, add 8 new tests, update class comment

## 5. Tests required (ship-gate per `feedback_test_edge_cases_as_ship_gate`)

Negative-assertion / edge-case tests that must exist before this work ships:

- ✅ The 8 new tests in §3.5 above cover the explicit edge cases.
- ✅ At least one test asserts the `IsSystem` short-circuit fires *before* the income/expense branch (`SystemCategory_DoesNotConsultCategoryType`).
- ✅ At least one test asserts a refund on a liability produces a negative balance (`Liability_RefundExceedsCharges_ReturnsNegativeBalance`).
- ✅ At least one test asserts the formula on an empty transaction list returns zero for both asset and liability (already covered: `Asset_EmptyTransactionList_ReturnsZero`, `Liability_EmptyTransactionList_ReturnsZero`).

If §3.3 option (a) is taken (NetWorthGenerator bug fix):

- New integration test: net worth via the report-generator path subtracts `LiabilityPayments` and matches the value returned by `ReportService.GetNetWorthAsync` for the same data.

## 6. Verification

1. `dotnet build` — zero warnings (`feedback_surface_build_pipeline_warnings`).
2. `dotnet test --filter "FullyQualifiedName~BalanceCalculationTests"` — all 20 tests green.
3. Full `dotnet test` — integration suite still green. Refactor is behavior-preserving (the helper is the same expression that was inlined), so this should be a no-op. Run as a regression guard. Expect ~15 min per `project_test_suite_performance`.
4. Manual browser check: Dashboard, Accounts list, single account ledger page (`/Accounts/{id}/Ledger`). Net worth, account balance, and ledger running totals match the values shown before the refactor.
5. If §3.3 (a) was taken, run any saved-report path that uses `NetWorthGenerator` and confirm liabilities now net the payments.

## 7. Risk

Low. The refactor is a verbatim lift of an inlined lambda into a static method; behavior is preserved by construction. The one place behavior *changes* is `NetWorthGenerator` if §3.3 (a) is taken — that change is deliberate and tested.

The dependent ratio (test-to-prod LOC, unit-vs-integration mix) shifts only marginally — that is fine. The point of this work is **drift reduction**, not a coverage-ratio improvement.
