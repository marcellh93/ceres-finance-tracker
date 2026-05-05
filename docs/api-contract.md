# API Contract

> **Diataxis type:** Reference — defines the conventions the API must follow when it is built. This document establishes the contract so that when Phase 2 JSON actions and Phase 3 API endpoints are written, they conform to a consistent shape from the start.
>
> **Current status:** No API exists. Phase 1 is MVC — controllers return HTML pages, not JSON. This document governs the JSON actions introduced in Phase 2 (for React components) and the full Web API built in Phase 3. Write it once, apply it consistently.

## Index

1. [When an API Exists](#when-an-api-exists)
2. [URL Design](#url-design)
3. [Response Shape](#response-shape)
4. [Error Shape](#error-shape)
5. [Authentication](#authentication)
6. [Versioning Strategy](#versioning-strategy)
7. [Phase-by-Phase API Surface](#phase-by-phase-api-surface)

---

## When an API Exists

| Phase | API? | Form |
|-------|------|------|
| Phase 1 | No | MVC controllers return HTML only |
| Phase 2 | Partial | Dedicated controller actions returning JSON for React chart/dashboard components. Not a versioned API — these actions are internal to the same app |
| Phase 3 | Yes | Full decoupled Web API. React SPA on a separate origin. Versioned. Requires auth on all endpoints |

The Phase 2 JSON actions are not a public API — they are co-located with the MVC app and not versioned. The conventions in this document apply fully from Phase 3. Apply them to Phase 2 actions anyway to avoid rework.

---

## URL Design

### Resource naming

- Use plural nouns for collections: `/api/transactions`, `/api/accounts`
- Use the resource ID in the path for individual records: `/api/transactions/{id}`
- Nest sub-resources only one level deep: `/api/transactions/{id}/attachments`
- Do not use verbs in URLs — the HTTP method carries the action

### HTTP methods

| Method | Use | Body |
|--------|-----|------|
| `GET` | Read — no side effects | None |
| `POST` | Create a new resource | Resource fields |
| `PUT` | Full replacement of an existing resource | Complete resource |
| `PATCH` | Partial update (specific fields) | Changed fields only |
| `DELETE` | Delete a resource | None |

### IDs in URLs

All user-facing resource IDs are UUIDs (see ADR-0018). URLs look like:

```
GET /api/transactions/3f7a2b1c-4d5e-6789-abcd-ef0123456789
```

Never expose sequential integer IDs in URLs. System lookup tables (Currency, AccountType, etc.) use integer IDs internally but do not appear in user-facing URLs.

### Query parameters

Use query parameters for filtering, sorting, and pagination — not path segments:

```
# Phase 1 — date range filter only, no pagination parameters
GET /Transactions?from=2026-01-01&to=2026-01-31
GET /Transactions?accountId={uuid}&from=2026-01-01&to=2026-01-31

# Phase 3 — offset-based pagination added
GET /api/transactions?accountId={uuid}&from=2026-01-01&to=2026-01-31&page=1&pageSize=50
GET /api/accounts?currencyId=1
```

> **Phase 1 note:** Transaction History returns the most recent 50 records by default. When the user applies a date range filter, all records within the range are returned with no cap — no `page` or `pageSize` parameters exist in Phase 1. Offset-based pagination (`page`, `pageSize`) is introduced in Phase 3. See ADR-0027.

---

## Response Shape

### Success response — single resource

```json
{
  "data": {
    "id": "3f7a2b1c-4d5e-6789-abcd-ef0123456789",
    "date": "2026-04-11",
    "amount": "120.50",
    "description": "Supermarket",
    "categoryId": "...",
    "accountId": "...",
    "createdAt": "2026-04-11T14:23:00Z"
  }
}
```

### Success response — collection

```json
{
  "data": [ { ... }, { ... } ],
  "meta": {
    "total": 142,
    "page": 1,
    "pageSize": 50
  }
}
```

The `meta` field is only present on paginated collections. Non-paginated list endpoints (e.g. accounts, categories) return `data` only.

### Pagination defaults

| Parameter | Default | Maximum | Notes |
|-----------|---------|---------|-------|
| `page` | `1` | — | 1-based |
| `pageSize` | `50` | `200` | Requests above 200 are rejected with `422` |

Offset-based pagination (`page` + `pageSize`) is used throughout Phase 3. Cursor-based pagination is not introduced until a performance requirement justifies it — do not add it preemptively.

### Amounts

Financial amounts are returned as **strings**, not numbers. JavaScript's `Number` type is IEEE 754 floating-point — it cannot represent all decimal values exactly. `120.50` stored as a float may be read back as `120.49999999...`. Return amounts as strings and parse them with a decimal library on the client.

```json
"amount": "120.50"   ✓ correct
"amount": 120.50     ✗ wrong — floating-point risk
```

### Dates

- **Dates without time** (transaction date, due date): ISO 8601 date string — `"2026-04-11"`
- **Timestamps** (createdAt, updatedAt): ISO 8601 UTC datetime — `"2026-04-11T14:23:00Z"`

Do not return Unix timestamps. Do not return locale-formatted dates from the API — formatting is the client's responsibility.

---

## Error Shape

All errors return a consistent JSON body regardless of the status code:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more fields are invalid.",
    "details": [
      { "field": "amount", "message": "Amount must be greater than zero." },
      { "field": "categoryId", "message": "Category not found or does not belong to you." }
    ]
  }
}
```

The `details` array is present only for validation errors (422). For other errors, `details` is omitted.

### HTTP status codes

| Status | When to use |
|--------|-------------|
| `200 OK` | Successful GET, PATCH, PUT |
| `201 Created` | Successful POST — include `Location` header pointing to the new resource |
| `202 Accepted` | Async job accepted (e.g. GDPR data export) — body: `{ "data": { "jobId": "...", "message": "Export started. You will be notified by email when ready." } }` |
| `204 No Content` | Successful DELETE — no body |
| `400 Bad Request` | Malformed request (invalid JSON, missing required fields at the HTTP level) |
| `401 Unauthorized` | Request is not authenticated |
| `404 Not Found` | Resource does not exist **or belongs to another user** — never use 403 for user-owned resources |
| `422 Unprocessable Entity` | Request is well-formed but fails business validation (negative amount, wrong currency, etc.) |
| `429 Too Many Requests` | Rate limit exceeded — always include `Retry-After: <seconds>` header |
| `500 Internal Server Error` | Unexpected server error — return a generic message, never a stack trace |

**Do not return `403 Forbidden` for user-owned resources.** Returning 403 confirms the resource exists, which is information an attacker can exploit to enumerate valid IDs. See `security-model.md → IDOR Prevention`.

### Validation responses (implementation note)

`Program.cs` configures `InvalidModelStateResponseFactory` so any `[ApiController]` action whose `ModelState` is invalid auto-returns **422 Unprocessable Entity** with the validation-error JSON shape above. This applies to all built-in attribute validation (`[Required]`, `[Range]`, `[StringLength]`, etc.).

For cross-field validation that runs after the auto-check (e.g. "source account must differ from destination"), do **not** call `return ValidationProblem(ModelState)` — that helper returns 400, bypassing the factory. Return `UnprocessableEntity(...)` directly with the same JSON shape so the response stays consistent at 422.

### 422 response body — dual-shape contract

Both `ModelState` validation failures and business-rule validation failures return **422 Unprocessable Entity** with `error.code = "VALIDATION_ERROR"`, but the `details` array has two shapes:

1. **Field validation** (`[Required]`, `[Range]`, `[StringLength]`, etc. — handled by `InvalidModelStateResponseFactory`): `details` is an array of objects `{ field, message }`, one per invalid field.
2. **Business rule validation** (controller or service-level checks resulting in `InvalidOperationException`): `details` is `[]` (empty array). The human-readable message lives in `error.message` instead.

Frontend code that needs to distinguish field-targeted errors from semantic/business errors should check `details.length`. Field errors render next to form inputs; business errors show as alerts.

Example field validation (422):
```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more fields are invalid.",
    "details": [
      { "field": "amount", "message": "Amount must be greater than zero." },
      { "field": "categoryId", "message": "Category not found." }
    ]
  }
}
```

Example business rule validation (422):
```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Source and destination accounts must be different.",
    "details": []
  }
}
```

### Deprecation header

When an endpoint or field is deprecated, include in the response:

```
Deprecation: true
Sunset: Sat, 01 Jan 2027 00:00:00 GMT
Link: <https://docs.projectceres.com/api/v2/migration>; rel="successor-version"
```

Define the deprecation policy before releasing v2: minimum notice period (recommended: 6 months), how deprecated endpoints are communicated (release notes, in-API header, email to registered developers).

---

## Authentication

Phase 3 API authentication uses **cookie-based auth**, not JWT bearer tokens.

- The React SPA is served from the same origin as the API (Option A hosting: React build in `wwwroot/`) — HttpOnly cookie is the correct credential mechanism
- HttpOnly cookies are inaccessible to JavaScript — safer than localStorage-stored JWTs
- ASP.NET Core Data Protection handles cookie encryption and validation
- No token is stored in `localStorage`, `sessionStorage`, or any JS-accessible state — see `security-model.md → Authentication Model`

**JWT access tokens are Phase 4+**, when a mobile client or third-party API consumer requires bearer token authentication. The identity masking section in `planning-phase3.md` (HMAC pseudonymisation, `USER_REF_SECRET`) applies to Phase 3 data storage, but the JWT token issuance mechanism is deferred. Do not implement JWT issuance in Phase 3.

If a future mobile app requires the API, a bearer token flow (short-lived JWT + refresh token stored as hash in `UserSession`) will be added at that point. This document will be updated.

### Anti-forgery (CSRF)

Cookie-authenticated API endpoints are vulnerable to CSRF if anti-forgery protection is not applied. Configure ASP.NET Core's anti-forgery middleware to validate on all state-changing API endpoints (`POST`, `PUT`, `PATCH`, `DELETE`). The React SPA must include the anti-forgery token in requests — read it from a cookie set by the server (`XSRF-TOKEN` pattern) and send it in the `X-XSRF-TOKEN` header.

### Unauthenticated requests

Any request to an authenticated endpoint without a valid session returns `401 Unauthorized` with:

```json
{ "error": { "code": "UNAUTHENTICATED", "message": "Authentication required." } }
```

Do not redirect to a login page from an API endpoint — return JSON.

---

## Versioning Strategy

### Phase 2 — no versioning

Phase 2 JSON actions are internal to the MVC app and consumed only by the co-located React components. No versioning is needed — any change to a Phase 2 action is accompanied by a matching change to the component that calls it.

### Phase 3 — URL prefix versioning

When the full Web API is introduced, version it from the first endpoint:

```
/api/v1/transactions
/api/v1/accounts
```

**Why URL prefix versioning:**
- Visible in logs, browser dev tools, and HTTP clients without inspecting headers
- Simple to implement in ASP.NET Core via route prefixes or `[Route("api/v1/[controller]")]`
- Easy to test and cache at the infrastructure level

**When to increment the version:**
- A response field is removed or renamed (breaking)
- The semantics of an existing field change (breaking)
- A required request field is added (breaking)

Adding new optional response fields or new endpoints is not breaking — do not increment the version.

**Maintaining v1 while v2 exists:** keep v1 handlers alive for at least one deprecation cycle before removing them. Announce deprecation via a `Deprecation` response header. Define the deprecation policy before v2 is released.

### No versioning by header

Do not version via `Accept: application/vnd.projectceres.v1+json`. Header-based versioning is invisible in logs and harder to test. URL prefix is the explicit, debuggable choice.

---

## Phase-by-Phase API Surface

This table lists what the API will expose. It is not a full endpoint specification — it records scope so future phases have a clear starting point.

### Phase 2 — Internal JSON actions (co-located, not versioned)

| Action | Method | URL | Purpose |
|--------|--------|-----|---------|
| Dashboard summary | GET | `/api/dashboard/summary` | Consolidated KPIs: net worth (per currency, array), MTD income/expenses/savings rate (nested `mtd` object), pending reminders count. `savingsRate` is a fraction (0–1), not a percentage. |
| Account balances | GET | `/api/dashboard/accounts` | All account balances, grouped by currency — for dashboard balance list |
| Category budget progress | GET | `/api/dashboard/category-budgets` | Spent vs. limit per expense category for the current month |
| Goal budget progress | GET | `/api/dashboard/goal-budgets` | Spent vs. target per goal budget |
| Financial health snapshot | GET | `/api/dashboard/health` | 15 fields, every numeric nullable: spendable balance components (liquid, imminent/later bills, budget reserve, available today, safe to spend), runway months + avg monthly expense, current and rolling-average income, income delta percent, budget burn rate + spent + total limit, currency code/symbol |
| Net worth trend chart | GET | `/api/dashboard/net-worth-trend` | `NetWorthTrendDto { currencyCode, currencySymbol, points: NetWorthTrendPoint[12] }` — `NetWorthTrendPoint { month: "yyyy-MM", assets, liabilities, netWorth }` |
| Income/expense trend chart | GET | `/api/dashboard/income-expense` | `IncomeExpenseDto { currencyCode, currencySymbol, points: IncomeExpensePoint[12] }` — `IncomeExpensePoint { month: "yyyy-MM", income, expenses }` |
| Spending by category chart | GET | `/api/dashboard/spending-by-category` | `SpendingByCategoryDto { currencyCode, currencySymbol, total, slices: SpendingByCategorySlice[] }` — `SpendingByCategorySlice { categoryName, amount }` |
| Account balances chart | GET | `/api/dashboard/account-balances` | `AccountBalancesDto { currencyCode, currencySymbol, rows: AccountBalanceRow[] }` — `AccountBalanceRow { accountName, balance }` |
| Cash flow trend chart | GET | `/api/dashboard/cash-flow` | `CashFlowDto { currencyCode, currencySymbol, points: CashFlowPoint[12] }` — `CashFlowPoint { month: "yyyy-MM", netFlow }` |
| Movements cleared toggle | PATCH | `/api/movements/{id}/cleared` | Toggle `IsCleared` on a Transaction, Transfer, or LiabilityPayment — body: `{ "type": "transaction"\|"transfer"\|"liabilitypayment", "cleared": bool }`. The `type` field accepts `"transaction"`, `"transfer"`, or `"liabilitypayment"`. |
| Movements list | GET | `/api/movements` | Returns `MovementsPageDto { items: MovementListItemDto[], totalCount, page, pageSize }`. Query params: `q` (text search), `accountId`, `from`, `to`, `page`, `pageSize` (default 50, max 200). |
| Create transaction | POST | `/api/transactions` | Body: `CreateTransactionRequest { date, amount, accountId, categoryId, description? }`. Returns `201 Created` with `{ id }`. Returns `422 Unprocessable Entity` with `ValidationProblemDetails` on invalid input. |
| Get transaction | GET | `/api/transactions/{id}` | Returns `TransactionEditDto { id, date, amount, accountId, categoryId, description, isCleared, attachments[] }`. `404` if missing. |
| Update transaction | PUT | `/api/transactions/{id}` | Body: `UpdateTransactionRequest`. `200` on success, `422` on validation, `404` on missing. |
| Delete transaction | DELETE | `/api/transactions/{id}` | Hard delete. `204` on success, `404` on missing. |
| Upload transaction attachment | POST | `/api/transactions/{id}/attachments` | Multipart upload. Returns `AttachmentDto`. |
| Delete transaction attachment | DELETE | `/api/transactions/attachments/{attachmentId}` | `204` on success, `404` on missing. |
| Create transfer | POST | `/api/transfers` | Body: `CreateTransferRequest { date, amount, sourceAccountId, destAccountId, description? }`. Returns `201 Created` with `{ id }`. Returns `422` if source == destination, cross-currency, or other validation failures. |
| Get transfer | GET | `/api/transfers/{id}` | Returns transfer edit DTO `{ id, date, amount, sourceAccountId, destAccountId, description, isCleared, attachments[] }`. `404` if missing. |
| Update transfer | PUT | `/api/transfers/{id}` | Body: `UpdateTransferRequest`. `200` on success, `422` on validation, `404` on missing. |
| Delete transfer | DELETE | `/api/transfers/{id}` | Hard delete. `204` on success, `404` on missing. |
| Upload transfer attachment | POST | `/api/transfers/{id}/attachments` | Multipart upload. Returns `AttachmentDto`. |
| Delete transfer attachment | DELETE | `/api/transfers/attachments/{attachmentId}` | `204` on success, `404` on missing. |
| Create liability payment | POST | `/api/liability-payments` | Body: `CreateLiabilityPaymentRequest { date, amount, assetAccountId, liabilityAccountId, description? }`. Returns `201 Created` with `{ id }`. Returns `422` on validation failures. |
| Get liability payment | GET | `/api/liability-payments/{id}` | Returns liability payment edit DTO `{ id, date, amount, assetAccountId, liabilityAccountId, description, isCleared }`. `404` if missing. No attachments. |
| Update liability payment | PUT | `/api/liability-payments/{id}` | Body: `UpdateLiabilityPaymentRequest`. `200` on success, `422` on validation, `404` on missing. |
| Delete liability payment | DELETE | `/api/liability-payments/{id}` | Hard delete. `204` on success, `404` on missing. |
| Movement discriminator | GET | `/api/movements/{id}` | Returns `{ id, movementType }` — used by SPA edit route to dispatch to the correct typed endpoint. |
| Bulk mark cleared | POST | `/api/movements/bulk-cleared` | Body: `BulkClearedRequest { from, to, accountId?, type? }`. Returns `{ cleared: int }` — count of rows transitioned. |
| Movements CSV export | GET | `/api/movements/export.csv` | CSV download. Same query params as `GET /api/movements` (`q`, `accountId`, `from`, `to`, `type`). |
| Active accounts | GET | `/api/accounts/active` | Returns `AccountOptionDto[] { id, name, currencyCode, currencySymbol, accountTypeName }`. Active accounts only. |
| Active categories | GET | `/api/categories/active` | Returns `CategoryOptionDto[] { id, name, categoryTypeName }`. Active, non-system categories only. |
| Import file headers | POST | `/api/import/headers` | Upload a CSV or XLSX file; returns detected column headers and auto-matched field mappings (`HeaderDetectionResult`) |
| Import transactions | POST | `/api/import` | Upload file + column mappings; runs the full import pipeline; returns `ImportResult` (rows imported, reconciled, flagged, failed) |
| List category budgets | GET | `/api/category-budgets` | Query: `currency` (code), `includeArchived` (bool). Returns `CategoryBudgetListItemDto[]`. |
| Create category budget | POST | `/api/category-budgets` | Body: `CreateCategoryBudgetRequest { categoryId, currencyId, limitAmount }`. Returns `201 Created` with `{ id }`. Returns `409 Conflict` with `error.code = "DUPLICATE_BUDGET"` (see below) when an active or archived budget already exists for the same `(categoryId, currencyId)`. Returns `422` on validation failures. |
| Get category budget | GET | `/api/category-budgets/{id}` | Returns `CategoryBudgetEditDto`. `404` if missing. |
| Update category budget | PUT | `/api/category-budgets/{id}` | Body: `UpdateCategoryBudgetRequest`. `204 No Content` on success, `422` on validation. |
| Archive category budget | PATCH | `/api/category-budgets/{id}/archive` | Sets `IsActive = false`. `204` on success, `404` on missing. |
| Reactivate category budget | PATCH | `/api/category-budgets/{id}/reactivate` | Sets `IsActive = true`. `204` on success, `404` on missing, `409 DUPLICATE_BUDGET` if another active budget already exists for the same `(categoryId, currencyId)`. |
| Category budget spend | GET | `/api/category-budgets/{id}/spend` | Query: `year`, `month`. Returns spend for the budget period whose end-date falls in `(year, month)` — see `BudgetPeriod` helper in `models.md`. |
| List goal budgets | GET | `/api/goal-budgets` | Query: `currency` (code), `type` (`spending` \| `savings`), `includeArchived` (bool). Returns `GoalBudgetListItemDto[]`. |
| Create goal budget | POST | `/api/goal-budgets` | Body: `CreateGoalBudgetRequest`. Returns `201 Created` with `{ id }`. `422` on validation failures (e.g. Savings goal with no `linkedAccountId`). |
| Get goal budget | GET | `/api/goal-budgets/{id}` | Returns `GoalBudgetEditDto`. `404` if missing. |
| Update goal budget | PUT | `/api/goal-budgets/{id}` | Body: `UpdateGoalBudgetRequest`. `204` on success, `422` on validation. |
| Archive goal budget | PATCH | `/api/goal-budgets/{id}/archive` | Sets `IsActive = false`. `204` on success, `404` on missing. |
| Reactivate goal budget | PATCH | `/api/goal-budgets/{id}/reactivate` | Sets `IsActive = true`. `204` on success, `404` on missing. |
| Goal budget progress | GET | `/api/goal-budgets/{id}/progress` | Returns progress payload. For Spending goals: SUM of tagged transactions vs. target. For Savings goals: linked-account balance vs. target. |
| Budget discriminator | GET | `/api/budgets/{id}` | Returns `{ id, kind: "CategoryBudget" \| "GoalBudget" }` — used by SPA edit route to dispatch to the correct typed endpoint. `404` if neither exists. |
| List currencies | GET | `/api/currencies` | Returns `{ id, code, symbol }[]` — supported currencies for budget/account forms. |
| List recurring transactions | GET | `/api/recurring-transactions` | Returns `RecurringTransactionListItemDto[]`. Query: `includeArchived` (bool). `estimatedAmount` is `decimal?` — `null` means "amount varies". |
| Create recurring transaction | POST | `/api/recurring-transactions` | Body: `CreateRecurringTransactionRequest`. Returns `201 Created` with `{ id }`. `422` on validation failures. |
| Get recurring transaction | GET | `/api/recurring-transactions/{id}` | Returns `RecurringTransactionEditDto`. `estimatedAmount` is `decimal?` (null = amount varies). `404` if missing. |
| Update recurring transaction | PUT | `/api/recurring-transactions/{id}` | Body: `UpdateRecurringTransactionRequest`. `204` on success, `422` on validation, `404` on missing. |
| Archive recurring transaction | PATCH | `/api/recurring-transactions/{id}/archive` | Sets `IsActive = false`. `204` on success, `404` on missing. |
| Reactivate recurring transaction | PATCH | `/api/recurring-transactions/{id}/reactivate` | Sets `IsActive = true`. `204` on success, `404` on missing. |
| Confirm recurring transaction | PATCH | `/api/recurring-transactions/{id}/confirm` | Creates a `Transaction` from the template and advances `NextDueDate`. `204` on success. |
| Dismiss recurring transaction | POST | `/api/recurring-transactions/{id}/dismiss` | Skips the current due date and advances `NextDueDate`. Accepts an optional body `{ "nextDueDate": "yyyy-MM-dd" }` — required when `frequency` is `ManualDate` (no auto-advance formula exists). `204` on success. |
| Pending reconciliations | GET | `/api/reconciliation-review/pending` | Returns `StagedTransactionDto[] { id, importedAt, accountId, accountName, accountCurrencyCode, accountCurrencySymbol, rawDate, rawAmount, rawDescription?, matchedTransactionId?, matchedTransactionDescription?, matchedTransactionDate, matchedTransactionAmount }`. |
| Pending reconciliation count | GET | `/api/reconciliation-review/pending/count` | Returns an integer. Drives the SPA sidebar `Review` badge. |
| Confirm reconciliation | POST | `/api/reconciliation-review/{id}/confirm` | Marks the staged row as the canonical match for `MatchedTransactionId`. `204` on success, `404` on missing, `422` with `{ error: { code, message } }` on policy failure. |
| Confirm all reconciliations | POST | `/api/reconciliation-review/confirm-all` | Confirms every pending row in one call (atomic). `204` on success, `422` with `{ error: { code, message } }` on policy failure. Backed by `TryConfirmAllAsync`. |
| Dispute reconciliation | POST | `/api/reconciliation-review/{id}/dispute` | Detaches the staged row from its candidate match. `204` on success, `404` on missing, `422` on policy failure. |
| Pending transfer-review rows | GET | `/api/transfer-review/pending` | Returns `StagedTransferDto[] { id, importedAt, accountId, accountName, accountCurrencyCode, accountCurrencySymbol, rawDate, rawAmount, rawDescription?, candidateTransactionId?, candidateTransactionDescription?, candidateTransactionDate?, candidateTransactionAmount? }`. |
| Pending transfer-review count | GET | `/api/transfer-review/pending/count` | Returns an integer. Drives the SPA sidebar `Review` badge alongside the reconciliation count. |
| Link staged row to existing transfer | POST | `/api/transfer-review/{stagedId}/link-to-existing` | Body: `{ otherAccountId: Guid }`. Pairs the staged row with an existing same-currency counterpart row to form a Transfer. `204` on success, `404`/`422` on failure. |
| Create transfer from staged row | POST | `/api/transfer-review/{stagedId}/create-as-transfer` | Body: `{ otherAccountId: Guid }`. Materialises the staged row as a new Transfer using the picked counterparty account (must share currency). `204` on success, `404`/`422` on failure. |
| Dismiss staged row as plain transaction | POST | `/api/transfer-review/{stagedId}/dismiss-as-transaction` | Drops the transfer-detection signal and lets the row import as a regular Transaction. `204` on success, `404`/`422` on failure. |

#### `EstimatedAmount` semantics

`estimatedAmount` is `decimal?` on both list and edit DTOs. A `null` value means "amount varies" and should be displayed as "Varies" in the UI. A non-null value is a positive decimal string (following the standard [amounts convention](#amounts)) representing the expected amount per occurrence.

#### `409 DUPLICATE_BUDGET` response shape

Returned by `POST /api/category-budgets` and `PATCH /api/category-budgets/{id}/reactivate` when activating a budget would violate the unique `(CategoryId, CurrencyId)` active-budget constraint. The body extends the standard error envelope with `existingBudgetId` and `existingIsActive` so the SPA can offer an inline "reactivate the existing one" flow without an extra round-trip:

```json
{
  "error": {
    "code": "DUPLICATE_BUDGET",
    "message": "A budget for this category and currency already exists.",
    "existingBudgetId": "3f7a2b1c-4d5e-6789-abcd-ef0123456789",
    "existingIsActive": true
  }
}
```

### Phase 3 — Full Web API (`/api/v1/`)

| Resource | Endpoints | Notes |
|----------|-----------|-------|
| Accounts | CRUD + list | Includes `Notes` field (Phase 3) and `ExcludeFromSpendable` flag |
| Transactions | CRUD + list (paginated, filterable by account/category/date/type/cleared) | |
| Transfers | CRUD + list | |
| Movements | List (paginated, filterable by account/date/type/cleared) | Unified ledger — read-only aggregate of Transactions and Transfers; no separate CRUD |
| Categories | List (system + user), create, update, deactivate | No hard delete |
| Budgets | CRUD + list | |
| CategoryBudgets | CRUD + list | Includes `PeriodStartDay` override field |
| SavedReports | CRUD + list | Soft-deletable; restorable |
| SavedSearches | CRUD + list | Per-user, per-table; stores filter set + text search; scoped to table name |
| RecurringTransactions | CRUD + list | Templates only; confirming a reminder creates a Transaction |
| Reports | GET only (generated on demand) | 8 reports: net-worth, income-expense, expense-breakdown, transaction-history, budget-vs-actual, largest-expenses, monthly-cash-flow, net-worth-over-time. All accept `?format=csv` for download. See `budget-vs-actual` row semantics below. |
#### `budget-vs-actual` row semantics

`GET /api/reports/budget-vs-actual?from=&to=&currencyId=` returns `BudgetVsActualRow[]`:

```json
{
  "categoryName": "Housing / Rent",
  "currencyCode": "EUR",
  "currencySymbol": "€",
  "limitAmount": 600,
  "actualSpend": 540,
  "variance": 60
}
```

**`limitAmount` is the summed budget across the number of periods that fall in `[from, to]`**, not the per-period cap. The period count is derived from `Settings.PeriodStartDay` using the same `BudgetPeriod` math as the dashboard. A category budget of €200/month queried over a 3-month range returns `limitAmount = 600`. A single-period query returns `limitAmount` equal to the configured cap.

| Attachments | Upload (POST), download (GET), delete | Scoped to a Transaction |
| Settings | GET (read preferences), PATCH (update preferences) | Per-user in Phase 3; includes `PeriodStartDay`, notification preferences |
| Notifications | GET (list preferences), PATCH (update preferences) | Controls: weekly digest opt-in, new session alert opt-out, Safe to Spend alert |
| DataExport | POST (request export), GET (download by token) | Async: POST returns `202 Accepted` with a job ID; user notified by email when ready; download link is time-limited (24 h) and authenticated |
| AuditLog | GET (list, paginated) | User's own audit log entries only — no delete endpoint |
| SupportTickets | POST (submit), GET list, GET by ID | User-facing; admin management surface is separate |
| Sessions | List active sessions, revoke session | Security settings |
| BlockedIps | List, add, remove | Security settings |
| Auth | Register, login (`POST /auth/login`), logout, TOTP verify (`POST /auth/totp`), MFA setup, MFA enroll verify, backup codes generate, password change, password reset request, password reset confirm | Cookie-based; sets HttpOnly session cookie on successful login |
