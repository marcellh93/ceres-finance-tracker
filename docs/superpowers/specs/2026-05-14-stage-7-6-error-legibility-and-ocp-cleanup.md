# Stage 7.6 — Error legibility and OCP cleanup

**Date:** 2026-05-14
**Stage:** Phase 3, Stage 7.6 (Batch 3c continued)
**Predecessor:** Stage 7.5 (PostgreSQL Row-Level Security) — landed 2026-05-14.
**Authority:** [ADR-0073](../../decisions/ADR-0073-user-context-as-discriminated-union.md) for the `UserContext` rewrite (row 7). Rows 1–6 are pre-existing-rules cleanup; no new ADR needed.

---

## Session state (last updated 2026-05-14, mid-stage)

**Three of seven sub-stages shipped on `main`.** Resume implementation from sub-stage 7.6.3.

| # | Sub-stage | Status | Commit |
|---|---|---|---|
| 7.6.1 | WAF test-mode exception bodies | ✅ Done | `33e00cd` |
| 7.6.2 | `RlsPolicyViolationException` + `RlsExceptionTranslator` | ✅ Done | (commit landed; see `RlsPolicyViolationException` & `RlsExceptionTranslator` files) |
| 7.6.3 | `AffectedRowCount` enforcement helper | ❌ Pending — START HERE | — |
| 7.6.4 | Iterate `UserOwnedTables.All` in `AppDbContext` | ✅ Done | `fdbf960` |
| 7.6.5 | `DbExceptionTranslator` for 23505 / 23503 / 23502 | ❌ Pending | — |
| 7.6.6 | Split `TestDbFixture` into three focused fixtures | ❌ Pending | — |
| 7.6.7 | `UserContext` discriminated union (ADR-0073) | ❌ Pending | — |

**Lessons baked in from the three shipped sub-stages** (read before starting 7.6.3):

1. **7.6.2 mechanism correction.** The spec originally said `IDbCommandInterceptor.CommandFailed` for translating Postgres 42501. **That hook is observational only — it cannot replace the propagating exception**, and `ISaveChangesInterceptor.SaveChangesFailed` is the same. The shipped implementation overrides `SaveChanges` / `SaveChangesAsync` on `AppDbContext` and calls a static `RlsExceptionTranslator.TryTranslate(...)` helper in a `catch` block. Same pattern likely applies to 7.6.5's `DbExceptionTranslator` — plan accordingly.

2. **7.6.2 message-parsing fallback.** Postgres does NOT populate the structured `TableName` field on RLS WITH CHECK violations (verified against PG 16). The translator parses the table name from the message text. Sub-stage 7.6.5's `DbExceptionTranslator` will likely face similar gaps for 23505/23503/23502 — verify which structured fields Npgsql populates for each before writing the matcher.

3. **7.6.4 closure-capture pitfall.** First attempt built filter lambdas via `Expression.Lambda` with `Expression.Constant(_currentUser, ...)` — baked the specific accessor instance into EF's cached model, breaking ~162 tests. The shipped version uses `MethodInfo.MakeGenericMethod(t.EntityType).Invoke(this, new[] { modelBuilder })` to dispatch to a generic helper `RegisterUserOwnedFilter<TEntity>` whose body is a regular C# lambda. The compiler emits the right `this`-closure semantics; EF parameterizes the filter correctly. See dotnet/efcore #14740 for the documented pitfall.

4. **Order-of-operations adjustment.** 7.6.4 lands BEFORE 7.6.2 in the as-shipped order (not after, as the original spec implied) because 7.6.2's `RlsExceptionTranslator` reads from `UserOwnedTables.All` directly — same source of truth. The remaining sub-stages have no such inter-dependency; 7.6.3 → 7.6.5 → 7.6.6 → 7.6.7 is the right order from here.

5. **Test counts at session pause.** Full suite green at 1021/1021 in 3m55s. Each remaining sub-stage adds tests per the table in § 4.

---

## 1. Context — why this stage exists

Two patterns of pain across Stages 6, 7, and 7.5:

1. **Adding any wall-of-defence layer to the schema breaks ~200–745 tests on the first run.** Stage 7's global query filters and Stage 7.5's RLS policies both surfaced this. The failures clustered around shared fixtures, hard-coded connection strings, and the fact that production-server-side exceptions are invisible inside the WAF test harness — every test failure required a custom diagnostic test to find the cause.

2. **`Guid.Empty` is overloaded across four distinct failure modes**, with the disambiguation living in a string-typed registry (`IPreAuthCallSiteTagger`) and developer memory. Stage 7.5's ADR-0068 implementation-note section documents three mechanism corrections that flowed from this overload.

Stage 7.6 closes seven gaps that, taken together, would have prevented most of the Stage 7.5 pain and that will compound across Stages 8–16. The cleanup is **infrastructure-only** — no user-facing behavior changes, no schema changes, no new RLS policies.

The user explicitly chose "all seven rows" + "insert as Stage 7.6 between 7.5 and 8."

---

## 2. The seven rows

Sub-stages 7.6.1–7.6.7 each correspond to one row. Each ships as its own commit on `main`. Sequencing is calibrated so that earlier rows make later rows easier to land.

### 7.6.1 — WAF test-mode returns exception bodies

**Goal.** When an integration test hits a 500 inside the WAF, the response body is the full exception text + stack trace, not "InternalServerError". Removes the need for the diagnostic-test pattern I used three times in Stage 7.5 to find a production bug.

**Files changed.**
- `ProjectCeres.Tests/Integration/WafCollection.cs` — `ConfigureWebHost` adds `builder.ConfigureServices(s => s.AddProblemDetails())` and an `app.UseExceptionHandler` configured to write `exception.ToString()` to the response body. Gated on `IHostEnvironment.IsDevelopment()` so production is unaffected.
- New helper `ProjectCeres.Tests/Integration/ExceptionBodyAssertions.cs` — Fluent-style extension `response.Should().NotBe500WithBody()` that fails with the body text on assertion failure.

**Tests.** None new — this is a test-infrastructure improvement. Verified by the next time a real production exception surfaces in a test (we'll see the stack instead of "InternalServerError").

**LOC.** ~30 production + ~20 helper.

### 7.6.2 — `RlsPolicyViolationException` wrapping `SqlState 42501` on user-owned tables

**Goal.** When Postgres rejects a write with `42501` on a user-owned table, the exception carries the table name, the attempted `UserId`, and the GUC `UserId` instead of a generic `PostgresException`. Differentiates RLS rejection from missing-GRANT or read-only-table rejection.

**Files added.**
- `ProjectCeres/Common/Exceptions/RlsPolicyViolationException.cs` — `record class RlsPolicyViolationException(string TableName, Guid? AttemptedUserId, Guid? GucUserId, PostgresException Original) : InvalidOperationException`.
- `ProjectCeres/Common/RlsExceptionTranslator.cs` — `IDbCommandInterceptor.CommandFailed` / `CommandFailedAsync` override. Inspects the failure: if `PostgresException.SqlState == "42501"` AND the offending table is in `UserOwnedTables.All`, re-throws as `RlsPolicyViolationException`. Otherwise rethrows unchanged.

**Files modified.**
- `ProjectCeres/Program.cs` — `AppDbContext` registration appends `RlsExceptionTranslator` after `RowLevelSecurityInterceptor`.
- `ProjectCeres.Tests/Integration/Rls/Group2_WriteRejectionTests.cs` — assertions tighten from `SqlState.Should().Be("42501")` to `Should().BeOfType<RlsPolicyViolationException>().Which.TableName.Should().Be("Accounts")`. Per `docs/testing.md` § Rules case 3 (contract intentionally changed) — the assertion contract is updated alongside.

**Tests added.**
- `ProjectCeres.Tests/Common/RlsExceptionTranslatorTests.cs` — 4 unit tests:
  - Translates 42501 on a user-owned table to `RlsPolicyViolationException`.
  - Leaves 42501 on a non-user-owned table alone (e.g. permission denied for migration history).
  - Leaves non-42501 PostgresExceptions alone (e.g. 23505 unique-violation).
  - Leaves non-PostgresExceptions alone (e.g. DbUpdateException with unrelated inner).

**LOC.** ~60 production + ~50 test.

### 7.6.3 — `AffectedRowCount` enforcement helper for bulk operations

**Goal.** A future "GUC didn't get set" bug fails loud instead of returning 0 rows silently from `ExecuteUpdateAsync` / `ExecuteDeleteAsync`. Wraps the bulk operations that semantically require "exactly N rows must be affected" and throws `AffectedRowCountMismatchException` when they don't.

**Files added.**
- `ProjectCeres/Common/Exceptions/AffectedRowCountMismatchException.cs` — carries `Expected`, `Actual`, `Operation`, `CallSite`.
- `ProjectCeres/Common/BulkOperationExtensions.cs` — extension methods `ExecuteUpdateExactlyAsync(this IQueryable<T>, ..., int expectedRows = 1, [CallerMemberName] string callSite = "")` and `ExecuteDeleteExactlyAsync(..., int expectedRows = 1, [CallerMemberName] string callSite = "")`. Both throw if the actual count differs from `expectedRows`.

**Files modified — eight call sites with exactly-one-row semantics.**
- `ProjectCeres/Common/Authentication/LockoutUnlockService.cs:68, 161` — token consumption (must affect 1 row).
- `ProjectCeres/Common/Authentication/EmailChangeService.cs:134, 287, 297, 304, 396` — token consumption + revoke (must affect 1 row each).
- `ProjectCeres/Common/Authentication/UserBlockedIpMiddleware.cs:32` — revoke a session (must affect ≥ 1 row, not 0).
- (Plus `MfaBackupCodeService.cs:95` and `TotpReplayGuard.cs:82` if their semantics also require exactly-N — TBD on read of the service code.)

Call sites that intentionally affect 0-or-more rows (retention sweeps, cleanup jobs) keep the plain `ExecuteDeleteAsync` — no change. Each modified call site gets a one-line comment naming why it requires exactly N.

**Tests added.**
- `ProjectCeres.Tests/Common/BulkOperationExtensionsTests.cs` — 6 unit tests against `project_ceres_test`:
  - Returns success when count matches.
  - Throws when count is less than expected (the "GUC silently dropped" regression case).
  - Throws when count is greater than expected.
  - Custom `expectedRows` is honoured.
  - `[CallerMemberName]` populates the `CallSite` field for log legibility.
  - Exception message includes both Expected and Actual values.

**LOC.** ~50 production + ~80 test + ~15 service-code one-liners.

### 7.6.4 — Iterate `UserOwnedTables.All` in `AppDbContext.OnModelCreating`

**Goal.** Adding a new user-owned entity is one append to `UserOwnedTables.All`, not two edits (the list + the hand-written `HasQueryFilter` registration). Closes the OCP gap surfaced in the cleanup-conversation audit.

**Files modified.**
- `ProjectCeres/Data/AppDbContext.cs § ConfigureGlobalQueryFilters` — the 22 explicit `modelBuilder.Entity<X>().HasQueryFilter(...)` calls are replaced by a single loop:
  ```csharp
  foreach (var t in UserOwnedTables.All)
  {
      var entity = modelBuilder.Entity(t.EntityType);
      var filterParam = Expression.Parameter(t.EntityType, "e");
      var userIdProp = Expression.Property(filterParam, "UserId");
      var currentUserId = Expression.Property(
          Expression.Constant(_currentUser, typeof(ICurrentUserAccessor)),
          nameof(ICurrentUserAccessor.UserId));
      var body = Expression.Equal(userIdProp, currentUserId);
      var lambda = Expression.Lambda(body, filterParam);
      entity.HasQueryFilter(lambda);
  }
  ```
  Exception: `Movement` (TPC root) is still registered separately because the filter must live on the abstract root, not on `UserOwnedTables.All`'s three concrete entries (`Transactions`, `Transfers`, `LiabilityPayments`). The loop skips those three by checking against a hard-coded `TpcConcreteTypes` set; or alternately, the three concrete entries in `UserOwnedTables.All` carry a `bool IsMovementSubtype` flag and the loop skips them.

**Tests.** No new tests needed — the existing `Every_user_owned_entity_carries_a_global_query_filter` architecture test and the RLS parity test cover this. The refactor's correctness is asserted by "all 1009 tests still pass."

**Risk.** The expression-tree construction must produce the same compiled SQL as the hand-written lambda. Verified by:
1. Running the full integration suite after the change — any drift surfaces as a test failure.
2. (Optional) Reading the generated SQL via `db.Database.GetDbConnection().CommandText` interception in one diagnostic test to compare before/after.

**LOC.** Net **negative** (~30 lines removed, ~25 lines added).

### 7.6.5 — `DbExceptionTranslator` mapping SqlState codes to typed exceptions

**Goal.** `catch (UniqueConstraintViolationException)` works in service code instead of inspecting `ex.InnerException.SqlState == "23505"` everywhere. Provides typed exceptions for the four `SqlState` codes that show up in this codebase:

| Postgres SqlState | Typed exception |
|---|---|
| `23505` unique violation | `UniqueConstraintViolationException` |
| `23503` FK violation | `ForeignKeyViolationException` |
| `23502` NOT NULL violation | `NullConstraintViolationException` |
| `42501` insufficient privilege | (handled separately by `RlsExceptionTranslator`, row 7.6.2) |

**Files added.**
- `ProjectCeres/Common/Exceptions/UniqueConstraintViolationException.cs` — carries `ConstraintName`, `Original`.
- `ProjectCeres/Common/Exceptions/ForeignKeyViolationException.cs` — carries `ConstraintName`, `Original`.
- `ProjectCeres/Common/Exceptions/NullConstraintViolationException.cs` — carries `ColumnName`, `Original`.
- `ProjectCeres/Common/DbExceptionTranslator.cs` — `SaveChangesInterceptor.SaveChangesFailing` + the async variant; on `DbUpdateException` with `PostgresException` inner, switches on `SqlState` and re-throws as the typed exception. Falls through for unmatched codes.

**Files modified.**
- `ProjectCeres/Program.cs` — `AppDbContext` AND `AdminDbContext` both register `DbExceptionTranslator`.
- Existing service code that today does `catch (DbUpdateException ex) when (ex.InnerException is PostgresException pe && pe.SqlState == "23505")` simplifies to `catch (UniqueConstraintViolationException)`. Expected: ~3 call sites total (most service code does not catch DbUpdateException at all today, so this is forward-looking).

**Tests added.**
- `ProjectCeres.Tests/Common/DbExceptionTranslatorTests.cs` — 5 unit tests:
  - 23505 translates to UniqueConstraintViolationException with constraint name extracted.
  - 23503 translates to ForeignKeyViolationException.
  - 23502 translates to NullConstraintViolationException with column name extracted.
  - 42501 passes through (the RlsExceptionTranslator from 7.6.2 handles that branch).
  - Unmatched SqlState passes through unchanged.

**LOC.** ~80 production + ~70 test.

### 7.6.6 — Split `TestDbFixture` into focused fixtures

**Goal.** Three single-responsibility fixtures replace one omnibus. Each fixture's failure mode is local; adding a new RLS-bound test doesn't require knowing what `TestDbFixture` does for non-RLS tests.

**Files added.**
- `ProjectCeres.Tests/Integration/Infrastructure/MigrationFixture.cs` — runs `dotnet ef database update` as `ceres_migrator` against `project_ceres_test`. Used as `[CollectionFixture]` so it runs once per test run.
- `ProjectCeres.Tests/Integration/Infrastructure/AppContextFactory.cs` — `CreateAsync(ICurrentUserAccessor)` returns an `AppDbContext` bound to `ceres_app` with the `RowLevelSecurityInterceptor` wired.
- `ProjectCeres.Tests/Integration/Infrastructure/AdminContextFactory.cs` — `CreateAsync()` returns an `AdminDbContext` bound to `ceres_admin`.

**Files modified.**
- `ProjectCeres.Tests/Integration/TestDbFixture.cs` — becomes a thin coordinator that composes the three new fixtures + holds the per-test transaction. The connection-string constants stay on this class (still referenced from `SettingsServiceTests` and `Rls/RlsTestFixture`).

**Tests.** No new tests. Existing tests' `IClassFixture<TestDbFixture>` continues to work because `TestDbFixture` keeps its public surface.

**Risk.** This is mechanical refactoring. The full suite must still pass; if any test breaks it's because something was sharing state via the omnibus fixture in a way I haven't predicted — diagnose and adjust.

**LOC.** ~50 added across three files + ~40 lines moved out of `TestDbFixture` (net roughly zero, but each file is single-purpose).

### 7.6.7 — `UserContext` discriminated union + `[PreAuthCallSite]` attribute + `UserContextRequiredException`

**Goal.** Per ADR-0073, replace the `Guid.Empty` overload with a typed `UserContext` discriminated union. `IPreAuthCallSiteTagger` is deleted; pre-auth routes are tagged at the call site via `[PreAuthCallSite]` attribute. Diagnostic surfaces (exception messages, log messages) name the case that fired.

**Sub-steps inside this sub-stage** — implementation lands as one commit but follows this internal order:

A. `ProjectCeres/Common/UserContext.cs` — the discriminated union (see ADR-0073 § Decision for the shape).

B. `ProjectCeres/Common/PreAuthCallSiteAttribute.cs` — `[AttributeUsage(AttributeTargets.Method)]` carrying `string Name`.

C. `ProjectCeres/Common/ICurrentUserAccessor.cs` — adds `UserContext Context { get; }`. The existing `Guid UserId { get; }` is preserved as a default-interface-method derived accessor.

D. `ProjectCeres/Common/Authentication/HttpContextCurrentUserAccessor.cs` — rewritten to construct the right `UserContext` case. Reads the cookie claim first (→ `Resolved`); then checks `IUserScope.Current` (→ `Resolved` if non-null, else `Background` if running outside an HTTP context, else `Uninitialized`); then checks the current `Endpoint.Metadata` for `[PreAuthCallSite]` (→ `PreAuth(name)`).

E. The 5 known pre-auth actions get the attribute:
- `AuthController.Register`
- `AuthController.Login`
- `AuthController.LoginTotp`
- `AuthController.PasswordResetRequest`
- `LockoutUnlockController.Confirm`

F. `ProjectCeres/Common/Exceptions/UserContextRequiredException.cs` — `(string Expected, string Actual)` constructor; message names the case.

G. `ProjectCeres/Common/CurrentUserAccessorExtensions.cs` — `Require()` extension returning `Resolved` or throwing.

H. `ProjectCeres/Common/RowLevelSecurityInterceptor.cs` — switches on `Context` instead of consulting `IPreAuthCallSiteTagger`.

I. `ProjectCeres/Common/BackgroundJobScope.cs` — exception message names the rejected context.

J. **Delete** `ProjectCeres/Common/IPreAuthCallSiteTagger.cs` + `PreAuthCallSiteTagger.cs` + `ProjectCeres.Tests/Common/PreAuthCallSiteTaggerTests.cs`.

K. `ProjectCeres.Tests/Common/FakeCurrentUserAccessor.cs` — takes `UserContext` (preserves the convenience `(Guid)` constructor that wraps in `Resolved`).

L. **Architecture test rewrite.** `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` gains `Every_anonymous_endpoint_that_hits_the_db_carries_a_PreAuthCallSite_attribute`. Roslyn-free implementation: enumerate `Assembly.GetTypes()`, find methods with both `[AllowAnonymous]` (or controllers under `[AllowAnonymous]`) and a body that references `AppDbContext`; assert each has `[PreAuthCallSite]`.

**Tests added.**
- `ProjectCeres.Tests/Common/UserContextTests.cs` — 4 unit tests: pattern matching exhaustiveness, the `UserId` convenience accessor returns `Resolved.UserId` for `Resolved` and `Guid.Empty` otherwise, equality semantics for records.
- `ProjectCeres.Tests/Common/CurrentUserAccessorExtensionsTests.cs` — 4 unit tests: `.Require()` returns the user id for `Resolved`; throws for `PreAuth`, `Background`, `Uninitialized` with each case's name in the exception message.
- `ProjectCeres.Tests/Common/CurrentUserAccessorResolutionTests.cs` (existing) — extended: cookie claim → `Resolved`; `[PreAuthCallSite]` endpoint → `PreAuth(name)`; `IUserScope.Current` set without HTTP context → `Resolved`; no context → `Uninitialized`.
- `ProjectCeres.Tests/Common/RowLevelSecurityInterceptorTests.cs` (existing) — rewritten to construct contexts directly instead of mocking the tagger.

**Tests removed.** `PreAuthCallSiteTaggerTests.cs` is deleted with its tagger. This is **not a violation of the "don't skip tests" rule** — the production code under test is deleted. The replacement coverage is in the rewritten resolution tests and the new `[PreAuthCallSite]` architecture test.

**Docs updated in the same commit.** Per ADR-0073 § Consequences:
- `docs/multi-tenancy-strategy.md` § Background processes — the "`Guid.Empty` as safe default" amendment line is annotated, not deleted, with the supersession note: `Guid.Empty` is now a derived accessor on `UserContext`, not the primary type.
- `docs/planning-resolved.md` — Stage 7 entry gains a supersession annotation alongside its `Guid.Empty` reference.

**LOC.** ~200 net across production + tests + docs. Largest sub-stage; lands in its own commit.

---

## 3. Order of operations

Sub-stages MUST land in numbered order — each builds on infrastructure earlier ones provide.

1. **7.6.1** WAF exception bodies — lands first because every later sub-stage's failure-diagnosis benefits from it. If a 7.6.5 unit test surfaces a real bug, the test output shows the stack instead of "InternalServerError."
2. **7.6.4** `UserOwnedTables` iteration — small, low-risk, sets up the source-of-truth pattern for the next two.
3. **7.6.2** `RlsPolicyViolationException` — requires 7.6.4's source-of-truth list to know which tables are user-owned.
4. **7.6.3** `AffectedRowCount` helper — independent of the above, but lands here to keep refactor pairs together.
5. **7.6.5** `DbExceptionTranslator` — independent of the prior typed exceptions; lands here because it consolidates the exception-translation pattern.
6. **7.6.6** Fixture split — purely test infrastructure; lands before the big rewrite so 7.6.7's test changes use the new fixtures.
7. **7.6.7** `UserContext` rewrite — the heaviest sub-stage, lands last. ADR-0073 is its design record.

Each sub-stage gates the next: full test suite must be 1009/1009 green before the next commit lands.

---

## 4. Tests as ship gate

Per `feedback_test_edge_cases_as_ship_gate` and `docs/testing.md` § Definition of Done:

| Sub-stage | New tests | Total at close |
|---|---|---|
| 7.6.1 | 0 (infra-only) | 1009 |
| 7.6.4 | 0 (refactor; existing parity + arch tests cover) | 1009 |
| 7.6.2 | 4 unit + tightened Group 2 assertions | 1013 |
| 7.6.3 | 6 unit | 1019 |
| 7.6.5 | 5 unit | 1024 |
| 7.6.6 | 0 (refactor; existing fixture-using tests cover) | 1024 |
| 7.6.7 | 8 new (UserContext + Require + resolution + arch); deletes 7 (PreAuthCallSiteTagger); rewrites 5 (interceptor) | ~1025 |

Net change: ~+16 tests at close-out.

The full test suite must be **all green at every commit**. The 1009 baseline at Stage 7.5 close is the floor; Stage 7.6 only adds.

---

## 5. Out of scope (deferred)

- **`IRlsCommandBuilder` extraction** — the SOLID-cleanup audit flagged this as speculative; Stage 7.6 keeps the interceptor monolithic until a second GUC actually ships.
- **Per-tenant payload encryption** — Phase 4 work, defends a different threat (database dump attacker). Documented in `security-model.md` § Layer 3.
- **Hangfire / Quartz / IHostedService discriminated-union extension** — `UserContext.Background` carries a `Reason` string; future schedulers add more cases by extending the union. Out of scope for Stage 7.6.
- **HTTP-status mapping for `UserContextRequiredException`** — defaults to 500 (server error); deferred to whichever stage first surfaces the exception in a public-facing path. Per `docs/api-contract.md`, the project's contract is 422 for user-input validation and 400 for malformed HTTP; this exception is neither.

---

## 6. Verification (close-out checklist mirrors `roadmap-phase-three.md` § Stage 7.6)

- [ ] `dotnet build ProjectCeres/ProjectCeres.csproj` clean (no errors, no new warnings).
- [ ] `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj` returns ≥1024 passed, 0 failed.
- [ ] WAF integration test failures show full server-side exception body (manual spot-check by deliberately throwing inside a controller).
- [ ] `UserOwnedTables.All` is the single source of truth (`AppDbContext.OnModelCreating` references it; no hand-written `HasQueryFilter` registrations remain except the `Movement` TPC root).
- [ ] `IPreAuthCallSiteTagger` interface + class + tests are deleted.
- [ ] All 5 documented pre-auth actions carry `[PreAuthCallSite]`; the architecture test passes.
- [ ] ADR-0073 status flipped from "proposed" to "Accepted" once Stage 7.6 closes.

---

## 7. Risks

1. **Sub-stage 7.6.4's expression-tree construction might produce subtly different SQL than the hand-written lambdas.** Mitigation: run the full integration suite after the refactor; if any test fails, the diff is the smoking gun. The Stage 7 IDOR suite + Stage 7.5 RLS suite together exercise every filter path.

2. **Sub-stage 7.6.7's removal of `IPreAuthCallSiteTagger` might leave a pre-auth route un-tagged.** Mitigation: the new architecture test fails the build if any `[AllowAnonymous]` action touches `AppDbContext` without `[PreAuthCallSite]`.

3. **Sub-stage 7.6.6's fixture split might surface shared-state assumptions** in tests that worked only because the old omnibus fixture cleaned up something. Mitigation: ship the split as one commit; if a test breaks, diagnose case-by-case.

4. **The HTTP-status mapping of the new typed exceptions is deferred.** A `UserContextRequiredException` thrown inside a controller action currently returns 500 — appropriate for "server forgot to wire a user" (it IS a server bug). If a Stage 8 flow ever surfaces this in a way the user could trigger, that's the stage where the exception filter for `UserContextRequiredException → 401` lands.

---

## 8. Cross-references

- [ADR-0065](../../decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md) — the EF filter layer.
- [ADR-0067](../../decisions/ADR-0067-background-process-user-resolution.md) — `IUserScope` / `IUserJobRunner`.
- [ADR-0068](../../decisions/ADR-0068-postgres-rls-as-phase-3-defence-in-depth.md) — Stage 7.5's RLS implementation + the mechanism-correction note that surfaced this stage's pain.
- [ADR-0073](../../decisions/ADR-0073-user-context-as-discriminated-union.md) — sub-stage 7.6.7's design record.
- `docs/multi-tenancy-strategy.md` § Background processes and non-HTTP contexts.
- `docs/testing.md` § Rules — especially case 3 (contract intentionally changed), which Stage 7.6's existing-test rewrites follow.
- `docs/api-contract.md` § Validation errors — 422 contract that new typed exceptions inherit if/when they surface in controllers.
- `docs/roadmap-phase-three.md` § Stage 7.6 — the close-out checklist.
