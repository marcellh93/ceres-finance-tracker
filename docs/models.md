# Data Models

## Index

1. [Database Normalization — Plain English](#database-normalization--plain-english)
   - [First Normal Form (1NF)](#first-normal-form-1nf--one-fact-per-cell)
   - [Second Normal Form (2NF)](#second-normal-form-2nf--every-column-depends-on-the-whole-key)
   - [Third Normal Form (3NF)](#third-normal-form-3nf--every-column-depends-on-nothing-but-the-key)
2. [Entity Definitions](#entity-definitions)
   - [Currency](#currency)
   - [AccountType](#accounttype)
   - [Account](#account)
   - [CategoryType](#categorytype)
   - [Category](#category)
   - [Transaction](#transaction)
   - [TransactionAttachment](#transactionattachment)
   - [Transfer](#transfer)
   - [CategoryBudget](#categorybudget)
   - [Budget](#budget)
   - [ReportType](#reporttype)
   - [SavedReport](#savedreport)
   - [Settings](#settings)
3. [Relationships](#relationships)
4. [Derived Values](#derived-values)
5. [Multi-Currency Reporting](#multi-currency-reporting)
6. [Deletion Rules](#deletion-rules)

---

## Database Normalization — Plain English

Normalization is a set of rules for organizing your tables so that data is stored cleanly,
without redundancy and without the risk of data falling out of sync with itself.
There are three forms, each building on the previous one.

---

### First Normal Form (1NF) — "One fact per cell"

**Rule:** Every column holds one single value. No lists, no comma-separated values, no repeating groups. Every row must be uniquely identifiable (i.e. there is a primary key).

**Bad example:**

| TransactionId | Date       | Categories          |
|---------------|------------|---------------------|
| 1             | 2026-01-01 | Groceries, Food     |

You can't query "all Groceries transactions" reliably if two categories are stuffed into one cell.

**Fixed:**
Split into separate rows or a separate table so each cell holds exactly one value.

---

### Second Normal Form (2NF) — "Every column depends on the whole key"

**Rule:** Must already be in 1NF. No column should depend on only *part* of the primary key.
This only becomes a problem when your primary key is made up of more than one column (composite key).

**Bad example** — imagine a table with a composite key of (AccountId, CategoryId):

| AccountId | CategoryId | AccountName    | CategoryName |
|-----------|------------|----------------|--------------|
| 1         | 3          | Chase Checking | Groceries    |

`AccountName` only depends on `AccountId`, not on the full key.
`CategoryName` only depends on `CategoryId`, not on the full key.
Both are partial dependencies — a 2NF violation.

**Fixed:**
Pull `AccountName` into an Accounts table and `CategoryName` into a Categories table.
The join table only holds the keys.

---

### Third Normal Form (3NF) — "Every column depends on nothing but the key"

**Rule:** Must already be in 2NF. No column should depend on *another non-key column*.
These are called transitive dependencies.

**Bad example:**

| TransactionId | CategoryId | CategoryName | CategoryType |
|---------------|------------|--------------|--------------|
| 1             | 3          | Groceries    | Expense      |

`CategoryName` and `CategoryType` depend on `CategoryId`, not on `TransactionId`.
If the category name changes, you'd have to update every transaction row — that is a transitive dependency.

**Fixed:**
`CategoryName` and `CategoryType` belong in a Categories table.
The Transactions table only stores `CategoryId` as a foreign key and looks the rest up from there.

---

## Entity Definitions

### Currency

Lookup table. Defines the supported currencies. Each account is assigned one currency.
No exchange rates are stored — reports filter by currency rather than converting between them.

| Column | Type    | Constraints      | Notes                                      |
|--------|---------|------------------|--------------------------------------------|
| Id     | int     | PK               |                                            |
| Code   | varchar | NOT NULL, UNIQUE | ISO 4217 code — e.g. "EUR", "USD"         |
| Name   | varchar | NOT NULL         | e.g. "Euro", "US Dollar"                  |
| Symbol | varchar | NOT NULL         | e.g. "€", "$", "£"                        |

**Supported currencies at launch:**

| Code | Name                  | Symbol |
|------|-----------------------|--------|
| EUR  | Euro                  | €      |
| USD  | US Dollar             | $      |
| GBP  | British Pound         | £      |
| COP  | Colombian Peso        | $      |
| ARS  | Argentine Peso        | $      |
| VED  | Venezuelan Bolívar Digital | Bs.D |

> **Note on ARS:** The user-provided code was ARP, but ARP refers to a historical Argentine currency
> that was only in circulation between 1983 and 1985. The current ISO 4217 code is ARS.

---

### AccountType

Lookup table. Keeps account classification in one place.

| Column | Type    | Constraints | Notes                  |
|--------|---------|-------------|------------------------|
| Id     | int     | PK          |                        |
| Name   | varchar | NOT NULL    | "Asset" or "Liability" |

---

### Account

Represents a financial account the user owns or owes on.

| Column        | Type    | Constraints    | Notes                                  |
|---------------|---------|----------------|----------------------------------------|
| Id            | int     | PK             |                                        |
| Name          | varchar | NOT NULL       | e.g. "Chase Checking"                  |
| AccountTypeId | int     | FK, NOT NULL   | → AccountType                          |
| CurrencyId    | int     | FK, NOT NULL   | → Currency                             |
| Description   | varchar | nullable       | e.g. "Main checking account"           |
| IsActive      | bit     | NOT NULL       | False = deactivated, hidden from UI but history preserved |

**Deactivated accounts in calculations:**
A deactivated account still counts toward net worth and account balance reports. Deactivation
means the account is closed or no longer in active use — the money still exists (or the debt
is still owed). Excluding it from calculations would silently produce a wrong net worth figure.
Deactivated accounts are hidden from pickers and the active account list, but included in
all aggregation queries.

**Opening balance:**
Account has no balance column — balance is always derived from transactions. When a user creates
an account and enters a starting balance, the app automatically creates an opening balance
transaction for that amount, tagged to the system "Opening Balance" category. The creation form
prompts for a starting balance (can be zero). The user never has to construct this transaction
manually — the app does it on their behalf. The transaction is visible in transaction history
labeled as the opening entry and can be edited if the user entered the wrong amount.

**Why AccountTypeId instead of storing "Asset" directly?**
Storing the string on every row means a rename requires updating every account record.
With a lookup table it changes in one place — and the string value can never drift. (3NF)

---

### CategoryType

Lookup table. Distinguishes income categories from expense categories.

| Column | Type    | Constraints | Notes                   |
|--------|---------|-------------|-------------------------|
| Id     | int     | PK          |                         |
| Name   | varchar | NOT NULL    | "Income" or "Expense"   |

---

### Category

User-defined labels for classifying transactions.

| Column         | Type    | Constraints  | Notes                          |
|----------------|---------|--------------|--------------------------------|
| Id             | int     | PK           |                                |
| Name           | varchar | NOT NULL     | e.g. "Groceries", "Salary"     |
| CategoryTypeId | int     | FK, NOT NULL | → CategoryType                 |
| IsActive       | bit     | NOT NULL     | False = deactivated, hidden from pickers but existing transactions unaffected |
| IsSystem       | bit     | NOT NULL     | True = seeded by the app, not editable or deletable by the user |

**System categories:**
Some categories are seeded by the app and must not be renamed or deleted because the application
logic depends on them by name or ID. Currently one: "Opening Balance" (Income type, IsSystem = true).
It is excluded from all income/expense report totals — its only purpose is to anchor the starting
balance of an account. The UI must hide edit and delete controls for any category where IsSystem = true.

---

### Transaction

The central record. Every dollar movement lives here.

| Column      | Type    | Constraints  | Notes                                         |
|-------------|---------|--------------|-----------------------------------------------|
| Id          | int     | PK           |                                               |
| Date        | date         | NOT NULL     | When the transaction occurred — stored as local date, no timezone (see note) |
| Amount      | decimal(18,2)| NOT NULL     | Always stored as a positive number — direction derived from Category → CategoryType |
| Description | varchar      | nullable     | e.g. "Whole Foods run"                        |
| AccountId   | int          | FK, NOT NULL | → Account (which account was affected)        |
| CategoryId  | int          | FK, NOT NULL | → Category (what kind of transaction this is) |
| CreatedAt   | datetime     | NOT NULL     | Set by the application on insert. Used as tiebreaker when ordering transactions that share the same date. Not editable — reflects when the record was entered, not when the transaction occurred. |
| BudgetId    | int          | FK, nullable | → Budget (optional — tags this transaction to a goal budget) |

**Note on Income vs. Expense classification:**
Whether a transaction is income or expense is not stored on this table. That classification
already lives in Category → CategoryType. Duplicating it here would create a transitive
dependency — a 3NF violation — because the value would depend on CategoryId, not on the
transaction's own key. If the two ever disagreed, there would be no way to know which is correct.

**Note on date and timezone:**
Date is stored as a local date with no timezone component. The assumption is that the user
records transactions in their local timezone and reports use the same timezone for period
boundaries (start/end of month, year). In Phase 3 (multi-user), this assumption must be
revisited — users in different timezones will have different interpretations of "today."

**Note on BudgetId when a Budget is deactivated:**
If the linked Budget is deactivated, the FK on this row is preserved. The transaction
still contributed to that budget's actual spend while it was active. Transaction History
views should display the budget name even when the budget is deactivated, so the link
remains interpretable.

**Flagged — split transactions (Phase 2):**
Currently one Transaction links to exactly one Category. Users who want to split a single
payment across multiple categories (e.g. one supermarket receipt split between Groceries
and Household Supplies) cannot do so. This will require a `TransactionLine` junction table
(TransactionId, CategoryId, Amount) replacing the direct CategoryId FK. Splitting is
opt-in — the current single-category flow remains the default. See Phase 2 in planning.md.

**Flagged — cleared / reconciliation status (Phase 2):**
There is no field to mark a transaction as verified against a bank statement. Adding this
requires a `ClearedAt date` (or `IsCleared bit`) column on this table. See Phase 2 in planning.md.

---

### TransactionAttachment

Stores metadata for files attached to a transaction. One transaction can have many attachments.
The actual file is saved to the filesystem — only the reference lives in the database.

| Column        | Type     | Constraints  | Notes                                                      |
|---------------|----------|--------------|------------------------------------------------------------|
| Id            | int      | PK           |                                                            |
| TransactionId | int      | FK, NOT NULL | → Transaction                                              |
| FileName      | varchar  | NOT NULL     | Original filename as uploaded (e.g. "receipt.pdf")        |
| StoredPath    | varchar  | NOT NULL     | Path used to locate the file. Phase 1/2: absolute filesystem path. Phase 3+: cloud storage URL or blob key — provider TBD, see open question in planning.md |
| ContentType   | varchar  | NOT NULL     | MIME type (e.g. "application/pdf", "image/jpeg")           |
| FileSizeBytes | bigint   | NOT NULL     | Size of the file in bytes                                  |
| UploadedAt    | datetime | NOT NULL     | When the file was attached                                 |

**Why not store the file in the database?**
Storing binary file data (BLOBs) in the database bloats it, slows down every backup,
and makes queries against other columns slower. The filesystem is purpose-built for files.
The database holds the path so the app can find it — that's the right split of responsibility.

**StoredPath naming convention:**
Files are saved under `uploads/{transactionId}/{guid}{extension}`.
The GUID prevents filename collisions. The transactionId subdirectory groups a transaction's
attachments together and makes manual inspection easier. `FileName` preserves the original
name for display in the UI — `StoredPath` is never shown to the user.
Files must be served via a controller action, not directly — they must not be accessible
without authentication and must not be placed inside `wwwroot`.

---

### Transfer

Represents a movement of money between two accounts the user owns.
A transfer is neither income nor expense — it does not have a category and is excluded
from all income/expense report calculations. Both accounts must share the same currency.

| Column          | Type     | Constraints  | Notes                                       |
|-----------------|----------|--------------|---------------------------------------------|
| Id              | int      | PK           |                                             |
| Date            | date     | NOT NULL     | When the transfer occurred — local date, no timezone |
| Amount          | decimal(18,2) | NOT NULL | Always stored as a positive number          |
| SourceAccountId | int      | FK, NOT NULL | → Account (money leaves here)               |
| DestAccountId   | int      | FK, NOT NULL | → Account (money arrives here)              |
| Description     | varchar  | nullable     | e.g. "Monthly savings transfer"             |
| CreatedAt       | datetime | NOT NULL     | Set by the application on insert. Used as tiebreaker when ordering transfers that share the same date. Not editable — reflects when the record was entered, not when the transfer occurred. |

**Constraint:** SourceAccountId and DestAccountId must reference accounts with the same currency.
Cross-currency transfers are not supported — they would require a conversion rate, which is out of scope.

**Flagged — cleared / reconciliation status (Phase 2):**
Same as Transaction — no field exists to mark a transfer as verified against a bank statement.
Requires a `ClearedAt date` (or `IsCleared bit`) column on this table. See Phase 2 in planning.md.

**Flagged — file attachments on transfers (Phase 2):**
There is currently no way to attach a file (e.g. a bank wire confirmation PDF) to a transfer.
Requires a `TransferAttachment` entity mirroring the structure of `TransactionAttachment`,
with a `TransferId` FK instead of `TransactionId`. See Phase 2 in planning.md.

---

### CategoryBudget

A monthly spending cap for an expense category. Resets every month.
Feeds the Category Budget Progress bars on the dashboard.

| Column     | Type    | Constraints  | Notes                                            |
|------------|---------|--------------|--------------------------------------------------|
| Id         | int     | PK           |                                                  |
| CategoryId | int     | FK, NOT NULL | → Category (must be an Expense category)         |
| CurrencyId | int     | FK, NOT NULL | → Currency                                       |
| LimitAmount| decimal(18,2) | NOT NULL  | Maximum amount to spend in this category per month |
| IsActive   | bit           | NOT NULL  | False = deactivated, hidden from dashboard       |

**Note:** CategoryBudget only applies to Expense categories — setting a cap on an Income
category is not meaningful. This constraint should be enforced at the application level.

**Uniqueness constraint:** Only one active CategoryBudget per CategoryId + CurrencyId combination
is permitted. Two active limits for the same category and currency would produce ambiguous
dashboard progress bars. Enforce via unique index on (CategoryId, CurrencyId) where IsActive = true.

---

### Budget

A named, purpose-driven financial target. Used to track planned vs. actual spend
for a specific goal such as a trip, renovation, or investment.
Transactions are optionally tagged to a budget to count toward its actual amount.

| Column       | Type     | Constraints  | Notes                                              |
|--------------|----------|--------------|----------------------------------------------------|
| Id           | int      | PK           |                                                    |
| Name         | varchar  | NOT NULL     | e.g. "Trip to Japan", "Kitchen Renovation"         |
| TargetAmount | decimal(18,2) | NOT NULL | The planned total for this goal                    |
| CurrencyId   | int      | FK, NOT NULL | → Currency                                         |
| StartDate    | date     | NOT NULL     | When tracking begins                               |
| EndDate      | date     | nullable     | Null = open-ended goal                             |
| Description  | varchar  | nullable     | Optional notes about the goal                      |
| IsActive     | bit      | NOT NULL     | False = completed or paused, hidden from active list |

**Actual spend** is derived — never stored. It is always calculated as the SUM of amounts
of all transactions tagged to this budget. Remaining = TargetAmount − actual spend.

**Transaction link:** The Transaction entity has an optional `BudgetId` FK.
One transaction can be linked to at most one goal budget. Not all transactions need a budget.

---

### ReportType

Lookup table. Defines the available report types in the system.

| Column | Type    | Constraints | Notes                                                                 |
|--------|---------|-------------|-----------------------------------------------------------------------|
| Id     | int     | PK          |                                                                       |
| Name   | varchar | NOT NULL    | e.g. "Net Worth Statement", "Income & Expense Summary"               |

---

### SavedReport

Stores a user-saved report configuration — a named snapshot of a report type plus its
parameters (date range, filters) so it can be re-run at any time without re-entering the inputs.

| Column      | Type     | Constraints  | Notes                                                          |
|-------------|----------|--------------|----------------------------------------------------------------|
| Id          | int      | PK           |                                                                |
| Name        | varchar  | NOT NULL     | User-given name, e.g. "March 2026 Overview"                   |
| ReportTypeId| int      | FK, NOT NULL | → ReportType                                                   |
| DateFrom    | date     | nullable     | Start of the date range filter                                 |
| DateTo      | date     | nullable     | End of the date range filter                                   |
| CategoryId  | int      | FK, nullable | → Category (optional filter by category)                       |
| AccountId   | int      | FK, nullable | → Account (optional filter by account)                         |
| CurrencyId  | int      | FK, nullable | → Currency (optional filter — scopes report to one currency)   |
| CreatedAt   | datetime | NOT NULL     | When this saved configuration was created                      |
| DeletedAt   | datetime | nullable     | Null = active. Set when user deletes — record is hidden but recoverable |

**Note:** SavedReport stores the *parameters* of a report, not the results.
Results are always recalculated live when the report is run, so they reflect current data.

**On soft delete:** Setting `DeletedAt` hides the report from normal views but keeps the record in
the database. The UI can offer a restore option for soft-deleted reports. This prevents accidental
permanent loss of a saved configuration.

---

### Settings

Stores application-wide formatting preferences. In Phase 1 (local, single user) this table
always contains exactly one row. In Phase 3 (multi-user), this table is replaced by a
per-user preferences table tied to the authenticated user — the columns remain the same,
only the scope changes.

| Column            | Type    | Constraints  | Notes                                                              |
|-------------------|---------|--------------|-------------------------------------------------------------------|
| Id                | int     | PK           |                                                                   |
| NumberFormat      | varchar | NOT NULL     | `"period_decimal"` (1,234.56) or `"comma_decimal"` (1.234,56)   |
| DateFormat        | varchar | NOT NULL     | `"DD/MM/YYYY"`, `"MM/DD/YYYY"`, or `"YYYY-MM-DD"`               |
| DateSeparator     | varchar | NOT NULL     | `"/"` (slash), `"-"` (dash), or `"."` (dot)                     |
| DefaultCurrencyId | int     | FK, NOT NULL | → Currency. Pre-selected in the account creation form. Seeds to EUR on first run. Editable via the Settings page. |

**Note:** Settings has one foreign key — `DefaultCurrencyId → Currency`. The per-user
migration in Phase 3 will add a UserId column and remove the single-row constraint.

**Initialization:** The single Settings row is seeded by EF Core on first run with the
following defaults: `NumberFormat = "comma_decimal"`, `DateFormat = "DD/MM/YYYY"`,
`DateSeparator = "/"`, `DefaultCurrencyId = EUR`. These defaults reflect the primary
user's locale (Spain). No setup prompt — defaults are applied silently and are
editable via the Settings page.
The app must not crash if the Settings row is missing — on startup, check for its existence
and create it with defaults if absent.

---

## Relationships

```
Currency ──< Account
             │
AccountType ─┤
             ├──< Transaction >── Category >── CategoryType
             │         │    └──> Budget (optional)
             │         └──< TransactionAttachment
             │
             ├──< Transfer (as SourceAccount)
             └──< Transfer (as DestAccount)

Category >── CategoryBudget
Currency ──< CategoryBudget
Currency ──< Budget

ReportType ──< SavedReport >── Currency (optional)
                    │
                    ├──> Account  (optional filter)
                    └──> Category (optional filter)

Currency ──< Settings (via DefaultCurrencyId)
```

- One Currency → many Accounts
- One AccountType → many Accounts
- One Account → many Transactions
- One Account → many Transfers (as source or destination)
- One Category → many Transactions
- One Category → many CategoryBudgets
- One Currency → many CategoryBudgets
- One Currency → many Budgets
- One Budget → many Transactions (via optional BudgetId)
- One CategoryType → many Categories
- One Transaction → many TransactionAttachments
- One ReportType → many SavedReports
- SavedReport optionally filters by one Account, one Category, and/or one Currency

---

## Derived Values

These are never stored as columns — they are always calculated at query time:

| Value              | How it is calculated                                                  |
|--------------------|-----------------------------------------------------------------------|
| Account Balance    | SUM of transaction amounts for a given account                        |
| Total Assets       | SUM of balances for all accounts where AccountType = "Asset"          |
| Total Liabilities  | SUM of balances for all accounts where AccountType = "Liability"      |
| Net Worth (Equity) | Total Assets − Total Liabilities                                      |
| Total Income       | SUM of amounts where CategoryType = "Income" for a date range, scoped to a currency        |
| Total Expenses     | SUM of amounts where CategoryType = "Expense" for a date range, scoped to a currency       |
| Net Cash Flow      | Total Income − Total Expenses (within the same currency)                                   |

---

## Multi-Currency Reporting

Each account belongs to exactly one currency. Transactions inherit the currency of their account —
there is no currency field on the transaction itself.

**There is no currency conversion.** Reports do not convert amounts between currencies.
Instead, when a user views a report, they select a currency and the report shows only the
accounts and transactions that belong to that currency. Accounts in other currencies are excluded.

This means:
- Net Worth, Income, Expenses, and Cash Flow are always shown per currency
- A user with accounts in EUR and USD will see two separate views — one per currency — not a combined total
- No exchange rate data needs to be stored or fetched

**Currency conversion is explicitly out of scope** and would be introduced only as a future
feature if there is clear demand for it.

---

## Deletion Rules

| Entity | Rule | Reason |
|--------|------|--------|
| Account | Deactivate (`IsActive = false`) — never hard delete | Has transactions linked to it. Hard delete would orphan financial history. |
| Category | Deactivate (`IsActive = false`) — never hard delete. System categories (`IsSystem = true`) cannot be deactivated either. | Has transactions linked to it. Hard delete would orphan financial history. System categories are required for app logic. |
| Transaction | Hard delete allowed — requires confirmation prompt | No downstream records depend on it. User-initiated correction. Removes its contribution from any linked Budget's actual spend. |
| Transfer | Hard delete allowed — requires confirmation prompt | No downstream records depend on it. User-initiated correction. |
| CategoryBudget | Deactivate (`IsActive = false`) — never hard delete | Historical dashboard and report data depends on it. |
| Budget | Deactivate (`IsActive = false`) — never hard delete | Transactions are linked to it. Deleting would orphan those links and erase goal tracking history. |
| TransactionAttachment | Hard delete allowed | File and record removed together. No history depends on an attachment. |
| SavedReport | Soft delete (`DeletedAt` timestamp) — restorable | No financial history attached, but accidental deletion should be recoverable. |
| AccountType | Not deletable — system-defined | Core reference data the system depends on. |
| CategoryType | Not deletable — system-defined | Core reference data the system depends on. |
| ReportType | Not deletable — system-defined | Core reference data the system depends on. |
| Currency | Not deletable — system-defined | Removing a currency used by accounts would break those accounts. |
| Settings | Not deletable — only editable | Always exactly one row in Phase 1. |

**Deactivate vs. soft delete — the distinction:**
- **Deactivate** (`IsActive`): the record is still in active use by historical data. It is hidden from pickers and active views but continues to appear correctly in past transactions and reports.
- **Soft delete** (`DeletedAt`): the user intentionally removed the record and it has no ongoing role in history. It is fully hidden but can be restored on request.

---

## Phase 2 — Entities To Be Defined

### InvestmentHolding (Phase 2)

To be defined when Phase 2 begins. Will link to Account and track individual positions within
an investment account: ticker or name, number of units, purchase price, current price (updated
manually), unrealized gain/loss (derived). See planning.md Phase 2 section for full context.
