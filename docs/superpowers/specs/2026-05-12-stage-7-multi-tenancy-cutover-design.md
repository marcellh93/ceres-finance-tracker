# Stage 7 — Multi-tenancy cutover (Batch 3c) — Design

**Status**: Approved-pre-implementation.
**Phase**: 3.
**ADRs**: [ADR-0065](../../decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md) · [ADR-0066](../../decisions/ADR-0066-sentinel-remap-to-first-registered-user.md) · [ADR-0067](../../decisions/ADR-0067-background-job-user-scope-with-iuserscope-and-runner.md).
**Spec sequel**: [ADR-0068](../../decisions/ADR-0068-postgres-rls-as-phase-3-defence-in-depth.md) — Stage 7.5 (RLS) immediately follows.

## 1. Why this stage exists

Stage 6 shipped real authentication. Every authenticated request now resolves a real user id via `HttpContextCurrentUserAccessor` reading the cookie's `ClaimTypes.NameIdentifier` claim. But the application still operates as a single-user system at the data layer:

- Every existing row in dev/staging is stamped with the Phase-1 sentinel UUID `00000000-0000-0000-0000-000000000001`.
- The `SingleUserAccessor` class still lives in production code (only its DI registration changed at Stage 6a — `Program.cs:62`).
- The `AppDbContext` has **zero** global query filters. The only thing keeping a future bug from leaking User B's transactions to User A is the convention that every service calls `.Owned(user)` on every query.
- The data model still has one shared-row anomaly: `Category` with `UserId = null` represents "system category every user sees". `CategoryService.TryDeactivateAsync` (`Services/CategoryService.cs:80`) reads that shared row through `OwnedOrShared(user)`, then sets `category.IsActive = false`. If User A archives "Opening Balance", every other user loses it too.

Stage 7 closes these gaps. After Stage 7:

- An `IUserScope` + `IUserJobRunner` pair lets background jobs (digests, reminders, GDPR export, retention sweeps) enter a per-user scope.
- EF Core global query filters apply on every user-owned entity. An accidentally-omitted `.Where(t => t.UserId == …)` returns zero rows instead of leaking.
- An architecture test fails the build if `IgnoreQueryFilters()` appears outside an allow-listed namespace (`Admin/`, `IUserJobRunner` impl, retention sweeps).
- The shared-Category anomaly is gone: every user gets their own copy of every default category at registration, the `IOptionallyUserOwned` interface is deleted, and a new `Category.IsReserved` column keeps "Uncategorized Income/Expense" un-editable.
- A one-shot transactional EF migration remaps the sentinel-tagged dev/staging data to the first real user.
- `SingleUserAccessor` and the sentinel UUID disappear from production code.
- An IDOR integration test suite asserts cross-tenant 404s against a real PostgreSQL.

Stage 7 is the highest-risk change in Phase 3 — every existing service is in the blast radius. The good news, surfaced during pre-spec audit: Stage 6a's universal `.Owned(user)` discipline plus the `UserOwnershipInterceptor` defence-in-depth mean the parallel service audit found **zero** services that query user-owned tables without filtering. Stage 7's job is therefore less "rewrite the world" and more "lock down what's already in place, add the framework safety net, and migrate the data".

## 2. Scope

**In scope:** EF query filters; `IUserScope`/`IUserJobRunner` primitives; updating `HttpContextCurrentUserAccessor` to fall back to `IUserScope`; per-user `Category` model + registration-flow change to copy default categories on signup; `Category.IsReserved` column; sentinel-data remap migration; `SingleUserAccessor`+sentinel-constant removal; service-layer & boot-time-hook audit confirmation; IDOR integration test suite; architecture test for the `IgnoreQueryFilters()` boundary.

**Out of scope (deferred):**
- **PostgreSQL Row-Level Security** — that is Stage 7.5 (ADR-0068). Stage 7 is application-layer defence; Stage 7.5 is database-layer defence.
- **Identity masking / HMAC `UserRef`** — Stage 15.
- **Multi-currency conversion** — explicit non-goal across Phase 3.
- **Admin transaction-level data viewing or user impersonation** — Phase 4+.

## 3. Sub-stages (execution order is load-bearing)

### 3.1 — `IUserScope` (new file)

`ProjectCeres/Common/IUserScope.cs`:
```csharp
public interface IUserScope
{
    IDisposable EnterAs(Guid userId);
    Guid? Current { get; }
}
```

`ProjectCeres/Common/UserScope.cs`: `AsyncLocal<Guid?>`-backed implementation. Nested `EnterAs` calls must compose: `using (scope.EnterAs(a)) { using (scope.EnterAs(b)) { /* sees b */ } /* sees a again */ }`. `Dispose()` restores the previous value, not the literal default.

Register as singleton in `Program.cs` (the AsyncLocal does the per-flow isolation; the *holder* is process-wide).

### 3.2 — `IUserJobRunner` (new file)

`ProjectCeres/Common/IUserJobRunner.cs`:
```csharp
public interface IUserJobRunner
{
    Task ForEachUserAsync(
        Expression<Func<ApplicationUser, bool>> filter,
        Func<Guid, Task> work,
        CancellationToken ct = default);
}
```

Implementation enumerates `db.Users.IgnoreQueryFilters().Where(filter)`. `AspNetUsers` is the *one* user-owned table that never gets a query filter — it IS the user list. The `IgnoreQueryFilters()` call is documented inline with the reason; the architecture-test allow-list grants this single file the exemption.

Per-user loop:
```csharp
foreach (var userId in userIds)
{
    using (_scope.EnterAs(userId))
    {
        try { await work(userId); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Per-user job failed for {UserId}", userId);
            // Continue — one user's failure must not abort the batch.
        }
    }
}
```

Register as scoped in `Program.cs`.

### 3.3 — Extend `ICurrentUserAccessor` (modify existing file)

`ProjectCeres/Common/Authentication/HttpContextCurrentUserAccessor.cs` becomes the unified accessor (rename optional; not strictly required). Resolution order:

1. HTTP context claim (`ClaimTypes.NameIdentifier`).
2. `IUserScope.Current` (the new background-job fallback).
3. Throw `InvalidOperationException` with the message *"No user context available. HTTP requests resolve from cookie; background jobs must enter via IUserScope.EnterAs()."*

**Throw type change**: `UnauthorizedAccessException` → `InvalidOperationException`. The verify-against-codebase audit confirmed zero catch sites for the old type; only one throw site (`HttpContextCurrentUserAccessor.cs:20`). Mechanical flip.

Inject `IUserScope` into the accessor's constructor alongside `IHttpContextAccessor`.

### 3.4a — Promote auth entities to `IUserOwned`

Add `: IUserOwned` to the class declarations of: `UserSession`, `UserBlockedIp`, `UserMfaBackupCode`, `TotpReplayEntry`, `PasswordResetToken`, `EmailChangeToken`, `LockoutUnlockToken`, `AuditLog`. Each one already has a `public Guid UserId { get; set; }` from Stage 6 — this is an interface declaration only, no schema change. `FailedLoginAttempt` stays unpromoted (its `UserId` is nullable and cross-tenant retention sweep needs `IgnoreQueryFilters()`).

### 3.4 — EF global query filters

Add a `ConfigureGlobalQueryFilters(modelBuilder, ICurrentUserAccessor accessor)` method, called from `OnModelCreating` after `ConfigureUserOwnership`. Inject `ICurrentUserAccessor` into the `AppDbContext` constructor.

**22 `HasQueryFilter` calls in total**:

| Group | Entities |
|---|---|
| Finance domain (11) | `Account`, `Budget`, `Category` (post-3.7a), `CategoryBudget`, `ImportProfile`, `ImportStagedTransaction`, `ImportStagedTransfer`, `ImportTransferExclusion`, `RecurringTransaction`, `SavedReport`, `Settings` |
| Movement subtypes (3) | `Transaction`, `Transfer`, `LiabilityPayment` (TPC mapping — abstract `Movement` cannot carry a TPC-root filter, so each concrete subtype gets its own) |
| Auth-internal (8, post-3.4a) | `UserSession`, `UserBlockedIp`, `UserMfaBackupCode`, `TotpReplayEntry`, `PasswordResetToken`, `EmailChangeToken`, `LockoutUnlockToken`, `AuditLog` |

Each filter is the same shape: `HasQueryFilter(e => e.UserId == accessor.UserId)`.

**Not filtered (and why)**:
- `TransactionAttachment`, `TransferAttachment` — no `UserId` column; service code scopes via parent (`a.Transaction.UserId == user.UserId`).
- `FailedLoginAttempt` — nullable `UserId`; retention sweep is intentionally cross-tenant.
- `AccountType`, `CategoryType`, `Currency`, `ReportType` — system reference tables.
- `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` — Identity-managed; cross-tenant by definition.

### 3.5 — Architecture test: `IgnoreQueryFilters()` boundary

Extend `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`. Scan all `.cs` files under `ProjectCeres/` for the literal substring `IgnoreQueryFilters(`. Each hit must be in one of these documented exception locations:

- `ProjectCeres/Admin/**` (admin surface; cross-user by definition; namespace doesn't exist yet, but the allow-list anticipates Phase 4+).
- `ProjectCeres/Common/UserJobRunner.cs` (the per-user enumeration entry).
- Retention sweep services: `TokenRetentionSweepService` (purges expired tokens cross-tenant), `AuditLogRetentionSweepService`, `FailedLoginRetentionSweepService` (already exists). Each must carry an inline comment `// Cross-tenant by design: <reason>` next to the `IgnoreQueryFilters()` call.

Also replace the placeholder `FailedLoginAttempt_NotInGlobalQueryFilterList` test with the real assertion: enumerate `AppDbContext.Model.GetEntityTypes()`, confirm `FailedLoginAttempt` has no query filter, confirm every other expected entity (the 22 above) has one.

### 3.7a — `Category.IsReserved` column

Per the verify-against-codebase finding: `CategoryPolicies.IsReserved` (`Services/CategoryPolicies.cs:17–20`) currently hard-codes two GUIDs. Once every user has their own copies, the GUIDs no longer match. Fix:

1. Add `public bool IsReserved { get; set; }` to `Category` model.
2. EF migration `AddIsReservedToCategory` adds the column with `default false`.
3. Backfill in the same migration: `UPDATE categories SET IsReserved = true WHERE Id IN ('20000000-0000-0000-0000-000000000025', '20000000-0000-0000-0000-000000000026')` (the two existing "Uncategorized" rows).
4. Rewrite `CategoryPolicies.IsReserved(Guid)` → `IsReserved(Category)` reading `category.IsReserved`. Remove the hard-coded GUID constants.
5. Tests adjusted.

### 3.7b — Drop `IOptionallyUserOwned`; `Category` becomes `IUserOwned`

The shared-Category model is the only consumer of `IOptionallyUserOwned`. Once Stage 7 ships per-user category copies, the interface has no remaining purpose.

1. Change `Category : IOptionallyUserOwned` → `Category : IUserOwned`. `UserId` becomes non-nullable `Guid`.
2. Delete `IOptionallyUserOwned` interface from `ProjectCeres/Common/IUserOwned.cs`.
3. Delete `QueryableExtensions.OwnedOrShared` + `BuildOwnedOrSharedPredicate`.
4. Delete the `IOptionallyUserOwned` branch in `UserOwnershipInterceptor.Stamp`.
5. Every `.OwnedOrShared(user)` call site flips to `.Owned(user)`. Sites: `Controllers/Api/CategoriesApiController.cs` (3), `Services/CategoryService.cs` (5), `Services/RecurringTransactionService.cs` (2), `Services/CategoryBudgetService.cs` (1). 11 sites total — mechanical.

### 3.7c — Per-user copy on registration

Add a `CategorySeedService.CopyDefaultsForUserAsync(Guid userId)` (or fold into the existing registration handler). On every new-user registration:

1. Inside a transaction, copy every row from a canonical seed list into the user's own `Category` rows (new `Guid.NewGuid()` per row; `UserId` = the new user; `IsActive` = true; `IsReserved` flag preserved).
2. The canonical seed list is the existing seed in `AppDbContext.SeedCategories` extracted into a static `Categories.Defaults` list in `Common/`. The `HasData(...)` call in `AppDbContext` is removed — Stage 7 ships zero seed rows for `Category`.
3. The two "Uncategorized" rows are copied with `IsReserved = true`. The historic `IsSystem = true` "Opening Balance" row is copied with `IsSystem = true` (per-user, but still un-editable via the existing `CategoryPolicies.CanEdit` rule).

Per-user-copy on the **existing** dev/staging sentinel user happens in 3.6 (the data migration) — the migration also generates per-user category rows for the first real user after the remap.

### 3.6 — Sentinel-to-real-user data migration

EF migration `RemapSentinelToFirstUser`. Single transaction. Order:

1. Pre-check: `SELECT COUNT(*) FROM "AspNetUsers"` = 1. If 0 (fresh install), no-op cleanly. If >1, abort.
2. Pre-check: at least one row stamped with the sentinel in any user-owned table. If zero across all, no-op cleanly (already remapped or fresh install).
3. `UPDATE` each user-owned table, replacing sentinel with the real user's id. Explicit table list, no dynamic SQL:
   - `accounts`, `categories`, `transactions`, `transfers`, `liability_payments`, `category_budgets`, `budgets`, `recurring_transactions`, `transaction_attachments` (via FK chain — no UserId column), `saved_reports`, `csv_import_profiles`, `import_staged_transactions`, `import_staged_transfers`, `import_transfer_exclusions`, `settings` (UNIQUE-constrained — works because exactly one sentinel Settings row and exactly one real user).
4. Post-check: `SELECT COUNT(*) FROM <every table> WHERE user_id = '00000000-0000-0000-0000-000000000001'` = 0. Rollback otherwise.

Auth tables (`UserSession`, `AuditLog`, etc.) are **not** in the UPDATE list — they were created after Stage 6 with real user ids and never held sentinel data.

**Pre-deployment manual gate**: doc-only entry in the deployment runbook — operator takes a DB snapshot before deploying the migration. Not enforceable in code; lives in `docs/operations/runbook.md` (to be created at Stage 16).

### 3.7 — Delete `SingleUserAccessor` and the sentinel constant

Sequenced after 3.6 succeeds in every environment. In one PR:

1. Delete the `SingleUserAccessor` class from `ProjectCeres/Common/ICurrentUserAccessor.cs` (lines 14–18). Keep the `ICurrentUserAccessor` interface.
2. Delete `SeedAccounts` and `SeedSettings` methods from `AppDbContext.cs`. Delete the `SeedCategories` body (the in-code `HasData(...)` block) — its content moves to `Common/Categories.Defaults` per 3.7c. Remove the `using` of `SingleUserAccessor` from `AppDbContext`.
3. Update doc-only sentinel references in historic migrations (`20260502144425_AddUserIdToUserOwnedEntities.cs`, `20260502163812_AddUserIdToImportEntities.cs`) — these are comments only; no code change required, but the new `RemapSentinelToFirstUser` migration must not re-stamp the sentinel in its `Down()`.
4. Update the **78 sites** in `ProjectCeres.Tests/` that construct `new SingleUserAccessor()`. Each becomes `new FakeCurrentUserAccessor(fixture.TestUserId)` (or equivalent). New file `ProjectCeres.Tests/Common/FakeCurrentUserAccessor.cs`:
   ```csharp
   public sealed class FakeCurrentUserAccessor(Guid userId) : ICurrentUserAccessor
   {
       public Guid UserId { get; } = userId;
   }
   ```
   Default test fixture exposes a `TestUserId` getter; tests that span multiple users instantiate multiple fakes.

### 3.8 — Service-layer audit (confirm only — no code change)

The pre-spec parallel audit found all 22 user-scoped services already use `.Owned(user)` discipline. The spec records this as a verified state, not a coding step. New audit at spec time: grep `_currentUser.UserId` across `Services/` and confirm every read either chains `.Owned(user)` or is a constant-time admin-equivalent path. Output: a one-page audit checklist in `docs/multi-tenancy-strategy.md` § "Phase 3 service audit (closed)".

### 3.9 — Boot-time hook regression test

`ProjectCeres.Tests/Integration/Startup/EmptyDbStartupTests.cs` (new):

```csharp
[Fact]
public async Task App_boots_with_zero_users_and_no_boot_time_exception()
{
    // Spin up WebApplicationFactory against a fresh empty DB; assert /health
    // returns 200 without any IHostedService throwing on startup.
}
```

This is the regression test for the Stage 6a removal of `ISettingsService.EnsureExistsAsync` from `Program.cs` — confirms no boot-time hook regresses by querying user-owned tables.

### 3.10 — IDOR integration test suite

New file `ProjectCeres.Tests/Integration/MultiTenancy/IdorIsolationTests.cs`. Inherits the standard Auth integration fixture (real PostgreSQL, real EF, real interceptor, cookie auth).

Pattern:
1. **Arrange**: create User A + User B. Seed A with one row of each user-owned entity: Account, Transaction, Transfer, LiabilityPayment, Budget, CategoryBudget, Category, RecurringTransaction, SavedReport, TransactionAttachment, TransferAttachment, ImportProfile.
2. **Authenticate as User B** via cookie.
3. **Assert** for each entity (one test method per entity, ≈12 tests):
   - `GET /api/{entity}/{A's id}` → **404 Not Found** (not 403).
   - `GET /api/{entity}` (list) → does not contain A's row.
   - `PATCH /api/{entity}/{A's id}` → 404.
   - `DELETE /api/{entity}/{A's id}` → 404.
   - Aggregate endpoints (dashboard, report generators) return only B's data.
   - Attachment-specific: `POST /api/transactions/{A's id}/attachments` → 404; `GET /api/attachments/{A's-attachment-id}/content` → 404.

4. **Negative-assertion test** (required by `feedback_test_edge_cases_as_ship_gate`): a single representative test that temporarily disables the service-layer `.Owned()` filter on one endpoint (via a test-only helper) and asserts the global query filter still produces 404 independently. Proves the framework safety net works without the service-layer belt.

Total: ≈16 test methods. All hit real DB; no mocks.

## 4. Decisions locked in brainstorming (2026-05-12)

| Decision | Locked value | Why |
|---|---|---|
| Filter scope | All 22 user-scoped entities including auth-internal | Defence-in-depth applies uniformly; retention sweeps get architecture-test exceptions |
| Category model | Per-user copy on registration | Eliminates the cross-user archive bug; drops `IOptionallyUserOwned` entirely |
| `Category.IsReserved` | New column + policy rewrite | Hard-coded GUIDs in `CategoryPolicies.IsReserved` break under per-user copies — needs a row-level flag |
| Throw type | `InvalidOperationException` (ADR-0067) | Zero catch sites in production code; mechanical flip |
| Commit split | Two commits (scaffold then destructive) | Reversibility checkpoint between 3.1–3.5+3.10 and 3.6–3.7 |
| Test double | `FakeCurrentUserAccessor` | Matches Stage 6 `Fake*` naming; 78 sites swap mechanically |

## 5. Files changed

**New files (10)**:
- `ProjectCeres/Common/IUserScope.cs`
- `ProjectCeres/Common/UserScope.cs`
- `ProjectCeres/Common/IUserJobRunner.cs`
- `ProjectCeres/Common/UserJobRunner.cs`
- `ProjectCeres/Common/Categories.cs` (the default-category seed list extracted from `AppDbContext`)
- `ProjectCeres/Services/CategorySeedService.cs` (per-user copy on registration)
- `ProjectCeres/Migrations/<timestamp>_AddIsReservedToCategory.cs`
- `ProjectCeres/Migrations/<timestamp>_RemapSentinelToFirstUser.cs`
- `ProjectCeres.Tests/Common/FakeCurrentUserAccessor.cs`
- `ProjectCeres.Tests/Integration/MultiTenancy/IdorIsolationTests.cs`
- `ProjectCeres.Tests/Integration/Startup/EmptyDbStartupTests.cs`

**Modified files (≈20 in production + 78 test sites)**:
- `ProjectCeres/Common/ICurrentUserAccessor.cs` — delete `SingleUserAccessor` class
- `ProjectCeres/Common/IUserOwned.cs` — delete `IOptionallyUserOwned`
- `ProjectCeres/Common/QueryableExtensions.cs` — delete `OwnedOrShared` + helper
- `ProjectCeres/Common/UserOwnershipInterceptor.cs` — delete `IOptionallyUserOwned` branch
- `ProjectCeres/Common/Authentication/HttpContextCurrentUserAccessor.cs` — fall back to `IUserScope`; change throw type
- `ProjectCeres/Data/AppDbContext.cs` — inject `ICurrentUserAccessor`; add `ConfigureGlobalQueryFilters`; remove sentinel seeds; remove `SingleUserAccessor` reference
- `ProjectCeres/Models/Category.cs` — flip to `IUserOwned`, non-nullable `UserId`, add `IsReserved`
- 8 auth-internal model files — add `: IUserOwned`
- `ProjectCeres/Models/{Account,Settings,RecurringTransaction,SavedReport,Budget,CategoryBudget,ImportProfile,ImportStagedTransaction,ImportStagedTransfer,ImportTransferExclusion}.cs` — touched only if a query-filter-aware type needs it (most do not)
- `ProjectCeres/Services/CategoryPolicies.cs` — rewrite `IsReserved`
- `ProjectCeres/Services/CategoryService.cs` — `OwnedOrShared` → `Owned`
- `ProjectCeres/Controllers/Api/CategoriesApiController.cs` — `OwnedOrShared` → `Owned`
- `ProjectCeres/Services/RecurringTransactionService.cs`, `Services/CategoryBudgetService.cs` — `OwnedOrShared` → `Owned`
- `ProjectCeres/Services/Authentication/RegistrationService.cs` (or equivalent) — wire `CategorySeedService.CopyDefaultsForUserAsync`
- `ProjectCeres/Program.cs` — register `IUserScope`, `IUserJobRunner`, `CategorySeedService`; remove the "SingleUserAccessor stays in the codebase" comment
- `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — extend with `IgnoreQueryFilters()` boundary test + real `FailedLoginAttempt_NotInGlobalQueryFilterList`
- 78 test files — `new SingleUserAccessor()` → `new FakeCurrentUserAccessor(...)`

**Doc files**:
- `docs/planning-phase3.md` — close Stage 7 open questions, append entries to `planning-resolved.md`
- `docs/roadmap-phase-three.md` — flip Stage 7 to ✅ Done at completion; tick verification checklist
- `docs/security-model.md` — add Stage 7 "global query filter" diagram + cross-tenant access-denial flow
- `docs/multi-tenancy-strategy.md` — flip "planned" sections to "implemented"; add § "Phase 3 service audit (closed)"
- `docs/models.md` — `Category.UserId` becomes non-nullable; document `IsReserved`; document `OwnedOrShared` removal

## 6. Commit split

**Commit 1 — scaffolding (reversible)**:
- 3.1 (`IUserScope`), 3.2 (`IUserJobRunner`), 3.3 (accessor extension)
- 3.4a (promote auth entities), 3.4 (global query filters)
- 3.5 (architecture test)
- 3.7a (`IsReserved` column + migration + policy rewrite)
- 3.7b (`OwnedOrShared` removal — every site flips to `Owned`)
- 3.7c (`CategorySeedService` + registration-flow wiring)
- 3.9 (boot-time regression test), 3.10 (IDOR suite)

**Commit 2 — destructive (irreversible; requires pre-deploy DB snapshot)**:
- 3.6 (sentinel remap migration)
- 3.7 (delete `SingleUserAccessor`, sentinel constant, sentinel-stamped seed data, swap 78 test sites)

## 7. Verification

**Before merging Commit 1**:
- `dotnet build` and `pnpm --dir ProjectCeres.Client build` succeed.
- `dotnet test` green — all 303 existing Auth integration tests stay green; new IDOR suite (≈16 tests) passes; new architecture test passes; new boot-time smoke test passes.
- Manual intentional violation: add `IgnoreQueryFilters()` to a normal service, run the architecture test, confirm it fails. Revert.
- Manual: log in as User A in the browser, look at every page; log in as User B, confirm A's data is invisible.

**Before merging Commit 2**:
- Apply `RemapSentinelToFirstUser` against a copy of dev DB. Confirm post-check passes (zero sentinel rows). Confirm the rollback path triggers cleanly when pre-conditions fail (manually insert two `AspNetUsers` rows in a scratch DB and confirm the migration aborts).
- `grep -r SingleUserAccessor ProjectCeres/` returns zero hits in production code (historic-migration comment hits are acceptable).
- `grep -r "00000000-0000-0000-0000-000000000001" ProjectCeres/` returns zero hits in production code.
- `dotnet test` green after the 78-site test-double swap.

## 8. Open items and known risks

- **Phase 1 dev data preservation**: the developer's existing transactions, accounts, budgets, and import state are exactly the realistic end-to-end test data Phase 3 launch validates against. The migration must not lose them — Commit 2's pre-deploy DB snapshot is the safety net. If 3.6 fails mid-flight, restoring from snapshot is acceptable.
- **`AspNetUsers` query in `IUserJobRunner`**: the one production `IgnoreQueryFilters()` call lives in the runner. The architecture-test allow-list grants this exemption explicitly. Future drift risk: if someone adds another cross-tenant runner without updating the allow-list, the build catches it.
- **Per-user category copy at scale**: on a fresh install the registration flow now inserts ≈25 rows in a transaction. Negligible at invite-only-beta scale; revisit if onboarding latency becomes a complaint.
- **`Category.IsReserved` semantic overlap with `IsSystem`**: both signal "users can't edit this row". `IsSystem` is the existing flag; `IsReserved` is the new one. Distinction: `IsSystem = true` means the row was system-generated (e.g. "Opening Balance"); `IsReserved = true` means "the application logic depends on this row existing for this user". Reserved rows are user-owned (each user has their own copy) but immutable. The spec keeps both flags; tests document the distinction.

## 9. Out-of-spec follow-ups

- **Stage 7.5 (PostgreSQL RLS)** lands immediately after Stage 7 — same Batch 3c — per ADR-0068. Stage 7.5 spec authored separately after Stage 7 ships.
- **Admin namespace** — `ProjectCeres/Admin/` does not exist yet. The architecture-test allow-list anticipates it; the first admin file lands in Phase 4 with its own ADR.
- **Per-user category onboarding UX** — Stage 15.5 (onboarding wizard) can optionally let new users choose which default categories to keep. Out of Stage 7 scope; the registration flow copies all 25 unconditionally.
