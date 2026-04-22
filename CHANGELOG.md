# Changelog

## [Unreleased]

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
