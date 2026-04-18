# Changelog

## [Unreleased]

### Added

**Accounts**
- Per-account balance ledger view at `/Accounts/{id}/Ledger` — shows every entry contributing to the account balance (opening balance, transactions, transfers, liability payments) in chronological order with a running balance column; linked from the Accounts index

**Transactions**
- File attachment field on the New Transaction form — optional, regular transactions only; validates magic bytes and file size before saving the transaction record

**Services**
- `IFileAttachmentService.ValidateAsync` — pre-save file validation method that runs size and magic-byte checks without writing anything; used by the Create flow to fail fast before any DB write

### Changed

**Transactions**
- `ITransactionService.CreateAsync` now returns `Guid` (the new record's ID) instead of `void`, enabling post-save operations like attaching a file
- Transaction Edit view: attachments section moved above the Save/Cancel buttons for better UX; submit button linked back to form via `form=` attribute to support the layout

**Documentation**
- `docs/planning.md` — added account ledger sub-page and attachment-on-create flow to Phase 1 features; balance audit trail open question removed (resolved)
- `docs/planning-resolved.md` — balance audit trail decision archived
- `docs/planning-phase2.md` — added schema note clarifying Budget/CategoryBudget entities were scaffolded in Phase 1
- `docs/roadmap-phase-one.md` — Budget phase placement clarified in Steps 2, 5, and 7; verification checklist updated with session results; balance audit trail item marked complete
- `docs/security-model.md` — added pre-save `ValidateAsync` pattern to upload validation rules

### Fixed

**Transactions**
- Attachment upload section was absent from the New Transaction (Create) form — attachments could only be added by editing an existing transaction

---

### Added

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

**Liability payments**
- `ILiabilityPaymentService` / `LiabilityPaymentService` — full CRUD for liability payments with validation (currency match, account type guards, date-before-opening-balance guard)
- `TransactionListItemViewModel` — unified read model for the Transactions Index list that represents either a regular `Transaction` or a `LiabilityPayment` row via a `TransactionType` discriminator field
- `TransactionService.GetByIdForEditAsync` — returns a fully-populated `TransactionEditViewModel` covering both regular and liability-payment records
- `TransactionsController` — conditional server-side validation that removes `CategoryId` requirement for `LiabilityPayment` type and `LiabilityAccountId` requirement for regular transactions

**Number formatting**
- `NumberFormatHelper` — locale-aware decimal formatting for display (`FormatAmount`) and input pre-fill (`FormatInputValue`)
- `NumberFormatActionFilter` — global `IAsyncActionFilter` that injects `ViewData["NumberFormat"]` before every controller action
- `DecimalModelBinder` / `DecimalModelBinderProvider` — parses all `decimal` and `decimal?` form fields using the user's configured culture, with invariant-culture fallback for copy-pasted values

**Tests**
- Unit tests: `BalanceCalculationTests` (6 tests), `SavingsRateTests` (7 tests)
- Integration tests: `TransferValidationTests` (3 tests) — real PostgreSQL database with per-test transaction rollback isolation via `TestDbFixture`
- Integration tests for `BudgetService` — `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeactivateAsync`, and `GetActualSpendAsync` (including isolation between budgets)
- Integration tests for `RecurringTransactionService` — all 7 methods including `ConfirmAsync` (transaction creation + `NextDueDate` advancement for all four frequencies) and `DismissAsync` (date advancement with no transaction created)
- Integration tests for `ReportService` — `GetNetWorthAsync`, `GetIncomeExpenseSummaryAsync`, `GetExpenseBreakdownAsync`, and `GetTransactionHistoryAsync` with date range, account, category, and pagination filters
- Integration tests for `DashboardService` — MTD income/expense sums, savings rate, pending reminder count
- Integration tests for `SettingsService` — `GetAsync`, `UpdateAsync`, `EnsureExistsAsync` including create-from-scratch paths
- Integration tests for `FileAttachmentService` — `UploadAsync` (happy path + all validation guards), `GetAsync`, `DeleteAsync` using a per-test temp directory and a `Mock<IWebHostEnvironment>`

**Documentation**
- `docs/guide/07-testing/mocking-with-moq.md` — new guide file covering `Mock<T>`, `.Setup()`, `.Returns()`, `It.IsAny<T>()`, mocking `IWebHostEnvironment`, constructing fake `IFormFile`, and magic-byte patterns for MIME detection tests
- `docs/guide/02-dotnet-platform/logging-with-ilogger.md` — new guide file covering `ILogger<T>` injection, log level table, structured `{Property}` placeholders vs. string interpolation, and `appsettings.json` log-level filtering

**Frontend build**
- Tailwind CSS v3 build pipeline — pnpm + Tailwind CLI, input at `ProjectCeres/Styles/app.css`, output to `ProjectCeres/wwwroot/css/site.css`, wired into MSBuild pre-build target so `dotnet build` / `dotnet run` automatically regenerates CSS

**Documentation**
- Architecture Decision Records 0012–0030 (see previous entry)
- `docs/architecture.md` — layer model, request flow, phase evolution, frontend build pipeline section
- `docs/security-model.md` — threat model, data protection, access control
- `docs/api-contract.md` — API conventions, response shapes, versioning strategy
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan
- `docs/guide/` — structured developer guide organized by stack topic (replaced learning-journal.md); 23 topic files across 7 modules
- `docs/roadmap-phase-one.md` — Phase 1 feature roadmap
- `.claude/skills/sync-docs/doc-agent-instructions.md` — documentation routing rules and ADR numbering guide
- `dev-teacher` Claude Code skill for post-session learning journals
- `sync-docs` Claude Code skill for keeping docs in sync with code changes

### Changed

**Liability payments**
- `TransactionsController.Index` — now returns `IEnumerable<TransactionListItemViewModel>` (merged regular transactions + liability payments) instead of raw `Transaction` entities
- `TransactionsController.Create` POST — branches on `vm.TransactionType`; routes to `LiabilityPaymentService.CreateAsync` for `LiabilityPayment`, `TransactionService.CreateAsync` otherwise; success message distinguishes between the two types
- `TransactionCreateViewModel` / `TransactionEditViewModel` — added `TransactionType`, `LiabilityAccountId` fields to support the unified form
- `Views/Transactions/Create.cshtml`, `Edit.cshtml`, `Index.cshtml`, `Delete.cshtml` — updated to handle both transaction types in a single form/list

**Number formatting**
- All decimal display views updated to use `NumberFormatHelper.FormatAmount(...)` instead of `.ToString("N2")`
- All decimal input views updated to `type="text"` with explicit `value` pre-fill using `NumberFormatHelper.FormatInputValue(...)`

**Documentation**
- `docs/testing.md` — added Phase 1 service coverage table listing all 11 integration-tested services
- `docs/planning.md` — documented that transfers affect account balance (source decreases, destination increases, no `Transaction` rows created)
- `docs/guide/07-testing/test-structure-and-patterns.md` — added temp directory cleanup pattern for file I/O integration tests
- `docs/guide/02-dotnet-platform/dependency-injection.md` — added `AddControllersWithViews()`, action filters with the `ServiceFilter` pattern, and `IServiceProvider` manual resolution
- `docs/guide/02-dotnet-platform/configuration-and-settings.md` — added User Secrets section with `init`, `set`, and `list` commands and storage path
- `docs/models.md` — removed `DateSeparator` column from Settings entity; added `DateFormat` explanatory note that separator is embedded in the format string
- `docs/planning.md` — added Tailwind CSS v3 to tech stack; added shadcn/ui and JS charting libraries to Phase 2 planned features; updated working assumptions to reflect actual implementation (Service Layer pattern, no Strategy for reports); added pointer to `docs/testing.md` in the Testing Strategy section; added open questions for Phase 1
- `docs/architecture.md` — updated directory listing to reflect actual project structure; added Frontend Build Pipeline section
- `CLAUDE.md` — updated tech stack with Tailwind CSS and pnpm; added `watch:css` command; added shadcn/ui deferral note under What NOT to Do
- `docs/planning-phase2.md`, `docs/planning-phase3.md`, `docs/planning-future.md` — open questions added to each
- `docs/guide/` — reviewed and updated topic files across all modules

### Fixed

**Settings**
- `SettingsService.UpdateAsync` — fixed dead guard (`if (settings.Id == 0)`) that prevented creating a settings row when none existed; `CreateDefaults()` always sets `Id = 1` so the guard never fired; replaced with an explicit `isNew` boolean

**Transactions**
- `TransactionService.GetRecentAsync` — merged transaction list is now sorted by date descending, then by `CreatedAt` descending so same-day entries appear in insertion order

**Liability Payments**
- `TransactionListItemViewModel` — added `CreatedAt` field so liability payment rows carry their creation timestamp for correct sort ordering alongside regular transactions

**Accounts**
- `AccountService.GetBalanceAsync` — transfer amounts are now correctly added/subtracted from account balances (`transfersIn` increases balance, `transfersOut` decreases it)

**Liability account balance**
- `AccountService.GetBalanceAsync` — system (opening balance) transactions now always add to balance regardless of account type; regular transactions on liability accounts correctly apply inverted polarity (expense adds, income subtracts)
- `ReportService` — liability account balances in Net Worth and Income & Expense reports now use the same polarity logic as `AccountService`, ensuring consistent figures across views
- Opening balance transactions on liability accounts were incorrectly being subtracted from the balance instead of added

**Reports**
- `GetTransactionHistoryAsync` was missing `!t.Category.IsSystem` filter — opening balance transactions were appearing in the Transaction History report

**Number formatting**
- `[Range(typeof(decimal), ...)]` attributes on ViewModels now use `ParseLimitsInInvariantCulture = true` — previously threw `FormatException` when the system locale used comma as decimal separator

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
