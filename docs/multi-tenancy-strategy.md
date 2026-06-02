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
| `EmailDeliveryEvent`, `FailedLoginAttempt` | Cross-user by design (pre-auth / address-keyed); nullable or absent userId (ADR-0067) |

> **Superseded by Stage 7.5 (RLS) + Stage 9.5b (model-derived set).** This table originally
> listed `TransactionAttachment`/`TransferAttachment` and the auth-internal tables
> (`UserSession`, `UserBlockedIp`, `UserMfaBackupCode`, `TotpReplayEntry`, …) as "no direct
> UserId". That is no longer true: Stage 7.5's RLS migration (Phase A) added a denormalized
> `UserId` column to the attachment tables, and the auth-internal tables implement `IUserOwned`
> with their own `UserId` + a `user_isolation` RLS policy. The authoritative user-owned set is
> now **derived from the EF model** by `UserOwnedModel.RlsTables(model)` (25 tables) — not a
> hand-list — and is boot-verified by `RlsParityStartupCheck` (Stage 9.5b). See § *Row-Level
> Security* in `security-model.md`.

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

- **`IUserScope.EnterAs(userId)`** returns an `IDisposable`; the user id lives in `AsyncLocal<Guid?>` and propagates across `await` boundaries within a single logical flow. Stack semantics: nested `EnterAs` calls restore the previous value on dispose, not `null`. **As of Stage 7.5 Commit 6 (2026-05-14), `IUserScope.EnterAs` must only be called from inside `BackgroundJobScope.RunAsync`** — an architecture test in `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs § EnterAs_only_called_inside_BackgroundJobScope` pins this and fails the build if any other production-code file calls it.
- **`IBackgroundJobScope.RunAsync(userId, jobName, work)`** *(Stage 7.5)* is the single doorway for background-job entry into a user-scoped context. Wraps `IUserScope.EnterAs` with a `Guid.Empty` doorway refusal: if no user is declared, the wrapper logs an error and throws `InvalidOperationException` before invoking `work`. Use this from every new background-job entry point.
- **`IUserJobRunner.ForEachUserAsync(filter, work)`** is the convenience layer for per-user iteration jobs (weekly digest, recurring reminders); it enters/exits the scope per user via `IBackgroundJobScope.RunAsync` and isolates per-user exceptions. `OperationCanceledException` matching the runner's own `CancellationToken` re-throws to exit the batch; all other per-user exceptions are caught and logged. Backed by `AdminDbContext` (Postgres role `ceres_admin` with `BYPASSRLS`, Stage 7.5) so user enumeration works once RLS is on; `IgnoreQueryFilters()` remains for defence-in-depth on the EF filter side.
- **`ICurrentUserAccessor`** resolves in fixed precedence: HTTP context (`ClaimTypes.NameIdentifier`) → background scope (`IUserScope.Current`) → return **`Guid.Empty`** as the safe default. *(Amendment to ADR-0067, Stage 7 Task 9, 2026-05-12: the original ADR said throw `InvalidOperationException` and forbade `Guid.Empty` fallback. EF Core eagerly evaluates global query filter expressions at model creation time, before any HTTP context or `IUserScope` is established. A throw at that moment crashes the app on startup. The `Guid.Empty` fallback means the filter then produces a `WHERE UserId = '00000000-…'` clause that matches no rows — the safe failure mode. The "no leakage" invariant is preserved by the `IgnoreQueryFilters()` allow-list architecture test, which forces every legitimate cross-tenant reader to opt in explicitly.)* **Superseded by [ADR-0073](decisions/ADR-0073-user-context-as-discriminated-union.md) (Stage 7.6.7, shipped 2026-05-14):** the contract is now `ICurrentUserAccessor.Context: UserContext` (a discriminated union of `Resolved`, `PreAuth(callSite)`, `Background(reason)`, `Uninitialized`). `UserId: Guid` remains as a derived convenience accessor returning `Resolved.UserId` or `Guid.Empty` for any other case — so the safe-default behaviour described above still holds for the EF global query filter, but it is now produced from a typed `Uninitialized` case rather than serving as the primary contract. Pre-auth call sites are tagged compile-time via `[PreAuthCallSite("...")]` on the action method itself instead of through a string-typed `IPreAuthCallSiteTagger` registry, and an architecture test enforces that every `[AllowAnonymous]` action on an `[ApiController]` carries the attribute (or appears in an explicit no-DB allow-list).
- **Genuinely cross-tenant code paths** (token verify before authentication, session revocation validators, MFA-pending lookups, retention sweeps, per-user job enumeration) use `IgnoreQueryFilters()` explicitly with an inline comment, and the file is enumerated in the allow-list maintained by `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs § IgnoreQueryFilters_only_appears_in_documented_exception_paths`. Adding the call anywhere else fails the architecture test.
- **`Program.cs` boot-time hooks that touch user-owned data are removed.** `ISettingsService.EnsureExistsAsync` is no longer called at startup — Settings rows are created during user registration per [ADR-0066](decisions/ADR-0066-sentinel-remap-to-first-registered-user.md). Regression test: `ProjectCeres.Tests/Integration/Startup/EmptyDbStartupTests.cs`.

`IUserScope` and `IUserJobRunner` shipped in Stage 7 Commit 1, before any Phase 3 background feature lands.

### Phase 3 service audit (closed 2026-05-12)

**Status: ✅ Done.** Every method in these services that queries a user-owned table chains `.Owned(user)` (the project's `ICurrentUserAccessor`-aware EF extension), and every entity write sets `UserId = user.UserId` either explicitly or via the `UserOwnershipInterceptor` default-Guid stamp. Defence-in-depth is supplied by the Stage 7 EF global query filters; the IDOR suite's negative-assertion test pins that the filter catches a leak independently of the service-layer chain.

Audited services:

- `AccountService`, `TransactionService`, `TransferService`, `LiabilityPaymentService`
- `CategoryService`, `CategoryBudgetService`, `BudgetService` (goal budgets)
- `RecurringTransactionService`, `SavedReportService` (via API surface — entity present in the EF model, no dedicated service class)
- `SettingsService` (per-user post-Stage-7), `DashboardService`
- `ReportService` + all 8 report generators (`NetWorth`, `IncomeExpense`, `ExpenseBreakdown`, `TransactionHistory`, `BudgetVsActual`, `LargestExpenses`, `MonthlyCashFlow`, `NetWorthOverTime`)
- `ImportService`, `CsvImportProfileService` (named `ImportProfileService` in code), `TransferReviewService`, `ImportStagedTransactionService`
- `FileAttachmentService` — scopes via parent Transaction/Transfer's `UserId` at the service layer. (Since Stage 7.5 the attachment tables also carry a denormalized `UserId` column + a `user_isolation` RLS policy, so the database enforces isolation independently.)

Deferred (not yet implemented in the project):

- `ReminderCountProvider` server-side counterpart — SPA-only today; the server-side count will be added when the reminders feature lands
- `ReviewCountProvider` server-side counterpart — likewise deferred

List-based queries (e.g. "get all accounts") also call `.Owned(user)` to filter by current user, and the Stage 7 global query filter is the second safety net.

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

**`IgnoreQueryFilters()` is allow-listed AND compile-time-enforced.** Two complementary layers:

- **Architecture-test allow-list** (Stage 7 Commit 1, still active): `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs § IgnoreQueryFilters_only_appears_in_documented_exception_paths`. The test scans every `.cs` file under `ProjectCeres/` and fails the build if `IgnoreQueryFilters(` appears in a non-comment line outside the allow-list. The allow-list contains: `Common/UserJobRunner.cs` (cross-tenant user enumeration), `Services/CategorySeedService.cs` (registration-time idempotency check), `Common/Authentication/PasswordResetService.cs`, `EmailChangeService.cs`, `LockoutUnlockService.cs`, `MfaBackupCodeService.cs`, `SessionRevocationValidator.cs`, `PersistentCookieRotationMiddleware.cs`, `TotpReplayGuard.cs`. The future `ProjectCeres/Admin/` namespace is pre-granted.
- **Roslyn analyzer CER002** (Stage 9.5c, shipped 2026-05-27): fires on `IgnoreQueryFilters()` invocations where the receiver's element type implements `IUserOwned` AND the containing class uses `AppDbContext` (NOT `AdminDbContext`). Requires `[RlsBypassJustified("CER-NNNN")]` on the enclosing method as documented intent. 15 existing methods retroactively carry the attribute with tickets `CER-1001`–`CER-1015` (mapping in `docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md` Appendix A). Side-analyzer **CER010** validates the ticket regex `^(CER\|TICKET\|ADR)-\d+$` to catch lazy "temp"/"TODO" justifications.

Per ADR-0077, the two layers are complementary: the architecture test enforces file-level allow-listing at runtime; CER002 enforces method-level justification at compile time with the bypass intent documented at the call site. A future addition that needs `IgnoreQueryFilters` outside the allow-list fails the architecture test; one inside the allow-list without `[RlsBypassJustified]` fails CER002.

**Sibling analyzer CER001** (Stage 9.5c) enforces the pre-auth scope discipline: classes marked `[PreAuthScope]` must use `BeginPreAuthUserScopeAsync(userId, ct)` from this section's "Service Layer Changes" above, not plain `BeginTransactionAsync`. The 7 services that call `BeginPreAuthUserScopeAsync` today (`AuditLogWriter`, `EmailConfirmationService`, `LockoutUnlockService`, `MfaBackupCodeService`, `PasswordResetService`, `TotpReplayGuard`, `AuthController`) all carry the marker.

The global filter expression resolves `_currentUser.UserId` via `ICurrentUserAccessor`, which reads from HTTP context first, then from the background scope set by `IUserScope.EnterAs` (see ADR-0067), then returns `Guid.Empty` (the safe default — see "Background processes and non-HTTP contexts" above for why this differs from ADR-0067's original "throw"). Raw SQL queries against user-owned tables are not subject to the filter — either avoid raw SQL on user-owned tables or always include an explicit `WHERE UserId` clause. PostgreSQL Row-Level Security in Phase 3 (Stage 7.5, ADR-0068 — **built 2026-05-14**) catches this category at the database level as the final defence-in-depth layer.

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

**Status: ✅ Done at Stage 7 Task 11 (2026-05-12).** The suite lives at `ProjectCeres.Tests/Integration/MultiTenancy/IdorIsolationTests.cs` — 15 cross-tenant assertions against `AuthTestWebApplicationFactory` with real PostgreSQL, real cookie auth, real CSRF, no mocks. Companion files: `GlobalQueryFilterTests.cs` (filter applied via `IUserScope.EnterAs`); the architecture tests in `Authentication/ArchitectureTests.cs` (filter coverage + `IgnoreQueryFilters()` allow-list); `IUserOwnedConformanceTests.cs` (the 8 auth-internal entities implement the interface). See `testing.md § Phase 3 Multi-Tenancy Coverage` for the full mapping.

| Test | Status |
|------|--------|
| User A cannot read User B's accounts (404, NOT 403) | ✅ `UserB_cannot_read_UserA_account_by_id`, `UserB_cannot_read_UserA_account_ledger` |
| User A cannot read User B's transactions | ✅ `UserB_cannot_read_UserA_transaction_by_id`, `UserB_cannot_read_UserA_movement_type_by_id` |
| User A cannot read User B's transfers | ⏳ Deferred (requires two-account-per-user setup with currency match; Transfer-specific IDOR test post-Batch-2) |
| User A cannot read User B's liability payments | ✅ Via Movement TPC root — global filter on `Movement` propagates to `LiabilityPayment` |
| User A cannot read User B's budgets | ✅ `UserB_cannot_read_UserA_goal_budget_by_id`, `UserB_cannot_read_UserA_budget_discriminator`, `UserB_cannot_read_UserA_category_budget_by_id` |
| User A cannot read User B's categories | ✅ Per-user category copies on registration with disjoint GUIDs (`RegistrationSeedsCategoriesTests.Two_users_get_independent_category_copies`); legacy shared-Category surface eliminated by Task 7 |
| User A cannot read User B's recurring transactions | ✅ `UserB_cannot_read_UserA_recurring_transaction_by_id` |
| User A cannot read User B's saved reports | ⏳ Endpoint not yet implemented; data-layer scoping pinned by `Every_user_owned_entity_carries_a_global_query_filter` |
| User A cannot read User B's transaction/transfer attachments | ⏳ Deferred; service-layer scopes via parent `Transaction.UserId == ...` so the parent 404 propagates |
| User A cannot edit User B's records | ✅ `UserB_cannot_patch_cleared_on_UserA_transaction` |
| User A cannot delete User B's records | ✅ `UserB_cannot_delete_UserA_transaction` |
| List queries return only the authenticated user's data | ✅ `UserB_account_list_excludes_UserA_accounts`, `UserB_movements_list_excludes_UserA_transactions`, `UserB_goal_budgets_list_excludes_UserA_budgets` |
| Report queries are user-scoped | ✅ Generalised via the negative-assertion test (see below); every report generator is `ReportService`-scoped per the service audit |
| **Negative-assertion safety net** | ✅ `Global_query_filter_alone_catches_leak_without_service_Owned` — issues a raw `db.Accounts.ToListAsync()` under User B's `IUserScope`, asserts User A's account is invisible. Proves the EF filter catches a leak when the service-layer `.Owned()` chain is bypassed. Per `feedback_test_edge_cases_as_ship_gate`. |

The suite runs against `project_ceres_test` (a real PostgreSQL DB), not mocks. See `testing.md § Integration Test Database` for setup.
