# Stage 16.5 — Extract `AccountBalanceCalculator`

**Status:** Design approved by user 2026-10-01, pending spec self-review and user sign-off before `writing-plans`.

## Context

[`docs/code-quality-audit-2026-05-21.md`](../../code-quality-audit-2026-05-21.md) § "The five highest-leverage fixes" #3 flagged that the account-balance-derivation formula had already diverged across 5+ independently-maintained copies. On 2026-09-28 a real user hit exactly the predicted failure: four of those copies never summed `Transfer` records into an account's balance, so a liability account funded entirely by a transfer silently contributed nothing to the dashboard's Net Worth card, the saved Net Worth report, and the Net Worth Over Time chart. That incident was fixed as four independently-scoped patches — the duplication itself was deliberately left unresolved and tracked as this stage (`docs/roadmap-phase-three.md` § Stage 16.5).

On 2026-10-01, Stage 16.5's own audit checklist item (are the two unaudited sites — `DashboardService.GetSpendableBalanceAsync` and `GetRunwayAsync` — also missing Transfer aggregation?) was resolved: `GetSpendableBalanceAsync` was already correct (fixed independently 2026-04-28, predating the September incident entirely); `GetRunwayAsync` had the identical gap and was fixed with the same pattern as the other four, plus a new regression test (`DashboardServiceTests.GetHealthSnapshotAsync_Runway_IncludesLiabilityBalanceFundedEntirelyByATransfer`, commit `352e3af7`).

This spec covers the actual extraction: replacing all 8 now-individually-correct-but-still-duplicated implementations with one shared piece of code.

## Research findings (verified against source, not the roadmap's prose)

All 8 call sites, their current state, and return shapes:

| # | Site | Account filter | Transfers? | Payments? | Return shape |
|---|------|-----------------|-----------|-----------|--------------|
| 1 | `AccountService.GetBalanceAsync(Guid id)` | single account by id | yes | yes | flat `decimal` |
| 2 | `AccountService.GetLedgerAsync(Guid id)` | single account by id | yes (per-row) | yes (per-row) | `AccountLedgerViewModel` (row-level ledger, running balance) |
| 3 | `DashboardService.GetSpendableBalanceAsync(int currencyId)` | `IsActive && CurrencyId == x && AccountType == "Asset" && !ExcludeFromSpendable` | yes (fixed 2026-04-28, independent of the Sept. incident) | yes (Asset-account side only) | 5-tuple of nullable decimals |
| 4 | `DashboardService.GetRunwayAsync(int currencyId)` | `!ExcludeFromReports && CurrencyId == x` (fixed 2026-10-01, was bare `IsActive`) | yes (fixed 2026-10-01) | yes | 2-tuple of nullable decimals |
| 5 | `ReportService.GetNetWorthAsync()` | `!ExcludeFromReports` | yes | yes | `IReadOnlyList<NetWorthEntry>` grouped by currency |
| 6 | `DashboardApiController.GetNetWorthTrend()` | `CurrencyId == x && !ExcludeFromReports` | yes, re-filtered by month | yes, re-filtered by month | `NetWorthTrendDto` (12 monthly points) |
| 7 | `NetWorthGenerator.GenerateAsync` | `!ExcludeFromReports` | yes | yes | `IReadOnlyList<NetWorthEntry>` (near-identical to #5) |
| 8 | `NetWorthOverTimeReportGenerator.GenerateAsync` | `CurrencyId == x && !ExcludeFromReports` | yes, re-filtered by month | yes, re-filtered by month | `List<NetWorthSnapshotRow>` (near-identical to #6) |

No 9th production site exists — a repo-wide sweep for balance/net-worth-summing patterns outside these 8 found only listing/filtering/import-matching code with no independent balance computation.

`ProjectCeres.Tests/Unit/BalanceCalculationTests.cs` independently re-implements the sign formula via a hand-copied local helper to test it — a 9th, test-only copy of the same logic.

**The one truly shared primitive** is the per-account signed-balance formula, netted against Transfers (both directions) and LiabilityPayments (both directions). **Account selection** (single-account-by-id vs. all-active vs. Asset-only-plus-ExcludeFromSpendable vs. per-currency-grouped) and **output shape** (flat decimal, ledger rows, tuples, grouped lists, time series) genuinely differ per site and must not be forced into one signature.

## Decisions

**D1 — Shape: pure static function over pre-loaded data, not an injectable service.**
`AccountBalanceCalculator` has no `DbContext`, no `async`, no DI registration. Callers query and filter however their own purpose requires, then pass the resulting in-memory collections in. This matches the project's one existing precedent for this kind of helper (`BudgetPeriod.cs` — pure math, no I/O) rather than the injectable-service pattern, which doesn't fit since the calculator itself never touches the database.

**D2 — The calculator also does direction-splitting, not just arithmetic.**
`ComputeBalance` takes the *raw* `IEnumerable<Transfer>`/`IEnumerable<LiabilityPayment>` rows touching an account (both directions) and splits them internally (`SourceAccountId`/`DestAccountId`, `AssetAccountId`/`LiabilityAccountId`). Sites 3–8 currently repeat that same four-line split-and-sum by hand (as a per-account dictionary lookup); centralizing it removes that duplication too, not just the sign formula's. Sites 1–2 already query each direction separately per account (by `id`, not via a dictionary) since they operate on one account at a time — `ComputeBalance` accepts both shapes identically, since it only needs "the rows touching this account," regardless of how the caller arrived at that slice.

**D3 — A second, smaller function for per-row sign.**
`GetLedgerAsync` needs the signed contribution of one transaction at a time (to build individual ledger rows with a running balance), not an account total. `SignedAmount(Transaction, bool isLiability)` is exposed publicly and is also what `ComputeBalance` calls internally via `.Sum()` — one formula, two consumers, rather than the formula living twice.

**D4 — The calculator never filters or selects accounts.**
No default-filter helper, no `IQueryable` overload. Every site's "which accounts matter" decision is made entirely by the caller before the calculator is ever called. This is deliberate: the three real filter variants (`!ExcludeFromReports`, `IsActive`, `!ExcludeFromSpendable` + Asset-only) encode genuinely different business meanings (net worth vs. a point-in-time lookup vs. spendable cash), and centralizing filtering risks exactly the kind of silent behavior drift this stage exists to eliminate.

**D5 — File location: `ProjectCeres/Services/AccountBalanceCalculator.cs`, flat.**
Not `Services/Reports/` (misrepresents scope — `AccountService` and `DashboardService` are callers too, not just reports) and not a new `Services/Accounts/` subfolder (no existing precedent for it; `BudgetPeriod.cs`'s flat placement is the established pattern for this exact kind of helper).

**D6 — Migrate all 8 sites in the same stage, not phased.**
Order, lowest-risk first:
1. `AccountService.GetBalanceAsync` — migrate first, to prove the calculator reproduces the reference implementation exactly.
2. `AccountService.GetLedgerAsync` — per-row `SignedAmount` usage.
3. `DashboardService.GetRunwayAsync` — freshest in context (just fixed 2026-10-01).
4. `DashboardService.GetSpendableBalanceAsync`.
5. `ReportService.GetNetWorthAsync`.
6. `NetWorthGenerator` (near-identical to #5).
7. `NetWorthOverTimeReportGenerator`.
8. `DashboardApiController.GetNetWorthTrend`.

Each step: swap the inline arithmetic for calculator calls, run that site's existing test file, confirm green, commit, move to the next. Migrating fewer than all 8 would leave some copies un-migrated — exactly the half-finished state this stage exists to close out, and the project's finished-stages rule (no unchecked items) would block declaring Stage 16.5 done with any site still on the old inline formula.

**D7 — Test strategy: add a calculator-level unit test file, keep all 5 existing per-site integration tripwires.**
New `ProjectCeres.Tests/Unit/AccountBalanceCalculatorTests.cs` covers the formula exhaustively and fast (no DB): Transfer-funded liability, Transfer-funded asset, LiabilityPayment reduction on both legs, opening-balance/`IsSystem` handling, asset-vs-liability sign flip.

The 5 existing per-site Transfer-regression tests (`ReportServiceTests.GetNetWorthAsync_IncludesLiabilityBalanceFundedEntirelyByATransfer`, `ReportGeneratorTests.NetWorth_...FundedEntirelyByATransfer`, `ReportGeneratorTests.NetWorthOverTime_...FundedEntirelyByATransfer`, `DashboardApiTests.GetNetWorthTrend_...FundedEntirelyByATransfer`, `DashboardServiceTests.GetHealthSnapshotAsync_Runway_...FundedEntirelyByATransfer`) are **kept, not deleted**. This clarifies an ambiguity in the roadmap's own wording ("add one test on the calculator itself... *instead of* the current per-call-site duplication") — read narrowly, that phrase could mean deleting the 5 integration tests. It does not: those 5 tests, once the migration lands, prove each *site* still correctly wires the shared calculator in (queries the right data, passes the right slices) — a failure mode the calculator's own unit tests cannot catch, since they never touch a real query or that site's specific grouping/shaping logic. Only the *test-only, hand-copied formula duplication* is being retired (D8), not the integration-level wiring proof.

**D8 — Delete `ProjectCeres.Tests/Unit/BalanceCalculationTests.cs`.**
Its 12 `[Fact]`s test real, valuable scenarios (sign flip, opening-balance handling) — but against a hand-copied local helper, not the real production code. That is a 9th copy of the formula, now in test code instead of production code, with the same silent-drift risk the whole stage exists to eliminate. Its cases move into the new `AccountBalanceCalculatorTests.cs`, asserting against the real `ComputeBalance`/`SignedAmount` methods instead.

## Non-goals

- No change to any site's account-selection logic, output shape, or external behavior beyond what's needed to call the shared calculator — this is a refactor, not a feature or a further bug fix.
- No change to `GetSpendableBalanceAsync`'s Asset-only + `ExcludeFromSpendable` semantics, or any other site's filter semantics.
- No new DI registration (the calculator is static).
- A separate finding during research — `docs/architecture.md` documented a non-existent `ILiabilityProjectionService`/`LiabilityProjectionService` (zero files, zero usages anywhere in the repo) — was unrelated to this extraction and was fixed directly as a one-line doc correction rather than deferred; see `docs/architecture.md`'s Services-layer list.

## Testing

- New: `ProjectCeres.Tests/Unit/AccountBalanceCalculatorTests.cs` (exhaustive formula coverage, no DB).
- Kept, unmodified in assertion intent: the 5 existing per-site integration tripwires (their *setup* may need superficial adjustment if a site's internal variable names change, but their asserted values and scenarios do not change).
- Deleted: `ProjectCeres.Tests/Unit/BalanceCalculationTests.cs` (superseded by the new unit test file).
- Each of the 8 migration steps re-runs that site's existing test file(s) before moving to the next step (see D6).
- Full `dotnet build` + full `dotnet test` once at the end of the migration, per project convention (not after every single-site step, to avoid the full-suite-reseed cost the project has already hit once this cycle).

## Roadmap updates (same commit as the final migration step)

- Tick `docs/roadmap-phase-three.md` Stage 16.5 checklist items 1 and 3.
- Correct the ambiguous "instead of" wording in item 3 to explicitly state both test layers are kept (per D7).
- Update the tripwire line: the 5 named tests now also prove calculator-wiring correctness at each site, not just pre-extraction regression coverage.
