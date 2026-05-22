# Data Models

> **Diataxis type:** Reference — defines every entity, its columns, relationships, and deletion rules.

## Index

1. [Database Normalization — Plain English](#database-normalization--plain-english)
   - [First Normal Form (1NF)](#first-normal-form-1nf--one-fact-per-cell)
   - [Second Normal Form (2NF)](#second-normal-form-2nf--every-column-depends-on-the-whole-key)
   - [Third Normal Form (3NF)](#third-normal-form-3nf--every-column-depends-on-nothing-but-the-key)
2. [Domain Model](#domain-model)
3. [Entity Definitions](#entity-definitions)
   - [Primary Key Strategy](#primary-key-strategy)
   - [Currency](#currency)
   - [AccountType](#accounttype)
   - [Account](#account)
   - [CategoryType](#categorytype)
   - [Category](#category)
   - [Transaction](#transaction)
   - [TransactionAttachment](#transactionattachment)
   - [RecurringTransaction](#recurringtransaction)
   - [Transfer](#transfer)
   - [CategoryBudget](#categorybudget)
   - [Budget](#budget)
   - [ReportType](#reporttype)
   - [SavedReport](#savedreport)
   - [Settings](#settings)
4. [Relationships](#relationships)
5. [Derived Values](#derived-values)
6. [Multi-Currency Reporting](#multi-currency-reporting)
7. [Deletion Rules](#deletion-rules)
8. [Database Indexes](#database-indexes)
9. [Phase 2 — Schema Additions](#phase-2--schema-additions)
   - [ImportStagedTransfer](#importstagedtransfer-new-entity--phase-2-stage-35)
   - [ImportTransferExclusion](#importtransferexclusion-new-entity--phase-2-stage-35)
10. [Phase 3 — Auth + MFA Entities](#phase-3--auth--mfa-entities)
    - [SavedSearch](#savedsearch-phase-3)
    - [SupportTicket](#supportticket-phase-3)
    - [UserSession](#usersession-phase-3)
    - [UserBlockedIp](#userblockedip-phase-3)
    - [CustomerArchive](#customerarchive-phase-3)
    - [AdminAuditLog](#adminauditlog-phase-3)
    - [UserMfaBackupCode](#usermfabackupcode-phase-3-stage-6b1)
    - [TotpReplayEntry](#totpreplayentry-phase-3-stage-6b1)
    - [FailedLoginAttempt](#failedloginattempt-phase-3-stage-6b2)
    - [AuditLog](#auditlog-phase-3-stage-614)
    - [PasswordResetToken](#passwordresettoken-phase-3-stage-6c1)
    - [LockoutUnlockToken](#lockoutunlocktoken-phase-3-stage-610)
    - [EmailConfirmationToken](#emailconfirmationtoken-phase-3-stage-93)

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

## Domain Model

Conceptual overview — relationships and responsibilities only. For column-level detail see the entity definitions below.

```mermaid
classDiagram
    direction TB

    class Currency["Currency\n(lookup)"]
    class AccountType["AccountType\n(lookup)"]
    class CategoryType["CategoryType\n(lookup)"]
    class ReportType["ReportType\n(lookup)"]

    class Account {
        Asset or Liability
        IsActive
    }
    class Category {
        Income or Expense
        IsSystem
        IsActive
        LifestyleTag
    }
    class Transaction {
        Amount always positive
        Direction via Category type
    }
    class Transfer {
        No category
        Same currency enforced
    }
    class LiabilityPayment {
        Asset → Liability
        No category
    }
    class TransactionAttachment {
        File on disk
        Path in DB
    }
    class RecurringTransaction {
        Template only
        No auto-creation
    }
    class CategoryBudget {
        Expense only
        Monthly cap
    }
    class Budget {
        Goal-based
        Actual spend derived
    }
    class SavedReport {
        Stores parameters
        Soft delete
    }
    class Settings {
        Single row (Phase 1)
        Per-user (Phase 3)
    }

    AccountType "1" --> "many" Account : classifies
    Currency "1" --> "many" Account : denominated in

    Account "1" --> "many" Transaction : records movement on
    Category "1" --> "many" Transaction : classifies
    CategoryType "1" --> "many" Category : types
    Budget "1" --> "many" Transaction : optionally tagged to

    Account "1" --> "many" Transfer : source
    Account "1" --> "many" Transfer : destination

    Account "1" --> "many" LiabilityPayment : asset side
    Account "1" --> "many" LiabilityPayment : liability side

    Transaction "1" --> "many" TransactionAttachment : has files

    Account "1" --> "many" RecurringTransaction : default account
    Category "1" --> "many" RecurringTransaction : default category

    Category "1" --> "many" CategoryBudget : caps spending for
    Currency "1" --> "many" CategoryBudget : scoped to

    Currency "1" --> "many" Budget : denominated in

    ReportType "1" --> "many" SavedReport : typed as
    Currency "1" --> "o" SavedReport : optional filter
    Account "1" --> "o" SavedReport : optional filter
    Category "1" --> "o" SavedReport : optional filter

    Currency "1" --> "1" Settings : default currency
```

---

## Entity Definitions

### Primary Key Strategy

User-created entities use `uuid` as their primary key. System lookup tables use `int`.

| PK type | Applied to | Reason |
|---------|-----------|--------|
| `uuid` | Account, Category, Transaction, Transfer, TransactionAttachment, CategoryBudget, Budget, SavedReport, UserSession, UserBlockedIp | Appear in user-facing URLs — UUIDs prevent sequential ID enumeration and information leakage |
| `int` | Currency, AccountType, CategoryType, ReportType, Settings | System-seeded, never appear in URLs, conventional int is fine |

FK columns follow the referenced table's PK type: `Account.AccountTypeId` stays `int` (points to a lookup table), but `Transaction.AccountId` is `uuid` (points to Account).

**EF Core implementation:** `Guid` in C# maps to `uuid` in PostgreSQL natively via Npgsql. UUIDs are generated client-side on insert via `Guid.NewGuid()` — no database sequence or trigger required.

---

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
| Id            | uuid    | PK             |                                        |
| Name          | varchar | NOT NULL       | e.g. "Chase Checking"                  |
| AccountTypeId | int     | FK, NOT NULL   | → AccountType                          |
| CurrencyId    | int     | FK, NOT NULL   | → Currency                             |
| Description   | varchar | nullable       | e.g. "Main checking account"           |
| IsActive      | bit     | NOT NULL       | False = deactivated, hidden from UI but history preserved |
| Notes         | text    | nullable       | Phase 3. Free-text annotation for the account (e.g. "emergency fund — minimum €5,000"). Displayed on the account detail view. |

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
labeled as the opening entry. The Amount and Date fields are editable if the user entered the wrong value. The CategoryId is locked — the service layer must reject any attempt to change it away from the Opening Balance system category, regardless of how the request arrives. Allowing a category change would silently move the opening balance into income reports and distort all financial totals.

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
| Id             | uuid    | PK           |                                |
| Name           | varchar | NOT NULL     | e.g. "Groceries", "Salary"     |
| CategoryTypeId | int     | FK, NOT NULL | → CategoryType                 |
| IsActive       | bit     | NOT NULL     | False = deactivated, hidden from pickers but existing transactions unaffected |
| IsSystem       | bit     | NOT NULL     | True = seeded by the app, not editable or deletable by the user |
| IsReserved     | bit     | NOT NULL     | (Added Stage 7 Task 5, 2026-05-12.) True = user-owned but the application code depends on the row existing for this user (e.g. "Uncategorized Income", "Uncategorized Expense" — the fallback target when a category is removed). Distinct from `IsSystem`: a reserved row IS user-owned (each user has their own copy with their own GUID, stamped at registration by `CategorySeedService`); the immutability protection lives in `CategoryPolicies.CanEdit` which trips on either `IsSystem` or `IsReserved`. |
| LifestyleTag   | varchar | nullable     | "Needs" or "Wants" — used for ratio-based budgeting framework reports (50/30/20). Null = untagged. Only meaningful for Expense categories; ignored on Income and system categories. Seeded expense categories ship with suggested default tags. Prompted once when a user creates a custom category. |
| UserId         | uuid    | FK, nullable (transitional)  | (Stage 7 bridge state.) Owner; `IUserOwned`-style scope. **Currently nullable** because the single system "Opening Balance" row historically used `UserId = NULL` to mean "shared across all users". Stage 7 Task 9's `StampOpeningBalanceWithSentinel` migration moved that row into the sentinel cohort so the global query filter (`e.UserId == _currentUser.UserId`) can find it for each user. Task 16 (Commit 2) makes the column non-nullable after the sentinel-to-real-user remap; until then `Category` implements BOTH `IUserOwned` and `IOptionallyUserOwned` via an explicit interface bridge returning `UserId ?? Guid.Empty` for the `IUserOwned.UserId` getter. |

**System categories:**
Some categories are seeded by the app and must not be renamed or deleted because the application
logic depends on them by name or ID. Currently one: "Opening Balance" (Income type, `IsSystem = true`).
It is excluded from all income/expense report totals — its only purpose is to anchor the starting
balance of an account. The UI must hide edit and delete controls for any category where `IsSystem = true`.
The service layer must also enforce this — reject any edit or delete request for a row where `IsSystem` OR `IsReserved` is true, regardless of how the request arrives. UI-only enforcement is bypassed by direct HTTP requests. Stage 7 changed the response shape: archive/patch attempts against a system row return **422 `SYSTEM_CATEGORY_IMMUTABLE`** when the row is visible to the user (which it always is post-`StampOpeningBalanceWithSentinel`, because every seeded Category — including the system Opening Balance — sits in the sentinel cohort that Task 15 will remap to the first real user).

**Per-user category seeding:**
At registration, `CategorySeedService.CopyDefaultsForUserAsync` writes 26 rows from the canonical
`Categories.Defaults` list (`ProjectCeres/Common/Categories.cs`) stamped with the new user's id and
new per-row GUIDs. Two of those rows ("Uncategorized Income", "Uncategorized Expense") are flagged
`IsReserved = true`. The "Opening Balance" copy is flagged `IsSystem = true`. The remaining 23 are
freely editable per-user copies of the default chart of accounts. The seeder is idempotent — a
second call is a no-op via `IgnoreQueryFilters().AnyAsync(c => c.UserId == userId)`.

**Opening Balance and the balance sign convention:**
Although the Opening Balance category carries `CategoryTypeId = Income`, it is treated as a neutral
starting point — not as income or expense — in all balance calculations. The service layer checks
`Category.IsSystem` *before* applying the income/expense direction rule, and always adds the amount
to the balance regardless of account type. This means a liability account's opening balance correctly
increases what is owed, even though it is stored as an Income-typed transaction.

---

### Transaction

The central record. Every dollar movement lives here.

| Column      | Type    | Constraints  | Notes                                         |
|-------------|---------|--------------|-----------------------------------------------|
| Id          | uuid    | PK           |                                               |
| Date        | date         | NOT NULL     | When the transaction occurred — stored as local date, no timezone (see note) |
| Amount      | decimal(18,2)| NOT NULL     | Always stored as a positive number — direction derived from Category → CategoryType |
| Description | varchar      | nullable     | e.g. "Whole Foods run"                        |
| AccountId   | uuid         | FK, NOT NULL | → Account (which account was affected)        |
| CategoryId  | uuid         | FK, NOT NULL | → Category (what kind of transaction this is) |
| CreatedAt   | datetime     | NOT NULL     | Set by the application on insert. Used as tiebreaker when ordering transactions that share the same date. Not editable — reflects when the record was entered, not when the transaction occurred. |
| BudgetId    | uuid         | FK, nullable | → Budget (optional — tags this transaction to a goal budget) |

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

**Split transactions — deferred, not Phase 2:**
One Transaction links to exactly one Category. Split transactions (one payment across
multiple categories) are not supported in Phase 1 or Phase 2. Revisit at Phase 3 scope
definition — outcome may be implement, defer again, or discard. See ADR-0041.

**Cleared / reconciliation status (Phase 2; SPA surfaces in Phase 3):**
`IsCleared bool NOT NULL DEFAULT false` — added in Phase 2. Marks a transaction as
verified against a bank statement. Set automatically on clean CSV imports; held false
for potential duplicates pending reconciliation review. See ADR-0039.
Can be set from four surfaces: (1) the `MovementClearedToggle` inline switch on the
Movements list (PATCHes `/api/movements/{id}/cleared`); (2) the bulk-cleared button on
the Movements list (POSTs `/api/movements/bulk-cleared`); (3) the `IsClearedSwitch`
inside `MovementForm` on the Edit page (sent as a JSON field on PUT); (4) the same
switch on the Create page (sent on POST). Surfaces 3 + 4 share the same form component.

**Needs review flag (Phase 2):**
`NeedsReview bool NOT NULL DEFAULT false` — set to `true` automatically on every transaction
created by the import pipeline. Signals that the transaction has not been manually verified
and may need recategorisation. The user clears this flag after reviewing the imported row.
Not exposed as a standalone toggle yet — displayed in the Transactions list as a badge.

---

### TransactionAttachment

Stores metadata for files attached to a transaction. One transaction can have many attachments.
The actual file is saved to the filesystem — only the reference lives in the database.

| Column        | Type     | Constraints  | Notes                                                      |
|---------------|----------|--------------|------------------------------------------------------------|
| Id            | uuid     | PK           |                                                            |
| TransactionId | uuid     | FK, NOT NULL | → Transaction                                              |
| UserId        | uuid     | FK, NOT NULL, indexed | → AspNetUsers. Added Stage 7.5 (2026-05-14) for Postgres Row-Level Security — backfilled from the parent Transaction. `IUserOwned` so the EF global query filter + RLS `user_isolation` policy apply to the attachment row directly, not just via parent FK. |
| FileName      | varchar  | NOT NULL     | Original filename as uploaded (e.g. "receipt.pdf")        |
| StoredPath    | varchar  | NOT NULL     | Path used to locate the file. Phase 1/2: absolute filesystem path. Phase 3+: cloud storage URL or blob key — provider TBD, see open question in planning.md |
| ContentType   | varchar  | NOT NULL     | MIME type (e.g. "application/pdf", "image/jpeg")           |
| FileSizeBytes | bigint   | NOT NULL     | Size of the file in bytes                                  |
| UploadedAt    | datetime | NOT NULL     | When the file was attached                                 |

**Deletion rule:** hard delete. Both the DB row and the file on disk are deleted together by `FileAttachmentService.DeleteAsync`. This is called explicitly in two places: (1) the user clicks Remove on the Edit view, and (2) `TransactionService.DeleteAsync` cascades deletion to all attachments before removing the transaction row. EF Core cascade delete is not used — deletion is handled in the service layer to ensure the filesystem file is also removed.

**Why not store the file in the database?**
Storing binary file data (BLOBs) in the database bloats it, slows down every backup,
and makes queries against other columns slower. The filesystem is purpose-built for files.
The database holds the path so the app can find it — that's the right split of responsibility.

**StoredPath naming convention:**
Files are saved under `uploads/{transactionId}/{guid}{extension}`.
The transactionId is a UUID (the PK of the Transaction record). A second GUID prevents filename collisions within the same transaction. The transactionId subdirectory groups a transaction's
attachments together and makes manual inspection easier. `FileName` preserves the original
name for display in the UI — `StoredPath` is never shown to the user.
Files must be served via a controller action, not directly — they must not be accessible
without authentication and must not be placed inside `wwwroot`.

**File upload security requirements:**
The following constraints must be enforced in the service layer before any file is written to disk:
- **Allowed MIME types (whitelist):** `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `application/pdf`. Reject anything not on this list — do not use a blacklist.
- **Maximum file size:** 10 MB per file. Enforce in both the controller (via `RequestSizeLimitAttribute` or `IFormFile.Length` check) and the service layer.
- **Path traversal prevention:** The stored path must be constructed entirely from application-controlled values (`transactionId` and a freshly generated `Guid`) — never from any part of the user-supplied filename. Never pass `FileName` to any filesystem API. Only `StoredPath` (constructed by the app) is used for disk operations.
- **Content-type verification:** Do not trust the MIME type declared by the browser in the HTTP request. Verify the actual file content using magic bytes (file header inspection) before saving. Libraries such as `MimeDetective` can handle this.
- **File extension:** Derive the stored file extension from the verified MIME type, not from the original filename.
- **Polyglot file awareness:** magic bytes verification reduces risk but does not eliminate polyglot files — files that are simultaneously valid as two formats (e.g. a file that passes JPEG header checks but also contains executable HTML). The `Content-Disposition: attachment` header set at serve time is the primary defence against polyglots rendering in the browser. Both layers must be consistently applied — a single endpoint missing the header breaks the protection.
- **Attachment limits:** enforce a maximum number of attachments per transaction (e.g. 10 files) and a maximum total storage per user (e.g. 1 GB) at the service layer. Without these caps, a user could attach large volumes of files and exhaust disk space. Limits should be configurable via application settings, not hardcoded.

**File serving security requirements:**
When streaming a file to the user, the controller action must:
- Verify that the authenticated user owns the transaction the attachment belongs to before streaming — never serve a file based on `StoredPath` alone.
- Set `Content-Disposition: attachment; filename*=UTF-8''<percent-encoded FileName>` — forces the browser to download rather than render, preventing stored XSS via uploaded HTML or SVG. Use the RFC 5987 `filename*` parameter for non-ASCII characters and to prevent header injection via filenames containing quotes or semicolons. A plain `filename` parameter may be included alongside it as a fallback for older clients.
- Set `Content-Type` from the stored `ContentType` value, not from the filename or from any user-supplied header.
- Re-verify the MIME type whitelist at serve time — reject any record whose `ContentType` is not on the allowed list, even if it passed the upload check.

---

### RecurringTransaction

A template for a predictable financial event that repeats on a regular schedule (salary, rent, subscriptions). Does **not** create transactions automatically — instead surfaces a reminder when the due date arrives, which the user must confirm before a real `Transaction` record is written. This prevents unverified entries from appearing in the ledger.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| Name | varchar | NOT NULL | User-given label, e.g. "Monthly Rent", "Salary" |
| EstimatedAmount | decimal(18,2) | nullable | Optional estimated amount pre-filled in the confirmation form. `null` means the amount varies per occurrence. `0` is a valid stored amount. |
| AccountId | uuid | FK, NOT NULL | → Account |
| CategoryId | uuid | FK, NOT NULL | → Category |
| Frequency | varchar | NOT NULL | "monthly", "weekly", "biweekly", "annual" |
| DayOfPeriod | int | nullable | Day within the frequency period for Snap-to-calendar-day reminders. Monthly: day of month (1–31). Weekly/Biweekly: day of week (1=Monday … 7=Sunday, ISO 8601). For Annual and non-Snap reminders, DayOfPeriod must be null. |
| NextDueDate | date | NOT NULL | Date on which the next reminder appears. Advances to the next period automatically after the user confirms. |
| IsActive | bit | NOT NULL | False = paused, hidden from the dashboard pending list |

**How confirmation works:** when `NextDueDate` is reached (or within a configurable look-ahead window, e.g. 3 days before), the dashboard shows a pending reminder count. The user opens the reminder to see a pre-filled transaction form using the template values. They adjust any field if needed — the actual amount or date may differ from the estimate — then confirm to write a real `Transaction` record. On confirmation, `NextDueDate` advances to the next period. Dismissing a reminder does not create a transaction but does advance `NextDueDate` per the `ReminderBehaviour`.

**Why not auto-create:** amounts vary (utility bills, freelance income), payment dates shift (holidays, bank processing delays), and some periods may be skipped or cancelled. Auto-creation silently produces wrong data in the ledger. The reminder model keeps the user in control while eliminating the need to remember when entries are due.

**Deletion rule:** hard delete allowed. Deleting a template does not affect any previously confirmed transactions. Pausing (`IsActive = false`) is preferred for temporary suspension.

---

### Transfer

Represents a movement of money between two accounts the user owns.
A transfer is neither income nor expense — it does not have a category and is excluded
from all income/expense report calculations. Both accounts must share the same currency.

| Column          | Type     | Constraints  | Notes                                       |
|-----------------|----------|--------------|---------------------------------------------|
| Id              | uuid     | PK           |                                             |
| Date            | date     | NOT NULL     | When the transfer occurred — local date, no timezone |
| Amount          | decimal(18,2) | NOT NULL | Always stored as a positive number          |
| SourceAccountId | uuid     | FK, NOT NULL | → Account (money leaves here)               |
| DestAccountId   | uuid     | FK, NOT NULL | → Account (money arrives here)              |
| Description     | varchar  | nullable     | e.g. "Monthly savings transfer"             |
| CreatedAt       | datetime | NOT NULL     | Set by the application on insert. Used as tiebreaker when ordering transfers that share the same date. Not editable — reflects when the record was entered, not when the transfer occurred. |

**Constraint:** SourceAccountId and DestAccountId must not be equal — a transfer from an account to itself is logically invalid and must be rejected by the service layer.

**Constraint:** SourceAccountId and DestAccountId must reference accounts with the same currency.
Cross-currency transfers are not supported — they would require a conversion rate, which is out of scope.

**Cleared / reconciliation status (Phase 2; SPA surfaces in Phase 3):**
`IsCleared bool NOT NULL DEFAULT false` — added in Phase 2. Same semantics as Transaction.
Transfers are internal movements and are not reconciled against CSV imports. See ADR-0039.
Set from three surfaces: (1) the inline `MovementClearedToggle` on the Movements list;
(2) the `IsClearedSwitch` inside `MovementForm` on the Edit page; (3) the same switch on
the Create page.

**File attachments on transfers (Phase 2):**
`TransferAttachment` entity added in Phase 2 — mirrors `TransactionAttachment` with a
`TransferId` FK. Hard delete with confirmation. See ADR-0042.

---

### LiabilityPayment

Represents a payment made from an asset account toward a liability account — for example,
paying a credit card balance from a checking account. Records the event atomically as a
single entry visible in the Transactions list.

A liability payment is semantically distinct from a `Transfer`:
- A `Transfer` moves money between two accounts the user owns, with no P&L effect (asset → asset or liability → liability).
- A `LiabilityPayment` reduces a debt while simultaneously reducing an asset — a balance-sheet event that affects both sides.

Like `Transfer`, a `LiabilityPayment` has no category and is excluded from all income/expense
report calculations. Net worth is not changed by a liability payment (the asset decrease is
exactly offset by the liability decrease).

| Column             | Type          | Constraints  | Notes                                        |
|--------------------|---------------|--------------|----------------------------------------------|
| Id                 | uuid          | PK           |                                              |
| Date               | date          | NOT NULL     | When the payment occurred — local date, no timezone |
| Amount             | decimal(18,2) | NOT NULL     | Always stored as a positive number           |
| AssetAccountId     | uuid          | FK, NOT NULL | → Account (money leaves here — must be Asset type) |
| LiabilityAccountId | uuid          | FK, NOT NULL | → Account (debt reduced here — must be Liability type) |
| Description        | varchar       | nullable     | e.g. "Credit card payment March"            |
| IsCleared          | bool          | NOT NULL DEFAULT false | Added Phase 2. Reconciliation status — same semantics as Transaction. See ADR-0039. |
| CreatedAt          | datetime      | NOT NULL     | Set by the application on insert. Tiebreaker for same-date ordering. |

**Constraint:** `AssetAccountId` must reference an account with `AccountType.Name == "Asset"`. Enforced at the service layer.

**Constraint:** `LiabilityAccountId` must reference an account with `AccountType.Name == "Liability"`. Enforced at the service layer.

**Constraint:** Both accounts must share the same currency. Cross-currency payments are not supported.

**Constraint:** Date must not be before the opening balance date of either account. Enforced at the service layer.

**Balance calculation impact:**
- Asset account balance: liability payment amounts are subtracted (money left the account).
- Liability account balance: liability payment amounts are subtracted (debt was reduced).
- Both subtractions are applied in `AccountService.GetBalanceAsync` by querying `LiabilityPayments` separately from `Transactions`.

**UI integration:**
`LiabilityPayment` records are surfaced within the Transactions UI — not on a separate page.
The Create/Edit form has a type toggle ("Transaction" / "Liability Payment") that conditionally
shows/hides the relevant fields. The Transactions Index list merges both `Transaction` and
`LiabilityPayment` rows into a unified view using `TransactionListItemViewModel`.

**Deletion rule:** hard delete with confirmation prompt (same as `Transaction` and `Transfer`).

---

### CategoryBudget

A monthly spending cap for an expense category. Resets every month.
Feeds the Category Budget Progress bars on the dashboard.

| Column     | Type    | Constraints  | Notes                                            |
|------------|---------|--------------|--------------------------------------------------|
| Id         | uuid    | PK           |                                                  |
| CategoryId | uuid    | FK, NOT NULL | → Category (must be an Expense category)         |
| CurrencyId | int     | FK, NOT NULL | → Currency                                       |
| LimitAmount    | decimal(18,2) | NOT NULL  | Maximum amount to spend in this category per month |
| IsActive       | bit           | NOT NULL  | False = deactivated, hidden from dashboard       |
| PeriodStartDay | int           | nullable  | **Deferred.** Originally planned as a per-budget override, but the global `Settings.PeriodStartDay` shipped first (2026-05-01). The override can be added later without breaking existing data. |

**Note:** CategoryBudget only applies to Expense categories — setting a cap on an Income
category is not meaningful. This constraint should be enforced at the application level.

**Uniqueness constraint:** Only one active CategoryBudget per CategoryId + CurrencyId combination
is permitted. Two active limits for the same category and currency would produce ambiguous
dashboard progress bars. Enforce via unique index on (CategoryId, CurrencyId) where IsActive = true.

**Currency matching for actual spend:** When calculating how much has been spent against a CategoryBudget, only transactions from accounts whose `CurrencyId` matches the budget's `CurrencyId` are included. An expense recorded from a USD account does not count toward a EUR budget for the same category — they are tracked independently.

**Actual spend method signature:** `GetActualSpendAsync(id, year, month)` — the caller always specifies the calendar month. The dashboard passes the current month; the Budget vs. Actual report (Stage 7) passes historical months. No default overload exists — call sites must be explicit. See ADR-0056.

---

### Budget

A named, purpose-driven financial target. Two archetypes exist, distinguished by `GoalType`. See ADR-0040.

| Column          | Type          | Constraints  | Notes                                                        |
|-----------------|---------------|--------------|--------------------------------------------------------------|
| Id              | uuid          | PK           |                                                              |
| Name            | varchar       | NOT NULL     | e.g. "Trip to Japan", "Kitchen Renovation"                   |
| TargetAmount    | decimal(18,2) | NOT NULL     | The planned total for this goal                              |
| CurrencyId      | int           | FK, NOT NULL | → Currency                                                   |
| StartDate       | date          | NOT NULL     | When tracking begins                                         |
| EndDate         | date          | nullable     | Null = open-ended goal                                       |
| Description     | varchar       | nullable     | Optional notes about the goal                                |
| IsActive        | bit           | NOT NULL     | False = completed or paused, hidden from active list         |
| GoalType        | varchar(20)   | NOT NULL     | `Spending` or `Savings` — determines progress source        |
| LinkedAccountId | uuid          | FK, nullable | → Account. Required when `GoalType = Savings`, null otherwise |

**Spending goal** — tracks money spent toward a target. Progress = SUM of tagged expense transactions.
One transaction can be tagged to at most one goal budget via the optional `BudgetId` FK on Transaction.

**Savings goal** — tracks money accumulated in a designated account. Progress = balance of `LinkedAccountId`.
No transaction tagging — progress is always derived from the account balance. See ADR-0040.

**GoalType validation rules (enforced at application level):**
- `GoalType = Savings` → `LinkedAccountId` is required
- `GoalType = Spending` → `LinkedAccountId` must be null

**Progress** is always derived via `GetProgressAsync(id)` — never stored. Remaining = TargetAmount − progress.

**Transaction link:** The Transaction entity has an optional `BudgetId` FK.
One transaction can be linked to at most one goal budget. Not all transactions need a budget.
Savings goals do not use transaction tagging — their progress comes from the linked account balance.

---

### ReportType

Lookup table. Defines the available report types in the system.

| Column | Type    | Constraints | Notes                                                                 |
|--------|---------|-------------|-----------------------------------------------------------------------|
| Id     | int     | PK          |                                                                       |
| Name   | varchar | NOT NULL    | e.g. "Net Worth Statement", "Income & Expense Summary"               |

**Phase 1 report types (seed data):**

| Id | `ReportTypeKey` enum | Name |
|----|----------------------|------|
| 1  | `NetWorth`           | Net Worth Statement |
| 2  | `IncomeExpense`      | Income & Expense Summary |
| 3  | `ExpenseBreakdown`   | Expense Breakdown |
| 4  | `TransactionHistory` | Transaction History |

**Phase 2 report types (added in Phase 2 — see ADR-0054 for prioritisation rationale):**

| Id | `ReportTypeKey` enum | Name |
|----|----------------------|------|
| 5  | `BudgetVsActual`     | Budget vs. Actual |
| 6  | `LargestExpenses`    | Largest Expenses |
| 7  | `MonthlyCashFlow`    | Monthly Cash Flow Trend |
| 8  | `NetWorthOverTime`   | Net Worth Over Time |

Each report type is handled by a dedicated `IReportGenerator` implementation registered in `ReportGeneratorFactory`. See [architecture.md](architecture.md) for the Strategy pattern details.

---

### SavedReport

Stores a user-saved report configuration — a named snapshot of a report type plus its
parameters (date range, filters) so it can be re-run at any time without re-entering the inputs.

| Column      | Type     | Constraints  | Notes                                                          |
|-------------|----------|--------------|----------------------------------------------------------------|
| Id          | uuid     | PK           |                                                                |
| Name        | varchar  | NOT NULL     | User-given name, e.g. "March 2026 Overview"                   |
| ReportTypeId| int      | FK, NOT NULL | → ReportType                                                   |
| DateFrom    | date     | nullable     | Start of the date range filter                                 |
| DateTo      | date     | nullable     | End of the date range filter                                   |
| CategoryId  | uuid     | FK, nullable | → Category (optional filter by category)                       |
| AccountId   | uuid     | FK, nullable | → Account (optional filter by account)                         |
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
| DateFormat        | varchar | NOT NULL     | `"DD/MM/YYYY"`, `"MM/DD/YYYY"`, or `"YYYY-MM-DD"`. The separator is embedded in the format string — no separate separator field is needed. |
| DefaultCurrencyId    | int     | FK, NOT NULL | → Currency. Pre-selected in the account creation form. Seeds to EUR on first run. Editable via the Settings page. |
| PeriodStartDay       | int     | NOT NULL DEFAULT 1 | **Implemented (2026-05-01).** Day of month all monthly cycles start (1–31). Drives every monthly view in the app — Cycle to Date, Spending by Category, Income vs. Avg, and Budget periods. For months shorter than the chosen day (e.g., 31 in April), the cycle starts on that month's last day. Period boundaries are computed via the `BudgetPeriod` helper; periods are named after their end-date's calendar month. Default 1 = calendar months. Renamed from `BudgetPeriodStartDay` (2026-05-01) once it grew beyond budgets. |
| Language             | varchar(5) | NOT NULL DEFAULT `'en'` | Phase 3. BCP 47 language tag. Supported values: `en`, `es`. Validated at service layer — reject unsupported codes. Determines language for UI, transactional emails, and generated reports. |
| Country              | varchar(2) | nullable | Phase 3. ISO 3166-1 alpha-2 country code (e.g. `ES`, `US`, `GB`, `CO`, `AR`, `VE`). Nullable — users who select "Other" or skip without specifying are stored as null. Used for legal/compliance scoping. No lookup table — country is a preference label until it drives data logic. |

**Note:** Settings has one foreign key — `DefaultCurrencyId → Currency`. The per-user
migration in Phase 3 will add a UserId column and remove the single-row constraint.

**Initialization:** The single Settings row is seeded by EF Core on first run with the
following defaults: `NumberFormat = "comma_decimal"`, `DateFormat = "DD/MM/YYYY"`,
`DefaultCurrencyId = EUR`. These defaults reflect the primary
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
| Account Balance    | Depends on AccountType — see sign convention note below               |
| Total Assets       | SUM of balances for all accounts where AccountType = "Asset"          |
| Total Liabilities  | SUM of balances for all accounts where AccountType = "Liability"      |
| Net Worth (Equity) | Total Assets − Total Liabilities                                      |
| Total Income       | SUM of amounts where CategoryType = "Income" for a date range, scoped to a currency        |
| Total Expenses     | SUM of amounts where CategoryType = "Expense" for a date range, scoped to a currency       |
| Net Cash Flow      | Total Income − Total Expenses (within the same currency)                                   |

**Account balance — sign convention by account type:**
The formula differs depending on whether the account is an Asset or a Liability. Transfers and LiabilityPayments also affect the balance, not just transactions.

- **Asset account balance** = SUM(system transaction amounts) + SUM(Income transaction amounts) − SUM(Expense transaction amounts) + SUM(incoming transfer amounts) − SUM(outgoing transfer amounts) − SUM(outgoing liability payment amounts)
- **Liability account balance** = SUM(system transaction amounts) + SUM(Expense transaction amounts) − SUM(Income transaction amounts) − SUM(incoming transfer amounts) + SUM(outgoing transfer amounts) − SUM(liability payment amounts received)

System transactions (i.e. `IsSystem = true`, currently only "Opening Balance") always add to the balance regardless of account type — they are a neutral starting point, not income or expense.

For an Asset account (e.g. checking): income and transfers in add to the balance; expenses, transfers out, and liability payments out subtract.
For a Liability account (e.g. credit card): expenses add to the balance (debt grows); income (e.g. refunds), transfers in (debt payments via Transfer), and liability payments (via `LiabilityPayment`) subtract from it.

Liability balances are always positive in normal use — they represent what is owed. Net Worth = Total Assets − Total Liabilities holds because both totals are expressed as positive numbers.

`AccountService.GetBalanceAsync` handles this by running two extra aggregation queries against `LiabilityPayments` (one for `AssetAccountId`, one for `LiabilityAccountId`) and subtracting both from the transaction-derived balance.

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
| Transaction | Hard delete allowed — requires confirmation prompt | Cascades to all linked `TransactionAttachment` records — both the DB rows and the files on disk are deleted atomically by the service layer before the transaction row is removed. Removes its contribution from any linked Budget's actual spend. |
| Transfer | Hard delete allowed — requires confirmation prompt | No downstream records depend on it. User-initiated correction. |
| LiabilityPayment | Hard delete allowed — requires confirmation prompt | No downstream records depend on it. User-initiated correction. |
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

## Database Indexes

These must be created via Fluent API in `OnModelCreating` or via explicit migration scripts. EF Core creates indexes for FK columns automatically, but does not create indexes for non-FK columns, compound columns, or filtered indexes — those must be defined explicitly via `HasIndex(...).HasFilter(...)`.

| Table | Index columns | Type | Reason |
|-------|--------------|------|--------|
| Transaction | (AccountId) | Standard | Account balance and transaction history queries filter by account |
| Transaction | (CategoryId) | Standard | Expense breakdown and category budget spend queries filter by category |
| Transaction | (Date DESC) | Standard | All date-range report queries order by date |
| Transaction | (BudgetId) WHERE BudgetId IS NOT NULL | Filtered | Budget actual spend calculation — partial index avoids indexing the majority of null rows |
| Transfer | (SourceAccountId) | Standard | Transfer history queries filter by source account |
| Transfer | (DestAccountId) | Standard | Transfer history queries filter by destination account |
| Transfer | (Date DESC) | Standard | Transfer history date ordering |
| LiabilityPayment | (AssetAccountId) | Standard | Balance calculation and list queries filter by paying account |
| LiabilityPayment | (LiabilityAccountId) | Standard | Balance calculation and list queries filter by receiving account |
| LiabilityPayment | (Date DESC) | Standard | Unified transaction list date ordering |
| CategoryBudget | (CategoryId, CurrencyId) WHERE IsActive = true | Filtered compound | Enforces the uniqueness constraint and speeds up dashboard lookups |
| SavedReport | (DeletedAt) WHERE DeletedAt IS NOT NULL | Filtered | Soft-delete purge job filters on DeletedAt |

---

## Phase 2 — Schema Additions

The following fields and entities are added in Phase 2. Full column definitions are added
to entity sections above when implemented or in the entity sections below for Stage 3.5 additions.

### Account — new Phase 2 fields

| Field | Type | Notes |
|---|---|---|
| `ExcludeFromSpendable` | bool NOT NULL DEFAULT false | Excludes account from spendable balance display. Net worth unaffected. See ADR-0050. |
| `LiabilityRepaymentType` | varchar NULL | `FullMonthly` or `Amortising`. Null for asset accounts. See ADR-0043. |
| `InterestRate` | decimal NULL | Optional. Only meaningful for `Amortising` liability accounts. See ADR-0043. |

### Transaction — new Phase 2 fields

| Field | Type | Notes |
|---|---|---|
| `IsCleared` | bool NOT NULL DEFAULT false | Verified against bank statement. See ADR-0039. |

### Transfer — new Phase 2 fields

| Field | Type | Notes |
|---|---|---|
| `IsCleared` | bool NOT NULL DEFAULT false | Verified against bank statement. See ADR-0039. |

### LiabilityPayment — new Phase 2 fields

| Field | Type | Notes |
|---|---|---|
| `IsCleared` | bool NOT NULL DEFAULT false | Reconciliation status — same semantics as Transaction. See ADR-0039. Added outside the Phase 2 baseline migration as part of Stage 2.5 (required for the Movement base class). |

### Movement — abstract base class (Phase 2, Stage 2.5)

`Movement` is an abstract C# class, not a DB table. It captures the six columns shared by `Transaction`, `Transfer`, and `LiabilityPayment`: `Id`, `Date`, `Amount`, `Description`, `IsCleared`, `CreatedAt`. EF Core is configured with **Table Per Concrete type (TPC)**: each concrete subtype maps to its own existing DB table — no base table, no schema change. The generated `TpcMovementHierarchy` migration has an empty `Up()` and `Down()`. See ADR-0058.

`IMovementService.GetRecentAsync` and `CountAsync` query all three concrete tables and merge/sort the results in memory. The `/Movements` controller is the primary consumer.

### Budget — new Phase 2 fields

| Field | Type | Notes |
|---|---|---|
| `GoalType` | varchar NOT NULL | `Spending` or `Savings`. See ADR-0040. |
| `LinkedAccountId` | uuid FK NULL | → Account. Required when `GoalType = Savings`. See ADR-0040. |

### RecurringTransaction — new Phase 2 fields

| Field | Type | Notes |
|---|---|---|
| `ReminderBehaviour` | varchar NOT NULL DEFAULT 'SnapToCalendarDay' | `SnapToCalendarDay`, `RelativeToLastConfirmation`, or `ManualDate`. See ADR-0045, ADR-0051. |
| `EstimatedAmount` | decimal NULL | Optional estimated amount for variable-amount recurring transactions. |

### TransferAttachment (new entity — Phase 2)

Mirrors `TransactionAttachment` with `TransferId` FK instead of `TransactionId`.
Hard delete with confirmation. See ADR-0042. Also gains a `UserId uuid NOT NULL` column in Stage 7.5 (2026-05-14) — backfilled from the parent Transfer, `IUserOwned`, and covered by the same Postgres RLS `user_isolation` policy that protects all other user-owned tables.

### ImportProfile (new entity — Phase 2)

Stores named column mapping profiles for CSV and Excel import. See ADR-0047, ADR-0059.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| Id | uuid | PK | |
| Name | varchar | NOT NULL | User-given name e.g. "BBVA" |
| ColumnMappings | varchar/jsonb | NOT NULL | Serialised `ImportColumnMappings` — `{ "dateColumn": "Fecha", "amountColumn": "Importe", ... }` |
| Format | varchar | NOT NULL DEFAULT 'Csv' | `Csv` or `Excel` — stored as `ImportFormat` enum string |
| SheetName | varchar | NULL | Excel only: override which worksheet to read. Null = first worksheet. |
| CreatedAt | datetime | NOT NULL | |
| DeletedAt | datetime | NULL | Soft delete — 90-day recovery window shown to user |

### ImportStagedTransfer (new entity — Phase 2, Stage 3.5)

Holds import rows that triggered transfer detection and are awaiting manual review before
they can be imported as transactions or linked to existing transfers. One row per staged
import row. Rows are never deleted — they transition through `Status` values instead.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| Id | uuid | PK | |
| ImportedAt | datetime | NOT NULL | When the import ran that produced this row |
| AccountId | uuid | FK NOT NULL → Account | The destination account from the import |
| RawDate | date | NOT NULL | Date parsed from the CSV/XLSX row |
| RawAmount | decimal(18,2) | NOT NULL | Signed — negative = debit/withdrawal |
| RawDescription | varchar | NULL | Description from the import row |
| CandidateTransactionId | uuid | FK NULL → Transaction (SetNull) | Set when cross-account pairing finds a likely counterpart transaction |
| Status | varchar | NOT NULL DEFAULT 'Pending' | `Pending`, `Linked`, `CreatedAsTransfer`, `DismissedAsTransaction` |
| ResolvedAt | datetime | NULL | Set when status transitions out of Pending |

**FK behavior:** `AccountId` → Restrict (staged row cannot outlive its account). `CandidateTransactionId` → SetNull on delete (staged row survives if the candidate transaction is deleted). See ADR-0061.

**Status lifecycle:**

| Status | Meaning |
|---|---|
| `Pending` | Awaiting user review on the Transfer Review screen |
| `Linked` | User confirmed the candidate match; a `Transfer` record was created using the candidate transaction |
| `CreatedAsTransfer` | User specified the other account; a new `Transfer` record was created |
| `DismissedAsTransaction` | User confirmed this is not a transfer; the row was imported as a plain transaction |

**Deletion:** Never hard-deleted. Resolved rows remain for audit history. See ADR-0061.

---

### ImportTransferExclusion (new entity — Phase 2, Stage 3.5)

Training store for description patterns the user has dismissed as "not a transfer". During
detection, rows whose description contains any stored pattern (case-insensitive substring
match) are skipped and imported directly as transactions without staging. Patterns are added
automatically when the user dismisses a staged row with "Not a transfer". See ADR-0061.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| Id | uuid | PK | |
| DescriptionPattern | varchar | NOT NULL, unique index | Stored as-is; matched via case-insensitive substring |
| CreatedAt | datetime | NOT NULL | When the pattern was first saved |

**Deletion:** Hard delete. Users can manage exclusions if needed — no soft-delete required.

---

### InvestmentHolding — deferred to Phase 3 or Phase 4

Investment tracking is not in Phase 2 scope. Revisit at Phase 3 or Phase 4 scope
definition based on whether the user actively uses investment accounts.

---

## Phase 3 — Auth + MFA Entities

### ApplicationUser (Phase 3, Stage 6a)

ASP.NET Core Identity user. Inherits `IdentityUser<Guid>` — the standard Identity columns (`Email`, `NormalizedEmail`, `EmailConfirmed`, `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp`, `PhoneNumber`, `PhoneNumberConfirmed`, `TwoFactorEnabled`, `LockoutEnd`, `LockoutEnabled`, `AccessFailedCount`) live on `AspNetUsers`. The Project-Ceres-specific column is below.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| CreatedAt | timestamp with time zone | NOT NULL DEFAULT current_timestamp | Phase 3, Stage 6b.1. Audit-only — when the account was created. No behavioural role in the login flow per ADR-0069 (MFA opt-in, no grace cliff). Useful for analytics and the eventual user-list admin surface. |

**Password storage:** `PasswordHash` is an Argon2id PHC string (`$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>`) per `Argon2idPasswordHasher` registered as the default `IPasswordHasher<ApplicationUser>`. See `security-model.md` § Passwords.

**TwoFactorEnabled:** flipped to `true` only after the user successfully verifies their first TOTP code against a candidate authenticator key (Identity's two-step `GenerateNewAuthenticatorKey` → `VerifyTwoFactorTokenAsync` → `SetTwoFactorEnabledAsync(true)` flow). MFA is opt-in per [ADR-0069](decisions/ADR-0069-mfa-opt-in-for-personal-users.md).

**TOTP seed storage:** Identity stores the authenticator seed in `AspNetUserTokens` (one row per user keyed under `[AspNetUserStore].AuthenticatorKey`), encrypted at rest via ASP.NET Core Data Protection. Production key-storage hardening (KMS / encrypted external volume) is a Stage 16 (Hosting + ops) launch gate.

**Normalizer DI changes:** any change to the registered `ILookupNormalizer` (e.g. swapping `LowercaseLookupNormalizer` for a different variant) requires a same-commit EF migration that backfills `AspNetUsers.NormalizedEmail` + `NormalizedUserName` (and `AspNetRoles.NormalizedName` if roles are populated) to the new normalizer's output. See `security-model.md` § ASP.NET Core Identity Hardening for the full rule and the Stage 9.1.5.h precedent.

---

### UserSession (Phase 3, Stage 6a)

Tracks active authenticated sessions server-side. Enables multi-device support, user-visible session management, per-session revocation, IP enforcement, and the `__Host-Persist` "remember me" rotation flow. One row per active session per device. Inserted on successful login (or successful TOTP step in the MFA flow); marked `RevokedAt = now` on logout, IP block, or persistent-token rotation.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | The "sid" claim value embedded in the auth ticket. UUID prevents sequential ID enumeration. |
| UserId | uuid | NOT NULL | → AspNetUsers.Id. FK added in Stage 7 (sentinel-to-real-user remap, ADR-0066). |
| PersistentTokenHash | varchar(512) | nullable | Argon2id hash of the SECRET portion of the persistent cookie value. The cookie value itself is `{base64url(UserSession.Id)}.{secret}` so server-side lookup is O(1) by indexed Id (no N×Argon2id linear scan on the pre-auth hot path). Null for non-persistent sessions. Rotated on each use of the persistent cookie. Stage 6b.3. |
| IpCreatedAt | varchar(45) | NOT NULL | IP at session creation. Used for IP enforcement (per-session, not per-user) and for matching against `UserBlockedIp` entries. IPv6-sized. |
| UserAgent | varchar(512) | NOT NULL | UA header at session creation. Displayed in active-sessions UI. 90-day retention cap per `security-model.md` § Sensitive Fields at Rest — purge job is a Stage 7 deliverable via `IUserJobRunner`. |
| CreatedAt | timestamp | NOT NULL | When the session was created. |
| LastUsedAt | timestamp | NOT NULL | Updated on authenticated requests via `SessionRevocationValidator.OnValidatePrincipal`, debounced to at most one write per 60 seconds (Stage 6b.3). The SELECT still runs on every request (revocation guarantee). Future-work options tracked in `planning-future.md` § *Session-validation per-request DB write*. |
| RevokedAt | timestamp | nullable | Stamped when the session is terminated. Non-null = session no longer accepted (the next request with this cookie returns 401). |
| IsPersistent | bool | NOT NULL | True = "remember me" session paired with a `__Host-Persist` cookie + `PersistentTokenHash` row. |
| UsedBackupCodeAtLogin | bool | NOT NULL, default false | True = this session was authenticated via the backup-code branch of `/api/auth/login/totp` (rather than the authenticator app). Drives the Stage 9.7 dashboard backup-code banner: `MeResponse.UsedBackupCodeAtLastLogin` is the value of this column for the row identified by the current request's `sid` claim. Per-session by design — signing in normally on a second device leaves the first session's flag set, so the user's dashboard keeps nudging them to re-enrol until they navigate away or dismiss. Set to false on the password-only and TOTP-app branches, true on the backup-code branch. |

**Indexes:** `(UserId, RevokedAt)` for the revocation lookup; `(LastUsedAt)` for the eventual purge job.

**IP enforcement is per session:** when the user has IP enforcement enabled (the toggle UI ships in 6c), each incoming request is validated against the `IpCreatedAt` of its own session row — not against a single account-wide IP. Multiple devices with different IPs are fully compatible with enforcement on, because each device has its own session anchored to its own creation IP.

**Persistent token rotation (Stage 6b.3):** on each request that arrives with a `__Host-Persist` cookie but no valid `__Host-Session` cookie, `PersistentCookieRotationMiddleware` (runs before `UseAuthentication`) extracts `UserSession.Id` from the cookie prefix (`{base64url(Id)}.{secret}`), looks up the row by PK (O(1)), verifies `Argon2id(secret) == PersistentTokenHash`, rotates the token (issue new + replace the hash), and writes fresh `__Host-Session` + `__Host-Persist` cookies — but does NOT authenticate the current request. The current request returns 401; the next request (with the new `__Host-Session` cookie) succeeds. This closes the SecurityStamp window on the rotation hop. Old `__Host-Persist` cookie value replayed after rotation returns 401. A per-token `SemaphoreSlim` prevents concurrent duplicate-rotation races (single-host band-aid; Stage 16 structural fix required).

---

### UserBlockedIp (Phase 3, Stage 6a)

Records IP addresses explicitly blocked by a user. Any authenticated request from a blocked IP is rejected with 403 and all active `UserSession` rows from that IP for that user are revoked immediately. Users manage this list from the Security settings page (UI ships in Stage 6c onwards), informed by the login/logout audit log.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| UserId | uuid | NOT NULL | → AspNetUsers.Id. FK added in Stage 7. |
| IpAddress | varchar(45) | NOT NULL | The blocked IP address. IPv6-sized. |
| BlockedAt | timestamp | NOT NULL | When the user added this block. |
| Reason | varchar(256) | nullable | User-provided reason (e.g. "suspicious login from unknown location"). |

**Uniqueness constraint:** unique on `(UserId, IpAddress)`.

**Effect on existing sessions:** `UserBlockedIpMiddleware` (runs after authentication) checks every authenticated request against `UserBlockedIps` for the current user. On match, all `UserSession` rows for that user where `IpCreatedAt = blocked_ip` are stamped `RevokedAt = now`, and the request returns 403.

**Relationship to IP enforcement toggle:** IP blocking is always active regardless of whether the user has IP enforcement enabled. Blocking is a manual, explicit action; enforcement is an automatic per-request check. They are independent.

---

### UserMfaBackupCode (Phase 3, Stage 6b.1)

One row per MFA backup code. Generated in batches of 10 at first MFA enrollment + on explicit regenerate. Single-use: `UsedAt` is stamped on first successful verify. Regeneration deletes all rows for the user and inserts 10 new ones. See [ADR-0069](decisions/ADR-0069-mfa-opt-in-for-personal-users.md) for MFA opt-in policy.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| UserId | uuid | NOT NULL | → AspNetUsers.Id. FK added in Stage 7. |
| CodeHash | varchar(512) | NOT NULL | Argon2id PHC string of the raw 16-char Crockford base-32 code (un-formatted, hyphens stripped before hashing). Same hasher pinned to `m=19456 t=2 p=1` as passwords + persistent-cookie tokens. |
| CreatedAt | timestamp | NOT NULL | When the code was generated. |
| UsedAt | timestamp | nullable | Stamped on first successful verify. Single-use. |
| UsedFromIp | varchar(45) | nullable | IP from which the code was redeemed. For audit trail. |

**Indexes:** `(UserId)` plus a Postgres partial index on `(UserId, UsedAt)` filtered to `WHERE "UsedAt" IS NULL` (named `IX_UserMfaBackupCodes_UserId_Unused`) — speeds up the unused-codes lookup, which is the hot path during verify.

**Code format:** raw codes are 16 characters from the Crockford base-32 alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ` (no I/L/O/U) → ~80 bits of entropy. Displayed as `XXXX-XXXX-XXXX-XXXX` to the user; hyphens are stripped server-side before hashing, so verify accepts both hyphenated and unhyphenated inputs.

**Format validation regex:** `^[0-9A-HJKMNP-TV-Z]{16}$` (case-insensitive, after stripping `-` and ` `). Per `security-model.md` § TOTP Backup Codes.

---

### TotpReplayEntry (Phase 3, Stage 6b.1)

One row per TOTP code accepted within the 2-minute replay window. Per `security-model.md` § Login → TOTP replay prevention: a 6-digit code is cryptographically valid for ~30 seconds plus Identity's tolerance window, so the same code might verify twice if it isn't tracked. Argon2id-hashed (per-row salt). Opportunistic purge runs on every `TryAcceptAsync` call — no scheduled job needed at single-user-beta scale.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| UserId | uuid | NOT NULL | → AspNetUsers.Id. FK added in Stage 7. Replay scoping is per-user; different users with the same numeric code don't collide. |
| CodeHash | varchar(512) | NOT NULL | Argon2id PHC string of the 6-digit numeric code. **Argon2id, not SHA-256**: the 1M-value TOTP code space is rainbow-tableable from a database dump if hashed with an unsalted fast hash. Per-row Argon2id salt closes that gap. The ~50ms cost is below the human latency-perception threshold. See `2026-05-09-stage-6b-1-totp-mfa-design.md` § Sub-decision 1 for the full reasoning. |
| AcceptedAt | timestamp | NOT NULL | When the code was first accepted. Window is `now - 2 minutes`. |

**Indexes:** `(UserId)` for the per-user replay scan; `(AcceptedAt)` for the purge query.

**Lifecycle:** `TotpReplayGuard.TryAcceptAsync` (a) verifies the new code does not match any existing row in the 2-min window for this user, (b) inserts the new row + executes a single `DELETE FROM "TotpReplayEntries" WHERE "AcceptedAt" < now - 2min` against the table-wide rows (cheap, idempotent, runs on every accept call). This pattern replaces a scheduled background job.

### FailedLoginAttempt (Phase 3, Stage 6b.2)

Records every rejected authentication attempt for credential-stuffing forensics and per-IP/per-account anomaly detection. Intentionally **cross-tenant** — captures attempts against accounts that may not exist (`UserId` is nullable). Per ADR-0065, this entity does NOT receive a global query filter when Stage 7 wires multi-tenancy: it is read by background purge jobs as a cross-tenant operation per ADR-0067 § Decision-6.

**Schema:**

| Field | Type | Constraint | Purpose |
|-------|------|------------|---------|
| Id | uuid | PK | |
| EmailAttempted | varchar(256) | nullable | Lowercased + trimmed copy of the submitted email. NULL on absurd-input cases. Truncated to AspNetUsers.NormalizedEmail length. Anonymized to NULL on GDPR erasure (Stage 6c flow). |
| UserId | uuid? | nullable | NULL when the email did not resolve to any AspNetUser (UnknownUser case). Set when the user exists. No FK — the entity intentionally has no cascading relationship with AspNetUsers (an AspNetUsers delete must NOT cascade-delete forensic history). |
| IpAddress | varchar(45) | NOT NULL, default `"unknown"` | Client IP at attempt time. IPv6-sized. `"unknown"` when `Connection.RemoteIpAddress` is null (test contexts, unix sockets). |
| UserAgent | varchar(512) | NOT NULL | Truncated User-Agent header. Empty string when missing. |
| Reason | text (enum-as-string) | NOT NULL | One of: `BadCredentials`, `BadTotp`, `BadBackupCode`, `LockedOut`, `UnknownUser`, `PasswordResetUnknownEmail` (Stage 6c.1 — recorded when the password-reset request endpoint is hit with an email that does not resolve to any user; provides observability for distributed enumeration without leaking back to the caller). Stored as string via `HasConversion<string>()` per project enum convention. |
| OccurredAt | timestamp with time zone | NOT NULL | Wall-clock UTC at attempt time. |

**Indexes:**

- `(IpAddress, OccurredAt)` — supports "this IP is attacking many accounts" queries.
- `(EmailAttempted, OccurredAt)` — supports "this account is being targeted by many IPs" queries.
- `(OccurredAt)` — supports the retention purge sweep (1-year flat DELETE; first cross-tenant background job in Stage 7).

**Writer:** `FailedLoginRecorder` (`ProjectCeres/Common/Authentication/FailedLoginRecorder.cs`), Scoped DI lifetime, takes `IServiceScopeFactory` so each `RecordAsync` call gets a private DbContext (insulates the recorder from a contaminated request DbContext after Identity raises a concurrency exception).

**Failure contract:** `RecordAsync` is a synchronous DB write on the request hot path. If the write throws, the exception bubbles — login returns 500 and no session cookie is issued. Loud-failure is intentional: a recorder failure must NOT silently let an attacker through. Verified by `FailedLoginRecorderTests.SaveChangesFailure_BubblesAs500_DoesNotIssueSession`.

**Multi-tenancy exemption:** intentionally NO global query filter. See ADR-0065 § Decision-1 for the enumeration of cross-tenant exempt entities.

**GDPR / retention:**

- Erasure (Stage 6c): on right-to-erasure, the 6c flow nullifies `EmailAttempted` for matching rows (column is nullable, no schema change required).
- Retention purge (Stage 7+): 1-year flat cross-tenant `DELETE WHERE OccurredAt < now() - interval '1 year'`. Different cadence from `AuditLog` (6 months, per-user fan-out via `IUserJobRunner`).

### AuditLog (Phase 3, Stage 6.14)

Append-only record of security-relevant authentication events for the user's own account: successful logins (no-MFA / MFA / backup-code branches), logout, registration, password reset (request known-email branch and confirm), email-address change (request, confirm, revoke), MFA enrolment, backup-code regeneration. The user can later see "what has happened to my account" via the Stage 12 read endpoint. Distinct from `FailedLoginAttempt` (rejected attempts, cross-tenant) — `AuditLog` records successful state changes, scoped per user.

**Schema:**

| Field | Type | Constraint | Purpose |
|-------|------|------------|---------|
| Id | uuid | PK | |
| UserId | uuid | NOT NULL | The user the event happened to. No FK in 6.14 — Stage 7 adds the FK to `AspNetUsers.Id` alongside the global query filter. |
| Action | text (enum-as-string) | NOT NULL | `AuditLogAction` value. EF `HasConversion<string>()`. The .NET enum is the source of truth; an architecture test pins values against `docs/superpowers/specs/2026-05-11-stage-6-14-audit-log-design.md` § 3.1. |
| EntityType | varchar(64) | nullable | Set together with `EntityId`. Null on identity-self events (login, logout, register, MFA enrol, password-reset, email-change). |
| EntityId | uuid | nullable | Set together with `EntityType`. |
| OccurredAt | timestamp with time zone | NOT NULL | UTC wall-clock at write time. |
| IpAddress | varchar(45) | NOT NULL, default `"unknown"` | IPv6-sized. `"unknown"` when `Connection.RemoteIpAddress` is null (background contexts, test paths). |

**DB-level CHECK constraint:** `CK_AuditLog_EntityPair` — `(EntityType IS NULL AND EntityId IS NULL) OR (EntityType IS NOT NULL AND EntityId IS NOT NULL)`. Belt-and-braces with the application-level invariant guard in `AuditLogWriter.RecordAsync`.

**Indexes:**

- `(UserId, OccurredAt DESC)` — supports the Stage 12 GET endpoint pagination AND the Stage 7+ per-user 6-month purge.
- `(OccurredAt)` — defensive ops query support.

**Writer:** `AuditLogWriter` (`ProjectCeres/Common/Authentication/AuditLogWriter.cs`), Scoped DI lifetime, fresh `DbContext` via `IServiceScopeFactory` per `RecordAsync` call. Same rationale as `FailedLoginRecorder`: insulates the writer from a request `DbContext` left holding a stale entity after an Identity-internal concurrency race.

**Failure contract:** `RecordAsync` is a synchronous DB write on the request hot path. If the write throws, the exception bubbles — the calling auth flow returns 500 and no state-change side effect (cookie, session, password) survives in a user-visible way. Loud-failure is intentional. Verified end-to-end by `AuditLogIntegrationTests.Failed_audit_insert_during_login_returns_500_AND_does_NOT_issue_session_cookie`.

**Multi-tenancy:** scoped per user. Stage 7's cutover adds the EF global query filter on `UserId`. Until then, all queries explicitly filter by `UserId`.

**GDPR / retention:**

- Erasure (Stage 13): `UserId` is **not** nulled — it remains as a pseudonymized identifier (the user-row's PII is erased separately). `IpAddress` is rewritten to `"erased"` in the same erasure transaction.
- Retention purge (Stage 7+): 6-month per-user fan-out via `IUserJobRunner` — `DELETE WHERE UserId = @u AND OccurredAt < now() - interval '6 months'`. Different cadence from `FailedLoginAttempt` (1-year flat cross-tenant `DELETE`).

**No financial amounts:** `AuditLog` has no `Amount`, `Balance`, `Value`, `Total`, or any `decimal` property. Enforced by architecture test `AuditLog_entity_contains_no_financial_amount_columns`.

### PasswordResetToken (Phase 3, Stage 6c.1)

One row per active or recently-consumed password-reset token. The raw token is a 256-bit RNG value, base64url-encoded, transmitted once in the reset email URL fragment. Only the Argon2id hash is persisted. Single-use: `ConsumedAt` is stamped synchronously inside the same DB transaction as the password write. `MfaVerifiedAt` is stamped on the MFA path after the TOTP step succeeds. Supersession: a new `/request` for a user with prior unconsumed tokens stamps `ConsumedAt` on those rows.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| UserId | uuid | NOT NULL | → AspNetUsers.Id. FK added in Stage 7. |
| TokenHash | varchar(512) | NOT NULL | Argon2id PHC string of the raw 256-bit base64url token. Same hasher pinned to `m=19456 t=2 p=1` as passwords + persistent-cookie tokens + backup codes. |
| CreatedAt | timestamp with time zone | NOT NULL | When the token was issued. |
| ExpiresAt | timestamp with time zone | NOT NULL | `CreatedAt + 15min`. Confirm rejects tokens past this with `INVALID_RESET_TOKEN`. |
| ConsumedAt | timestamp with time zone | nullable | Set on success or supersession. Single-use. |
| MfaVerifiedAt | timestamp with time zone | nullable | Set on the MFA path after the TOTP step succeeds. Informational; the password write happens in the same transaction. |

**Indexes:** `(UserId, ConsumedAt)` for the supersede-prior-unused query and per-user lookups; `(ExpiresAt)` for future cleanup sweeps.

**Token format:** raw token is 32 bytes from `RandomNumberGenerator.GetBytes(32)`, encoded as base64url (≈43 chars). Carried in the reset URL fragment (`/app/password-reset#token=<base64url>`) so it never appears in server logs or `Referer` headers per `security-model.md` § Logging and PII Redaction.

**Lifecycle:**
- `RequestAsync` issues a new token: bulk-supersedes any prior unconsumed tokens for the user via `ExecuteUpdateAsync`, generates + hashes the new value, inserts the row, sends the email. Wrapped in a per-user `SemaphoreSlim`.
- `ConfirmAsync` resolves the token by Argon2-verifying it against active candidates (`WHERE ConsumedAt IS NULL AND ExpiresAt > now()`), re-reads the matched row inside the per-user lock to handle concurrent confirms, validates MFA if `user.TwoFactorEnabled = true`, performs the password write, then stamps `ConsumedAt` (and `MfaVerifiedAt` on the MFA branch) via `ExecuteUpdateAsync`.

**Constant-time discipline:** `ConfirmAsync` runs at least one Argon2 verify even when zero candidates match, so timing doesn't reveal "no rows."

**Multi-tenancy:** scoped per user. Stage 7 will add a global query filter alongside the FK; until then, all queries explicitly filter by `UserId`.

### LockoutUnlockToken (Phase 3, Stage 6.10)

One row per active or recently-consumed lockout-unlock token. Issued by `AuthController.Login` on the lockout transition (the failing `PasswordSignInAsync` that flipped `LockoutEnd` null → not null). The user receives a single email per lockout window; clicking the link POSTs to `/api/auth/lockout-unlock` and clears `AccessFailedCount` + `LockoutEnd` immediately. Defends the legitimate user against an attacker-induced lockout-loop DoS.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| UserId | uuid | NOT NULL | → AspNetUsers.Id. FK added in Stage 7. |
| TokenHash | varchar(512) | NOT NULL | Argon2id PHC string of the raw 256-bit base64url token. Same hasher pinned to `m=19456 t=2 p=1` as passwords + persistent-cookie tokens + backup codes + password-reset + email-change tokens. |
| CreatedAt | timestamp with time zone | NOT NULL | When the token was issued — i.e., the moment lockout engaged. |
| ExpiresAt | timestamp with time zone | NOT NULL | `CreatedAt + 15min`. Matches the lockout duration; once the natural lockout self-expires, an unlock link has no marginal value. |
| ConsumedAt | timestamp with time zone | nullable | Set on first successful confirm OR on supersession by a new issuance. Single-use. |

**No `MfaVerifiedAt`** (deliberately diverges from `PasswordResetToken`) — confirm has no MFA gate. The unlock is undo-only: it does NOT change the password, does NOT change the email, does NOT log the user in. The unlocked account is still password-protected and (if MFA enabled) still MFA-protected on the next login attempt.

**Indexes:** `(UserId, ConsumedAt)` for the supersede-prior-unused query; `(ExpiresAt)` for the future Stage 7+ cleanup sweep.

**Token format:** raw token is 32 bytes from `RandomNumberGenerator.GetBytes(32)`, encoded as base64url (≈43 chars). Carried in the unlock URL fragment (`/app/lockout-unlock#token=<base64url>`) so it never appears in server logs or `Referer` headers per `security-model.md` § Logging and PII Redaction.

**Lifecycle:**
- `LockoutUnlockService.IssueAsync` is called by `AuthController.Login` on the transition only — gated by `wasLockedBefore == false && isLockedAfter == true`, captured inside the existing per-user `_loginLocks` semaphore. Already-locked accounts that receive subsequent bad-password attempts do NOT trigger additional issuances. This is the email-DoS defence (otherwise an attacker who knows the victim's email amplifies their bad-password loop into an email flood).
- `IssueAsync` bulk-supersedes prior unconsumed tokens for the user, generates + hashes the new value, inserts the row, sends the email. Email-send failures are logged but do NOT roll back the token write or break the user-visible `ACCOUNT_LOCKED_OUT` response.
- `ConfirmAsync` resolves the token by Argon2-verifying against active candidates (`WHERE ConsumedAt IS NULL AND ExpiresAt > now()`), re-reads the matched row inside the per-user lock to handle concurrent confirms, calls `ResetAccessFailedCountAsync` + `SetLockoutEndDateAsync(user, null)`, stamps `ConsumedAt` via `ExecuteUpdateAsync`, and writes an `AuditLog` row with `Action = LockoutSelfServiceUnlock`.

**Constant-time discipline:** `ConfirmAsync` runs at least one Argon2 verify even when zero candidates match, so timing doesn't reveal "no rows".

**Side effects of unlock (by deliberate omission):** does NOT revoke `UserSession` rows, does NOT regenerate `SecurityStamp`, does NOT touch `EmailConfirmed`, does NOT sign anyone in. The unlock is reversing a side-effect of failed-login attempts — it's not a credential change or a recovery flow, so it doesn't escalate security state. Mirrors `EmailChangeService.RevokeAsync`'s "undo-only operations don't move security state" posture.

**Multi-tenancy:** scoped per user. Stage 7 adds the global query filter + FK alongside every other user-owned entity. Until then, all queries explicitly filter by `UserId`.

**Retention:** the `(ExpiresAt)` index supports a future Stage 7+ flat cleanup sweep (`DELETE WHERE ConsumedAt IS NOT NULL OR ExpiresAt < now() - interval '1 day'`); not in 6.10 (no background-runner abstraction until Stage 7).

### EmailConfirmationToken (Phase 3, Stage 9.3)

One row per active or recently-consumed email-confirmation token issued at registration. Issued by `AuthController.Register` on a fresh-create AND on the duplicate-unconfirmed branch (the legitimate owner gets a fresh verification link); the confirmed-existing branch issues NO new token and runs a dummy Argon2id to mirror the issue cost for timing parity. Confirmed via `POST /api/auth/email/verify`, which sets `ApplicationUser.EmailConfirmed = true` in the same transaction as `ConsumedAt`.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| UserId | uuid | NOT NULL | → AspNetUsers.Id. FK added in Stage 7. |
| TokenLookup | bytea | NOT NULL, unique | HMAC-SHA256(serverSecret, rawToken). Unique index supports O(1) lookup at `/email/verify` time instead of Argon2id-O(N) over candidates. Stage 6.15 / 9.1.5.a pattern. |
| TokenHash | varchar(512) | NOT NULL | Argon2id PHC string of the raw 256-bit base64url token. Same hasher pinned to `m=19456 t=2 p=1` as passwords + password-reset/email-change/lockout-unlock tokens + persistent-cookie tokens + backup codes. |
| CreatedAt | timestamp with time zone | NOT NULL | When the token was issued. |
| ExpiresAt | timestamp with time zone | NOT NULL | `CreatedAt + 30min`. Verify rejects tokens past this with `INVALID_VERIFICATION_TOKEN`. |
| ConsumedAt | timestamp with time zone | nullable | Set on first successful verify OR on supersession by a new issuance. Single-use. |

**No `MfaVerifiedAt`** — email confirmation never gates on TOTP.

**Indexes:** `(UserId)` for the supersede-prior-unused query (`Where(t => t.UserId == x && t.ConsumedAt == null).ExecuteUpdateAsync(...)`); unique `(TokenLookup)` for the O(1) verify lookup. No `(ExpiresAt)` index in the 9.3 migration — added in a future cleanup-sweep stage alongside the other token tables.

**Token format:** raw token is 32 bytes from `RandomNumberGenerator.GetBytes(32)`, encoded as base64url (≈43 chars). Carried in the verification URL fragment (`/email-verify#token=<base64url>`) so it never appears in server logs or `Referer` headers per `security-model.md` § Logging and PII Redaction.

**Lifecycle:**
- `EmailConfirmationService.IssueAsync` is called by `AuthController.Register` on the fresh-create path (AFTER `tx.CommitAsync` — email-send failures must not roll back user creation) AND on the duplicate-unconfirmed path (AFTER the same commit; the user already exists in the DB). It is also called by `RequestResendAsync` when the resend endpoint resolves a known-but-unconfirmed user.
- `IssueAsync` opens a `PreAuthUserScope` (RLS scope for the userId), bulk-supersedes any prior unconsumed tokens for the user, generates + hashes the new value, computes the TokenLookup, inserts the row, then sends the email outside the lock. Email-send failures are logged but do NOT roll back the token write.
- `ConfirmAsync` looks up the row by `TokenLookup` via `AdminDbContext` (BYPASSRLS, because the userId isn't known until after the lookup), Argon2-verifies the `TokenHash` defence-in-depth, opens a `PreAuthUserScope(match.UserId)`, re-reads the row inside the per-user semaphore, sets `EmailConfirmed = true` on the user, stamps `ConsumedAt` via `ExecuteUpdateAsync`, and writes `AuditLogAction.EmailVerified`.
- `RequestResendAsync` is anti-enumerating: unknown email → two dummy Argon2 hashes → 204; known + confirmed → two dummy Argon2 hashes → 204 (no token issued); known + unconfirmed → call `IssueAsync` → 204. Per-email rate limit: 5 requests / 1 hour (MemoryCache `RateBucket` pattern shared with `PasswordResetService`).

**Constant-time discipline:** All paths in `RequestResendAsync` pay the same Argon2id cost (two hashes) regardless of outcome. `ConfirmAsync` runs at least one Argon2 verify even when zero candidates match.

**Side effects of confirm:** sets `ApplicationUser.EmailConfirmed = true`. Does NOT revoke sessions (no sessions exist yet for an unconfirmed user — `RequireConfirmedEmail = true` blocks `/login` until verification). Does NOT regenerate `SecurityStamp`. Does NOT log the user in. The user is expected to navigate to `/login` after verification.

**Multi-tenancy:** scoped per user. Stage 7 adds the global query filter + FK alongside every other user-owned entity. Until then, all queries explicitly filter by `UserId` AND use `IgnoreQueryFilters()` because the pre-auth context has no `app.current_user_ref` set when the resend endpoint is invoked.

**Retention:** future cleanup sweep deferred to Stage 7+ (same posture as `PasswordResetToken` and `LockoutUnlockToken`).

### EmailDeliveryEvent (Phase 3, Stage 8e)

Records every event Resend's webhook reports about an outgoing email (`sent`, `delivered`, `bounced`, `complained`). On `email.bounced`, the receiving handler flips `ApplicationUser.EmailConfirmed = false` for the affected address — so a hard-bounced address stops receiving further mail until the user re-verifies. Intentionally **cross-tenant** — bounces from addresses that no longer resolve to any `ApplicationUser` (e.g. after an email-change away from the bounced address) are still recorded.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| UserId | uuid? | nullable | Resolved by lower-cased `EmailAddress` lookup against `AspNetUsers.NormalizedEmail` at write time. NULL when no user currently owns the address. No FK — webhook events must persist even when no matching user exists. |
| MessageId | varchar | NOT NULL | Resend's `email_id` from the webhook payload. Correlates an `email.sent` row with later `delivered` / `bounced` / `complained` rows for the same send. |
| Type | text (enum-as-string) | NOT NULL | One of `email.sent`, `email.delivered`, `email.bounced`, `email.complained`. Stored as string via `HasConversion<string>()` per project enum convention. |
| EmailAddress | varchar(256) | NOT NULL | The recipient address Resend reported, lower-cased + trimmed. Matches `ApplicationUser.NormalizedEmail` shape so the `UserId` resolution lookup is a direct equality match. |
| Payload | jsonb | NOT NULL | Raw event JSON. Kept verbatim for incident diagnostics — provider field shapes drift over time and we never want to be parsing-bound on a bounce investigation. |
| OccurredAt | datetimeoffset | NOT NULL | When Ceres recorded the event (server clock at controller entry — not the timestamp inside the provider payload). |

**Indexes:** `(EmailAddress, OccurredAt DESC)` — supports "what's the bounce history for this address" queries on user support paths and the future bounce-aware retry suppression.

**Multi-tenancy exemption:** intentionally NO global query filter. Webhook events arrive from Resend with no session context — `ICurrentUserAccessor` would throw if consulted. The `UserId` column is the result of a best-effort address-lookup, not a tenancy boundary. Does NOT implement `IUserOwned` (the project's tenancy-marker interface) — `IUserOwned.UserId` is non-nullable, and bounces from unknown addresses must still record. Follows the `FailedLoginAttempt` precedent (Stage 6b.2). See ADR-0065 § Decision-1 enumeration of cross-tenant exempt entities.

**Writer:** `ResendWebhookController` (controller, not a Scoped service) accepts the webhook POST, validates the Svix HMAC signature, persists the row, and on `email.bounced` calls `UserManager.SetEmailConfirmedAsync(user, false)` if the address resolved. Per `security-model.md` § Email Security Rules, the webhook is the only inbound surface Resend can hit; the request is anonymous but signature-locked.

**Deletion rule:** **hard-deletes allowed.** Operational records, not user-owned data — no soft-delete retention obligation. A future retention sweep can delete by `OccurredAt` (cadence not set in Stage 8; revisit alongside the broader Stage 7+ retention purge work).

**GDPR:** on right-to-erasure (Stage 13), rows where `UserId` matches the erased user are deleted outright. The `Payload` JSONB contains the recipient's email address verbatim — keeping the row would leak the erased user's address back into ops queries.

### CustomerArchive (Phase 3)

Created when a user account is closed. Records the closure type and manages the archive lifecycle. See ADR-0029 for the full lifecycle specification.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| UserId | uuid | NOT NULL | References the now-inactive user record |
| ClosureType | varchar | NOT NULL | Enum: `NaturalChurn` \| `GdprErasure` |
| ClosedAt | datetime | NOT NULL | UTC timestamp of account closure |
| GracePeriodEndsAt | datetime | nullable | 30 days after `ClosedAt`; null for `GdprErasure` |
| PermanentDeletionScheduledAt | datetime | nullable | 180 days after `ClosedAt`; null for `GdprErasure` |
| ArchiveFilePath | varchar | nullable | Filesystem path to the sealed archive file; null for `GdprErasure` or after deletion |
| ArchivedFileDeletedAt | datetime | nullable | Stamped when the archive file is permanently deleted |
| RestorationFee | decimal(18,2) | nullable | Set at archive creation time by policy; null for `GdprErasure` |
| RestoredAt | datetime | nullable | Stamped when restoration completes; null if never restored |

**Critical constraint:** `ClosureType = GdprErasure` rows must never have an `ArchiveFilePath` set. Enforced at the service layer, not just the UI.

**Deletion rule:** `CustomerArchive` rows are never hard deleted — they are the audit trail of the closure itself. Only the `ArchiveFilePath` file is deleted at `PermanentDeletionScheduledAt`.

### SavedSearch (Phase 3)

Stores a named set of filter parameters for a specific table, so users can re-apply frequently used filter combinations without re-entering them. One row per saved search, scoped to the authenticated user and a specific table name (e.g. "transactions", "movements").

| Column        | Type     | Constraints  | Notes                                                                           |
|---------------|----------|--------------|---------------------------------------------------------------------------------|
| Id            | uuid     | PK           |                                                                                 |
| UserId        | uuid     | FK, NOT NULL | → User                                                                          |
| TableName     | varchar  | NOT NULL     | The table this search applies to — e.g. `"transactions"`, `"movements"`, `"transfers"` |
| Name          | varchar  | NOT NULL     | User-given name, e.g. "This month groceries"                                   |
| FilterSetJson | jsonb    | NOT NULL     | Serialised filter state — shape varies by table; parsed by the relevant list component |
| CreatedAt     | datetime | NOT NULL     | When the saved search was created                                               |

**Deletion rule:** hard delete with confirmation. No soft delete — no financial history is attached to a saved search.

**Uniqueness:** no uniqueness constraint on `(UserId, TableName, Name)` — the user may create duplicates if they choose.

---

### SupportTicket (Phase 3)

Stores user-submitted support requests. Admin management is handled via a separate admin surface.

| Column    | Type     | Constraints  | Notes                                                               |
|-----------|----------|--------------|---------------------------------------------------------------------|
| Id        | uuid     | PK           |                                                                     |
| UserId    | uuid     | FK, NOT NULL | → User (the submitting user)                                        |
| Subject   | varchar  | NOT NULL     |                                                                     |
| Message   | text     | NOT NULL     |                                                                     |
| Status    | varchar  | NOT NULL     | `Open`, `InProgress`, `Resolved`, `Closed`                         |
| Priority  | varchar  | NOT NULL     | `Low`, `Normal`, `High`, `Urgent`                                   |
| CreatedAt | datetime | NOT NULL     |                                                                     |
| UpdatedAt | datetime | NOT NULL     | Stamped on every status or priority change                          |

**Email notification:** on creation, an email is sent to the configurable admin address.

**Deletion rule:** no deletion — tickets are the audit trail of user contact. Status transitions to `Closed` when resolved.

---

### AdminAuditLog (Phase 3)

Append-only record of every action performed by an admin. No endpoint exposes edit or delete on this table. See ADR-0028.

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | uuid | PK | |
| AdminUserId | uuid | FK, NOT NULL | → User (the admin who performed the action) |
| TargetUserId | uuid | FK, nullable | → User (the user affected); null for system-level actions |
| Action | varchar | NOT NULL | Enum: `Deactivate`, `Reactivate`, `ForceLogout`, `Impersonate`, `ImpersonateEnd`, `GdprExport`, `GdprErasure`, `PasswordReset`, `PlanOverride` |
| Detail | jsonb | nullable | Before/after state or relevant parameters for the action |
| PerformedAt | datetime | NOT NULL | UTC timestamp |
| ImpersonationSessionId | uuid | nullable | Groups all actions within one impersonation session |

**Append-only:** INSERT is the only permitted operation. No UPDATE or DELETE — not even for admins.
