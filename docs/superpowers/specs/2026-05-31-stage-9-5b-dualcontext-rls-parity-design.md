# Stage 9.5b — Production-parity DB layer: model-derived user-owned set + fail-closed RLS-parity check + dual-context test fixture

> **Diataxis type:** Reference + explanation — design spec for sub-stage 9.5b under `## Stage 9.5h — Phase 1 hardening container` in `docs/roadmap-phase-three.md`.
>
> **Status:** Draft — design approved (section-by-section) 2026-05-31. Awaiting implementation plan + user review of this spec.
>
> **Predecessor:** 9.5k (codified subagents, shipped) + 9.5c (analyzers at `error`, shipped). **Prerequisite met:** 9.5k — this stage's brainstorm dispatched the codified `ceres-architect` / `ceres-tech-lead` / `ceres-security-reviewer` agents. **Successor:** 9.5d (`ProjectCeres.Tests.Integration.AppRole` — the full switch of the auth suite to the `ceres_app` role).

## 1. Problem

A user-owned table can ship without its PostgreSQL Row-Level Security (RLS) policy, leaking rows across users, and the test suite cannot catch it. This shipped once: `EmailConfirmationTokens` (Stage 9.3) reached `main` with `rowsecurity = false` and every integration test still passed.

Two independent holes produced the gap; closing it needs both:

1. **Hole A — the test fixture routes through `ceres_admin` (BYPASSRLS).** `ProjectCeres.Tests/Integration/WafCollection.cs:75-76` overrides `ConnectionStrings:ApplicationConnection` to the admin string, so `AppDbContext` in every integration test runs as the role that *ignores* RLS. A missing policy is invisible at test runtime.
2. **Hole B — the parity oracle derives "expected" from the same hand-list that was missing the entity.** `ParityTests` builds its expected set from `UserOwnedTables.All`. `EmailConfirmationTokens` was missing from that list too, so the test compared `{list − token}` against `{db − token}` and went green. A test whose expectation comes from the same source as the bug cannot catch the bug.

A third, compounding factor: the user-owned table set is a **hand-typed list** (`ProjectCeres/Common/UserOwnedTables.cs`, `UserOwnedTables.All`). Hand-typed lists rot; the rot is *how* the entity got missed.

## 2. Locked decisions

| Lock | Decision | Source |
|---|---|---|
| D1 | The live-DB strictness check requires **both** `relrowsecurity = true` AND `relforcerowsecurity = true` per table. Without FORCE, the table-owning role silently bypasses the policy. | User Q (2026-05-31) + security-reviewer |
| D2 | The parity check runs at **production startup AND in tests**, fail-closed. | User Q (2026-05-31) |
| D3 | The **production-startup** check compares the model-derived user-owned set against **what the assembly's migrations declare** (a compile-/assembly-time expectation), NOT against live `pg_class`. The live-`pg_class` comparison runs only in the **test suite**. Rationale: a live-DB check at boot turns a routine rolling-deploy migration race (new binary up, migration not yet applied) into a hard outage; comparing against declared-migration state fails on the developer mistake without failing on the deploy race. | User Q (2026-05-31) + security-reviewer §3 |
| D4 | **Full consolidation.** Delete `UserOwnedTables.All`; a new model-reflection helper becomes the single source; all readers repoint to it. The one exception is the historical RLS migration (see D5). | User Q (2026-05-31) |
| D5 | The **historical RLS migration** (`20260514011916_AddRowLevelSecurityPolicies.cs`) inlines its OWN frozen `string[]` of the 24 table names. Migrations must emit byte-identical output forever; they must NOT depend on mutable app types or runtime reflection. Future user-owned tables get their policy via a NEW migration, so this frozen list never grows. | User Q (2026-05-31, "Decision A") + tech-lead + architect |
| D6 | The dev-seed tool (`SeedDevUser.cs`) repoints its own separate 16-name list to a **finance-only view** of the helper. It must stay finance-only (it must NOT begin remapping auth-internal sentinel rows like `UserSessions`/`AuditLogs`). Zero hand-typed lists survive. | User Q (2026-05-31) + all three agents |
| D7 | **Scope of the test switch:** 9.5b builds the `ceres_app` real-account test *capability* and the new safety-check tests use it. The ~90 existing auth tests stay on `ceres_admin` for now; the full switch is **9.5d**. 9.5b delivers capability, 9.5d delivers enforcement. (Roadmap line 1094 option (b).) | User Q (2026-05-31, "Decision B") + security-reviewer §1 |

## 3. Findings the design is built on (ceres-* strategy pass, 2026-05-31)

Three codified strategy agents read the surface under their read-first contract; all three independently converged on the load-bearing risks. Corrections I made to agent claims via direct verification are noted.

- **The Movement TPC trap (highest-risk reflection bug).** `Movement` is abstract, implements `IUserOwned`, and has no physical table under TPC mapping; its three concrete children (`Transactions`, `Transfers`, `LiabilityPayments`) carry the real tables. A naive "all `IUserOwned` implementers" scan emits a policy for a non-existent `Movements` table and drops the three real ones. The helper MUST enumerate **concrete, mapped** types.
- **The attachment divergence.** `TransactionAttachment`/`TransferAttachment` ARE in the RLS set (they gained `UserId` in the RLS migration's Phase A) but are NOT in the EF query-filter set (no EF `HasQueryFilter` — scoped via parent at the EF layer). "RLS set" ≠ "query-filter set." The helper exposes the RLS set; the query-filter check keeps its narrower view; the two are reconciled with an explicit, tested, commented exception.
- **The "has a UserId" false-friend.** `FailedLoginAttempt` and `EmailDeliveryEvent` have a `UserId` column but deliberately do NOT implement `IUserOwned` (cross-user by ADR-0067). The predicate MUST be `typeof(IUserOwned).IsAssignableFrom(t)`, never "has a UserId property."
- **The fifth consumer.** `SeedDevUser.cs:60-78` holds its OWN 16-name `string[]` — it does not reference `UserOwnedTables.All`, so deleting `.All` does not break its compile; it would silently keep a stale list. D6 repoints it.
- **The exact-type trap.** `AdminDbContext : AppDbContext`. The architecture test (Section 4) must match `AdminDbContext` by **exact type**, not `IsAssignableFrom` — otherwise an unmarked admin injection satisfies an "is AppDbContext" check and sails through the test meant to flag it.
- **The `ceres_app` test-setup 42501 trap.** Under the real account, test setup that creates rows without an established current-user context is rejected (RLS filters to zero / insert refused). Discipline: **seed via the admin handle, assert via the app handle** — the pattern `RlsTestFixture` already demonstrates.
- **CORRECTION — `[RequiresAdminContext]` already exists.** Two agents claimed it does not. Direct grep found `ProjectCeres.Analyzers.Annotations/RequiresAdminContextAttribute.cs` (`AttributeTargets.Class | AttributeTargets.Method`), shipped with 9.5c's annotations. 9.5b **consumes** it; it is not created here.
- **CORRECTION — the dual-account test infrastructure already exists.** `ProjectCeres.Tests/Integration/Infrastructure/AppContextFactory.cs` (RLS-active `ceres_app` + production interceptors) and `AdminContextFactory.cs` (`ceres_admin` bypass), plus `RlsTestFixture`. 9.5b **reuses** these, not reinvents.
- **`ceres_app` role is live** in both `project_ceres` and `project_ceres_test` with `rolbypassrls = false` (verified). The fixture should fail loudly with a "run scripts/setup-postgres-roles.sql" message on a fresh clone, mirroring `PrivilegeLeakStartupCheck`'s error text.

## 4. Architecture

Four pieces, each independently testable.

### 4.1 The model-derived user-owned helper

New `ProjectCeres/Common/UserOwnedModel.cs`. Given the EF model (or a static reflection over the `ProjectCeres.Models` assembly), returns the user-owned table set:

- Predicate: `typeof(IUserOwned).IsAssignableFrom(t)` AND the type is **concrete** (`!IsAbstract`) AND **mapped** (`GetTableName()` is non-null).
- Table name resolved via EF's own `entityType.GetTableName()` — never via the deleted hand-list.
- Exposes at least two views:
  - `RlsTables` — the full RLS set (includes the two attachment tables; excludes abstract `Movement`; includes the three concrete Movement subtypes).
  - `FinanceTables` — the finance+attachment subset for `SeedDevUser` (D6), excluding auth-internal tables.
- The query-filter set (used by `ConfigureGlobalQueryFilters`) is `RlsTables` minus the two attachment tables — encoded as an explicit, commented reconciliation, not a silent filter.

### 4.2 The fail-closed RLS-parity startup check

New check beside `PrivilegeLeakStartupCheck`, invoked at `Program.cs` ~line 544 (after `builder.Build()`), gated by the same `Stage75:SkipPrivilegeLeakCheck` knob.

- **Boot oracle (D3):** assert every entry in `UserOwnedModel.RlsTables` has a corresponding RLS policy **declared in a migration in this assembly**. Does not query live `pg_class`. Deploy-order-independent. Throws on mismatch with a message naming the offending table(s) and the fix.
- **Test oracle (D2 + live form):** the test-suite variant compares `UserOwnedModel.RlsTables` against live `pg_class` (`relrowsecurity = true AND relforcerowsecurity = true`, per D1). Full strength; no availability cost. This is the existing `ParityTests` logic, repointed to the helper.
- The two oracles are deliberately different and the spec/code say so, so a future reader does not "simplify" them into one and reintroduce the deploy-race outage.

### 4.3 The dual-context test fixture

New `DualContextWebApplicationFactory` (smallest landing: add a `UseAppRoleConnection` virtual, default `false`, to the base `TestWebApplicationFactory`; gate the `WafCollection.cs:75-76` override on it; subclass overrides it to `true`). The factory boots the app wired to `ceres_app` (RLS-active) and exposes both `AppDbContext` (rules enforced) and `AdminDbContext` (rules bypassed) from the booted host. Reuses `AppContextFactory`/`AdminContextFactory`/`RlsTestFixture` patterns. Setup discipline (Section 3 trap): seed via admin, assert via app.

### 4.4 The architecture test (app-vs-admin context discipline)

New test scanning `ProjectCeres.Controllers`, `ProjectCeres.Controllers.Api`, `ProjectCeres.Services` (the real namespaces — roadmap's "Endpoints.*/Services.*" was shorthand). Any type injecting `AdminDbContext` must carry `[RequiresAdminContext]` (class or method). **Exact-type match on `AdminDbContext`**, never `IsAssignableFrom` (the subclass trap). Likely passes at zero markers today (the 7 current admin consumers live mostly in `Common.Authentication`, outside the scanned namespaces, plus `SeedDevUser`/`UserJobRunner` which are not Controllers/Services) — it is a **forward tripwire**, not a cleanup. The spec will record which of the 7, if any, fall inside the scan.

## 5. Testing (designed against the original failure mode)

1. **Meta-test (ship-gate).** Construct a scenario where a user-owned table is missing its RLS rule; assert the parity check **throws**. Proves the oracle works on the one input the old test got wrong (a missing entity).
2. **Trap tests:** `Transactions`/`Transfers`/`LiabilityPayments` present + `Movement` absent (TPC); attachments in RLS set but reconciled out of query-filter set; `FailedLoginAttempt`/`EmailDeliveryEvent` excluded.
3. **Real-account isolation test** (DualContext fixture): write a row as user A via admin, prove user B's app-side access can't see it. The live "the rule actually bites" check (D3 test oracle).
4. **Strictness test:** every user-owned table reports both `relrowsecurity` AND `relforcerowsecurity` true (D1).
5. **Architecture meta-test:** an undecorated `AdminDbContext` injection is flagged; a decorated one passes.
6. **Setup discipline:** all tests seed via admin, assert via app (the 42501 trap).
7. **No test weakened to pass** (`feedback_never_skip_tests_to_make_them_pass`): consumer-migration breakage is fixed in production code or by rewriting the assertion to what's now true — never by dropping an assertion.

## 6. Consumer migration + doc loose ends (same change)

The five readers of `UserOwnedTables.All`, all migrated in this change (D4):

| Reader | Action |
|---|---|
| `AppDbContext.ConfigureGlobalQueryFilters` (the filter loop) | Repoint to `UserOwnedModel` (query-filter view) |
| `ParityTests` | Rewrite to derive expectation from `UserOwnedModel.RlsTables` |
| `ArchitectureTests` query-filter test(s) (incl. the parallel ~20-type hard-coded list) | Repoint to the helper, OR keep one as a deliberate hand-cross-check with a comment (decided at impl time; either way no silent drift) |
| `SeedDevUser.cs:60-78` (its own 16-name list) | Repoint to `UserOwnedModel.FinanceTables` (D6) |
| `20260514011916_AddRowLevelSecurityPolicies.cs` (the historical migration) | Inline a frozen 24-name `string[]` (D5) |

Also: `RlsExceptionTranslator.cs` and `RlsPolicyViolationException.cs` reference `UserOwnedTables.All` — repoint to the helper.

Doc updates (same change):

- **Roadmap line 1094** — discharge the open "test-infra parity strategy" decision; record option (b) (capability now, full switch in 9.5d).
- **`docs/multi-tenancy-strategy.md`** — stale (lists 8 user-owned entities; claims attachments/auth tables don't get `UserId`). Update to the 24-table reality. Documented-decision overwrite → sync-docs supersession sweep applies.
- **`docs/security-model.md`** — note the user-owned set is now model-derived and boot-verified.

## 7. Out of scope (fences)

| Item | Where it goes |
|---|---|
| Full switch of all ~90 auth tests to `ceres_app` | Stage 9.5d (D7) |
| Pre-auth-write RLS audit (roadmap line 1095) | Its own `[ ]`, stays put |
| Any new user-owned entity / feature work | None in 9.5b |
| Code-fix providers / new analyzers | 9.5f/9.5g/9.5j |

## 8. Effort

Medium. The hard infrastructure already exists (dual-account contexts, the `ceres_app` role, a `pg_class` parity query, the startup-check precedent). 9.5b is mostly consolidation + promoting a check to startup. The single delicate piece is the migration frozen-list extraction (D5) — get it wrong and `dotnet ef` stops being reproducible.

## 9. Open questions

None remaining. All brainstorm decisions locked in §2.

## 10. Cross-references

- **Roadmap:** `docs/roadmap-phase-three.md` § Stage 9.5h sub-stage 9.5b (line 1254); open decision line 1094; § Stage 9.10 (line 1089).
- **Motivating bug:** `EmailConfirmationTokens` RLS gap — commit `9db67df` (the fix), `feedback_iuserowned_requires_five_registries` (the memory pin).
- **Predecessor ADRs:** ADR-0068 (Postgres RLS as Phase 3 defence-in-depth), ADR-0073 (RowLevelSecurityInterceptor / UserContext), ADR-0077 (analyzers — the `[RequiresAdminContext]` annotation's home).
- **Existing infrastructure reused:** `ProjectCeres.Tests/Integration/Infrastructure/AppContextFactory.cs` + `AdminContextFactory.cs`, `ProjectCeres.Tests/Integration/Rls/RlsTestFixture.cs` + `ParityTests.cs`, `ProjectCeres/Common/PrivilegeLeakStartupCheck.cs` (startup-check pattern), `ProjectCeres/Data/AdminDbContext.cs`.
- **Strategy pass:** ceres-architect / ceres-tech-lead / ceres-security-reviewer, 2026-05-31 (workflow `wf_897fedb0-a2b`).
- **Feedback memories:** `feedback_iuserowned_requires_five_registries`, `feedback_never_skip_tests_to_make_them_pass`, `feedback_clean_dead_code_immediately`.
