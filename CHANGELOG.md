# Changelog

## [Unreleased]

### Added
- ASP.NET Core MVC project scaffold (`ProjectCeres/`) with Controllers, Models, Views, Data, Services, ViewModels, Helpers, Filters, ModelBinders layers
- xUnit test project (`ProjectCeres.Tests/`) with solution file (`ProjectCeres.sln`)
- All 13 EF Core entity models: `Account`, `AccountType`, `Category`, `CategoryType`, `Currency`, `Transaction`, `TransactionAttachment`, `Transfer`, `Budget`, `CategoryBudget`, `RecurringTransaction`, `ReportType`, `SavedReport`, `Settings`
- `AppDbContext` with full Fluent API configuration, seed data for all system-defined lookup tables, and `Frequency` enum stored as string
- Initial EF Core migration (`InitialCreate`) applied to local PostgreSQL database
- Migration `RemoveDateSeparator` — dropped redundant `DateSeparator` column from `Settings`
- Full CRUD for: Accounts, Categories, Transactions, Transfers, Recurring Transactions, Settings
- Deactivate (soft-delete) flows for Accounts and Categories
- Hard-delete with confirmation for Transactions and Transfers
- Four financial reports: Net Worth Statement, Income & Expense Summary, Expense Breakdown by Category, Transaction History
- Dashboard with month-to-date income/expense totals, savings rate, and pending recurring transaction reminders
- Opening balance management on Account Create and Edit — stored as a system-managed `Transaction` with `Category.IsSystem = true`, excluded from all reports
- File attachment upload, serve, and delete for transactions (`FileAttachmentService`) — magic-byte MIME validation via Mime-Detective, filesystem storage outside `wwwroot/`, system-generated storage paths, re-verification at serve time
- `NumberFormatHelper` — locale-aware decimal formatting for display (`FormatAmount`) and input pre-fill (`FormatInputValue`)
- `NumberFormatActionFilter` — global `IAsyncActionFilter` that injects `ViewData["NumberFormat"]` before every controller action
- `DecimalModelBinder` / `DecimalModelBinderProvider` — parses all `decimal` and `decimal?` form fields using the user's configured culture, with invariant-culture fallback for copy-pasted values
- Unit tests: `BalanceCalculationTests` (6 tests), `SavingsRateTests` (7 tests)
- Integration tests: `TransferValidationTests` (3 tests) — real PostgreSQL database with per-test transaction rollback isolation via `TestDbFixture`
- Tailwind CSS v3 build pipeline — pnpm + Tailwind CLI, input at `ProjectCeres/Styles/app.css`, output to `ProjectCeres/wwwroot/css/site.css`, wired into MSBuild pre-build target so `dotnet build` / `dotnet run` automatically regenerates CSS
- Architecture Decision Records 0012–0030 (see previous entry)
- `docs/architecture.md` — layer model, request flow, phase evolution, frontend build pipeline section
- `docs/security-model.md` — threat model, data protection, access control
- `docs/api-contract.md` — API conventions, response shapes, versioning strategy
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan
- `docs/learning-journal.md` — session learnings log including full Phase 1 retrospective
- `docs/roadmap-phase-one.md` — Phase 1 feature roadmap
- `doc-agent-instructions.md` — documentation routing rules and ADR numbering guide
- `dev-teacher` Claude Code command for post-session learning journals
- `sync-docs` Claude Code command for keeping docs in sync with code changes

### Changed
- `docs/models.md` — removed `DateSeparator` column from Settings entity; added `DateFormat` explanatory note that separator is embedded in the format string
- `docs/planning.md` — added Tailwind CSS v3 to tech stack; added shadcn/ui and JS charting libraries to Phase 2 planned features; updated working assumptions to reflect actual implementation (Service Layer pattern, no Strategy for reports)
- `docs/architecture.md` — updated directory listing to reflect actual project structure; added Frontend Build Pipeline section
- `CLAUDE.md` — updated tech stack with Tailwind CSS and pnpm; added `watch:css` command; added shadcn/ui deferral note under What NOT to Do
- All decimal display views updated to use `NumberFormatHelper.FormatAmount(...)` instead of `.ToString("N2")`
- All decimal input views updated to `type="text"` with explicit `value` pre-fill using `NumberFormatHelper.FormatInputValue(...)`

### Fixed
- `GetTransactionHistoryAsync` was missing `!t.Category.IsSystem` filter — opening balance transactions were appearing in the Transaction History report
- `[Range(typeof(decimal), ...)]` attributes on ViewModels now use `ParseLimitsInInvariantCulture = true` — previously threw `FormatException` when the system locale used comma as decimal separator
- Category Edit GET action was missing `PopulateViewBagAsync()` call — Lifestyle Tag dropdown rendered empty on the edit page

---

## [0.1.0] — 2026-01-01

### Added
- Initial project setup
- Full documentation: planning, models, legal, business model
- Architecture Decision Records 0001–0011
- Claude Code configuration (CLAUDE.md, sync-docs, hooks)
- `.env.example` with required environment variables
