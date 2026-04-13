# Phase 1 Build Roadmap

This document is the step-by-step build order for Phase 1. Each step can be reviewed and approved before moving on. Nothing runs until you say so.

---

## Step 1 — Project Scaffold ✅

Create the solution and two projects:

```
ProjectCeres/          ← ASP.NET Core MVC app
ProjectCeres.Tests/    ← xUnit test project
ProjectCeres.sln       ← solution file
```

**Commands (to be run when you approve):**
```bash
dotnet new mvc -n ProjectCeres -o ProjectCeres
dotnet new xunit -n ProjectCeres.Tests -o ProjectCeres.Tests
dotnet new sln -n ProjectCeres
dotnet sln add ProjectCeres/ProjectCeres.csproj
dotnet sln add ProjectCeres.Tests/ProjectCeres.Tests.csproj
```

**NuGet packages for ProjectCeres:**
- `Npgsql.EntityFrameworkCore.PostgreSQL` — PostgreSQL driver for EF Core
- `Microsoft.EntityFrameworkCore.Design` — enables `dotnet ef` CLI commands
- `MimeDetective` — magic-byte MIME verification for file uploads (security requirement)

**NuGet packages for ProjectCeres.Tests:**
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Moq` — mocking framework
- `FluentAssertions` — readable test assertions
- Project reference to ProjectCeres

---

## Step 2 — Entity Models ✅

Create one `.cs` file per entity in `ProjectCeres/Models/`.

### Lookup entities (int PKs — system-seeded, never deletable by users)

| File | Columns |
|------|---------|
| `Currency.cs` | Id (int), Code, Name, Symbol |
| `AccountType.cs` | Id (int), Name — values: "Asset", "Liability" |
| `CategoryType.cs` | Id (int), Name — values: "Income", "Expense" |
| `ReportType.cs` | Id (int), Name |

### Core entities (Guid PKs — appear in URLs)

| File | Key columns |
|------|-------------|
| `Account.cs` | Id (Guid), Name, AccountTypeId, CurrencyId, Description?, IsActive |
| `Category.cs` | Id (Guid), Name, CategoryTypeId, IsActive, IsSystem, LifestyleTag? |
| `Transaction.cs` | Id (Guid), Date (DateOnly), Amount (decimal, always positive), Description?, AccountId, CategoryId, BudgetId?, CreatedAt |
| `TransactionAttachment.cs` | Id (Guid), TransactionId, FileName, StoredPath, ContentType, FileSizeBytes, UploadedAt |
| `Transfer.cs` | Id (Guid), Date (DateOnly), Amount, SourceAccountId, DestAccountId, Description?, CreatedAt |
| `RecurringTransaction.cs` | Id (Guid), Name, EstimatedAmount?, AccountId, CategoryId, Frequency (enum), DayOfPeriod, NextDueDate, IsActive |
| `CategoryBudget.cs` | Id (Guid), CategoryId, CurrencyId, LimitAmount, IsActive |
| `Budget.cs` | Id (Guid), Name, TargetAmount, CurrencyId, StartDate, EndDate?, Description?, IsActive |
| `SavedReport.cs` | Id (Guid), Name, ReportTypeId, DateFrom?, DateTo?, CategoryId?, AccountId?, CurrencyId?, CreatedAt, DeletedAt? |
| `Settings.cs` | Id (int, always 1), NumberFormat, DateFormat, DateSeparator, DefaultCurrencyId |

**Why Guid for user entities?** They appear directly in URLs (e.g. `/transactions/{id}`). Sequential int IDs leak record counts; Guids don't.

---

## Step 3 — DbContext ✅

Create `ProjectCeres/Data/AppDbContext.cs`.

- One `DbSet<T>` per entity
- Fluent API relationships configured in `OnModelCreating` (no cascade delete on Account/Category — they use soft delete)
- **Seeded data baked into the migration** (runs on first `database update`):
  - 6 currencies: EUR, USD, GBP, COP, ARS, VED
  - 2 account types: Asset, Liability
  - 2 category types: Income, Expense
  - 4 report types: Net Worth Statement, Income & Expense Summary, Expense Breakdown by Category, Transaction History
  - 4 starter accounts: Cash, Checking Account, Savings Account, Credit Card (Liability)
  - 24 default categories: 6 income + 17 expense + 1 system ("Opening Balance", IsSystem=true)
  - 1 Settings row: NumberFormat=comma_decimal, DateFormat=DD/MM/YYYY, DateSeparator=/, DefaultCurrency=EUR

Register in `Program.cs` using the connection string from `appsettings.json`.

---

## Step 4 — Initial Migration & Database ✅

```bash
dotnet ef migrations add InitialCreate --project ProjectCeres
dotnet ef database update --project ProjectCeres
```

Also create the test database manually:
```bash
createdb project_ceres_test
```

After this step the app can start and all seeded data will be present.

---

## Step 5 — Services Layer ✅

Create `ProjectCeres/Services/` with an interface + implementation pair for each area. Controllers receive these via constructor injection — no business logic lives in controllers.

| Interface | Implementation | Responsibilities |
|-----------|----------------|-----------------|
| `IAccountService` | `AccountService` | CRUD, derive account balance (SUM of transactions), auto-create Opening Balance transaction |
| `ICategoryService` | `CategoryService` | CRUD, deactivate instead of delete, guard IsSystem categories from edit/delete |
| `ITransactionService` | `TransactionService` | CRUD (hard delete with confirm), attach/remove files |
| `ITransferService` | `TransferService` | CRUD (hard delete with confirm), same-currency validation |
| `IRecurringTransactionService` | `RecurringTransactionService` | Template CRUD, compute due dates, confirm (opens pre-filled transaction form), dismiss |
| `IReportService` | `ReportService` | Net worth, income/expense summary, expense breakdown, transaction history |
| `IDashboardService` | `DashboardService` | Live net worth, MTD income/expense, savings rate, pending reminders count |
| `IFileAttachmentService` | `FileAttachmentService` | Secure upload (magic bytes), filesystem storage, serve with Content-Disposition: attachment |
| `ISettingsService` | `SettingsService` | Read/update settings row, guarantee row exists on startup |

**Key business rules enforced in services (not controllers):**
- `Account.Balance` = SUM of transactions — never stored as a column
- `Transaction.Amount` is always positive — income vs. expense inferred from `Category → CategoryType`
- Transfer source and destination must share the same `CurrencyId` — reject otherwise
- `CategoryBudget` only allowed on Expense-type categories
- `Account` and `Category` are deactivated (`IsActive = false`), never hard deleted
- `SavedReport` soft delete sets `DeletedAt` timestamp
- Settings row is guaranteed present — created with defaults if missing

---

## Step 6 — ViewModels ✅

Create `ProjectCeres/ViewModels/`. One ViewModel per write operation — EF entities are never bound directly from POST data.

- `AccountCreateViewModel`, `AccountEditViewModel`
- `CategoryCreateViewModel`, `CategoryEditViewModel`
- `TransactionCreateViewModel`, `TransactionEditViewModel`
- `TransferCreateViewModel`, `TransferEditViewModel`
- `RecurringTransactionCreateViewModel`, `RecurringTransactionEditViewModel`
- `SettingsEditViewModel`

---

## Step 7 — Controllers & Views ✅

Build each feature fully (controller + views) before starting the next. Plain Razor/HTML only — no JavaScript, no chart libraries (those are Phase 2).

### Build order

| # | Feature | Controller | Views |
|---|---------|-----------|-------|
| 1 | **Settings** | `SettingsController` | Edit |
| 2 | **Accounts** | `AccountsController` | Index, Create, Edit, Deactivate |
| 3 | **Categories** | `CategoriesController` | Index, Create, Edit, Deactivate |
| 4 | **Transactions** | `TransactionsController` | Index (50 recent + date filter), Create, Edit, Delete |
| 5 | **Transfers** | `TransfersController` | Index, Create, Edit, Delete |
| 6 | **Recurring Reminders** | `RecurringTransactionsController` | Index, Create, Edit, Confirm, Dismiss |
| 7 | **Dashboard** | `DashboardController` | Index (net worth, MTD totals, savings rate, pending count) |
| 8 | **Reports** | `ReportsController` | Net Worth, Income & Expense Summary, Expense Breakdown, Transaction History |
| 9 | **File Attachments** | `AttachmentsController` | Upload (via transaction form), Serve, Delete |

Settings is built first because number format and date format are needed to display values on every other page.

---

## Step 8 — File Attachment Security ✅

The security model specifies strict rules for file uploads. Implemented in `FileAttachmentService`:

1. **Magic bytes verification** — use MimeDetective to read actual file content; reject if it doesn't match the whitelist (`image/jpeg`, `image/png`, `image/gif`, `image/webp`, `application/pdf`). The browser-declared MIME type is not trusted.
2. **Extension from verified MIME** — derive the file extension from the verified MIME type, never from the uploaded filename.
3. **Path construction** — `uploads/{transactionId}/{Guid.NewGuid()}{ext}`. Only app-controlled values touch the filesystem. The user-supplied filename is stored in `FileName` for display only and never passed to filesystem APIs.
4. **Serve via controller** — attachments are served through a controller action, never from `wwwroot`. Files are not publicly accessible by URL.
5. **Content-Disposition: attachment** — every serve response includes this header to prevent browsers from rendering uploaded files inline.
6. **Re-verify at serve time** — MIME whitelist is re-checked when serving, not just at upload.
7. **Limits** — max 10 MB per file, approximately 10 files per transaction.

---

## Step 9 — Tests ✅

Create `ProjectCeres.Tests/`:

**Unit tests (no database):**
- [x] Account balance derivation (SUM of transactions) — `BalanceCalculationTests.cs`
- [x] Savings rate calculation: (Income − Expenses) ÷ Income — `SavingsRateTests.cs`
- [x] Same-currency transfer validation — expect rejection when currencies differ — `TransferValidationTests.cs`
- [x] CategoryBudget expense-only guard — expect rejection when applied to Income category — `CategoryBudgetGuardTests.cs`

**Integration tests (against `project_ceres_test` database):**
- [x] Test fixture — `TestDbFixture.cs` exists in `Integration/`
- [x] `AccountServiceTests.cs` — opening balance auto-creation, balance derivation via EF query, deactivate
- [x] `CategoryServiceTests.cs` — system category edit/deactivate guard, non-system deactivate
- [x] `TransactionServiceTests.cs` — create, update, delete, system transaction exclusion from GetRecentAsync
- [x] `TransferValidationTests.cs` — same-currency enforcement, cross-currency rejection

---

## Verification Checklist

Once all steps are complete. **Prerequisite: Step 9 must be finished first.**

- [ ] `dotnet build` — zero errors, zero warnings
- [ ] `dotnet ef database update` — all migrations apply cleanly
- [ ] `dotnet run` — app starts, dashboard loads with seeded data
- [ ] Create an account with opening balance → verify Opening Balance transaction is auto-created
- [ ] Record a transaction → verify account balance updates correctly
- [ ] Record a transfer → verify it appears in Transfer History but NOT in income/expense reports
- [ ] Attempt a cross-currency transfer → verify it is rejected
- [ ] Confirm a recurring reminder → verify transaction form pre-fills from template
- [ ] Upload a file attachment → verify it is served with `Content-Disposition: attachment`
- [ ] Upload a spoofed file (e.g. .exe renamed to .jpg) → verify it is rejected
- [ ] `dotnet test` — all tests pass
