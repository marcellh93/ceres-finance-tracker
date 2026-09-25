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
| Create transaction | POST | `/api/transactions` | Body: `CreateTransactionRequest { date, amount, accountId, categoryId, description?, isCleared? }`. `isCleared` defaults to `false` when omitted (matches the QuickAdd-modal contract); the full Create form sends the toggle's actual value. Returns `201 Created` with `{ id }`. Returns `422 Unprocessable Entity` with `ValidationProblemDetails` on invalid input. |
| Get transaction | GET | `/api/transactions/{id}` | Returns `TransactionEditDto { id, date, amount, accountId, categoryId, description, isCleared, attachments[] }`. `404` if missing. |
| Update transaction | PUT | `/api/transactions/{id}` | Body: `UpdateTransactionRequest`. `200` on success, `422` on validation, `404` on missing. |
| Delete transaction | DELETE | `/api/transactions/{id}` | Hard delete. `204` on success, `404` on missing. |
| Upload transaction attachment | POST | `/api/transactions/{id}/attachments` | Multipart upload. Returns `AttachmentDto`. |
| Delete transaction attachment | DELETE | `/api/transactions/attachments/{attachmentId}` | `204` on success, `404` on missing. |
| Create transfer | POST | `/api/transfers` | Body: `CreateTransferRequest { date, amount, sourceAccountId, destAccountId, description?, isCleared? }`. `isCleared` defaults to `false` when omitted. Returns `201 Created` with `{ id }`. Returns `422` if source == destination, cross-currency, or other validation failures. |
| Get transfer | GET | `/api/transfers/{id}` | Returns transfer edit DTO `{ id, date, amount, sourceAccountId, destAccountId, description, isCleared, attachments[] }`. `404` if missing. |
| Update transfer | PUT | `/api/transfers/{id}` | Body: `UpdateTransferRequest`. `200` on success, `422` on validation, `404` on missing. |
| Delete transfer | DELETE | `/api/transfers/{id}` | Hard delete. `204` on success, `404` on missing. |
| Upload transfer attachment | POST | `/api/transfers/{id}/attachments` | Multipart upload. Returns `AttachmentDto`. |
| Delete transfer attachment | DELETE | `/api/transfers/attachments/{attachmentId}` | `204` on success, `404` on missing. |
| Create liability payment | POST | `/api/liability-payments` | Body: `CreateLiabilityPaymentRequest { date, amount, assetAccountId, liabilityAccountId, description?, isCleared? }`. `isCleared` defaults to `false` when omitted. Returns `201 Created` with `{ id }`. Returns `422` on validation failures. |
| Get liability payment | GET | `/api/liability-payments/{id}` | Returns liability payment edit DTO `{ id, date, amount, assetAccountId, liabilityAccountId, description, isCleared }`. `404` if missing. No attachments. |
| Update liability payment | PUT | `/api/liability-payments/{id}` | Body: `UpdateLiabilityPaymentRequest`. `200` on success, `422` on validation, `404` on missing. |
| Delete liability payment | DELETE | `/api/liability-payments/{id}` | Hard delete. `204` on success, `404` on missing. |
| Movement discriminator | GET | `/api/movements/{id}` | Returns `{ id, movementType }` — used by SPA edit route to dispatch to the correct typed endpoint. |
| Bulk mark cleared | POST | `/api/movements/bulk-cleared` | Body: `BulkClearedRequest { from, to, accountId?, type? }`. Returns `{ cleared: int }` — count of rows transitioned. |
| Movements CSV export | GET | `/api/movements/export.csv` | CSV download. Same query params as `GET /api/movements` (`q`, `accountId`, `from`, `to`, `type`). |
| Active accounts | GET | `/api/accounts/active` | Returns `AccountOptionDto[] { id, name, currencyCode, currencySymbol, accountTypeName }`. Active accounts only. |
| Active categories | GET | `/api/categories/active` | Returns `CategoryOptionDto[] { id, name, categoryTypeName }`. Active, non-system categories only. |
| Import file headers | POST | `/api/import/headers` | **Beta-fenced (404 in beta/Production; live in Development/E2E/Testing — ADR-0078).** Upload a CSV or XLSX file; returns detected column headers and auto-matched field mappings (`HeaderDetectionResult`) |
| Import transactions | POST | `/api/import` | **Beta-fenced (ADR-0078).** Upload file + column mappings; runs the full import pipeline; returns `ImportResult` (rows imported, reconciled, flagged, failed) |
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
| Pending reconciliations | GET | `/api/reconciliation-review/pending` | **Beta-fenced (404 in beta/Production — ADR-0078).** Returns `StagedTransactionDto[] { id, importedAt, accountId, accountName, accountCurrencyCode, accountCurrencySymbol, rawDate, rawAmount, rawDescription?, matchedTransactionId?, matchedTransactionDescription?, matchedTransactionDate, matchedTransactionAmount }`. |
| Pending reconciliation count | GET | `/api/reconciliation-review/pending/count` | **Beta-fenced (ADR-0078).** Returns an integer. Drives the SPA sidebar `Review` badge. |
| Confirm reconciliation | POST | `/api/reconciliation-review/{id}/confirm` | **Beta-fenced (ADR-0078).** Marks the staged row as the canonical match for `MatchedTransactionId`. `204` on success, `404` on missing, `422` with `{ error: { code, message } }` on policy failure. |
| Confirm all reconciliations | POST | `/api/reconciliation-review/confirm-all` | **Beta-fenced (ADR-0078).** Confirms every pending row in one call (atomic). `204` on success, `422` with `{ error: { code, message } }` on policy failure. Backed by `TryConfirmAllAsync`. |
| Dispute reconciliation | POST | `/api/reconciliation-review/{id}/dispute` | **Beta-fenced (ADR-0078).** Detaches the staged row from its candidate match. `204` on success, `404` on missing, `422` on policy failure. |
| Pending transfer-review rows | GET | `/api/transfer-review/pending` | **Beta-fenced (404 in beta/Production — ADR-0078).** Returns `StagedTransferDto[] { id, importedAt, accountId, accountName, accountCurrencyCode, accountCurrencySymbol, rawDate, rawAmount, rawDescription?, candidateTransactionId?, candidateTransactionDescription?, candidateTransactionDate?, candidateTransactionAmount? }`. |
| Pending transfer-review count | GET | `/api/transfer-review/pending/count` | **Beta-fenced (ADR-0078).** Returns an integer. Drives the SPA sidebar `Review` badge alongside the reconciliation count. |
| Link staged row to existing transfer | POST | `/api/transfer-review/{stagedId}/link-to-existing` | **Beta-fenced (ADR-0078).** Body: `{ otherAccountId: Guid }`. Pairs the staged row with an existing same-currency counterpart row to form a Transfer. `204` on success, `404`/`422` on failure. |
| Create transfer from staged row | POST | `/api/transfer-review/{stagedId}/create-as-transfer` | **Beta-fenced (ADR-0078).** Body: `{ otherAccountId: Guid }`. Materialises the staged row as a new Transfer using the picked counterparty account (must share currency). `204` on success, `404`/`422` on failure. |
| Dismiss staged row as plain transaction | POST | `/api/transfer-review/{stagedId}/dismiss-as-transaction` | **Beta-fenced (ADR-0078).** Drops the transfer-detection signal and lets the row import as a regular Transaction. `204` on success, `404`/`422` on failure. |
| Request data export | POST | `/api/profile/export` | GDPR access/portability (Stage 13.8). `[RequireRecentAuth]`, rate-limited 1/24h. Optional body `{ "format": "zip" }` (default `zip`; other values → `422`). Dedupes an in-flight job. `202 Accepted` with `{ "data": { "jobId", "message" } }`; `429` (with `Retry-After`) on the 2nd request in 24h; `401 REAUTH_REQUIRED` without recent auth. A background worker builds the ZIP and emails a one-time link. |
| Download data export | GET | `/api/profile/export/download?token=<raw>` | `[Authorize]` (login required) **AND** a valid token — the dual gate, since the link lands in an inbox. Two-factor: HMAC `TokenLookup` locates the job, Argon2id `TokenHash` verifies it. `200` streams the ZIP (`application/zip`) and stamps single-use `ConsumedAt`; `404` for a foreign/unknown token or a hash-verify miss (no existence oracle); `410 Gone` for a consumed or expired (>24h) link. |

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

### Canonical error codes

The table below lists every `error.code` value the API can return, grouped by domain. Auth codes were added in Stage 6b.1–6b.3.

| Code | Status | Returned by | When |
|------|--------|-------------|------|
| `VALIDATION_ERROR` | 422 | All endpoints | Model-state or business-rule validation failure. `details[]` is populated for field errors; empty for semantic/business errors. |
| `RATE_LIMITED` | 429 | Rate-limited auth endpoints | Request exceeds the endpoint's rate-limit policy. Includes `Retry-After` header. |
| `UNAUTHENTICATED` | 401 | All authenticated endpoints | No valid session cookie. |
| `DUPLICATE_BUDGET` | 409 | `POST /api/category-budgets`, `PATCH /api/category-budgets/{id}/reactivate` | Activating would violate the unique `(CategoryId, CurrencyId)` active-budget constraint. |
| `MFA_ALREADY_ENROLLED` | 409 | `POST /api/auth/mfa/enroll` | `user.TwoFactorEnabled = true` — MFA is already active. Disable MFA (Stage 6c) before re-enrolling. Stage 6b.3. |
| `MFA_NOT_ENABLED` | 409 | `POST /api/auth/mfa/backup-codes/regenerate` | MFA is not enabled for this user; there are no backup codes to regenerate. Stage 6b.3. |
| `INVALID_MFA_CODE` | 401 | `POST /api/auth/mfa/backup-codes/regenerate`; MFA verification endpoints | The supplied TOTP or backup code is rejected (wrong, expired, replayed, or already used). Stage 6b.3. |
| `NO_ENROLLMENT_IN_PROGRESS` | 400 | `POST /api/auth/mfa/enroll/verify` | No authenticator key is staged for this user — `/enroll` must be called first. Stage 6b.3. |
| `INVALID_RESET_TOKEN` | 401 | `POST /api/auth/password-reset/confirm` | Reset token is unknown, malformed, expired, or already consumed. Stage 6c.1. |
| `INVALID_REAUTH` | 401 | `POST /api/auth/reauth` | Submitted password / TOTP / backup-code-shape was rejected. Stage 6c.2. |
| `REAUTH_REQUIRED` | 401 | Any `[RequireRecentAuth]` action | Authenticated but `LastReauthAt` claim is missing, malformed, future, or older than 5 minutes. Step up via `POST /api/auth/reauth`. Stage 6c.2. |
| `INVALID_EMAIL_CHANGE_TOKEN` | 401 | `POST /api/auth/email-change/confirm`, `POST /api/auth/email-change/revoke` | Token unknown, malformed, expired, already consumed, or superseded by a password reset. Stage 6.12. |
| `EMAIL_ALREADY_IN_USE` | 422 | `POST /api/auth/email-change/request`, `POST /api/auth/email-change/confirm` | The new email address is already registered to another account at request time, or got grabbed by another user between request and confirm. At confirm time the matched VerifyNew token is NOT consumed — caller can `/revoke` to clean up. Stage 6.12. |
| `EMAIL_UNCHANGED` | 422 | `POST /api/auth/email-change/request` | The submitted new email equals the current address-of-record (case-insensitive against `NormalizedEmail`). No token rows written. Stage 6.12. |
| `INVALID_LOCKOUT_UNLOCK_TOKEN` | 401 | `POST /api/auth/lockout-unlock` | Token unknown, malformed, expired, or already consumed. Stage 6.10. |
| `INVALID_VERIFICATION_TOKEN` | 401 | `POST /api/auth/email/verify` | Email-confirmation token unknown, malformed, expired, or already consumed. Stage 9.3. |
| `EMAIL_NOT_CONFIRMED` | 401 | `POST /api/auth/login` | `ApplicationUser.EmailConfirmed = false`. The user must verify via the link sent at registration before logging in. SPA shows a Resend Verification affordance. The mild enumeration leak is accepted in exchange for telling the legitimate user something actionable (sibling services GitHub/Stripe do the same). Stage 9.3. |

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
| AuditLog | GET (list, paginated) | User's own audit log entries only — no delete endpoint. **Writes ship in Stage 6.14; read endpoint ships in Stage 12.** |
| SupportTickets (conversation) | POST `tickets` (create), GET `tickets` (list), GET `tickets/{id}` (thread), POST `tickets/{id}/messages` (reply), POST `tickets/{id}/close`, POST `messages/{messageId}/attachments`, GET `/api/attachments/support/{id}` | User-facing; the operator surface is separate (below). **Stage 12.6 conversation model.** Create returns `{ id, firstMessageId }` — the client attaches buffered files to that first message. GET `tickets/{id}` returns the full ordered thread (each message with `authorRole`, `body`, `createdAt`, and that message's attachments); IDOR → 404. GET `tickets` list gains `messageCount` + `lastMessageAt`. POST `tickets/{id}/messages` is the user reply, driven by `SupportTicketStateMachine`: it returns the ticket to Open and reopens a Solved ticket, and is **refused server-side (422) on a Closed ticket** — not merely hidden in the UI. Attachments hang off a **message**, not the ticket. Close answers 404 for an unreachable ticket and 422 `TICKET_ALREADY_CLOSED` for your own already-closed one. Both create and reply are rate-limited (`EmailByUser` + IP): each sends mail. Enum fields (`status`, `authorRole`, `priority`) cross the wire as **integer ordinals** (no `JsonStringEnumConverter`). |
| Support operator | GET `/api/admin/support/tickets` (list), GET `tickets/{id}` (thread), POST `tickets/{id}/messages` (reply/status) — all under `/api/admin/support` | `[RequireAdmin]` (live per-request DB role check) + `[RequiresAdminContext]`. **Read surface (Stage 12.5.2):** GET `tickets` lists **every** user's tickets (offset-paginated, `AdminDbContext` + `IgnoreQueryFilters()` BYPASSRLS) with the owner email joined; query params `page` (1-based, default 1), `pageSize` (default 25, **max 100**), optional `status`, `priority`; response `{ items: AdminTicketListItemDto[], page, pageSize, total }` where `total` is the global count. GET `tickets/{id}` returns the owner + full ordered thread; 404-not-403 on a miss. Both are read-only, not rate-limited. **Write (Stage 12.6):** POST `tickets/{id}/messages` posts an operator reply and/or sets a status; body `{ body?, status }` (empty body = status-only). The Agent message is owner-stamped with the **ticket owner's** id from the loaded ticket. 404 on unknown, 422 on illegal transition, 403 for a non-admin; rate-limited (it emails the user). Enum fields cross the wire as integer ordinals. See `security-model.md` § Access Control. |
| Sessions | GET (list active), DELETE `{id}` (revoke), POST `{id}/anchor` (IP-anchor toggle) | Security settings; all `[RequireRecentAuth]`. **Anchor (Stage 12.5.1):** body `{ anchored: bool }`; `204` on success, `404` for a foreign/unknown/revoked session (IDOR-scoped to the caller). Sets `UserSession.IsIpAnchored`; an anchored session is signed out by `SessionRevocationValidator` on any request whose IP is not an exact match for its origin IP. `SessionDto` gains `isIpAnchored`. See `security-model.md` § Session IP Anchoring. |
| BlockedIps | List, add, remove | Security settings |
| Auth | See § *Auth endpoints* below | Cookie-based; sets `__Host-Session` HttpOnly cookie on successful login (or after second-step TOTP for MFA users). MFA is opt-in per [ADR-0069](decisions/ADR-0069-mfa-opt-in-for-personal-users.md). |

#### Auth endpoints

Stage 6a (shipped 2026-05-09) introduced register/login/logout. Stage 6b.1 (shipped 2026-05-09) added the TOTP MFA flow. Stage 6c.1 (shipped 2026-05-10) added the password-reset flow. Stage 6c.2 (shipped 2026-05-10) added the reauthentication-for-sensitive-operations flow + `[RequireRecentAuth]` gate, retroactively gating `/mfa/enroll`, `/mfa/enroll/verify`, and `/mfa/backup-codes/regenerate`. Endpoints below are mounted at the route base shown — Stage 6a uses `/api/auth` directly; the `/api/v1/` prefix kicks in alongside Stage 11's URL cleanup.

| Endpoint | Method | Auth | Body | Response |
|---|---|---|---|---|
| `/api/auth/register` | POST | Anonymous, CSRF-validated | `{ email, password }` | `204 No Content` always on the three "happy/anti-enum" branches: fresh-create (issues email-verification token + sends email), duplicate-unconfirmed (re-issues a fresh verification token for the existing user), duplicate-confirmed (no token, dummy Argon2id mirrors the issue cost for timing parity). `400` (RFC7807 ProblemDetails) on Identity password-policy failure (HIBP breach screening, length policy) — the controller calls `ValidationProblem(ModelState)` directly, which bypasses the 422 factory; model-binding / DataAnnotations failures (missing/malformed email) still return the `422 VALIDATION_ERROR` envelope via the factory. Does not auto-sign-in — `RequireConfirmedEmail = true` blocks `/login` until the user verifies via the link. Stage 6a + Stage 9.3 (three-branch anti-enum). |
| `/api/auth/login` | POST | Anonymous, CSRF-validated | `{ email, password, rememberMe }` | `204` + `__Host-Session` cookie if MFA off. `200 { requiresTotp: true }` + scoped `Identity.TwoFactorUserId` cookie if MFA on (no session cookie issued — second-step required). `401 INVALID_CREDENTIALS` on bad credentials (constant-time — also returned for unknown email). `401 ACCOUNT_LOCKED_OUT` on locked-out user. `401 EMAIL_NOT_CONFIRMED` on `signIn.IsNotAllowed` (correct password but `EmailConfirmed = false`) — Stage 9.3 wires this; the SPA shows a Resend Verification button. Always runs Argon2id (dummy hash on user-not-found) for constant-time enumeration prevention. |
| `/api/auth/login/totp` | POST | Anonymous (relies on `Identity.TwoFactorUserId` scoped cookie set by `/login`), CSRF-validated | `{ code }` — 6-digit TOTP **or** 16-char Crockford backup code (with or without `-` separators) | `204` + `__Host-Session` cookie on success. Replayed code → `401 { error: "replay" }`. Wrong code → `401`. Missing/expired scoped cookie → `401`. |
| `/api/auth/logout` | POST | Authenticated, CSRF-validated | (empty) | `204`. Stamps `UserSession.RevokedAt`, revokes paired `__Host-Persist` row if present, signs out via Identity, rotates CSRF cookie. |
| `/api/auth/csrf` | GET | Anonymous (safe method, no antiforgery validation needed) | — | `204` + fresh `__Host-XSRF` cookie. SPA + integration-test helper for binding the CSRF token to the current authentication context (anonymous before login; authenticated after). |
| `/api/auth/mfa/enroll` | POST | Authenticated, CSRF-validated | (empty) | `200 { otpAuthUri, manualEntryKey }`. `Cache-Control: no-store, no-cache`. Generates a fresh authenticator key (overwriting any prior unverified candidate). **`409 MFA_ALREADY_ENROLLED`** if `user.TwoFactorEnabled = true` — no silent re-enrollment (Stage 6b.3). |
| `/api/auth/mfa/enroll/verify` | POST | Authenticated, CSRF-validated | `{ code }` — 6-digit TOTP | `200 { backupCodes: [...10 strings] }` on success — flips `TwoFactorEnabled = true` and returns the freshly-generated batch of 10 backup codes once. `400 { error: { code: "NO_ENROLLMENT_IN_PROGRESS", message: "..." } }` when no key is staged. `401 { error: { code: "INVALID_MFA_CODE", message: "..." } }` on wrong code. `Cache-Control: no-store, no-cache`. |
| `/api/auth/mfa/backup-codes/regenerate` | POST | Authenticated, `[RequireRecentAuth]` (5-min freshness), CSRF-validated | (empty body) | `200 { backupCodes: [...10 strings] }` — invalidates all existing codes and returns a new batch once. `401 REAUTH_REQUIRED` if `LastReauthAt` claim missing/stale (Stage 6c.2 replaces in-body TOTP). `409 MFA_NOT_ENABLED` if user has no MFA. `Cache-Control: no-store, no-cache`. |
| `/api/auth/reauth` | POST | Authenticated, CSRF-validated, per-user rate-limited (10/min) | `{ password? }` (no MFA) or `{ totpCode? }` (MFA) — server enforces required field based on `user.TwoFactorEnabled` | `204` on success — refreshes `__Host-Session` cookie with updated `LastReauthAt` claim and preserves existing `sid` claim. `401 INVALID_REAUTH` on bad credentials/wrong TOTP/replayed TOTP/backup-code-shape. `401 ACCOUNT_LOCKED_OUT` if locked. `422 VALIDATION_ERROR` with `details: [{field, message}]` on missing required field. Wrong password counts toward lockout via `AccessFailedAsync`; wrong TOTP does not (uses `VerifyTwoFactorTokenAsync` directly). Backup codes are NOT accepted at reauth. Stage 6c.2. |
| `/api/auth/password-reset/request` | POST | Anonymous, CSRF-validated | `{ email }` | `204 No Content` always (constant-time, no enumeration leak — known and unknown emails return identical response and wall-clock time). Issues a 256-bit base64url token, Argon2id-hashed in `PasswordResetTokens`, 15-min expiry, single-use; supersedes any prior unconsumed tokens for the user. Email sent via `IEmailService` to a known address only (unknown branch is silent). Rate limits: per-IP `auth-login-by-ip` (10/min/IP) AND service-side per-email window (5/hour/email) → 429 `RATE_LIMITED` with `Retry-After` if exceeded. Stage 6c.1. |
| `/api/auth/password-reset/confirm` | POST | Anonymous, CSRF-validated | `{ token, newPassword, totpCode? }` | `204 No Content` on success. `200 { requiresTotp: true }` if user has MFA enabled and `totpCode` is omitted (token NOT consumed). `401 INVALID_RESET_TOKEN` if token unknown/malformed/expired/consumed. `401 INVALID_MFA_CODE` if MFA-enabled user submits a wrong/replayed/non-TOTP-shape code (backup codes are NOT accepted at reset per ADR-0069). `422 VALIDATION_ERROR` with `details: [{ field: "newPassword", ... }]` on policy violation (HIBP breach, length, etc.) — token NOT consumed, user can retry. On success: password written, all `UserSession` rows revoked, `SecurityStamp` regenerated, lockout cleared, `EmailConfirmed` promoted, MFA-pending cookie cleared, password-changed notification email queued, **any pending email-change for the same user is cancelled** and a "Pending email change cancelled" notification is sent to the old address. Per-IP rate limit `auth-login-by-ip`. Stage 6c.1 + Stage 6.12 cross-feature. |
| `/api/auth/email-change/request` | POST | Authenticated, CSRF-validated, `[RequireRecentAuth]` (5-min freshness) | `{ newEmail }` | `202 Accepted { data: { message } }` on success — issues a verify-token (256-bit, 30-min, base64url) and a revoke-token (256-bit, 7-day) atomically; sends one email to the new address (verify link) and one to the old address (revoke link). Supersedes any prior unconsumed pair for the user. `422 EMAIL_UNCHANGED` if `newEmail` matches current address (case-insensitive). `422 EMAIL_ALREADY_IN_USE` if registered to another user. `401 REAUTH_REQUIRED` if `LastReauthAt` claim missing/stale. `429 RATE_LIMITED` with `Retry-After` if 6th request against the same `newEmail` within 1h (service-side `MemoryCache` window, 5/hour). Old address remains address-of-record until verified. Stage 6.12. |
| `/api/auth/email-change/confirm` | POST | Anonymous, CSRF-validated, per-IP rate-limited (`auth-login-by-ip`) | `{ token }` | `204 No Content` on success — updates `Email` + `NormalizedEmail` + `UserName` + `NormalizedUserName` to the new address, sets `EmailConfirmed = true`, consumes both VerifyNew + sibling RevokeOld rows, revokes all `UserSession` rows, regenerates `SecurityStamp`, sends "Your Project Ceres email address was changed" notification to new address. **Does NOT clear lockout** (explicit divergence from password-reset — email proof ≠ password recovery). `401 INVALID_EMAIL_CHANGE_TOKEN` on unknown/malformed/expired/consumed token. `422 EMAIL_ALREADY_IN_USE` if another user grabbed the address between request and confirm — token NOT consumed; caller can `/revoke`. Stage 6.12. |
| `/api/auth/email-change/revoke` | POST | Anonymous, CSRF-validated, per-IP rate-limited (`auth-login-by-ip`) | `{ token }` | `204 No Content` on success — consumes both RevokeOld + sibling VerifyNew rows; sends "Email change cancelled" notification to old address only. **`Email` is NOT updated** (cancel path); sessions NOT revoked, `SecurityStamp` NOT regenerated. `401 INVALID_EMAIL_CHANGE_TOKEN` on unknown/malformed/expired/consumed token. Stage 6.12. |
| `/api/auth/lockout-unlock` | POST | Anonymous, CSRF-validated, per-IP rate-limited (`auth-login-by-ip`) | `{ token }` | `204 No Content` on success — clears `AccessFailedCount` + `LockoutEnd`, consumes the token, writes `AuditLogAction.LockoutSelfServiceUnlock`. **Does NOT revoke `UserSession` rows, does NOT regenerate `SecurityStamp`, does NOT sign anyone in** (undo-only). `401 INVALID_LOCKOUT_UNLOCK_TOKEN` on unknown/malformed/expired/consumed token. `422 VALIDATION_ERROR` on empty token. The token is issued by `AuthController.Login` on the lockout transition (not user-initiated); the email-DoS guard ensures at most one unlock email per lockout window. 15-min token expiry. Stage 6.10. SPA route: `/account/unlock#token=...` (Stage 9.5). |
| `/api/auth/email/verify` | POST | Anonymous, CSRF-validated, per-IP rate-limited (`auth-login-by-ip`) | `{ token }` | `204 No Content` on success — sets `ApplicationUser.EmailConfirmed = true`, consumes the token, writes `AuditLogAction.EmailVerified`. `401 INVALID_VERIFICATION_TOKEN` on unknown/malformed/expired/consumed token. Does NOT log the user in (user navigates to `/login` after). 30-min token expiry. Stage 9.3. |
| `/api/auth/email/verify/resend` | POST | Anonymous, CSRF-validated, per-IP + per-email rate-limited | `{ email }` | `204 No Content` always — anti-enumerating. Unknown email: two dummy Argon2id hashes, no token. Known + confirmed: two dummy hashes, no token. Known + unconfirmed: supersedes prior unconsumed tokens + issues a new one + sends the verification email. `429 RATE_LIMITED` with `Retry-After` if 6th request against the same `email` within 1 hour (service-side `MemoryCache` window, 5/hour). Stage 9.3. |

**Cookies in play:**
- `__Host-Session` — short-lived Identity session cookie (sliding 30-min). HttpOnly, Secure (Production; SameAsRequest in dev/test), SameSite=Lax, Path=/.
- `__Host-Persist` — 30-day rotated remember-me token. HttpOnly, Secure, SameSite=Lax, Path=/. Issued only when `rememberMe=true`. Hashed in `UserSession.PersistentTokenHash`.
- `__Host-XSRF` — antiforgery double-submit cookie. JS-readable (not HttpOnly), Secure, SameSite=Lax, Path=/.
- `Identity.TwoFactorUserId` — framework-managed scoped auth cookie, set by `PasswordSignInAsync` when `RequiresTwoFactor=true`. Not a session — only valid for `/api/auth/login/totp`. Cleared on successful TOTP verify. (Other authenticated endpoints reject this cookie.)
- `Mfa.RememberMe` — transient cookie (`Path=/api/auth/login`, 10-min TTL) carrying the `rememberMe` preference between the credentials step and the TOTP step. HttpOnly, Secure (per-environment), SameSite=Lax. Cleared after the second-step succeeds.

**Out of scope for 6a/6b.1 (deferred to 6b.2 + 6c):**
- Lockout self-service unlock signed-token endpoint (6b.2).
- Rate limiting on `/login`, `/login/totp`, `/register`, `/mfa/*` (6b.2).
- Password reset request + confirm endpoints (6c).
- Email verification + email-change endpoints (6c).
- MFA disable endpoint (6c).
- Reauth gate on `/mfa/enroll/verify` re-enrollment + `/mfa/backup-codes/regenerate` (6c).
