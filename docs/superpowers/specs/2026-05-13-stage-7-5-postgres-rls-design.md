# Stage 7.5 — PostgreSQL Row-Level Security (RLS)

**Date:** 2026-05-13
**Stage:** Phase 3, Stage 7.5 (Batch 3c continued)
**Authority:** [ADR-0068 — PostgreSQL Row-Level Security as Phase 3 Defence in Depth](../../decisions/ADR-0068-postgres-rls-as-phase-3-defence-in-depth.md)
**Predecessor:** Stage 7 (multi-tenancy cutover) — ships under this spec only after Stage 7 is on `main` with a live `AspNetUsers` table.

---

## 1. Goal

Close the named gap left by Stage 7: hand-written SQL (`FromSqlRaw`, raw `DbCommand`, `IgnoreQueryFilters()` mistakes) bypasses EF Core global query filters. RLS moves the per-user row filter from the application layer into PostgreSQL itself, so every query — including hand-written SQL — is filtered at the database before EF or service code sees a row.

The wall is on for every user-owned table. For each command the application sends to the database, the application tells the database who the current user is, and PostgreSQL filters rows to that user. Cross-tenant work (Admin services, `IUserJobRunner` background jobs) flows through a separate database role with `BYPASSRLS`. Database upgrades run as a third role with DDL + `BYPASSRLS`. Tests prove all of this works.

---

## 2. The five components

### 2.1 Three PostgreSQL roles

| Role | DML | DDL | `BYPASSRLS` | Used by |
|---|---|---|---|---|
| `ceres_app` | yes | no | **no** | The running app for normal request handling. Cookie-bound user identity. |
| `ceres_admin` | yes | no | **yes** | Admin services + `IUserJobRunner` cross-tenant background work. |
| `ceres_migrator` | yes | **yes** | yes | `dotnet ef database update` only. Never used by the running app. |

Three connection strings in configuration:

- `ConnectionStrings:ApplicationConnection` — bound to `ceres_app`
- `ConnectionStrings:AdminConnection` — bound to `ceres_admin`
- `ConnectionStrings:MigrationConnection` — bound to `ceres_migrator`

The legacy `ConnectionStrings:DefaultConnection` is removed after the migration off it (see § 6, step 3).

#### Local development bootstrap

A `scripts/setup-postgres-roles.sql` file (idempotent) creates all three roles + their passwords, granted `CONNECT` on the `project_ceres` database. The README's "Setup" section runs this once on a fresh checkout. A second invocation against `project_ceres_test` creates the same three roles in the test database.

#### Production bootstrap

The hosting stage (Stage 16) owns the production runbook. This stage adds a note to that stage's roadmap entry: "production database setup creates `ceres_app`, `ceres_admin`, `ceres_migrator` per Stage 7.5; only `ceres_app` and `ceres_admin` credentials are deployed with the application; `ceres_migrator` credentials are held by the deploy operator and used only when applying migrations."

#### Privilege-leak startup check

On `ProgramStartup`, the application opens a connection under `ceres_app` and issues `CREATE TABLE _privilege_check_<guid> (id int)` inside a transaction it rolls back. If the statement succeeds, the wrong role is wired up and the application refuses to start with a clear error. If it raises `42501` permission denied, the check passes silently and startup continues.

### 2.2 The `RowLevelSecurityInterceptor`

A new `RowLevelSecurityInterceptor : DbCommandInterceptor` is registered on the application `DbContext` (not the admin variant). It overrides `ScalarExecuting`, `ReaderExecuting`, and `NonQueryExecuting` (plus the async pairs). Before each command runs:

1. It reads the current user from `ICurrentUserAccessor`.
2. If a user is resolved: it issues `SET LOCAL "app.current_user_ref" = '<uuid>'` against the same connection inside the same transaction, immediately before the command runs. `SET LOCAL` is transaction-scoped — safe with Npgsql connection pooling because it does not leak across transactions.
3. If `ICurrentUserAccessor.UserId == Guid.Empty` (pre-auth: registration, login, password-reset request): it skips the `SET LOCAL` and logs a warning entry `"DB command issued with no resolved user. Call site: {StackFrame}. Tagged pre-auth: {IsPreAuth}"`. The interceptor consults an injected `IPreAuthCallSiteTagger` (see § 2.6) to decide whether to tag the call site as legitimate pre-auth or as a missing-declaration signal. **The interceptor never throws on `Guid.Empty`** — that decision (option A in the brainstorming session) is final; the doorway refusal lives in `UserJobRunner` instead (§ 2.5).
4. The interceptor is **not** registered on the admin `DbContext` variant — admin queries skip the `SET LOCAL` entirely because the `ceres_admin` role has `BYPASSRLS`.

Implementation lives at `ProjectCeres/Common/RowLevelSecurityInterceptor.cs`. The corresponding `IPreAuthCallSiteTagger` interface + `PreAuthCallSiteTagger` implementation list the legitimate pre-auth call sites (the four endpoints below) by stable identifier — the controller + action name — and return `true` for those, `false` for everything else.

Legitimate pre-auth call sites in Stage 7.5 (the list ships in `PreAuthCallSiteTagger`):

- `AuthController.Register`
- `AuthController.Login`
- `AuthController.LoginTotp` (reads `AspNetUsers` to verify the second factor; user is not yet logged in)
- `PasswordResetService.RequestAsync`
- `LockoutUnlockController.Confirm`

Any database command made while the call stack matches one of these is tagged pre-auth and produces no warning. Anything else with `Guid.Empty` produces a warning.

### 2.3 Two `DbContext` configurations

The codebase already has one `AppDbContext` (an `IdentityDbContext`). Stage 7.5 adds a thin subclass:

```csharp
public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options, ICurrentUserAccessor currentUser)
    : AppDbContext(ConvertOptions(options), currentUser);
```

Why a subclass rather than keyed services or a second instance of the same type:

- ASP.NET Identity is hard-wired to a single `DbContext` type via `AddEntityFrameworkStores<AppDbContext>()`. The subclass approach lets Identity continue using `AppDbContext` while admin code resolves `AdminDbContext` explicitly.
- The subclass shares `OnModelCreating` with `AppDbContext` (same query filters, same model), so the model definition stays in one place. The query filters are still active on `AdminDbContext` instances; admin code that needs cross-tenant access uses `.IgnoreQueryFilters()` (already permitted under the Stage 7 allow-list).
- The interceptor distinguishes by `DbContext` runtime type — it is registered only on the `AppDbContext` registration's options builder, not the `AdminDbContext` one.

DI registration in `Program.cs`:

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("ApplicationConnection"));
    options.AddInterceptors(sp.GetRequiredService<UserOwnershipInterceptor>());
    options.AddInterceptors(sp.GetRequiredService<RowLevelSecurityInterceptor>());
});

builder.Services.AddDbContext<AdminDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("AdminConnection"));
    options.AddInterceptors(sp.GetRequiredService<UserOwnershipInterceptor>());
    // NB: no RowLevelSecurityInterceptor — ceres_admin role has BYPASSRLS.
});
```

Admin call sites that today resolve `AppDbContext` switch to `AdminDbContext`. The scope of "admin call sites" in Stage 7.5 is small and explicit:

- `UserJobRunner.ForEachUserAsync` — enumerates `AspNetUsers` cross-tenant.
- (Future) `Admin/*` controllers and admin-only services — none ship in Stage 7.5; the boundary is established for them.

### 2.4 The migration: `AddRowLevelSecurityPolicies`

A single EF migration installs the wall on every user-owned table. The migration is split into three SQL phases inside a single `Up()`:

**Phase A — Attachment-table `UserId` schema migration** (must run before policies, because the policies on `TransactionAttachments` and `TransferAttachments` read the new column). The system has two attachment tables — `TransactionAttachment` (child of `Transaction`) and `TransferAttachment` (child of `Transfer`) — both structurally identical and neither carrying an owner column today. `LiabilityPayment` has no attachments. Both attachment tables receive the same treatment:

```sql
-- TransactionAttachments
ALTER TABLE "TransactionAttachments" ADD COLUMN "UserId" uuid NULL;

UPDATE "TransactionAttachments" a
SET "UserId" = t."UserId"
FROM "Transactions" t
WHERE a."TransactionId" = t."Id";

ALTER TABLE "TransactionAttachments" ALTER COLUMN "UserId" SET NOT NULL;

CREATE INDEX "IX_TransactionAttachments_UserId" ON "TransactionAttachments" ("UserId");

-- TransferAttachments (identical shape)
ALTER TABLE "TransferAttachments" ADD COLUMN "UserId" uuid NULL;

UPDATE "TransferAttachments" a
SET "UserId" = t."UserId"
FROM "Transfers" t
WHERE a."TransferId" = t."Id";

ALTER TABLE "TransferAttachments" ALTER COLUMN "UserId" SET NOT NULL;

CREATE INDEX "IX_TransferAttachments_UserId" ON "TransferAttachments" ("UserId");
```

The matching C# model changes in the same commit:
- `TransactionAttachment` implements `IUserOwned`.
- `TransferAttachment` implements `IUserOwned`.
- `AppDbContext.OnModelCreating` gains `HasQueryFilter` registrations for both via `UserOwnedTables.All` (§ 3).
- `TransactionService` and `TransferService` write paths stamp `Attachment.UserId` from the parent movement (already covered by `UserOwnershipInterceptor`'s default-`Guid` rule, so this is automatic — but `TransactionService.CreateAsync` and `TransferService.CreateAsync` must be audited to confirm the attachment collection is built inside the parent's `using` scope, so the interceptor sees a non-default `UserId` from `ICurrentUserAccessor`).

**Phase B — Enable RLS + `FORCE RLS` on all 24 protected tables.** The exact table list (PostgreSQL table names, post-Stage-7). The pre-Stage-7.5 `IUserOwned` count was 22; `TransactionAttachment` and `TransferAttachment` are the 23rd and 24th, added in this stage:

Finance domain (13):
1. `Accounts`
2. `Budgets`
3. `Categories`
4. `CategoryBudgets`
5. `ImportProfiles`
6. `ImportStagedTransactions`
7. `ImportStagedTransfers`
8. `ImportTransferExclusions`
9. `RecurringTransactions`
10. `SavedReports`
11. `Settings`
12. `TransactionAttachments` (after Phase A)
13. `TransferAttachments` (after Phase A)

Movement TPC concrete tables (3):
14. `Transactions`
15. `Transfers`
16. `LiabilityPayments`

Auth-internal (8):
17. `UserSessions`
18. `UserBlockedIps`
19. `UserMfaBackupCodes`
20. `TotpReplayEntries`
21. `PasswordResetTokens`
22. `EmailChangeTokens`
23. `LockoutUnlockTokens`
24. `AuditLog`

(24 tables total. Cross-check against `UserOwnedTables.All` in § 3.)

**Excluded** (system / reference tables — no RLS):
- `AccountTypes`, `CategoryTypes`, `Currencies`, `ReportTypes`, `SystemCategories` (shared lookup data, no `UserId`).
- `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens` (Identity tables — `AspNetUsers` is read pre-auth during login).
- `__EFMigrationsHistory` (EF infrastructure).

**Deferred** (not in this stage's policy list):
- `SupportTickets` — does not exist in the schema. Stage 12 builds the table and adds the RLS policy in the same migration. **Roadmap note must be added to Stage 12** so this doesn't slip.

The policy template applied to each table (example for `Transactions`):

```sql
ALTER TABLE "Transactions" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Transactions" FORCE ROW LEVEL SECURITY;
CREATE POLICY user_isolation ON "Transactions"
  USING ("UserId" = current_setting('app.current_user_ref', true)::uuid)
  WITH CHECK ("UserId" = current_setting('app.current_user_ref', true)::uuid);
```

The `true` second argument to `current_setting` makes it return `NULL` (instead of raising) when the GUC is unset — that's how option A's "skip `SET LOCAL` → see zero rows" behaviour falls out: `UserId = NULL` evaluates to `unknown`, which the policy treats as `false`, which filters all rows.

`FORCE ROW LEVEL SECURITY` is critical: without it, table owners (which `ceres_app` will be by default if it created the table) bypass policies. The migrator role (`ceres_migrator`) creates the tables and is the owner, so `ceres_app` is not the owner — but `FORCE` is added defensively in case ownership ever changes.

**Phase C — `GRANT BYPASSRLS` on the admin role** (the role already has the attribute from § 2.1's setup script; this phase is a no-op idempotency check that verifies the attribute is present and `RAISE EXCEPTION` if not):

```sql
DO $$ BEGIN
  IF NOT (SELECT rolbypassrls FROM pg_roles WHERE rolname = 'ceres_admin') THEN
    RAISE EXCEPTION 'ceres_admin role exists but does not have BYPASSRLS. Run scripts/setup-postgres-roles.sql.';
  END IF;
END $$;
```

**Migration `Down()`** drops all policies and disables RLS, in reverse order. The schema migration (Phase A) is **not** reversed — the `TransactionAttachment.UserId` column stays. (Standard EF practice: down-migrations are best-effort and we don't want to drop data.)

### 2.5 The `UserJobRunner` doorway refusal

The existing `UserJobRunner.ForEachUserAsync` already calls `scope.EnterAs(userId)` inside the loop before invoking `work`. Today nothing prevents a caller from invoking `work` outside the `using` — there is no caller doing that yet, but the stage adds a guard.

New: every background job MUST enter through `BackgroundJobScope.RunAsync` (which internally calls `IUserScope.EnterAs`). Direct calls to `IUserScope.EnterAs` from outside `BackgroundJobScope` are forbidden and enforced by the architecture test in § 4 / § 11 risk 2. The doorway refusal lives at the call boundary that all background work routes through. Concretely:

1. **`UserScope.EnterAs`** — gains a no-op (the current implementation is already correct: it returns an `IDisposable` that restores the previous value on dispose). No change needed.

2. **A new `BackgroundJobScope` wrapper** — `IBackgroundJobScope.RunAsync(Guid userId, Func<Task> work)`. The wrapper enters `IUserScope.EnterAs(userId)` for the duration of `work`. If `userId == Guid.Empty`, the wrapper throws `InvalidOperationException("Background jobs must declare a user. Call EnterAs with a non-empty user id.")` before invoking `work`. Background-job entry points use this wrapper instead of touching `IUserScope` directly. `UserJobRunner.ForEachUserAsync` is updated to route through `IBackgroundJobScope.RunAsync`.

3. **Logging at the doorway** — the wrapper logs `LogError` ("Background job refused: no user declared. Job: {JobName}") before throwing. The scheduler (`IHostedService` or `BackgroundService` subclass) catches the exception via the standard ASP.NET background-service error path, which already logs and continues to the next iteration.

Stage 7.5 ships the wrapper interface, the implementation, the `UserJobRunner` update, and tests. No existing background job today routes through anywhere other than `UserJobRunner`, so the surface area is small.

### 2.6 The pre-auth call-site tagger

```csharp
public interface IPreAuthCallSiteTagger
{
    bool IsLegitimatePreAuth();
}

public sealed class PreAuthCallSiteTagger(IHttpContextAccessor http) : IPreAuthCallSiteTagger
{
    private static readonly HashSet<string> PreAuthRoutes = new(StringComparer.Ordinal)
    {
        "AuthController.Register",
        "AuthController.Login",
        "AuthController.LoginTotp",
        "PasswordResetService.RequestAsync", // service method — needs explicit signalling
        "LockoutUnlockController.Confirm",
    };

    public bool IsLegitimatePreAuth()
    {
        var endpoint = http.HttpContext?.GetEndpoint();
        var descriptor = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (descriptor is null) return false;
        var key = $"{descriptor.ControllerName}Controller.{descriptor.ActionName}";
        return PreAuthRoutes.Contains(key);
    }
}
```

`PasswordResetService.RequestAsync` is called from `AuthController.PasswordResetRequest` — adding the controller-level entry covers it; the service-method entry is a safety net for direct callers (none today).

This list is short by design — adding to it should be a deliberate code review, not a default.

---

## 3. The shared user-owned-tables list

To prevent the three lists (EF filters, RLS policies, parity test) from drifting apart, this stage introduces one source of truth:

```csharp
// ProjectCeres/Common/UserOwnedTables.cs
public static class UserOwnedTables
{
    public static readonly IReadOnlyList<UserOwnedTable> All = new[]
    {
        new UserOwnedTable("Accounts",              typeof(Account)),
        new UserOwnedTable("Budgets",               typeof(Budget)),
        // ... 24 entries total
    };
}

public sealed record UserOwnedTable(string PostgresTableName, Type EntityType);
```

The migration iterates `UserOwnedTables.All` to generate its SQL. `OnModelCreating` iterates the same list to register `HasQueryFilter`. The parity test (§ 4) iterates it to compare against `pg_policies`.

Adding a new user-owned entity is a single edit (append to `UserOwnedTables.All`); the migration generator, the filter registration, and the parity test all pick up the new entry automatically — and if the migration for the new entity forgets to add the policy, the parity test fails the build.

---

## 4. Tests (the four groups + parity)

Every test in this section lives in `ProjectCeres.Tests/Integration/Rls/`. The tests connect to `project_ceres_test` (which gains the same three roles in local-dev setup, § 2.1).

### Group 1 — Bypass case

For each of the 24 protected tables, one test:

```
Rls.Group1_BypassCase
  - Transactions_FromSqlRaw_under_userB_returns_only_userB_rows
  - Transfers_FromSqlRaw_under_userB_returns_only_userB_rows
  - ... (one per table)
```

Each test:
1. Open a fixture with two users A and B seeded in `AspNetUsers`.
2. Insert one row owned by A.
3. Switch the request scope to B.
4. Execute `db.<Entities>.FromSqlRaw("SELECT * FROM \"<Table>\"").ToListAsync()`.
5. Assert the result is empty.

### Group 2 — Write rejection

For each protected table, one test:

```
Rls.Group2_WriteRejection
  - Transactions_insert_with_foreign_UserId_raises_42501
  - Transactions_update_to_foreign_UserId_raises_42501
  - Transactions_delete_against_foreign_UserId_affects_0_rows
  - ... (one set per table)
```

Each test:
1. Open a fixture as user A.
2. For INSERT: build an entity with `UserId = B.Id`, call `SaveChangesAsync`, assert `PostgresException` with `SqlState == "42501"`.
3. For UPDATE: insert as A (passes), then try to set `UserId = B.Id`, assert `42501`.
4. For DELETE: try to delete a row known to be B's, assert `0` rows affected.

### Group 3 — Admin + background-job paths

```
Rls.Group3_AdminAndBackground
  - AdminDbContext_with_IgnoreQueryFilters_returns_rows_from_all_users
  - BackgroundJob_via_EnterAs_userA_sees_only_userA_rows
  - BackgroundJobScope_refuses_when_userId_is_Guid_Empty_before_work_runs
  - BackgroundJobScope_invokes_work_when_userId_is_valid
```

### Group 4 — Backstops (option A)

```
Rls.Group4_Backstops
  - Interceptor_warns_when_Guid_Empty_outside_preauth_paths
  - Interceptor_does_not_warn_on_login_endpoint
  - Interceptor_does_not_warn_on_registration_endpoint
  - Interceptor_does_not_warn_on_password_reset_request
  - SET_LOCAL_does_not_leak_across_transactions_under_Npgsql_pooling
```

The last test is the connection-pooling guarantee:
1. Open a connection from the pool, begin transaction.
2. Set the GUC for user A, run a SELECT, commit.
3. Release the connection back to the pool.
4. Acquire a new connection from the pool (likely the same physical connection).
5. Begin transaction without setting the GUC.
6. Run the same SELECT.
7. Assert: zero rows. (`SET LOCAL` did not survive the previous transaction.)

### Parity test

`Rls.Parity_QueryFilterListMatchesRlsPolicyList`:
1. Connect as `ceres_migrator` (only role that can read `pg_policies` fully).
2. Query `SELECT tablename FROM pg_policies WHERE policyname = 'user_isolation'`.
3. Compare against `UserOwnedTables.All.Select(t => t.PostgresTableName)`.
4. Assert: same set, no missing, no extra.

This test runs against a freshly-migrated test database, so it tests the *actual installed policies*, not parsed migration text.

---

## 5. Test infrastructure updates

Mandatory (not optional) work that lands in this stage:

1. `ProjectCeres.Tests/Integration/TestDbFixture.cs` — currently constructs `new AppDbContext(...)` directly with a hard-coded connection string. Must:
   - Use the three connection strings (`ApplicationConnection`, `AdminConnection`, `MigrationConnection`) pointed at `project_ceres_test`.
   - Construct either `AppDbContext` (default, app role, RLS-bound) or `AdminDbContext` (admin role, BYPASSRLS) depending on the test's needs. Most tests get `AppDbContext`; the cross-tenant tests in Group 3 get `AdminDbContext`.
   - Add `RowLevelSecurityInterceptor` to the `AppDbContext` options builder when constructing.

2. `ProjectCeres.Tests/Integration/WafCollection.cs` — `TestWebApplicationFactory.ConfigureWebHost` currently sets only `ConnectionStrings:DefaultConnection`. Must set all three:
   ```csharp
   builder.UseSetting("ConnectionStrings:ApplicationConnection", AppConnString);
   builder.UseSetting("ConnectionStrings:AdminConnection",       AdminConnString);
   builder.UseSetting("ConnectionStrings:MigrationConnection",   MigratorConnString);
   ```

3. A new `RlsTestFixture` in `Rls/` — wraps two users, the three roles, and a helper to switch between "request scope" and "background scope" for the four test groups.

4. `scripts/setup-postgres-roles.sql` invocation in `project_ceres_test` is part of the README's "Run tests" section. CI adds the same script invocation before `dotnet test`.

5. `FakeCurrentUserAccessor` (test double from Stage 7) — no change needed; the interceptor reads from `ICurrentUserAccessor` and the fake still satisfies that.

---

## 6. Order of operations

Seven steps. Each is a separate logical change; they may collapse into fewer commits, but the order must be preserved.

1. **`scripts/setup-postgres-roles.sql`** — create the three roles in `project_ceres` and `project_ceres_test`. README updated. Local-dev verification: each role can `\c` into the database with the documented password.

2. **`RowLevelSecurityInterceptor` + `IPreAuthCallSiteTagger`** — committed without DI registration. Unit tests verify the interceptor's `SET LOCAL` text, the `Guid.Empty` skip-and-log path, and the tagger's pre-auth recognition logic. No effect on running app yet.

3. **DI split + connection-string migration** — `Program.cs` gains the three connection strings and the `AdminDbContext` registration. `appsettings.json` updates. The interceptor is wired onto `AppDbContext` only. `UserJobRunner` switches to `AdminDbContext`. **No RLS policies yet**, so the app still works exactly as before — the interceptor's `SET LOCAL` is harmless (no policies consume it). Running the full test suite at this step confirms zero regression from the DI change alone.

4. **Privilege-leak startup check** — added to `Program.cs`. Throws at startup if `ceres_app` can `CREATE TABLE`. Tested by deliberately mis-wiring the connection string in a unit test and asserting startup fails.

5. **`UserOwnedTables.cs` + `OnModelCreating` refactor + attachment-table model changes + the RLS migration — committed together.** The shared list (`UserOwnedTables.All`) replaces the three duplicated lists. `TransactionAttachment` and `TransferAttachment` start implementing `IUserOwned`. `OnModelCreating` iterates `UserOwnedTables.All` to register `HasQueryFilter`. The migration `AddRowLevelSecurityPolicies` runs in the same commit and contains phases A (attachment-table `UserId` schema additions), B (RLS + policies on 24 tables), C (admin BYPASSRLS verification). Migration runs as `ceres_migrator`. The model changes and the schema migration must land together — registering a `HasQueryFilter` on `TransactionAttachment.UserId` without first adding the column would break the build. After this step the wall is live.

6. **`BackgroundJobScope` + `UserJobRunner` routing through it** — the doorway refusal. New tests cover the refusal and the happy path.

7. **Doc sync** — `security-model.md` § PostgreSQL Row-Level Security updated from "Phase 4" to "Phase 3 Stage 7.5 — built". `multi-tenancy-strategy.md` § EF Core Global Query Filters updated. `roadmap-phase-three.md` Stage 7.5 checklist items ticked. `roadmap-phase-three.md` Stage 12 entry gains the note: "When SupportTicket table ships, the same migration must add the RLS `user_isolation` policy per Stage 7.5; add to `UserOwnedTables.All`." `planning-resolved.md` entry added for Stage 7.5 close.

---

## 7. Targeted refactor that lands in this stage

`UserOwnedTables.cs` (§ 3) — eliminates the three-way duplication between `HasQueryFilter` registrations, the RLS migration's table list, and the parity test. The file is being touched anyway; the duplication would grow with every future user-owned entity. The refactor is **not** in-scope cleanup for its own sake — it is the substrate the parity test runs on.

---

## 8. Out of scope, on purpose

- **Per-tenant payload encryption** — Phase 4. Defends a different threat (database dump attacker). Documented in `security-model.md` § Layer 3.
- **`SupportTicket` RLS policy** — Stage 12 ships the table; this stage adds the roadmap note that the policy ships with the table.
- **Production deployment runbook** — Stage 16 owns the runbook. This stage adds a single note to Stage 16's entry covering the three-role requirement.
- **`UserSession` retention purge** — Stage 13. Uses `IUserJobRunner`, which is now RLS-aware via this stage.
- **Admin UI / controllers** — none ship in Stage 7.5. The `AdminDbContext` is established; the controllers come later.
- **PostgreSQL role rotation procedure** — operational concern, captured in `docs/security-model.md` for Stage 16 to formalise.

---

## 9. Roadmap-vs-code reconciliation captured in this spec

Three differences between `roadmap-phase-three.md` § Stage 7.5 and the actual codebase are reconciled here. The roadmap will be updated in the doc-sync step (§ 6, step 8):

| Roadmap text | Codebase reality | Spec resolution |
|---|---|---|
| `CsvImportProfile` | Class is `ImportProfile` (file is `CsvImportProfile.cs`) | Use `ImportProfile` (class name) in `UserOwnedTables.All`. Roadmap text updated. |
| `TransactionAttachment` listed as protected | No `UserId` column today | Phase A of the migration adds `UserId`; entity implements `IUserOwned`; standard policy applies. |
| `TransferAttachment` not mentioned | Same shape as `TransactionAttachment`, no `UserId` column today | Spec adds it symmetrically — Phase A also covers `TransferAttachments`; entity implements `IUserOwned`; standard policy applies. **Found during spec self-review.** |
| `SupportTicket` listed as protected | Table does not exist (Stage 12) | Dropped from this stage's list. Stage 12 roadmap entry gains a note to add it then. |
| `Movement` (singular table) | TPC root: three concrete tables `Transactions`, `Transfers`, `LiabilityPayments` | Spec lists all three concrete tables; `UserOwnedTables.All` has three entries plus `Movement` as the EF model anchor. |

---

## 10. Verification checklist (for stage close-out)

This list mirrors the roadmap's Stage 7.5 verification checklist; tests + manual checks are flagged.

### Postgres roles + connection strings (automated where marked)

- [ ] `setup-postgres-roles.sql` creates three roles in `project_ceres` and `project_ceres_test` (manual + smoke test)
- [ ] `ceres_app` has DML rights, no DDL, no `BYPASSRLS` (automated: privilege-leak startup check + `pg_roles` query test)
- [ ] `ceres_admin` has DML rights, no DDL, `BYPASSRLS` (automated: `pg_roles` query test)
- [ ] `ceres_migrator` has DDL rights, `BYPASSRLS` (manual: documented in setup script)
- [ ] Three connection strings configured in `appsettings.json`, `appsettings.Development.json`, user secrets, and test infra (automated: `DI_resolves_correct_DbContext` test)
- [ ] DI resolves `AppDbContext` for HTTP requests and `AdminDbContext` for admin / background paths (automated: architecture test)

### `RowLevelSecurityInterceptor` (automated)

- [ ] Implements all six interception methods (sync + async × Scalar/Reader/NonQuery) (unit test)
- [ ] Issues `SET LOCAL "app.current_user_ref" = '<uuid>'` against the same connection inside the same transaction, immediately before the EF command (unit test against an in-memory `DbCommand` spy)
- [ ] Skips `SET LOCAL` when `ICurrentUserAccessor.UserId == Guid.Empty` (unit test)
- [ ] Logs a warning when `Guid.Empty` outside pre-auth call sites (Group 4 test)
- [ ] Does NOT log a warning when `Guid.Empty` on a pre-auth call site (Group 4 test, × 3 endpoints)
- [ ] Not registered on `AdminDbContext` (architecture test asserting interceptor registration only on `AppDbContext`)
- [ ] `SET LOCAL` does not leak across transactions under Npgsql pooling (Group 4 test)

### RLS policies on every user-owned table (automated parity test)

- [ ] All 24 tables listed in § 2.4 Phase B have `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` (parity test)
- [ ] Each has a `user_isolation` policy with both `USING` and `WITH CHECK` clauses (parity test)
- [ ] System tables (`AccountTypes`, `CategoryTypes`, `Currencies`, `ReportTypes`, `SystemCategories`) have no RLS (parity test asserts absence)
- [ ] Identity tables (`AspNetUsers` et al.) have no RLS (parity test asserts absence)

### Per-table integration tests (automated, 24 tables × 4 assertions = 96 tests minimum)

- [ ] Group 1 (FromSqlRaw bypass returns 0 foreign rows) — × 24
- [ ] Group 2 (INSERT / UPDATE / DELETE rejection) — × 24 × 3
- [ ] Group 3 (admin sees all, background-scope sees own only, doorway refusal) — 4 tests
- [ ] Group 4 (backstop log line + pooling) — 5 tests

### Architecture tests

- [ ] `UserOwnedTables.All` ↔ `pg_policies` parity (parity test from § 4)
- [ ] Interceptor is registered only on `AppDbContext`, not `AdminDbContext` (architecture test)
- [ ] Outside `UserJobRunner` and (future) `Admin/` controllers, no code references `AdminDbContext` directly (architecture test)
- [ ] Every `BackgroundJobScope.RunAsync` call site passes a non-empty user id (architecture test or syntax scan)
- [ ] `IUserScope.EnterAs` callers always wrap in `using` (Roslyn or syntax-tree scan covering `ProjectCeres/**/*.cs`) — moved here from Stage 7 close-out

### Operational

- [ ] `scripts/setup-postgres-roles.sql` exists and is idempotent
- [ ] README's "Setup" section runs the script; "Run tests" section invokes it against `project_ceres_test`
- [ ] `appsettings.json` configuration template updated for the three connection strings
- [ ] Stage 16 roadmap entry references this stage's three-role requirement
- [ ] Stage 12 roadmap entry references `UserOwnedTables.All` for `SupportTicket` enrolment

---

## 11. Open risks

1. **`AspNetUsers` is read pre-auth.** The interceptor's `Guid.Empty` skip-and-log behaviour is the safety mechanism, but if a future feature adds an RLS policy to `AspNetUsers`, login breaks silently. Mitigation: `AspNetUsers` is explicitly excluded from `UserOwnedTables.All`, and the parity test will fail if anyone ever adds it.

2. **Background work outside `UserJobRunner`.** Stage 7.5 only knows about `UserJobRunner` as a background-work entry point. If future stages add a different scheduler (Hangfire, Quartz, IHostedService implementations), they must route through `BackgroundJobScope` — there is no compile-time enforcement. Mitigation: architecture test scanning for `IUserScope.EnterAs` outside `BackgroundJobScope.RunAsync`.

3. **`current_setting(..., true)` behaviour.** The `true` flag makes the function return `NULL` instead of raising when the GUC is unset. This is the basis for option A's correctness. If a future PostgreSQL upgrade changes this behaviour, the wall could start producing errors instead of zero rows. Mitigation: pinned PostgreSQL major version in the deployment runbook; covered by Group 4's pooling test.

4. **Race between `BEGIN` and `SET LOCAL`.** `SET LOCAL` requires an active transaction. If a code path issues a command outside a transaction (some EF read paths do auto-commit), `SET LOCAL` still works but is scoped to the implicit single-statement transaction — which is exactly the scope we want. Confirmed against Npgsql 8.x behaviour; covered by Group 4's pooling test.

---

## 12. Cross-references

- [ADR-0065 — EF Core Global Query Filters with Explicit Redundancy and Admin-Only Bypass](../../decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md)
- [ADR-0066 — Sentinel-to-real-user migration](../../decisions/ADR-0066-sentinel-to-real-user-migration.md)
- [ADR-0067 — Background-process user resolution with `IUserScope` and runner](../../decisions/ADR-0067-background-process-user-resolution.md)
- [ADR-0068 — PostgreSQL Row-Level Security as Phase 3 Defence in Depth](../../decisions/ADR-0068-postgres-rls-as-phase-3-defence-in-depth.md)
- [`security-model.md` § PostgreSQL Row-Level Security](../../security-model.md)
- [`multi-tenancy-strategy.md` § EF Core Global Query Filters](../../multi-tenancy-strategy.md)
- [`roadmap-phase-three.md` § Stage 7.5](../../roadmap-phase-three.md)
- Stage 7 spec (multi-tenancy cutover): [`docs/superpowers/specs/2026-05-12-stage-7-multi-tenancy-cutover-design.md`](2026-05-12-stage-7-multi-tenancy-cutover-design.md)
