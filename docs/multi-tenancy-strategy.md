# Multi-Tenancy Strategy

> **Diataxis type:** Explanation + How-To — explains the problem and documents the required migration steps for Phase 3.

## The Problem

Phase 1 is designed as a single-user, local application. Every query that reads data from the database implicitly returns *all* data, because there is only one user. No `WHERE UserId = ?` clause exists anywhere — it is not needed.

When Phase 3 introduces authentication and hosting, this assumption inverts: there are now multiple users, and every piece of data belongs to exactly one of them. A query that was safe in Phase 1 becomes an IDOR vulnerability in Phase 3 — `WHERE Id = ?` without a `UserId` filter allows User A to read or modify User B's records by guessing or enumerating IDs.

This is a **system-wide change** — it affects every service, every controller action that loads a resource by ID, and every EF Core query. It is not a small refactor.

This document describes:
1. Which entities need a `UserId` FK
2. What changes in the service layer
3. How the Settings table migrates
4. What integration tests must cover before Phase 3 launches

---

## Entities That Need UserId

Every entity that represents a user's own data must have a direct `UserId` FK. Relying on joins (e.g. Transaction → Account → UserId) is insufficient for access control — it adds complexity to every access check and is error-prone to maintain.

### User-owned entities — add `UserId uuid NOT NULL FK → AspNetUsers.Id`

| Entity | Notes |
|--------|-------|
| `Account` | Root entity; most other entities are scoped through Account |
| `Category` | User-defined categories only. System categories (`IsSystem = true`) are shared across all users and must NOT have a UserId — they are owned by the system, not any individual user |
| `Transaction` | Direct UserId is required for IDOR prevention — joining through Account on every access check is fragile |
| `Transfer` | Direct UserId required for the same reason |
| `Budget` | User's goal budgets |
| `CategoryBudget` | User's category spending limits |
| `SavedReport` | User's saved report configurations |
| `RecurringTransaction` | User's recurring templates |

### Entities that do NOT get UserId

| Entity | Reason |
|--------|--------|
| `Currency`, `AccountType`, `CategoryType`, `ReportType` | System lookup tables — not user data |
| `Category` (IsSystem = true rows) | Seeded by the system, shared across all users |
| `TransactionAttachment` | Scoped through its parent Transaction (which has UserId) |
| `UserSession`, `UserBlockedIp` | Already scoped to a user via their own FK structure |

---

## Service Layer Changes

Every service method that loads data by ID must be updated to include the authenticated user's ID as a filter.

### Pattern to apply everywhere

```csharp
// BEFORE (Phase 1 — single user, safe)
var transaction = await _db.Transactions
    .FirstOrDefaultAsync(t => t.Id == id);

// AFTER (Phase 3 — multi-user, required)
var transaction = await _db.Transactions
    .FirstOrDefaultAsync(t => t.Id == id && t.UserId == currentUserId);
```

If the record is not found (either because it does not exist or because it belongs to a different user), the result is `null`. The controller must return `404` — **not** `403`. Returning `403` confirms the resource exists, which leaks information. See `security-model.md` for the full IDOR rule.

### Getting the current user ID in services

Inject `IHttpContextAccessor` and resolve the user ID from the authenticated principal:

```csharp
var currentUserId = Guid.Parse(
    _httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!
);
```

Alternatively, pass `currentUserId` as a parameter to service methods from the controller. The controller retrieves it from `User.FindFirstValue(ClaimTypes.NameIdentifier)`. Either pattern is acceptable; consistency matters more than the specific approach.

### Services to audit for Phase 3

Every method in these services that performs a query by ID must be updated:

- `AccountService`
- `TransactionService`
- `CategoryService` (user-created categories only — system categories excluded)
- `TransferService`
- `BudgetService`
- `CategoryBudgetService`
- `SavedReportService`
- `RecurringTransactionService`
- `ReportService` (all report generators that accept account or category IDs)

List-based queries (e.g. "get all accounts") must also be filtered: `WHERE UserId = currentUser`. An unfiltered list query in a multi-user system returns all users' data.

---

## Settings Table Migration

The Phase 1 Settings table has exactly one row containing app-wide preferences (default currency, number format, date format). In Phase 3, this becomes per-user — each user has their own preferences row.

### Migration steps

1. Add `UserId uuid NOT NULL FK → AspNetUsers.Id` to the Settings table
2. Add a `UNIQUE` constraint on `UserId` (one row per user)
3. For the existing single row: decide what UserId it receives. Options:
   - Assign it to the first registered user (the developer/admin account)
   - Delete it and let each new user get a default row created on first login
   - Option 2 is cleaner — it avoids orphaned rows and ties defaults to the registration flow
4. The application must create a default Settings row for each new user on registration, seeded with system-defined defaults (e.g. EUR, European number format, DD/MM/YYYY date format)
5. Update all settings queries to scope by `UserId`

This decision is tracked as an open question in `planning.md`.

---

## EF Core Global Query Filters (Optional Optimization)

EF Core supports [Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters) — a `WHERE` clause applied automatically to every query for an entity type. This can reduce the risk of forgetting a `UserId` filter in a specific method.

```csharp
// In DbContext.OnModelCreating:
modelBuilder.Entity<Transaction>()
    .HasQueryFilter(t => t.UserId == _currentUserId);
```

**Trade-offs:**
- Automatically applied — harder to accidentally omit
- Harder to reason about — the filter is invisible at the call site
- Must be disabled explicitly for admin queries (`IgnoreQueryFilters()`) — admin actions that need cross-user access become more complex
- Requires the DbContext to know the current user ID (inject via constructor or property)

Whether to use global query filters is a Phase 3 implementation decision. If used, they must be set up before any multi-user service code is written — retrofitting is harder than applying from the start.

---

## Required Integration Tests Before Phase 3 Launch

These tests must exist and pass before the app opens to any user other than the developer. They are the primary guard against data leakage.

| Test | Description |
|------|-------------|
| User A cannot read User B's accounts | `GET /Accounts/{id}` authenticated as User A, where id belongs to User B → must return 404 |
| User A cannot read User B's transactions | Same pattern for Transactions |
| User A cannot read User B's transfers | Same pattern for Transfers |
| User A cannot read User B's budgets | Same pattern for Budgets |
| User A cannot read User B's saved reports | Same pattern for SavedReports |
| User A cannot edit User B's records | `POST /Transactions/Edit/{id}` authenticated as User A, where id belongs to User B → must return 404 |
| User A cannot delete User B's records | Delete actions with cross-user ID → must return 404 |
| List queries return only the authenticated user's data | `GET /Transactions` authenticated as User A must never include User B's transactions |
| Report queries are user-scoped | No report (income, expenses, net worth) must include data from other users |

These tests belong in the integration test suite that runs against a real PostgreSQL database (not mocked). See `planning.md → Testing Strategy` for the test database setup.
