# Phase 2 Build Roadmap

> **Principles:** Spec-Driven Development (spec before code) · Test-Driven Development (failing test before implementation) · SOLID architecture maintained throughout.
>
> **TDD rule:** Every new service method, business rule, and financial calculation gets a failing test written first. The test must fail because the implementation does not exist — not because it is misconfigured. Controllers are excluded (thin HTTP handlers, verified via `WebApplicationFactory` integration tests). Infrastructure plumbing (migrations, DI wiring) is verified by the first smoke test, not by unit tests against the plumbing itself.
>
> **UI/UX rule:** Phase 2 introduces React + shadcn/ui. Every page touched during Phase 2 is brought up to the shadcn/ui standard at the same time — no dedicated redesign sprint. Icons are added to all action buttons (edit, delete, confirm, dismiss, upload, download) using Lucide React (ships with shadcn/ui). Shared components (Navbar, Button, Badge, Card, Table, Dialog, ProgressBar) are built once in Stage 0 and reused everywhere. Razor pages that survive into Phase 3 get a functional upgrade; React components get a higher visual bar as they persist beyond Phase 2.
>
> **UI/UX gate rule:** Every stage that contains a UI/UX requirement must have at least one corresponding checklist item in the Verification Checklist section below. A stage is not complete until both its TDD items AND its UI/UX checklist items are checked. If a stage spec has a UI/UX note but no matching checklist item, the stage spec is incomplete — add the item before marking the stage done.

---

## Index

1. [Stage 0 — Foundation](#stage-0--foundation)
2. [Stage 1 — Budgeting UI](#stage-1--budgeting-ui)
3. [Stage 2 — Cleared / Reconciliation Status](#stage-2--cleared--reconciliation-status)
4. [Stage 2.5 — Movement Base Class + Unified Ledger](#stage-25--movement-base-class--unified-ledger)
5. [Stage 3 — CSV Import](#stage-3--csv-import)
6. [Stage 4 — Transfer Attachments](#stage-4--transfer-attachments)
7. [Stage 5 — Liability Enhancements](#stage-5--liability-enhancements)
8. [Stage 6 — Recurring Reminder Enhancements](#stage-6--recurring-reminder-enhancements)
9. [Stage 7 — Reports](#stage-7--reports)
10. [Stage 8 — Visual Dashboard + React Components](#stage-8--visual-dashboard--react-components)
11. [Stage 9 — Financial Health Metrics](#stage-9--financial-health-metrics)
12. [Stage 10 — CSV Export](#stage-10--csv-export)
13. [Verification Checklist](#verification-checklist)

---

## Stage 0 — Foundation

**Must be complete before any Phase 2 feature is written.** These are prerequisites, not features. Nothing in Stages 1–10 can start until Stage 0 is done.

### 0.1 — Schema migrations

A single baseline migration applies all Phase 2 schema additions before any service code is written. This is a schema-first commitment: the data model is finalized before implementation begins.

| Entity                 | Change                                                                                                                              |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `Account`              | Add `ExcludeFromSpendable bool NOT NULL DEFAULT false`, `LiabilityRepaymentType varchar(20) NULL`, `InterestRate decimal(5,4) NULL` |
| `Transaction`          | Add `IsCleared bool NOT NULL DEFAULT false`                                                                                         |
| `Transfer`             | Add `IsCleared bool NOT NULL DEFAULT false`                                                                                         |
| `Budget`               | Add `GoalType varchar(20) NOT NULL DEFAULT 'Spending'`, `LinkedAccountId uuid NULL` (FK → Account)                                  |
| `RecurringTransaction` | Add `ReminderBehaviour varchar(30) NOT NULL DEFAULT 'SnapToCalendarDay'`, `EstimatedAmount decimal(18,2) NULL`                      |
| `TransferAttachment`   | New table — Id (Guid), TransferId FK, FileName, StoredPath, ContentType, FileSizeBytes, UploadedAt                                  |
| `CsvImportProfile`     | New table — Id (Guid), Name, ColumnMappings (jsonb), CreatedAt, DeletedAt?                                                          |

**TDD:** No new behavior to test-first here. Migration is verified by running the full existing test suite after applying it — all existing integration tests must pass. If any test fails, the migration broke something.

```bash
dotnet ef migrations add Phase2Baseline
dotnet ef database update
dotnet test  # must be 0 failed
```

---

### 0.2 — ReportGeneratorFactory

Refactor `ReportService` to extract each Phase 1 report into its own `IReportGenerator` implementation. Wire `ReportGeneratorFactory` into DI. This is a structural refactor — no behavior changes.

**TDD — write tests first, then refactor:**

1. Write unit tests for the factory: one test per report type asserting `GetGenerator(ReportType.X)` returns the correct concrete generator type. All four tests fail (factory doesn't exist yet).
2. Extract each report into its own class implementing `IReportGenerator`.
3. Implement `ReportGeneratorFactory`.
4. Confirm: factory unit tests pass, all existing `ReportServiceTests` still pass (behavior unchanged).

**Structure (per ADR-0036):**

```csharp
public interface IReportGenerator
{
    Task<ReportResult> GenerateAsync(ReportParameters parameters);
}

public class ReportGeneratorFactory
{
    public IReportGenerator GetGenerator(ReportType type) => type switch
    {
        ReportType.NetWorth      => _netWorthGenerator,
        ReportType.IncomeExpense => _incomeExpenseGenerator,
        // one entry per type — grows with each new report in Stage 7
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
```

---

### 0.3 — API controller infrastructure

Create `Controllers/Api/` folder. Override `InvalidModelStateResponseFactory` in `Program.cs` to enforce the error shape documented in `api-contract.md` (per ADR-0035). Write one `HealthApiController` (`GET /api/health`) to prove routing and error shape work before any real endpoints are built.

**TDD — write tests first:**

1. Write a `WebApplicationFactory` integration test: `GET /api/health` → 200 OK with `{ "status": "ok" }`. Test fails (controller doesn't exist).
2. Write a second test: malformed POST to any API endpoint → 422 with `error.code = "VALIDATION_ERROR"`. Test fails (factory override not wired).
3. Create the controller and wire the factory override. Both tests pass.

```csharp
// Program.cs — wire before first API controller ships
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(err => new
                {
                    field = e.Key,
                    message = err.ErrorMessage
                }));

            return new UnprocessableEntityObjectResult(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "One or more fields are invalid.",
                    details = errors
                }
            });
        };
    });
```

---

### 0.4 — Vite + React bootstrap

Set up `ProjectCeres.Client/` at repo root (per ADR-0034):

- Vite + React + TypeScript scaffold
- `Vite.AspNetCore` NuGet package wired into `Program.cs`
- Vitest + React Testing Library configured
- **shadcn/ui installed and initialized** — this is the design system for all Phase 2 components
- **Lucide React installed** — icon library that ships with shadcn/ui; provides icons for all action buttons
- Shared base components built before any feature component: `Button`, `Card`, `Badge`, `Table`, `Dialog` (confirmation prompt), `ProgressBar`, `Navbar`
- One "hello world" React component mounted in a Razor view to prove the full pipeline (Vite → `Vite.AspNetCore` → browser render) works end-to-end

**TDD — write tests first:**

1. Write a Vitest test asserting the hello-world component renders its expected text. Test fails (component doesn't exist).
2. Scaffold the component. Test passes. This confirms the test runner is wired and working before any real component is written.

**UI/UX — shared component library (built once, used everywhere):**

| Component                 | Used in                                                                                                                                             |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Button` (with icon slot) | All action buttons: Edit (pencil icon), Delete (trash icon), Confirm (check icon), Dismiss (x icon), Upload (upload icon), Download (download icon) |
| `Card`                    | Dashboard panels, report cards, form containers                                                                                                     |
| `Badge`                   | IsCleared status, reminder count, "Needs review" flag                                                                                               |
| `Table`                   | Transaction list, transfer list, report tables                                                                                                      |
| `Dialog`                  | All confirmation prompts (delete, dismiss, deactivate)                                                                                              |
| `ProgressBar`             | Category budget progress, goal budget progress                                                                                                      |
| `Navbar`                  | Top navigation — updated once with nav badge for upcoming payments count                                                                            |

> **Icon standard:** Every action button in the app (Edit, Delete, Confirm, Dismiss, Upload, Download, Deactivate) must include a Lucide React icon. Text label is optional for small/icon-only buttons but must be present as `aria-label` for accessibility. Applied to all views during Phase 2 as each feature area is touched.

---

## Stage 1 — Budgeting UI

The `Budget`, `CategoryBudget`, and `BudgetService` entities and service were built in Phase 1. Stage 1 adds the controller, views, and dashboard integration that were deliberately deferred.

### 1.1 — Category Budget UI

**TDD — write tests first, then implement:**

1. Write integration tests:
   - Creating a `CategoryBudget` on an Income category → rejected (expense-only rule)
   - Creating two active budgets for the same Category + Currency → rejected (duplicate active guard)
   - Creating a valid `CategoryBudget` → succeeds; `GetActualSpendAsync` returns correct sum
     All three tests fail (no controller yet; service rule tests may already exist — confirm before writing duplicates).
2. Add `BudgetsController` with Create, Edit, Deactivate actions for `CategoryBudget`.
3. Add `CategoryBudgetCreateViewModel`, `CategoryBudgetEditViewModel`.

**UI/UX:** shadcn/ui Card + Table layout. All action buttons include Lucide icons (pencil for Edit, power-off for Deactivate). Form uses shadcn/ui form components.

**Dashboard integration:**

1. Write `WebApplicationFactory` test: `GET /api/dashboard/category-budgets` → JSON array with correct `spent`, `limit`, `percentUsed` fields. Test fails.
2. Add `DashboardApiController` (first real API controller in `Controllers/Api/`).
3. Implement the endpoint. Test passes.
4. Add React `CategoryBudgetBars` component in the dashboard. Vitest test: mock fetch → renders correct progress bars.

---

### 1.2 — Goal Budget UI

**TDD — write tests first, then implement:**

1. Write integration tests:
   - `Spending` goal: progress = SUM of linked transactions — seed transactions tagged to goal, assert correct total
   - `Savings` goal: progress = balance of `LinkedAccountId` — seed account with known balance, assert correct progress
   - `LinkedAccountId` required when `GoalType = Savings` → null value rejected
   - `LinkedAccountId` must be null when `GoalType = Spending` → non-null value rejected
     All four tests fail.
2. Extend `BudgetService` with `GetProgressAsync` covering both archetypes.
3. Add Goal Budget create/edit/deactivate views. `Transaction` create/edit forms updated: optional `BudgetId` dropdown (Spending goals only, filtered to active goals matching transaction's account currency).

**UI/UX:** Same Card + ProgressBar pattern as Category Budgets. Goal type selector (Spending / Savings) conditionally shows/hides the Linked Account field.

**Dashboard integration:** `GoalBudgetBars` React component consuming `GET /api/dashboard/goal-budgets`.

---

## Stage 2 — Cleared / Reconciliation Status

### 2.1 — IsCleared UI

**TDD — write tests first, then implement:**

1. Write unit tests for fingerprint matching logic:
   - Exact match (date, amount, description, account) → duplicate candidate
   - Date ± 1 day within tolerance → duplicate candidate
   - Amount mismatch → not a candidate
   - Description mismatch → not a candidate
     All tests fail (fingerprint logic doesn't exist yet).
2. Write integration test: importing a row that matches an existing transaction → row held as `IsCleared = false` (pending), not auto-cleared. Test fails.
3. Implement `FingerprintService` (or equivalent logic in `ImportService`).
4. All tests pass.

**UI/UX:**

- Transactions Index: `IsCleared` badge (Lucide `check-circle` icon + "Cleared" label in green; "Pending" in amber) on each row
- Transaction Edit: toggle to manually mark cleared (shadcn/ui `Switch` component)
- Transfer Edit: same toggle for transfers
- Bulk action: "Mark all cleared" within a date range (confirmation dialog)

---

## Stage 2.5 — Movement Base Class + Unified Ledger

Every financial movement in the app — `Transaction`, `Transfer`, and `LiabilityPayment` — shares a common set of columns (`Id`, `Date`, `Amount`, `Description`, `IsCleared`, `CreatedAt`). Stage 2.5 formalises this at the C# model layer by introducing an abstract `Movement` base class and configuring EF Core's **Table Per Concrete type (TPC)** inheritance strategy. The DB tables are not changed — TPC maps each concrete type to its own existing table. The payoff is a unified `/Movements` ledger view where all three row types appear together, sorted by date, with the `ClearedBadge` React component providing an inline async toggle without page reload.

**Why TPC and not TPH or TPT:**

- TPH (Table Per Hierarchy) would merge all three tables into one — rejected, breaks the existing schema.
- TPT (Table Per Type) adds a shared `Movements` base table that EF joins against — unnecessary overhead.
- TPC keeps the existing `Transactions`, `Transfers`, and `LiabilityPayments` tables exactly as they are. EF Core resolves a `db.Set<Movement>()` query as a `UNION ALL` across all three tables — one query, one sorted result.

**What does NOT change:**

- `Transactions`, `Transfers`, `LiabilityPayments` DB tables — no migration, no schema change.
- `ITransactionService`, `ITransferService`, `ILiabilityPaymentService` — signatures unchanged.
- All existing service implementations — internal logic unchanged.
- All existing tests — must pass without modification after the refactor.
- The `/Transactions` and `/Transfers` pages — remain fully functional.

---

### 2.5.1 — Movement base class + TPC configuration

**TDD — write tests first, then implement:**

1. Write integration tests for `MovementService` (all fail — service doesn't exist yet):
   - `GetRecentAsync` with seeded transactions, transfers, and liability payments → returns all three types interleaved, sorted `Date DESC`, `CreatedAt DESC`
   - `GetRecentAsync` with `accountId` filter → only rows involving that account
   - `GetRecentAsync` with date range filter → only rows within range
   - `CountAsync` with same filters → correct total across all three types
2. Introduce `Movement` abstract base class in `ProjectCeres/Models/Movement.cs` with the six shared columns.
3. Make `Transaction`, `Transfer`, and `LiabilityPayment` inherit from `Movement`; remove the six shared properties from each.
4. Configure TPC in `AppDbContext.OnModelCreating`: `modelBuilder.Entity<Movement>().UseTpcMappingStrategy()`.
5. Add `DbSet<Movement>` to `AppDbContext`.
6. Run `dotnet ef migrations add TpcMovementHierarchy` — **inspect the generated migration file**. If it contains any `AlterTable`, `AddColumn`, `DropColumn`, or `CreateTable` operations, stop and investigate before applying. The expected output is an empty `Up()` and `Down()`.
7. Implement `IMovementService` / `MovementService`.
8. All new tests pass; all 187 existing tests still pass.

**New files:**

| File                                                     | Purpose                                                                                                                         |
| -------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `ProjectCeres/Models/Movement.cs`                        | Abstract base class — `Id`, `Date`, `Amount`, `Description`, `IsCleared`, `CreatedAt`                                           |
| `ProjectCeres/Services/IMovementService.cs`              | `GetRecentAsync(accountId?, from?, to?, limit, offset)` + `CountAsync(accountId?, from?, to?)`                                  |
| `ProjectCeres/Services/MovementService.cs`               | Queries `db.Set<Movement>()` — EF TPC resolves as UNION ALL                                                                     |
| `ProjectCeres/ViewModels/MovementListItemViewModel.cs`   | Unified read model: `MovementType` enum (`Transaction`, `Transfer`, `LiabilityPayment`), all display fields for all three types |
| `ProjectCeres.Tests/Integration/MovementServiceTests.cs` | Integration tests written first                                                                                                 |
| `docs/decisions/ADR-0058-movement-base-class-tpc.md`     | Records the decision, strategy chosen, and alternatives rejected                                                                |

**Modified files:**

| File                                      | Change                                                                                            |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `ProjectCeres/Models/Transaction.cs`      | Inherits from `Movement`; removes `Id`, `Date`, `Amount`, `Description`, `IsCleared`, `CreatedAt` |
| `ProjectCeres/Models/Transfer.cs`         | Same                                                                                              |
| `ProjectCeres/Models/LiabilityPayment.cs` | Same                                                                                              |
| `ProjectCeres/Data/AppDbContext.cs`       | Add `DbSet<Movement>`; configure `UseTpcMappingStrategy()` on `Movement`                          |

---

### 2.5.2 — Unified Movements view + returnUrl routing

**TDD — write tests first, then implement:**

1. Write `WebApplicationFactory` integration test: `GET /Movements` → 200 OK, page contains rows from all three movement types. Test fails (controller doesn't exist).
2. Add `MovementsController` with `Index` action only. Test passes.
3. Write tests for `returnUrl` routing:
   - `GET /Transactions/Edit/{id}?returnUrl=/Movements` → after POST save → redirects to `/Movements`
   - `GET /Transactions/Edit/{id}` (no returnUrl) → after POST save → redirects to `/Transactions`
   - Same pair for `/Transfers/Edit/{id}`
   - Same pair for delete actions on both controllers
     All tests fail.
4. Add `returnUrl` parameter to `TransactionsController` Edit (GET + POST), Delete (GET + POST) and `TransfersController` Edit (GET + POST), Delete (GET + POST). Pass `returnUrl` through hidden field on forms; redirect to it after success if present and local, otherwise fall back to own Index.
5. All tests pass.

**New files:**

| File                                              | Purpose                                                                                                                               |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `ProjectCeres/Controllers/MovementsController.cs` | `Index` action only — unified list, filter bar, pagination                                                                            |
| `ProjectCeres/Views/Movements/Index.cshtml`       | Unified ledger — all three row types interleaved, `ClearedBadge` per row, Edit + Delete buttons routing to the correct sub-controller |

**Modified files:**

| File                                                 | Change                                                                                                                |
| ---------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `ProjectCeres/Controllers/TransactionsController.cs` | `Edit` and `Delete` GET + POST actions accept optional `returnUrl`; redirect to it after success if present and local |
| `ProjectCeres/Controllers/TransfersController.cs`    | Same                                                                                                                  |
| `ProjectCeres/Views/Transactions/Edit.cshtml`        | Pass `returnUrl` through hidden field so POST preserves it                                                            |
| `ProjectCeres/Views/Transactions/Delete.cshtml`      | Same                                                                                                                  |
| `ProjectCeres/Views/Transfers/Edit.cshtml`           | Same                                                                                                                  |
| `ProjectCeres/Views/Transfers/Delete.cshtml`         | Same                                                                                                                  |
| `ProjectCeres/Views/Shared/_Layout.cshtml`           | Add "Movements" as primary nav link; keep "Transactions" and "Transfers" as secondary                                 |

---

### 2.5.3 — ClearedBadge React component + MovementsApiController

The inline "Clear / Unmark" form-submit buttons on the Transactions and Transfers Index views are replaced by a `ClearedBadge` React component that calls a PATCH endpoint — no page reload. The same component is used on the new Movements view.

**TDD — write tests first, then implement:**

1. Write `WebApplicationFactory` test:
   - `PATCH /api/movements/{id}/cleared` with `{ "type": "transaction", "cleared": true }` → 200 OK; row has `IsCleared = true` in DB
   - `PATCH /api/movements/{id}/cleared` with unknown id → 404
     All tests fail.
2. Add `MovementsApiController` in `Controllers/Api/`. Test passes.
3. Write Vitest tests for `ClearedBadge`:
   - Renders green "Cleared" badge when `isCleared = true`
   - Renders amber "Pending" badge when `isCleared = false`
   - Click calls `PATCH /api/movements/{id}/cleared` with correct body
   - Badge flips optimistically before response; reverts on error
     All tests fail.
4. Implement `ClearedBadge.tsx`. Tests pass.
5. Replace the inline form toggle buttons on `Views/Transactions/Index.cshtml` and `Views/Transfers/Index.cshtml` with the React component mount point.

**New files:**

| File                                                       | Purpose                                                                                                           |
| ---------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `ProjectCeres/Controllers/Api/MovementsApiController.cs`   | `PATCH /api/movements/{id}/cleared` — routes to `ITransactionService` or `ITransferService` based on `type` field |
| `ProjectCeres.Client/src/components/ClearedBadge.tsx`      | Clickable badge — optimistic toggle, calls API, manages loading + error state                                     |
| `ProjectCeres.Client/src/components/ClearedBadge.test.tsx` | Vitest + React Testing Library tests                                                                              |

**Modified files:**

| File                                           | Change                                                               |
| ---------------------------------------------- | -------------------------------------------------------------------- |
| `ProjectCeres/Views/Transactions/Index.cshtml` | Replace inline form toggle with `ClearedBadge` React mount point     |
| `ProjectCeres/Views/Transfers/Index.cshtml`    | Add `ClearedBadge` React mount point (no toggle existed here before) |

---

## Stage 3 — CSV Import

Most complex Stage. Sub-stages ensure TDD leads every layer.

### 3.1 — CsvImportProfile CRUD

**TDD — write tests first, then implement:**

1. Write integration tests:
   - Create profile with valid column mappings → succeeds; mappings retrievable as correct ColumnMappings object
   - Soft-delete a profile → `DeletedAt` set; profile excluded from active list but visible in deleted list within 90-day window
   - Profile deleted > 90 days ago → excluded from deleted list
   - Update a profile's mappings → new mappings retrievable
     All four tests fail.
2. Implement `ICsvImportProfileService` / `CsvImportProfileService`.
3. Add CRUD controller + views for `CsvImportProfile`.

**UI/UX:** Column mapping form — user maps CSV column names to system fields (Date, Amount, Description, Category). Deleted profiles show a countdown ("Recoverable for 87 more days"). Recover button uses Lucide `rotate-ccw` icon.

---

### 3.2 — ImportService (unit layer)

Create fixture files before writing any service code:

```
ProjectCeres.Tests/Fixtures/
  valid_import.csv          — 10 rows, mix of income/expense, all clean
  duplicate_candidates.csv  — rows partially matching existing test transactions (date ±1 day, same amount)
  invalid_rows.csv          — malformed dates, missing required fields
  xlsx_attempt.xlsx         — rejected file type
```

**TDD — write unit tests first, then implement:**

1. Write unit tests (no database — mock profile, assert output):
   - `ParseAsync` with `valid_import.csv` → returns 10 rows with correct fields
   - Negative debit in CSV → amount flipped to positive in parsed row
   - Column mapping applied → date/amount/description mapped from correct columns
   - XLSX file → `ImportResult` with error "Only CSV files are supported. Please export your bank statement as CSV."
   - `GenerateFingerprint(row)` → deterministic hash of (date, amount, description, accountId)
     All tests fail.
2. Implement `ImportService.ParseAsync`, sign-flip logic, fingerprint generation.
3. All unit tests pass.

---

### 3.3 — ImportService (integration layer)

**TDD — write integration tests first, then implement:**

1. Write integration tests:
   - Full import of `valid_import.csv` → exactly 10 transactions inserted, all `IsCleared = true`
   - Import of `duplicate_candidates.csv` → flagged rows inserted with `IsCleared = false`, not auto-cleared
   - `ImportResult` returns correct counts: `rowsImported`, `rowsFlagged`, `rowsFailed`
     All tests fail.
2. Wire `ImportService` to `TransactionService.CreateAsync`.
3. All integration tests pass.

---

### 3.4 — Import UI

**TDD — write API test first, then implement:**

1. Write `WebApplicationFactory` test: POST multipart form with `valid_import.csv` → 200 with `ImportResult` JSON matching expected shape. Test fails.
2. Add `ImportApiController` in `Controllers/Api/`.
3. Implement the endpoint. Test passes.
4. Add import upload form (Razor page): profile selector dropdown + file input. Summary page shows counts and row-level errors.

**UI/UX:**

- Upload form: shadcn/ui `Select` for profile, file input with drag-and-drop affordance
- Summary: Card per result category (Imported / Flagged / Failed) with counts and expandable error list
- Flagged rows in Transactions Index: amber "Needs review" Badge with Lucide `alert-triangle` icon

---

## Stage 4 — Transfer Attachments

Mirrors `TransactionAttachment` exactly. Pattern is proven — lower risk than Stage 3.

**TDD — write tests first, then implement:**

1. Write integration tests mirroring existing `FileAttachmentServiceTests`:
   - Upload attachment to a transfer → file written to disk, DB record created
   - Serve transfer attachment → correct file returned with `Content-Disposition: attachment`
   - Delete transfer attachment → DB record removed, file deleted from disk
   - Upload spoofed file type → rejected
     All tests fail.
2. Extend `FileAttachmentService` to support transfers (or add `ITransferAttachmentService` if SRP warrants it — decide at implementation time based on whether the path logic differs).
3. Add attachment fields to Transfer Create/Edit views. Hard-delete confirmation dialog (shadcn/ui `Dialog`).

**UI/UX:** Same attachment UX pattern as transactions. Upload (Lucide `paperclip` icon), Remove (Lucide `trash-2` icon) with confirmation dialog.

---

## Stage 5 — Liability Enhancements

### 5.1 — Repayment type and interest rate

**TDD — write tests first, then implement:**

1. Write unit/integration tests:
   - Account with `LiabilityRepaymentType = Amortising` and `InterestRate = null` → rejected
   - Account with `LiabilityRepaymentType = FullMonthly` and `InterestRate = 0.15` → rejected (interest rate must be null for FullMonthly)
   - Account with `LiabilityRepaymentType = Amortising` and valid `InterestRate` → succeeds
     All three tests fail.
2. Add validation to `AccountService.CreateAsync` / `UpdateAsync`.
3. Update Account Create/Edit views: `LiabilityRepaymentType` selector, `InterestRate` field conditionally shown (Amortising only).

**UI/UX:** shadcn/ui `Select` for repayment type. Interest rate field uses conditional show/hide via React state (on Amortising account pages) or server-side conditional rendering (on Razor edit page). Tooltip explaining FullMonthly vs Amortising for user clarity.

---

### 5.2 — Payoff projection panel

**TDD — write tests first, then implement:**

1. Write unit tests for amortisation calculation:
   - Known balance + rate + monthly payment → known payoff date and total interest (verifiable with a loan calculator)
   - "What if €X extra/month" → earlier payoff date, lower total interest
   - Interest rate = 0 → payoff date = balance ÷ monthly payment (no interest)
   - Very small balance (< one month's payment) → payoff in 1 month
     All tests fail.
2. Add `ILiabilityProjectionService` / `LiabilityProjectionService`.
3. Projection panel on Amortising account detail view. `FullMonthly` accounts: no panel rendered.

**UI/UX:** shadcn/ui Card panel on the account detail page. Shows: estimated payoff date, total interest cost, "what if" scenario (input field for extra monthly payment amount → live-updating projection via React or form submit).

---

## Stage 6 — Recurring Reminder Enhancements

### 6.1 — ReminderBehaviour enum

**TDD — write tests first, then implement:**

1. Write unit tests for `AdvanceDueDateAsync` per behaviour:
   - `SnapToCalendarDay`: confirmed on the 5th, `DayOfPeriod = 15` → next due = 15th of next month
   - `SnapToCalendarDay`: confirmed late (20th), `DayOfPeriod = 15` → next due = 15th of following month (skips forward)
   - `RelativeToLastConfirmation`: confirmed on the 20th, monthly → next due = 20th of next month
   - `ManualDate`: confirmed without a new date set → validation error
   - `ManualDate`: confirmed with a new date → `NextDueDate` set to that date
     All tests fail.
2. Extend `RecurringTransactionService.AdvanceDueDateAsync` to handle the three branches.
3. Update Recurring Transaction Create/Edit views: `ReminderBehaviour` selector. `DayOfPeriod` field hidden when `ManualDate` selected (noise reduction per design decision). `EstimatedAmount` field added.

**UI/UX:** shadcn/ui `Select` for behaviour. Conditional field visibility for `DayOfPeriod`. Confirm flow for `ManualDate` reminders shows a date picker for the next due date.

---

### 6.2 — Upcoming Payments view

**TDD — write tests first, then implement:**

1. Write integration test:
   - Seed: 3 recurring transactions — due in 5 days, due in 35 days, due today
   - `GetUpcomingAsync(withinDays: 30)` → returns exactly 2 (today + 5 days); excludes the 35-day one
     Test fails.
2. Add `GetUpcomingAsync` to `RecurringTransactionService`.
3. Add "Upcoming Payments" view (read-only). Nav badge on Navbar shows count of payments due within 30 days.

**UI/UX:** shadcn/ui Table listing name, estimated amount, account, due date, and behaviour type. "Due today" rows highlighted with amber Badge. Navbar badge uses Lucide `bell` icon with count. Count is server-rendered (not React) — no async fetch needed.

---

## Stage 7 — Reports

With `ReportGeneratorFactory` already in place (Stage 0.2), each report is a new `IReportGenerator` class. Adding a report = new class + one line in the factory. Nothing else changes.

**TDD rule for every report generator:**

1. Write integration test with known seed data → assert exact output (totals, row count, date range correctness).
2. Test fails (generator class doesn't exist).
3. Implement the generator class.
4. Add factory entry + DI registration.
5. Test passes.

**Priority order (ADR-0054):**

| #   | Report                  | Generator class                   |
| --- | ----------------------- | --------------------------------- |
| 1   | Budget vs. Actual       | `BudgetVsActualReportGenerator`   |
| 2   | Largest Expenses        | `LargestExpensesReportGenerator`  |
| 3   | Monthly Cash Flow Trend | `MonthlyCashFlowReportGenerator`  |
| 4   | Net Worth Over Time     | `NetWorthOverTimeReportGenerator` |

Deferred to Phase 3 (ADR-0054): Spending by Category Over Time, Year-over-Year Comparison.

**UI/UX:** Report pages updated to shadcn/ui Card + Table layout as each is built. Date range pickers use shadcn/ui `DatePicker`. Filter controls use shadcn/ui `Select`. Export to CSV button on each report (Lucide `download` icon).

---

## Stage 8 — Visual Dashboard + React Components

Vite pipeline is already running from Stage 0.4. Shared components are already built. This stage adds the seven dashboard chart components.

### 8.1 — shadcn/ui Razor migration

Before adding charts: migrate existing Razor pages (Accounts, Transactions, Categories, Transfers, Recurring Transactions, Settings, Reports) to use shadcn/ui design tokens and Lucide icons. This is a visual upgrade — no business logic changes.

**Scope boundary:** These Razor pages will be deleted in Phase 3. The bar is: clean, consistent, functional. Not pixel-perfect. Budget roughly one session per feature area.

Icons added to all existing action buttons across the app during this pass:

| Button                 | Icon         |
| ---------------------- | ------------ |
| Edit                   | `pencil`     |
| Delete                 | `trash-2`    |
| Deactivate             | `power-off`  |
| Confirm reminder       | `check`      |
| Dismiss reminder       | `x`          |
| Upload file            | `upload`     |
| Download / Serve file  | `download`   |
| Remove attachment      | `trash-2`    |
| Add new (any entity)   | `plus`       |
| Recover (soft-deleted) | `rotate-ccw` |

---

### 8.2 — Dashboard chart components

One React component per chart. Data served from `DashboardApiController`. Each component follows the same TDD pattern:

**TDD rule for each chart:**

1. Write `WebApplicationFactory` test: `GET /api/dashboard/{endpoint}` → assert JSON shape matches expected fields. Test fails.
2. Add endpoint to `DashboardApiController`.
3. Test passes.
4. Write Vitest test: mock fetch → component renders without error, chart container is present. Test fails.
5. Implement React component using `react-chartjs-2`.
6. Test passes.

| Chart                    | Component              | API endpoint                              | Chart.js type        |
| ------------------------ | ---------------------- | ----------------------------------------- | -------------------- |
| Net Worth Over Time      | `NetWorthChart`        | `GET /api/dashboard/net-worth-trend`      | Line                 |
| Income vs. Expenses      | `IncomeExpenseChart`   | `GET /api/dashboard/income-expense`       | Bar                  |
| Spending by Category     | `SpendingDonutChart`   | `GET /api/dashboard/spending-by-category` | Doughnut             |
| Account Balances         | `AccountBalancesChart` | `GET /api/dashboard/account-balances`     | Horizontal Bar       |
| Category Budget Progress | `CategoryBudgetBars`   | `GET /api/dashboard/category-budgets`     | (shadcn ProgressBar) |
| Goal Budget Progress     | `GoalBudgetBars`       | `GET /api/dashboard/goal-budgets`         | (shadcn ProgressBar) |
| Monthly Cash Flow        | `CashFlowChart`        | `GET /api/dashboard/cash-flow`            | Bar                  |

---

## Stage 9 — Financial Health Metrics

### 9.1 — ExcludeFromSpendable flag

**TDD — write tests first, then implement:**

1. Write unit test:
   - Two asset accounts: one with `ExcludeFromSpendable = true`, one `false`
   - `GetSpendableBalanceAsync()` → returns only the non-excluded account's balance
     Test fails.
2. Extend `DashboardService` with `GetSpendableBalanceAsync`.
3. Update Account Create/Edit: `ExcludeFromSpendable` checkbox (Asset accounts only; hidden for Liability accounts).

**UI/UX:** shadcn/ui `Checkbox` on Account form. Tooltip: "Excluded accounts don't count toward your spendable balance but still appear in net worth."

---

### 9.2 — Financial health snapshot panel

**TDD — write tests first, then implement:**

1. Write integration tests with known seed data:
   - 6 months of transactions seeded with known totals
   - Runway = (assets − liabilities) ÷ avg monthly expenses → assert exact value
   - Income vs. 6-month rolling average → assert correct average
   - Budget burn rate → assert correct percentage
     All tests fail.
2. Extend `DashboardService` with health snapshot methods.
3. Add always-on dashboard panel (server-rendered Razor, not React — no schema changes, no async fetch needed).

**UI/UX:** shadcn/ui Card with three stat rows. Runway displayed as "X months" with a colour indicator: green (> 6 months), amber (3–6 months), red (< 3 months). Income vs. rolling average shown as a delta percentage. Budget burn rate shown as a percentage of monthly limit used.

---

## Stage 10 — CSV Export

Simple, low-risk. Added last because it depends on the final stable Transaction list shape from all previous stages.

**TDD — write tests first, then implement:**

1. Write unit tests for CSV injection sanitisation:
   - Value starting with `=` → prefixed with `'`
   - Value starting with `@`, `+`, `-` → prefixed with `'`
   - Normal value → unchanged
     All tests fail.
2. Write integration test: export endpoint returns a parseable CSV with correct row count matching the seed data.
3. Implement `TransactionExportService` (or extend `TransactionService`).
4. Add `GET /Transactions/Export?{filters}` action streaming the `.csv` response.

**UI/UX:** "Export CSV" button on the Transactions page (Lucide `download` icon). Applies current active filters to the export (same date range, account, category as the current view).

---

## Verification Checklist

Once all stages are complete. **Prerequisite: all tests must be passing before starting this checklist.**

### Foundation

- [x] `dotnet build` — zero errors, zero warnings
- [x] `dotnet ef database update` — Phase 2 baseline migration applies cleanly to a fresh database
- [x] `dotnet test` — 0 failed (all Phase 1 tests still passing after refactors)
- [x] `GET /api/health` → 200 OK with `{ "status": "ok" }`
- [x] Malformed POST to any API endpoint → 422 with `error.code = "VALIDATION_ERROR"` matching `api-contract.md` shape
- [x] Vite dev server starts (`cd ProjectCeres.Client && pnpm dev`) — no errors
- [x] `pnpm test` in `ProjectCeres.Client/` — Vitest runs, 0 failed
- [x] Hello-world React component renders on the test Razor page in the browser
- [x] Navbar updated with shadcn/ui styling and Lucide icons on all action buttons

### Budgeting

- [x] Create a `CategoryBudget` on an Income category → rejected with "Budgets can only be applied to Expense categories"
- [x] Create two active `CategoryBudget` entries for the same Category + Currency → second creation rejected
- [ ] Create a valid `CategoryBudget` → dashboard progress bar appears; `spent` value updates after recording a transaction in that category _(manual — requires browser)_
- [x] Create a `Spending` goal budget → tag a transaction to it → progress bar reflects the transaction amount
- [x] Create a `Savings` goal budget linked to an account → progress bar reflects the account balance
- [x] `LinkedAccountId` required when `GoalType = Savings` → null value rejected with validation error
- [ ] Tag a transaction to a goal budget → transaction appears in the goal budget's linked transaction list _(manual — no linked transaction list view exists yet)_
- [ ] **UI/UX (Stage 1.1):** Category Budgets Index — uses Card + Table layout; Edit button has pencil icon; Deactivate button has power-off icon _(manual — requires browser)_
- [ ] **UI/UX (Stage 1.1):** `CategoryBudgetBars` React component — progress bars render with correct `spent` / `limit` values; `pnpm test` passes for this component
- [ ] **UI/UX (Stage 1.2):** Goal Budgets Index — same Card + Table layout; Goal type selector conditionally shows/hides Linked Account field on create/edit forms _(manual — requires browser)_
- [ ] **UI/UX (Stage 1.2):** `GoalBudgetBars` React component — progress bars render; `pnpm test` passes for this component

### Reconciliation / IsCleared

- [ ] Record a transaction manually → `IsCleared = false` by default; toggle to `true` via Edit view → badge updates _(manual — requires browser)_
- [ ] Fingerprint matching: import a CSV row with date ±1 day and same amount as an existing transaction → row imported with `IsCleared = false` and "Needs review" badge _(partial — IsCleared = false is tested; "Needs review" badge not present in Transactions Index view)_
- [ ] "Different transaction" reconciliation: user marks the flagged row as a new distinct transaction → row cleared automatically; no manual follow-up required _(not implemented)_
- [x] Bulk "Mark all cleared" within a date range → all transactions in range set to `IsCleared = true`
- [ ] **UI/UX (Stage 2.1):** Transactions Index — each row shows `IsCleared` badge (green "Cleared" / amber "Pending"); Transaction Edit has a toggle to manually mark cleared _(manual — requires browser)_
- [ ] **UI/UX (Stage 2.1):** Transfer Edit — same `IsCleared` toggle present _(manual — requires browser)_

### Movement Base Class + Unified Ledger

- [x] `dotnet build` — zero errors after `Transaction`, `Transfer`, and `LiabilityPayment` inherit from `Movement`
- [x] `dotnet ef migrations add TpcMovementHierarchy` — generated migration `Up()` is empty (no schema changes); if non-empty, stop and investigate before applying
- [x] `dotnet test` — all 187 existing tests still pass; all new `MovementServiceTests` pass
- [ ] `GET /Movements` — shows rows from all three types (`Transaction`, `Transfer`, `LiabilityPayment`), interleaved and sorted `Date DESC`, `CreatedAt DESC` _(partial — controller test only asserts 200 OK + word "Movements"; does not seed and assert all 3 types are present in output; manual verification needed)_
- [x] `GET /Movements` with `accountId` filter — only rows involving that account appear
- [x] `GET /Movements` with date range filter — only rows within range appear
- [x] Edit button on a Transaction row in `/Movements` → routes to `/Transactions/Edit/{id}?returnUrl=/Movements` → save → redirects back to `/Movements`
- [x] Edit button on a Transfer row in `/Movements` → routes to `/Transfers/Edit/{id}?returnUrl=/Movements` → save → redirects back to `/Movements`
- [x] Delete on a Transaction row from `/Movements` → redirects back to `/Movements`
- [x] Delete on a Transfer row from `/Movements` → redirects back to `/Movements`
- [x] Navigating directly to `/Transactions` → edit/delete still redirects back to `/Transactions` (no returnUrl set)
- [x] Navigating directly to `/Transfers` → edit/delete still redirects back to `/Transfers`
- [x] `ClearedBadge` on `/Movements` — click → `PATCH /api/movements/{id}/cleared` called; badge flips without page reload
- [x] `ClearedBadge` on `/Transactions` — same behaviour
- [x] `ClearedBadge` on `/Transfers` — same behaviour
- [x] `PATCH /api/movements/{id}/cleared` with unknown id → 404
- [x] `ClearedBadge` reverts to original state if the API call fails
- [x] `pnpm test` — all `ClearedBadge` Vitest tests pass

### CSV Import

- [x] Upload an `.xlsx` file → rejected with "Only CSV files are supported. Please export your bank statement as CSV."
- [x] Upload `valid_import.csv` with a correctly configured profile → 10 transactions created, all `IsCleared = true`; summary shows "10 imported, 0 flagged, 0 errors"
- [ ] Upload `duplicate_candidates.csv` → flagged rows appear in Transactions Index with `IsCleared = false` and "Needs review" badge; summary shows correct flagged count _(partial — IsCleared = false and flagged count are tested; "Needs review" badge not present in Transactions Index view)_
- [x] Create a `CsvImportProfile` → mappings saved; auto-applied on next import of same-format CSV
- [x] Soft-delete a `CsvImportProfile` → excluded from active list; appears in deleted list with 90-day countdown
- [x] Recover a soft-deleted profile within 90 days → profile restored to active list
- [x] Negative debit in CSV → imported as positive amount with correct Expense category direction
- [ ] **UI/UX (Stage 3.1):** CsvImportProfile Index — deleted profiles show 90-day countdown; Recover button has Lucide `rotate-ccw` icon _(manual — requires browser)_
- [ ] **UI/UX (Stage 3.4):** Import upload form — profile selector uses shadcn/ui Select; summary page shows Card per result category (Imported / Flagged / Failed) with counts _(manual — requires browser)_
- [ ] **UI/UX (Stage 3.4):** Flagged rows in Transactions Index — amber "Needs review" Badge with Lucide `alert-triangle` icon visible on flagged imports _(not yet implemented — badge missing)_

### Transfer Attachments

- [ ] Upload a file attachment on a Transfer → file saved to disk, linked to transfer, served with `Content-Disposition: attachment`
- [ ] Remove transfer attachment → DB record deleted, file deleted from disk; confirmed via hard-delete dialog
- [ ] Upload spoofed file on transfer (e.g. `.exe` renamed to `.jpg`) → rejected with "File type not allowed"
- [ ] **UI/UX (Stage 4):** Transfer Create/Edit — Upload button has Lucide `paperclip` icon; Remove button has Lucide `trash-2` icon; removal uses shadcn/ui `Dialog` confirmation, not browser `confirm()` _(manual — requires browser)_

### Liability Enhancements

- [x] Create Amortising liability account with `InterestRate = null` → rejected with validation error
- [x] Create FullMonthly liability account with `InterestRate` set → rejected with validation error
- [x] Create Amortising account with valid interest rate → payoff projection panel appears on account detail page; shows payoff date and total interest
- [x] FullMonthly account detail page → no projection panel rendered
- [x] "What if €X extra/month" input → projection updates to show earlier payoff date and lower total interest
- [ ] **UI/UX (Stage 5.1):** Account Create/Edit — `LiabilityRepaymentType` uses shadcn/ui Select; `InterestRate` field conditionally shown for Amortising only; tooltip explaining the two types is visible _(manual — requires browser)_
- [ ] **UI/UX (Stage 5.2):** Amortising account detail — projection panel uses shadcn/ui Card; shows payoff date, total interest, and "what if" input field _(manual — requires browser)_

### Recurring Reminders

- [x] `SnapToCalendarDay` reminder confirmed late (e.g. on the 20th, `DayOfPeriod = 15`) → `NextDueDate` advances to the 15th of the following month (skips forward, not backward)
- [x] `RelativeToLastConfirmation` reminder confirmed on the 20th (monthly) → `NextDueDate` = 20th of next month
- [x] `ManualDate` reminder confirmed → user prompted for next due date; `DayOfPeriod` field hidden on form
- [x] `ManualDate` reminder confirmed without setting a new date → validation error "Please set the next due date"
- [x] Upcoming Payments view: shows all recurring transactions due within 30 days; excludes those due > 30 days away
- [x] Navbar badge shows correct count of upcoming payments due within 30 days
- [x] "Due today" rows in Upcoming Payments view are highlighted with amber badge
- [ ] **UI/UX (Stage 6.1):** Recurring Transaction Create/Edit — `ReminderBehaviour` selector uses shadcn/ui Select; `DayOfPeriod` field hidden when `ManualDate` selected; `EstimatedAmount` field present _(manual — requires browser)_
- [ ] **UI/UX (Stage 6.2):** Upcoming Payments view — uses shadcn/ui Table; "Due today" rows show amber Badge; Navbar badge shows Lucide `bell` icon with count _(manual — requires browser)_

### Reports

- [ ] Budget vs. Actual report → correct planned vs. actual amounts per category for selected date range
- [ ] Largest Expenses report → Top N transactions by amount, correctly ordered, matching seed data
- [ ] Monthly Cash Flow Trend report → income and expense totals correct per month for selected range
- [ ] Net Worth Over Time report → equity values correct per month; matches account balance ledger running totals
- [ ] All four reports: date range filter applied correctly; results change when range changes
- [ ] Export CSV button on each report → downloads a parseable CSV with correct row count
- [ ] **UI/UX (Stage 7):** Reports Index — card grid layout (8 report cards); each card shows title + description with hover effect _(manual — requires browser)_
- [ ] **UI/UX (Stage 7):** Budget vs. Actual — `page-header` with Export CSV button (Lucide download icon); filter bar has labeled Currency/From/To fields; data in `dashboard-card` + `data-table` layout _(manual — requires browser)_
- [ ] **UI/UX (Stage 7):** Largest Expenses — same layout as above; "Show top" selector present in filter bar _(manual — requires browser)_
- [ ] **UI/UX (Stage 7):** Monthly Cash Flow — same layout; totals row in tfoot; Export CSV present _(manual — requires browser)_
- [ ] **UI/UX (Stage 7):** Net Worth Over Time — same layout; Export CSV present _(manual — requires browser)_
- [ ] **UI/UX (Stage 7):** CSV injection prevention — exported cell with description starting with `=` is prefixed with `'` _(covered by Csv() helper in ReportsController — verify manually or add unit test)_

### Visual Dashboard

- [ ] Dashboard loads all seven chart components; no console errors
- [ ] Net Worth Over Time chart: points correspond to known account balances at end of each month
- [ ] Income vs. Expenses chart: bars match MTD totals shown in the text dashboard
- [ ] Spending by Category donut: slices sum to total expense amount for the current month
- [ ] Account Balances chart: all active accounts shown with correct balances
- [ ] Category Budget Progress bars: `spent` and `limit` values match `BudgetService.GetActualSpendAsync` output
- [ ] Goal Budget Progress bars: `Spending` goals reflect tagged transaction totals; `Savings` goals reflect account balances
- [ ] Monthly Cash Flow chart: net values (income − expenses) correct per month

### Financial Health

- [ ] `ExcludeFromSpendable` account: balance excluded from spendable balance dashboard stat; still included in net worth
- [ ] Runway panel: value matches manual calculation of (assets − liabilities) ÷ avg monthly expenses (last 6 months)
- [ ] Runway indicator: green when > 6 months, amber when 3–6 months, red when < 3 months
- [ ] Income vs. rolling average: delta percentage updates when a new income transaction is recorded

### CSV Export

- [ ] Export all transactions (no filter) → CSV row count matches transaction count in the database
- [ ] Export with date range filter → CSV contains only transactions within that range
- [ ] CSV injection prevention: transaction with description starting with `=` → exported cell prefixed with `'`; opens correctly in Excel/Google Sheets without formula execution

### UI/UX (cross-cutting)

- [ ] All action buttons across the app have Lucide icons (Edit, Delete, Deactivate, Confirm, Dismiss, Upload, Download, Remove, Add, Recover)
- [ ] All confirmation prompts (delete, dismiss, deactivate) use shadcn/ui `Dialog` — not browser `confirm()`
- [ ] All form pages use shadcn/ui form components (inputs, selects, checkboxes, switches)
- [ ] Navbar updated: Upcoming Payments badge visible when payments due within 30 days
- [ ] `dotnet test` — final count after all stages: 0 failed
- [ ] `pnpm test` — final Vitest count: 0 failed
