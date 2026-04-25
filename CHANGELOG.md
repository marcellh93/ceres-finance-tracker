# Changelog

## [Unreleased]

### Added

**Transactions**
- Goal Budget field on Transaction Create and Edit forms is now hidden when no active Spending-type goal budgets exist — avoids showing an empty, non-functional dropdown
- Goal Budget field on Transaction Edit hidden when no Spending budgets match the transaction's account currency — prevents tagging a transaction to a mismatched budget
- `NeedsReview bool` column on `Transaction` model — set to `true` by `ImportService` when an imported row is flagged as a duplicate candidate; defaults to `false` for all manually created transactions
- `ITransactionService.MarkNeedsReviewAsync` — sets or clears `NeedsReview` on a transaction by ID
- `TransactionService.MarkNeedsReviewAsync` — implementation of the above
- `NeedsReview` field added to `TransactionListItemViewModel` and `TransactionEditViewModel`; mapped through `GetRecentAsync` and `UpdateAsync`

**Typography**
- Inter variable font self-hosted under `wwwroot/fonts/inter/` — two files cover all weights and italic variants
- IBM Plex Mono self-hosted under `wwwroot/fonts/ibm-plex-mono/` — Regular, Italic, Medium, MediumItalic, SemiBold, SemiBoldItalic, Bold, BoldItalic weights
- Inter set as the global body font for all UI text (labels, headings, buttons, body copy)
- IBM Plex Mono applied to `.amount-income`, `.amount-expense`, and `.amount-neutral` CSS classes — numeric columns in transaction and movements tables now render in monospace for clean vertical digit alignment

**Migrations**
- `AddTransactionNeedsReview` — adds `NeedsReview boolean NOT NULL DEFAULT FALSE` to the `Transactions` table

**Tests**
- `ClearedBadge.test.tsx` — 2 new tests: `needsReview = true` renders "Needs review" badge; `isCleared = true` with `needsReview = true` still renders "Cleared" (cleared state takes priority)

### Changed

**Transactions**
- Goal Budget label updated to "Goal Budget (must match account currency)" on both Create and Edit forms
- `PopulateViewBagAsync` refactored to accept an optional `currencyFilterAccountId` parameter; on Edit, filters Spending budgets to those matching the selected account's currency; sets `ViewBag.Budgets = null` (hiding the field) when no qualifying budgets exist

**CSV Import**
- `ImportService.ImportAsync` — flagged duplicate rows now call `MarkNeedsReviewAsync(true)` in addition to leaving `IsCleared = false`; the `NeedsReview` flag drives the badge in the Transactions Index

**Movements**
- `ClearedBadge` component updated with a third render state: when `isCleared = false` and `needsReview = true`, renders an amber `AlertTriangle` badge labelled "Needs review" instead of the `Clock` "Pending" badge
- `ClearedBadge` now accepts optional `needsReview` prop (defaults to `false`); existing callers (Movements, Transfers Index) are unaffected
- `main.tsx` — `ClearedBadge` mount now reads `data-needs-review` dataset attribute and passes it as the `needsReview` prop
- `Transactions/Index.cshtml` — cleared badge mount point now emits `data-needs-review` from `item.NeedsReview`

**CsvImportProfiles**
- `CsvImportProfiles/Index.cshtml` — deleted profile countdown wording corrected to "Recoverable for X more day(s)"; Recover button SVG updated to the correct Lucide `rotate-ccw` path

### Fixed

**Tests**
- `TransactionServiceTests` — 4 new integration tests: budget currency mismatch on Create throws, budget currency match on Create succeeds, same two cases for Update
- `BudgetServiceTests` and `GoalBudgetServiceTests` — constructor call updated to pass `IAccountService` as the second argument; pre-existing compilation error surfaced when the test project compiled after the constructor signature change
- `GoalBudgetServiceTests.GetProgressAsync_SavingsGoal_BalanceGrowsWithTransactions` — test was seeding an Expense transaction to grow a Savings account balance; corrected to use the Salary (Income) category so the transaction correctly increases the account balance

---

### Added

**Budgets**
- `CategoryBudgetBars` component upgraded to use shadcn `Card`, `CardHeader`, `CardTitle`, `CardContent`, `Progress`, and `Badge` — progress bars now colour-coded green/amber/red by percent used
- `GoalBudgetBars` component upgraded to use shadcn `Card`, `CardHeader`, `CardTitle`, `CardContent`, and `Progress` — goal type shown as a blue pill badge matching the Goals index table
- Dashboard layout updated with two side-by-side mount points (`data-react="category-budget-bars"` and `data-react="goal-budget-bars"`) wired in `main.tsx`

**Movements**
- Type column in Movements table now renders colour-coded pill badges — blue for Transaction, purple for Transfer, orange for Liability Payment
- `CategoryTypeName` field added to `MovementListItemViewModel`; projected from `t.Category.CategoryType.Name` in `MovementService.QueryTransactions` via `ThenInclude`
- Amount column in Movements table now colour-coded — green for Income, red for Expense (matching the Transactions table), neutral for Transfers and Liability Payments

**Reports**
- `BudgetVsActualReportGenerator` — compares active category budget limits against actual spend for a given currency and date range; returns per-category rows with limit, actual, variance, and percent used
- `LargestExpensesReportGenerator` — returns top N expense transactions ordered by amount descending for a given currency and date range; respects `Limit` parameter (default 50)
- `MonthlyCashFlowReportGenerator` — returns income, expenses, and net grouped by calendar month for a given currency and date range; defaults to last 6 months when no range supplied
- `NetWorthOverTimeReportGenerator` — returns cumulative asset, liability, and net worth snapshots at the end of each month for a given currency and date range; defaults to last 12 months
- `ReportsController` actions: `BudgetVsActual`, `LargestExpenses`, `MonthlyCashFlow`, `NetWorthOverTime` — each reads from the corresponding generator with currency/date defaults from Settings
- `Views/Reports/BudgetVsActual.cshtml` — filter bar + table with limit/actual/variance/% used columns; over-budget rows highlighted in red
- `Views/Reports/LargestExpenses.cshtml` — filter bar with Top N selector (10/25/50/100) + table with date, description, category, account, amount
- `Views/Reports/MonthlyCashFlow.cshtml` — filter bar + month-by-month table with income, expenses, net columns; period totals in tfoot
- `Views/Reports/NetWorthOverTime.cshtml` — filter bar + monthly snapshot table with assets, liabilities, net worth columns
- Reports Index updated with links to all four new reports
- `ReportTypeKey` enum extended with `BudgetVsActual = 5`, `LargestExpenses = 6`, `MonthlyCashFlow = 7`, `NetWorthOverTime = 8`
- `_ViewImports.cshtml` — `@using ProjectCeres.Services.Reports` added so report row record types are available in all views

**Migrations**
- `Stage7ReportTypeSeed` migration — inserts `ReportType` rows for the four new report types (IDs 5–8); `Up()` contains only `InsertData` operations, no schema changes

**Docs**
- Developer guide updated: `GroupBy` with anonymous object key and `GroupBy` + `Select` summary pattern in LINQ file; cumulative snapshot pattern (one-query-then-filter-in-memory) in EF Core querying file; extending the factory checklist and enum–DB alignment rule in factory pattern file; seeding lookup table rows pattern with four-step workflow in migrations file

**Tests**
- 16 integration tests for the four Stage 7 generators: `BudgetVsActual` (returns correct plan vs. actual, excludes out-of-range transactions, excludes inactive budgets, shows zero actual when no spend), `LargestExpenses` (orders by amount desc, respects limit, excludes income, excludes out-of-range), `MonthlyCashFlow` (groups by month, excludes system transactions, filters by currency, omits months with no activity), `NetWorthOverTime` (monthly snapshots, includes liabilities, filters by currency, snapshots are cumulative)
- 4 factory dispatch unit tests added to `ReportGeneratorFactoryTests` — one per new generator

### Changed

**Reports**
- `ReportGeneratorFactory` constructor extended with four new generator parameters; switch extended with four new cases
- `ReportsController` — injects the four new generators directly as constructor parameters (not via factory) since each has a dedicated action
- `AppDbContext.HasData` — `ReportType` seed extended from 4 rows to 8 rows
- `Program.cs` — four new `AddScoped` registrations for Stage 7 generators

**Budgets**
- `BudgetService` constructor updated to accept `IAccountService`; `GetAccountBalanceAsync` now delegates to `AccountService.GetBalanceAsync` — fixes Savings goal progress showing incorrect balance (transfers were excluded)
- `Budgets/Index.cshtml` — Card + Table layout, status as coloured pill badge, Edit button has pencil icon, Deactivate button has power-off icon, New button has plus icon
- `Budgets/Goals.cshtml` — same Card + Table upgrade; GoalType shown as blue pill badge; Edit/Deactivate icons
- `Budgets/Create.cshtml` and `Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, Save/Cancel buttons have check/x icons
- `Budgets/CreateGoal.cshtml` and `EditGoal.cshtml` — same form styling upgrade; conditional Linked Account field preserved
- `Budgets/Deactivate.cshtml` and `DeactivateGoal.cshtml` — descriptive confirmation card with power-off icon on the confirm button

**Transactions**
- `Transactions/Index.cshtml` — Edit/Delete row buttons upgraded to `btn-sm` with pencil/trash-2 icons; New Transaction button has plus icon
- `Transactions/Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, attachment list has download/trash icons, Save/Cancel buttons have check/x icons

**Transfers**
- `Transfers/Index.cshtml` — Edit/Delete row buttons upgraded with pencil/trash-2 icons; New Transfer button has plus icon
- `Transfers/Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, attachment list has download/trash icons, Save/Cancel buttons have check/x icons

**Movements**
- `Movements/Index.cshtml` — Edit/Delete row action buttons upgraded with pencil/trash-2 icons; static cleared/pending spans for Liability Payments converted to pill badge style

**Frontend**
- File input (`input[type="file"].form-control`) globally styled in `app.css` using Tailwind `file:` pseudo-element utilities — picker button now shows as a styled pill with a right-border divider, matching the rest of the form controls

**Docs**
- Developer guide updated: `file:` pseudo-element utilities for styling native file inputs (Tailwind guide); `Progress` + `Card` data-display panel pattern (shadcn/ui guide); `ThenInclude` formal definition for multi-level eager loading (EF Core querying guide)

---

### Added

**Recurring Reminders**
- `ReminderBehaviour` dispatch in `RecurringTransactionService` — `ConfirmAsync` and `DismissAsync` now route date advancement through three strategies: `SnapToCalendarDay` (advances to `DayOfPeriod` in the next calendar month, skips an extra month if confirmed on or after that day), `RelativeToLastConfirmation` (advances from the actual confirm date rather than the scheduled due date), `ManualDate` (throws `InvalidOperationException` unless a `nextDueDate` is supplied)
- `IRecurringTransactionService.GetUpcomingAsync(int withinDays)` — returns active reminders with `NextDueDate` between today and today + N days, ordered by due date
- `RecurringTransactionsController.Upcoming` action — serves the Upcoming Payments view at `/RecurringTransactions/Upcoming`
- `Views/RecurringTransactions/Upcoming.cshtml` — table of reminders due within 30 days; rows due today highlighted with an amber "Due today" badge
- Navbar upcoming-payments badge — server-rendered count of reminders due within 30 days passed to the React `Navbar` component via a `data-upcoming-count` attribute on `#navbar-root`

**Docs**
- Developer guide updated: string-based switch expression dispatch, `@inject` in `_Layout.cshtml` for layout-level service calls, `data-*` attribute bridge for passing server values to React components, `DateOnly` range filter pattern in EF Core queries

**Tests**
- 5 integration tests for `ReminderBehaviour` advancement: `SnapToCalendarDay` on-time → correct next month snap, `SnapToCalendarDay` confirmed late → skips forward an extra month, `RelativeToLastConfirmation` monthly → advances from confirm date, `ManualDate` without `nextDueDate` → throws, `ManualDate` with `nextDueDate` → sets exact date
- 1 integration test for `GetUpcomingAsync` — reminders due today and in 5 days included; reminder due in 35 days excluded

### Changed

**Recurring Reminders**
- `IRecurringTransactionService.ConfirmAsync` — signature extended with optional `DateOnly? nextDueDate` parameter (backward-compatible default `null`)
- `RecurringTransactionCreateViewModel` / `RecurringTransactionEditViewModel` — `ReminderBehaviour` field added (defaults to `"SnapToCalendarDay"`)
- `RecurringTransactionService.CreateAsync` / `UpdateAsync` — `ReminderBehaviour` now persisted from ViewModel
- `Views/RecurringTransactions/Create.cshtml` and `Edit.cshtml` — `ReminderBehaviour` selector added; inline JavaScript hides `DayOfPeriod` field when `ManualDate` is selected
- `Views/RecurringTransactions/Confirm.cshtml` — `nextDueDate` date picker rendered when reminder's behaviour is `ManualDate`
- `RecurringTransactionsController.Confirm` POST — accepts optional `nextDueDate` parameter and forwards it to `ConfirmAsync`
- `_Layout.cshtml` — injects `IRecurringTransactionService` to compute upcoming count server-side; count embedded as `data-upcoming-count` on `#navbar-root`
- `main.tsx` — reads `data-upcoming-count` from `#navbar-root` and passes it as `upcomingPaymentsCount` prop to `<Navbar>`

---

**Accounts**
- `ILiabilityProjectionService` / `LiabilityProjectionService` — pure calculation service for amortising loan payoff projection; computes months to payoff, payoff date, total interest, and total paid given balance, annual rate, and monthly payment; throws when payment does not cover first month's interest
- Payoff projection panel on the Account Ledger view for Amortising accounts — form accepts a monthly payment amount and returns projection stats (payoff date, months, total interest, total paid); only rendered when account is Amortising with a non-zero balance
- `LiabilityProjectionViewModel` — read model carrying `MonthsToPayoff`, `PayoffDate`, `TotalInterest`, `TotalPaid`, `MonthlyPayment`

**Docs**
- Developer guide updated: pure calculation service pattern (no `DbContext` dependency, directly unit-testable), service-layer business rule validation via `InvalidOperationException`, conditional field visibility via inline JavaScript in Razor views

**Tests**
- 5 integration tests for `AccountService` — Amortising with null interest rate rejected, FullMonthly with interest rate rejected, Amortising with valid rate succeeds, `UpdateAsync` variants for both failure cases
- 5 unit tests for `LiabilityProjectionService` — known inputs verify payoff and interest range, extra payment yields earlier payoff and less interest, zero interest rate pays off in balance ÷ payment months, very small balance pays off in 1 month, payment too small to cover interest throws

### Changed

**Accounts**
- `AccountCreateViewModel` — added `LiabilityRepaymentType` and `InterestRate` fields
- `AccountEditViewModel` — added `LiabilityRepaymentType` and `InterestRate` fields
- `AccountService.CreateAsync` — validates repayment type / interest rate rules for Liability accounts before saving; maps `LiabilityRepaymentType` and `InterestRate` onto the new entity
- `AccountService.UpdateAsync` — same validation and mapping on edit; loads `AccountType` via `Include` to access the type name
- `AccountsController` — injects `ILiabilityProjectionService`; `Edit` GET passes `ViewBag.IsLiability`; `Ledger` GET and POST compute and pass projection via ViewBag when account is Amortising with a positive balance
- `Views/Accounts/Create.cshtml` — liability repayment type selector and interest rate field added; JavaScript show/hide driven by account type and repayment type selectors
- `Views/Accounts/Edit.cshtml` — liability repayment type selector and interest rate field added (server-side conditional on `ViewBag.IsLiability`); JavaScript toggles interest rate field based on repayment type
- `Views/Accounts/Ledger.cshtml` — payoff projection panel added; rendered only for Amortising accounts with a positive balance
- `Program.cs` — `ILiabilityProjectionService` registered as scoped

---

**CSV Import**
- `ICsvImportProfileService` / `CsvImportProfileService` — full CRUD for import profiles with soft-delete and 90-day recovery window; column mappings stored as `jsonb` and deserialized to `CsvColumnMappings`
- `CsvImportProfilesController` — Index, Create, Edit, Delete (soft-delete), Recover; Razor views with Lucide icons
- `IImportService` / `ImportService` — CSV parsing via CsvHelper with user-configured column mappings, optional debit sign flip, and SHA-256 fingerprinting for duplicate detection
- `ImportApiController` at `POST /api/import` — accepts multipart form with CSV file and column mapping parameters; returns `ImportResult` JSON
- `ImportController` — Razor upload form with profile selector and manual column mapping fields; Summary page showing imported/flagged/failed counts
- `Views/Import/Index.cshtml` and `Summary.cshtml` — upload form and per-category result summary
- `CsvColumnMappings` ViewModel — carries user-configured column names and flip-debit-sign flag
- `ImportResult` ViewModel — carries `RowsImported`, `RowsFlagged`, `RowsFailed`, and a list of row-level error messages
- `ParsedImportRow` ViewModel — intermediate row produced by `ParseAsync` before persistence
- `ImportRequestViewModel` — model-bound from the multipart form for `ImportApiController`
- `ImportUploadViewModel` / `ImportSummaryViewModel` — ViewModels for the Razor upload and summary pages
- Fixture files: `valid_import.csv`, `duplicate_candidates.csv`, `invalid_rows.csv`, `xlsx_attempt.xlsx` — used by unit and integration tests; registered with `CopyToOutputDirectory: PreserveNewest`

**Docs**
- Developer guide updated: CSV parsing with CsvHelper, SHA-256 fingerprinting, soft-delete with time-bounded recovery, `jsonb` column mapping in EF Core, `Mock<IFormFile>` with `CopyToAsync` setup, fixture files via `CopyToOutputDirectory`, optional constructor parameters for partial unit testability

**Tests**
- 5 integration tests for `CsvImportProfileService` — create/retrieve, soft-delete exclusion from active list, 90-day purge window, update mappings, recover within window
- 6 unit tests for `ImportService` — `ParseAsync` with valid CSV, debit sign flip, custom column mapping, XLSX rejection, `GenerateFingerprint` determinism, fingerprint sensitivity to amount change
- 3 integration tests for `ImportService.ImportAsync` — 10 rows all cleared, duplicate flagged as `IsCleared = false`, result count correctness
- 3 `WebApplicationFactory` tests for `ImportApiController` — shape test, missing `accountId` → 422 with `VALIDATION_ERROR`, `.xlsx` file → 400 with message

**Movements**
- `MovementsApiController` at `PATCH /api/movements/{id}/cleared` — routes to `ITransactionService` or `ITransferService` based on `type` field in request body; returns 404 for unknown id, 400 for unknown type
- React `ClearedBadge` component — clickable badge that calls `PATCH /api/movements/{id}/cleared`, flips state optimistically on click, and reverts to original state on API error or network failure
- `MovementsApiTests` — 5 `WebApplicationFactory` integration tests covering transaction clear, toggle back to false, transfer clear, unknown id → 404, and unknown type → 400
- `ClearedBadge.test.tsx` — 5 Vitest tests covering static rendering (Cleared/Pending), PATCH call correctness, optimistic flip, revert on HTTP error, and revert on network error
- `ClearedBadge` mount point in `main.tsx` — mounts from `[data-react="cleared-badge"]` elements; reads `data-id`, `data-movement-type`, and `data-cleared` dataset attributes
- `MovementsController` with `Index` action — unified ledger showing all three movement types (Transaction, Transfer, LiabilityPayment) interleaved, with account/date filters and pagination
- `Views/Movements/Index.cshtml` — unified table rendering all three row types with type-specific column display, cleared badge, and Edit/Delete links that pass `returnUrl=/Movements`
- `MovementsControllerTests` — 10 `WebApplicationFactory` tests covering: `GET /Movements` returns 200, Transaction/Transfer Edit and Delete redirect to `returnUrl` when present and local, fall back to own Index when absent, and open redirect safety (external URL rejected)
- `WafCollection.cs` — `[CollectionDefinition("IntegrationTests")]` grouping all 19 integration test classes into one xUnit collection to prevent parallel races on `project_ceres_test`

**Security**
- Open redirect rule added to `docs/security-model.md` under Input Validation Rules and the phase table — `Url.IsLocalUrl(returnUrl)` required on every action that accepts a `returnUrl` parameter

**Budgets**
- `ICategoryBudgetService` / `CategoryBudgetService` — monthly spend caps for expense categories; guards against non-expense categories and duplicate active budgets
- `CategoryBudgetService.GetActualSpendAsync(id, year, month)` — caller-specified period for current dashboard use and future Budget vs. Actual reports
- `BudgetsController` — single controller covering Category Budgets (Index, Create, Edit, Deactivate) and Goal Budgets (Goals, CreateGoal, EditGoal, DeactivateGoal)
- `BudgetProgressResult` model — computed `Remaining` and `PercentUsed` properties; never stored as columns
- Goal Budget `GoalType` and `LinkedAccountId` — two archetypes: Spending (sums tagged transactions) and Savings (reads linked account balance)
- `BudgetService.GetProgressAsync` — returns `BudgetProgressResult` for a goal budget; routes to transaction-sum or account-balance query based on `GoalType`
- `DashboardApiController` at `/api/dashboard/category-budgets` and `/api/dashboard/goal-budgets` — JSON endpoints for React components
- React `CategoryBudgetBars` component — fetches category budgets, renders progress bars colored green/amber/red by percent used
- React `GoalBudgetBars` component — fetches goal budgets, renders progress bars in blue/green

**Views**
- Category Budgets CRUD views: Index, Create (expense categories only), Edit, Deactivate confirmation
- Goal Budgets CRUD views: Goals index, CreateGoal, EditGoal (with inline JS show/hide for LinkedAccount field), DeactivateGoal confirmation

**Architecture Decision Records**
- ADR-0056 — `GetActualSpendAsync` caller-specified year/month signature
- ADR-0057 — single `BudgetsController` with documented refactor trigger conditions

**Tests**
- `TestWebApplicationFactory` — custom `WebApplicationFactory<Program>` subclass that overrides `ConnectionStrings:DefaultConnection` to `project_ceres_test` in `ConfigureWebHost`; replaces bare `WebApplicationFactory<Program>` as the shared collection fixture, making test database isolation structural rather than per-class
- 11 integration tests for `CategoryBudgetService` (all guards, actual spend calculation, deactivate, getAll)
- 8 integration tests for `GoalBudgetService` (GoalType validation, GetProgressAsync for both archetypes)
- 2 `WebApplicationFactory` tests for `DashboardApiController` verifying JSON shape
- 4 Vitest component tests for `CategoryBudgetBars` and `GoalBudgetBars` (mocked fetch, async DOM assertions)

**Transfer Attachments**
- `IFileAttachmentService` extended with three new methods: `UploadForTransferAsync`, `GetTransferAttachmentAsync`, `DeleteTransferAttachmentAsync` — same MIME whitelist and size limit as transaction attachments; transfer files stored under `uploads/transfers/{transferId}/`
- `AttachmentsController` — three new actions: `UploadForTransfer` (POST), `DownloadTransfer` (GET), `DeleteTransfer` (POST)
- Attachment section added to `Views/Transfers/Edit.cshtml` — file list with download links and per-attachment Remove button; file input with accepted type hint; out-of-form delete forms linked via HTML `form=` attribute

**Docs**
- Developer guide updated: extending a service interface to support a second entity type (reuse vs. split decision), subdirectory isolation for multi-entity file storage

**Tests**
- `TransferAttachmentServiceTests` — 4 integration tests: upload persists DB record and writes file to disk, serve returns correct data and metadata, delete removes DB record and file from disk, spoofed file type rejected with "not allowed" message

### Changed

**Transfer Attachments**
- `TransferEditViewModel` — added `IFormFile? Attachment` property
- `TransfersController` — injects `IFileAttachmentService`; Edit GET loads existing attachments into `ViewBag`; Edit POST handles optional file upload after record save; `enctype="multipart/form-data"` added to the Edit form

**CSV Import**
- `ProjectCeres.csproj` — CsvHelper 33.0.1 added

**Movements**
- `Movements/Index.cshtml` — static cleared/pending badge replaced with `ClearedBadge` React mount point for Transaction and Transfer rows; LiabilityPayment rows retain a static read-only badge
- `Transfers/Index.cshtml` — Status column added with `ClearedBadge` React mount point per row
- `Transactions/Index.cshtml` — Status column replaced with `ClearedBadge` React mount point; inline form toggle (ToggleCleared POST) removed from the Actions column
- Navbar updated: "Movements" added as a primary nav link between Dashboard and Accounts

**Transactions**
- `TransactionsController` Edit and Delete (GET + POST) accept optional `returnUrl` — redirects to it after success if local, falls back to `/Transactions` otherwise
- `Views/Transactions/Edit.cshtml` and `Delete.cshtml` — hidden `returnUrl` field added; Cancel link respects `returnUrl`
- Goal Budget dropdown on Transaction Create/Edit now filters to `GoalType == "Spending"` only — Savings goals track progress via account balance, not transaction tagging

**Transfers**
- `TransfersController` Edit and Delete (GET + POST) accept optional `returnUrl` — same pattern as Transactions
- `Views/Transfers/Edit.cshtml` and `Delete.cshtml` — hidden `returnUrl` field added; Cancel link respects `returnUrl`

**Navbar**
- Added Budgets and Import links between Categories and Reminders

**Data Models**
- `Budget` entity extended with `GoalType` (required) and `LinkedAccountId` (nullable FK to Account)

**Tests**
- `ApiInfrastructureTests`, `DashboardApiTests`, `MovementsApiTests`, `MovementsControllerTests` — updated to accept `TestWebApplicationFactory` instead of `WebApplicationFactory<Program>`; per-class `WithWebHostBuilder`/`UseSetting` overrides removed as redundant
- All 19 integration test classes annotated with `[Collection("IntegrationTests")]` — eliminates parallel races between `WebApplicationFactory` tests and `TestDbFixture` tests on `project_ceres_test`
- `MovementsControllerTests` WAF now overrides `ConnectionStrings:DefaultConnection` to target `project_ceres_test` instead of the dev database; seeded rows deleted via `ExecuteDeleteAsync` in `DisposeAsync`

### Fixed

**Tests**
- WAF tests were seeding data into the dev database (`project_ceres`) because individual test classes forgot to call `WithWebHostBuilder`; structural fix via `TestWebApplicationFactory` subclass makes this impossible going forward; orphaned rows cleaned from dev database (6 transactions, 3 transfers, 12 accounts removed)
- `ReportServiceTests` and `MovementServiceTests` were flakily failing when run in parallel with WAF tests — WAF tests were writing to `project_ceres_test` concurrently with `TestDbFixture` rollback transactions; resolved by the `[Collection("IntegrationTests")]` grouping
- WAF tests were seeding data into the dev database (`project_ceres`) because no connection string override was in place; dev database cleaned (39 accounts, 6 transfers, 9 transactions removed)

**Movements**
- `ClearedBadge` was rendering with identical gray styling for both Cleared and Pending states because `badge-success` and `badge-warning` CSS classes were not defined; replaced with Tailwind utility classes (`bg-green-100 text-green-700` for Cleared, `bg-yellow-100 text-yellow-700` for Pending)

### Removed

**Frontend**
- `HelloWorld` component and its test removed — React pipeline verification complete, component no longer needed

---

## [0.2.0] — 2026-04-21

### Phase 1

#### Added

**Project scaffold**
- ASP.NET Core MVC project scaffold (`ProjectCeres/`) with Controllers, Models, Views, Data, Services, ViewModels, Helpers, Filters, ModelBinders layers
- xUnit test project (`ProjectCeres.Tests/`) with solution file (`ProjectCeres.sln`)

**Data model**
- All 13 EF Core entity models: `Account`, `AccountType`, `Category`, `CategoryType`, `Currency`, `Transaction`, `TransactionAttachment`, `Transfer`, `Budget`, `CategoryBudget`, `RecurringTransaction`, `ReportType`, `SavedReport`, `Settings`
- `AppDbContext` with full Fluent API configuration, seed data for all system-defined lookup tables, and `Frequency` enum stored as string
- `LiabilityPayment` model — dedicated entity for recording debt payments from an asset account to a liability account (`Models/LiabilityPayment.cs`)

**Migrations**
- Initial EF Core migration (`InitialCreate`) applied to local PostgreSQL database
- Migration `RemoveDateSeparator` — dropped redundant `DateSeparator` column from `Settings`
- Migration `AddLiabilityPayment` — adds `LiabilityPayments` table with FK references to `Accounts`

**Features**
- Full CRUD for: Accounts, Categories, Transactions, Transfers, Recurring Transactions, Settings
- Deactivate (soft-delete) flows for Accounts and Categories
- Hard-delete with confirmation for Transactions and Transfers
- Four financial reports: Net Worth Statement, Income & Expense Summary, Expense Breakdown by Category, Transaction History
- Dashboard with month-to-date income/expense totals, savings rate, and pending recurring transaction reminders
- Opening balance management on Account Create and Edit — stored as a system-managed `Transaction` with `Category.IsSystem = true`, excluded from all reports
- File attachment upload, serve, and delete for transactions (`FileAttachmentService`) — magic-byte MIME validation via Mime-Detective, filesystem storage outside `wwwroot/`, system-generated storage paths, re-verification at serve time

**Accounts**
- Per-account balance ledger view at `/Accounts/{id}/Ledger` — shows every entry contributing to the account balance (opening balance, transactions, transfers, liability payments) in chronological order with a running balance column; linked from the Accounts index

**Transactions**
- File attachment field on the New Transaction form — optional, regular transactions only; validates magic bytes and file size before saving the transaction record
- File attachment upload on Edit Transaction — new attachment uploaded as part of the Save Changes submission; no separate Upload button required

**Liability payments**
- `ILiabilityPaymentService` / `LiabilityPaymentService` — full CRUD for liability payments with validation (currency match, account type guards, date-before-opening-balance guard)
- `TransactionListItemViewModel` — unified read model for the Transactions Index list that represents either a regular `Transaction` or a `LiabilityPayment` row via a `TransactionType` discriminator field
- `TransactionService.GetByIdForEditAsync` — returns a fully-populated `TransactionEditViewModel` covering both regular and liability-payment records
- `TransactionsController` — conditional server-side validation that removes `CategoryId` requirement for `LiabilityPayment` type and `LiabilityAccountId` requirement for regular transactions

**Number formatting**
- `NumberFormatHelper` — locale-aware decimal formatting for display (`FormatAmount`) and input pre-fill (`FormatInputValue`)
- `NumberFormatActionFilter` — global `IAsyncActionFilter` that injects `ViewData["NumberFormat"]` before every controller action
- `DecimalModelBinder` / `DecimalModelBinderProvider` — parses all `decimal` and `decimal?` form fields using the user's configured culture, with invariant-culture fallback for copy-pasted values

**Services**
- `IFileAttachmentService.ValidateAsync` — pre-save file validation method that runs size and magic-byte checks without writing anything; used by the Create flow to fail fast before any DB write

**Tests**
- Unit tests: `BalanceCalculationTests` (6 tests), `SavingsRateTests` (7 tests), `CategoryBudgetGuardTests`
- Unit tests for `NumberFormatHelper` — `FormatAmount`, `FormatInputValue`, and `TryParseDecimal` including round-trip correctness (12 tests in `NumberFormatHelperTests.cs`)
- Unit tests for decimal parsing — both number format modes, invariant-input tolerance, thousands separators, and invalid input (12 tests in `DecimalParsingTests.cs`)
- Integration tests: `TransferValidationTests` (3 tests) — real PostgreSQL database with per-test transaction rollback isolation via `TestDbFixture`
- Integration tests for `AccountService`, `CategoryService`, `TransactionService`, `TransferService`
- Integration tests for `BudgetService` — `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeactivateAsync`, and `GetActualSpendAsync`
- Integration tests for `RecurringTransactionService` — all 7 methods including `ConfirmAsync` and `DismissAsync`
- Integration tests for `ReportService` — all 4 report types with date range, account, category, and pagination filters
- Integration tests for `DashboardService` — MTD income/expense sums, savings rate, pending reminder count
- Integration tests for `SettingsService` — `GetAsync`, `UpdateAsync`, `EnsureExistsAsync` including create-from-scratch paths
- Integration tests for `FileAttachmentService` — `UploadAsync` (happy path + all validation guards), `GetAsync`, `DeleteAsync` using a per-test temp directory and `Mock<IWebHostEnvironment>`

**Frontend build**
- Tailwind CSS v3 build pipeline — pnpm + Tailwind CLI, input at `ProjectCeres/Styles/app.css`, output to `ProjectCeres/wwwroot/css/site.css`, wired into MSBuild pre-build target so `dotnet build` / `dotnet run` automatically regenerates CSS

**Documentation**
- Architecture Decision Records 0012–0031
- `docs/architecture.md` — layer model, request flow, phase evolution, frontend build pipeline section
- `docs/security-model.md` — threat model, data protection, access control
- `docs/api-contract.md` — API conventions, response shapes, versioning strategy
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan
- `docs/decisions/ADR-0004` — implementation note added documenting the tolerant decimal parsing fix and its implication for Phase 2 CSV/OFX import
- `docs/testing.md` — unit test priority list updated; Phase 1 unit and integration test coverage tables added
- `docs/guide/` — structured developer guide organized by stack topic; 23+ topic files across 7 modules
- `docs/roadmap-phase-one.md` — Phase 1 feature roadmap and verification checklist (fully checked)
- `docs/guide/07-testing/mocking-with-moq.md` — new guide file covering `Mock<T>`, `.Setup()`, `.Returns()`, and MIME detection test patterns
- `docs/guide/02-dotnet-platform/logging-with-ilogger.md` — new guide file covering `ILogger<T>` injection, log levels, structured placeholders
- `docs/guide/03-aspnetcore-mvc/model-binding-and-validation.md` — `DecimalModelBinder` section updated with tolerant parsing explanation and dry run of the corruption case
- `docs/guide/07-testing/test-structure-and-patterns.md` — new pattern added: extracting logic out of framework types for unit testability
- `dev-teacher` and `sync-docs` Claude Code skills added

#### Changed

**Transactions**
- `ITransactionService.CreateAsync` now returns `Guid` (the new record's ID) instead of `void`, enabling post-save operations like attaching a file
- Transaction form field order unified: Attachment field moved to after Description on both Create and Edit views
- Transaction Edit view: attachment upload merged into main form via `enctype="multipart/form-data"`; separate Upload form and button removed
- Transaction Edit view: per-attachment Remove forms moved outside the main `<form>` element and linked via HTML `form=` attribute — nested forms are silently ignored by browsers
- `TransactionService.DeleteAsync` now loads attachments and calls `FileAttachmentService.DeleteAsync` for each before removing the transaction row, ensuring disk cleanup on transaction delete
- `TransactionEditViewModel` — added `IFormFile? Attachment` property to support upload-on-save on the Edit flow
- `TransactionsController.Index` — now returns `IEnumerable<TransactionListItemViewModel>` (merged regular transactions + liability payments) instead of raw `Transaction` entities
- `TransactionsController.Create` POST — branches on `vm.TransactionType`; routes to `LiabilityPaymentService.CreateAsync` for `LiabilityPayment`, `TransactionService.CreateAsync` otherwise
- `TransactionCreateViewModel` / `TransactionEditViewModel` — added `TransactionType`, `LiabilityAccountId` fields to support the unified form

**Number formatting**
- `DecimalModelBinder` parsing logic extracted into `NumberFormatHelper.TryParseDecimal` — a pure static method with no framework dependencies, making it independently unit-testable
- All decimal display views updated to use `NumberFormatHelper.FormatAmount(...)` instead of `.ToString("N2")`
- All decimal input views updated to `type="text"` with explicit `value` pre-fill using `NumberFormatHelper.FormatInputValue(...)`

#### Fixed

**Number formatting**
- `DecimalModelBinder` silently corrupted amounts entered in invariant format (`100.00`) when the number format was set to `comma_decimal` — the period was interpreted as a thousands separator, producing `10000.00`; parsing order is now adjusted to detect and handle this case correctly
- `[Range(typeof(decimal), ...)]` attributes on ViewModels now use `ParseLimitsInInvariantCulture = true` — previously threw `FormatException` when the system locale used comma as decimal separator

**Recurring Transactions**
- Confirming a recurring reminder always reloaded the form without recording — `AccountId`, `CategoryId`, and `TransactionType` were missing from the Confirm view as hidden inputs, causing `ModelState.IsValid` to silently fail on every POST

**Transactions**
- Attachment upload section was absent from the New Transaction (Create) form — attachments could only be added by editing an existing transaction
- Remove button on Edit Transaction did not delete the file from disk or the DB record — the Remove `<form>` was nested inside the main edit `<form>`, causing browsers to silently discard it
- Deleting a transaction did not clean up its attached files from disk — `TransactionService.DeleteAsync` now iterates attachments and calls `FileAttachmentService.DeleteAsync` before removing the transaction row

**Settings**
- `SettingsService.UpdateAsync` — fixed dead guard (`if (settings.Id == 0)`) that prevented creating a settings row when none existed; replaced with an explicit `isNew` boolean

**Accounts**
- `AccountService.GetBalanceAsync` — transfer amounts are now correctly added/subtracted from account balances (`transfersIn` increases balance, `transfersOut` decreases it)
- `AccountService.GetBalanceAsync` — system (opening balance) transactions now always add to balance regardless of account type; regular transactions on liability accounts correctly apply inverted polarity

**Liability account balance**
- `ReportService` — liability account balances in Net Worth and Income & Expense reports now use the same polarity logic as `AccountService`, ensuring consistent figures across views
- Opening balance transactions on liability accounts were incorrectly being subtracted from the balance instead of added

**Reports**
- `GetTransactionHistoryAsync` was missing `!t.Category.IsSystem` filter — opening balance transactions were appearing in the Transaction History report

**Categories**
- Category Edit GET action was missing `PopulateViewBagAsync()` call — Lifestyle Tag dropdown rendered empty on the edit page

---

## [0.1.0] — 2026-01-01

### Added
- Initial project setup
- Full documentation: planning, models, legal, business model
- Architecture Decision Records 0001–0011
- Claude Code configuration (CLAUDE.md, sync-docs, hooks)
- `.env.example` with required environment variables
