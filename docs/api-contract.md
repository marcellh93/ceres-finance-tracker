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
| `204 No Content` | Successful DELETE — no body |
| `400 Bad Request` | Malformed request (invalid JSON, missing required fields at the HTTP level) |
| `401 Unauthorized` | Request is not authenticated |
| `404 Not Found` | Resource does not exist **or belongs to another user** — never use 403 for user-owned resources |
| `422 Unprocessable Entity` | Request is well-formed but fails business validation (negative amount, wrong currency, etc.) |
| `429 Too Many Requests` | Rate limit exceeded |
| `500 Internal Server Error` | Unexpected server error — return a generic message, never a stack trace |

**Do not return `403 Forbidden` for user-owned resources.** Returning 403 confirms the resource exists, which is information an attacker can exploit to enumerate valid IDs. See `security-model.md → IDOR Prevention`.

---

## Authentication

Phase 3 API authentication uses **cookie-based auth** (the same mechanism as the MVC app), not JWT bearer tokens. Reasons:
- The React SPA is served from the same domain as the API — a same-domain cookie setup is simpler and avoids storing tokens in localStorage (XSS risk)
- HttpOnly cookies are inaccessible to JavaScript — safer than localStorage-stored JWTs
- ASP.NET Core Data Protection handles cookie encryption and validation

If a future mobile app (React Native) requires the API, a separate bearer token flow (JWT or opaque token) will need to be designed at that point. This document will be updated.

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
| Dashboard summary | GET | `/api/dashboard/summary` | Net worth, income/expense totals, savings rate, pending reminders count — for React dashboard components |
| Account balances | GET | `/api/dashboard/accounts` | All account balances, grouped by currency — for dashboard balance list |
| Category budget progress | GET | `/api/dashboard/category-budgets` | Spent vs. limit per expense category for the current month |
| Goal budget progress | GET | `/api/dashboard/goal-budgets` | Spent vs. target per goal budget |
| Movements cleared toggle | PATCH | `/api/movements/{id}/cleared` | Toggle `IsCleared` on a Transaction or Transfer — body: `{ "type": "transaction"\|"transfer", "cleared": bool }` |
| Import file headers | POST | `/api/import/headers` | Upload a CSV or XLSX file; returns detected column headers and auto-matched field mappings (`HeaderDetectionResult`) |
| Import transactions | POST | `/api/import` | Upload file + column mappings; runs the full import pipeline; returns `ImportResult` (rows imported, reconciled, flagged, failed) |

### Phase 3 — Full Web API (`/api/v1/`)

| Resource | Endpoints | Notes |
|----------|-----------|-------|
| Accounts | CRUD + list | |
| Transactions | CRUD + list (paginated, filterable by account/category/date) | |
| Transfers | CRUD + list | |
| Categories | List (system + user), create, update, deactivate | No hard delete |
| Budgets | CRUD + list | |
| CategoryBudgets | CRUD + list | |
| SavedReports | CRUD + list | Soft-deletable; restorable |
| RecurringTransactions | CRUD + list | Templates only; confirming a reminder creates a Transaction |
| Reports | GET only (generated on demand) | Net Worth, Income/Expense, Expense Breakdown, Transaction History |
| Attachments | Upload (POST), download (GET), delete | Scoped to a Transaction |
| Settings | GET (read preferences), PATCH (update preferences) | Per-user in Phase 3 |
| Sessions | List active sessions, revoke session | Security settings |
| BlockedIps | List, add, remove | Security settings |
| Auth | Register, login, logout, MFA setup, password change, password reset | ASP.NET Core Identity endpoints or custom |
