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
| `UserSession`, `UserBlockedIp`, `UserMfaBackupCode`, `TotpReplayEntry` | Already scoped to a user via their own FK structure |

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

### Background processes and non-HTTP contexts

**Resolved by [ADR-0067](decisions/ADR-0067-background-job-user-scope-with-iuserscope-and-runner.md).**

Once Phase 3 wires an `HttpContextAccessor`-backed `ICurrentUserAccessor`, any code path that runs outside an HTTP request resolves `HttpContext` as `null`. The combination of global query filters from [ADR-0065](decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md) plus the safe-default fallback below means an unauthenticated, no-scope read returns zero rows rather than leaking data.

The decided mechanism (as shipped in Stage 7 Commit 1, 2026-05-12):

- **`IUserScope.EnterAs(userId)`** returns an `IDisposable`; the user id lives in `AsyncLocal<Guid?>` and propagates across `await` boundaries within a single logical flow. Stack semantics: nested `EnterAs` calls restore the previous value on dispose, not `null`.
- **`IUserJobRunner.ForEachUserAsync(filter, work)`** is the convenience layer for per-user iteration jobs (weekly digest, recurring reminders); it enters/exits the scope per user and isolates per-user exceptions. `OperationCanceledException` matching the runner's own `CancellationToken` re-throws to exit the batch; all other per-user exceptions are caught and logged.
- **`ICurrentUserAccessor`** resolves in fixed precedence: HTTP context (`ClaimTypes.NameIdentifier`) → background scope (`IUserScope.Current`) → return **`Guid.Empty`** as the safe default. *(Amendment to ADR-0067, Stage 7 Task 9, 2026-05-12: the original ADR said throw `InvalidOperationException` and forbade `Guid.Empty` fallback. EF Core eagerly evaluates global query filter expressions at model creation time, before any HTTP context or `IUserScope` is established. A throw at that moment crashes the app on startup. The `Guid.Empty` fallback means the filter then produces a `WHERE UserId = '00000000-…'` clause that matches no rows — the safe failure mode. The "no leakage" invariant is preserved by the `IgnoreQueryFilters()` allow-list architecture test, which forces every legitimate cross-tenant reader to opt in explicitly.)*
- **Genuinely cross-tenant code paths** (token verify before authentication, session revocation validators, MFA-pending lookups, retention sweeps, per-user job enumeration) use `IgnoreQueryFilters()` explicitly with an inline comment, and the file is enumerated in the allow-list maintained by `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs § IgnoreQueryFilters_only_appears_in_documented_exception_paths`. Adding the call anywhere else fails the architecture test.
- **`Program.cs` boot-time hooks that touch user-owned data are removed.** `ISettingsService.EnsureExistsAsync` is no longer called at startup — Settings rows are created during user registration per [ADR-0066](decisions/ADR-0066-sentinel-remap-to-first-registered-user.md). Regression test: `ProjectCeres.Tests/Integration/Startup/EmptyDbStartupTests.cs`.

`IUserScope` and `IUserJobRunner` shipped in Stage 7 Commit 1, before any Phase 3 background feature lands.

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

**Resolved by [ADR-0066](decisions/ADR-0066-sentinel-remap-to-first-registered-user.md).**

The Phase 1 Settings table has exactly one row containing app-wide preferences (default currency, number format, date format). In Phase 3, this becomes per-user — each user has their own preferences row.

### Migration steps

1. Add `UserId uuid NOT NULL FK → AspNetUsers.Id` to the Settings table
2. Add a `UNIQUE` constraint on `UserId` (one row per user)
3. The existing single Settings row is **remapped to the first registered user** along with every other sentinel-tagged row, in a single transaction with pre-/post-checks (see ADR-0066). The developer's existing currency, period start day, and number/date format become the first user's preferences automatically.
4. The application creates a default Settings row for each subsequent user on registration, seeded with system-defined defaults (e.g. EUR, European number format, DD/MM/YYYY date format)
5. Update all settings queries to scope by `UserId`

---

## EF Core Global Query Filters

**Resolved by [ADR-0065](decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md): adopted, with explicit redundancy and admin-only bypass.**

EF Core [Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters) apply a `WHERE` clause automatically to every query for an entity type. Phase 3 sets a filter on every user-owned entity in `ApplicationDbContext.OnModelCreating`:

```csharp
modelBuilder.Entity<Transaction>()
    .HasQueryFilter(t => t.UserId == _currentUser.UserId);
```

Service code continues to write `.Where(t => t.UserId == _currentUser.UserId)` explicitly — the redundancy documents intent at the call site and is a second layer that survives if the global filter is ever misconfigured.

**`IgnoreQueryFilters()` is allow-listed.** The allow-list lives in `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs § IgnoreQueryFilters_only_appears_in_documented_exception_paths`. The test scans every `.cs` file under `ProjectCeres/` and fails the build if `IgnoreQueryFilters(` appears in a non-comment line outside the allow-list. As of Stage 7 Commit 1, the allow-list contains: `Common/UserJobRunner.cs` (cross-tenant user enumeration), `Services/CategorySeedService.cs` (registration-time idempotency check), `Common/Authentication/PasswordResetService.cs`, `EmailChangeService.cs`, `LockoutUnlockService.cs`, `MfaBackupCodeService.cs`, `SessionRevocationValidator.cs`, `PersistentCookieRotationMiddleware.cs`, `TotpReplayGuard.cs` (every pre-auth token/session/MFA lookup). The future `ProjectCeres/Admin/` namespace is pre-granted.

The global filter expression resolves `_currentUser.UserId` via `ICurrentUserAccessor`, which reads from HTTP context first, then from the background scope set by `IUserScope.EnterAs` (see ADR-0067), then returns `Guid.Empty` (the safe default — see "Background processes and non-HTTP contexts" above for why this differs from ADR-0067's original "throw"). Raw SQL queries against user-owned tables are not subject to the filter — either avoid raw SQL on user-owned tables or always include an explicit `WHERE UserId` clause. PostgreSQL Row-Level Security in Phase 3 (Stage 7.5, ADR-0068) catches this category at the database level as the final defence-in-depth layer.

**Filters as shipped in Stage 7 Commit 1 (22 concrete entities):**

- *Finance domain (11):* `Account`, `Budget`, `Category`, `CategoryBudget`, `ImportProfile`, `ImportStagedTransaction`, `ImportStagedTransfer`, `ImportTransferExclusion`, `RecurringTransaction`, `SavedReport`, `Settings`.
- *Movement TPC hierarchy (filter on the abstract root):* `Movement` — EF Core's TPC mapping propagates the filter to `Transaction`, `Transfer`, and `LiabilityPayment` automatically; filters cannot be declared on the concrete subtypes when TPC is in use.
- *Auth-internal (8, promoted to `IUserOwned` in Stage 7 Task 4):* `UserSession`, `UserBlockedIp`, `UserMfaBackupCode`, `TotpReplayEntry`, `PasswordResetToken`, `EmailChangeToken`, `LockoutUnlockToken`, `AuditLog`.

**Intentionally NOT filtered:**

- `TransactionAttachment`, `TransferAttachment` — no `UserId` column; service code scopes them via parent (`a.Transaction.UserId == ...`). EF emits two `PendingModelChangesWarning`s about the parent-attachment FK pair being a "required end with a filtered parent"; the warnings are documented inline in `AppDbContext.ConfigureGlobalQueryFilters` and accepted as the cost of the no-UserId-column design.
- `FailedLoginAttempt` — cross-tenant by design per [ADR-0067](decisions/ADR-0067-background-job-user-scope-with-iuserscope-and-runner.md); nullable `UserId`; retention sweep iterates all rows.
- `AccountType`, `CategoryType`, `Currency`, `ReportType` — system reference tables.
- `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` — Identity-managed; cross-tenant by definition.

The boundary is pinned by `ArchitectureTests § Every_user_owned_entity_carries_a_global_query_filter` and `FailedLoginAttempt_has_no_global_query_filter`.

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
