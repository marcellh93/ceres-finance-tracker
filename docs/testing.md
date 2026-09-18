# Project Ceres — Testing Strategy

> **Diataxis type:** Reference — defines the testing approach, TDD workflow, and CI/CD scope for all phases.

---

## Rules

Tests describe the behavior the production code must have. The production code is the thing under test; the test is not.

### How tests are written

- Write or update the test before or alongside the production change. Run it and confirm it fails for the right reason (the assertion, not a misconfiguration) before making it pass.
- Assert on specific, observable outputs: `result.Should().Be(100m)`, `account.Balance.Should().Be(expectedBalance)`, or `Assert.Equal(expected, actual)`. Do not use `Assert.True(result)`, `result.Should().NotBeNull()`, or "any error is fine" matchers as the sole assertion of a test.
- One reason to fail per test. Name the test after the behavior: `TransferService_WhenCurrenciesDiffer_ThrowsValidationException`.
- Mock at boundaries only: `DbContext` via `WebApplicationFactory` or (Phase 3) Testcontainers, `TimeProvider`, randomness, filesystem, outbound HTTP. Never mock the service, controller, or calculation whose behavior the test claims to verify.
- Integration tests run against the real `project_ceres_test` PostgreSQL database, not the EF Core in-memory provider. The in-memory provider does not translate SQL the way Npgsql does and hides query bugs that surface in production.

### When a test fails

State the failing test name and the exact assertion that failed. Then state which of these is true:

1. **The production code is wrong** → fix the production code. Do not touch the test.
2. **The test's expected value was wrong** → name the user-facing behavior the test should describe, update the test, then update any related production code so test and production agree.
3. **The contract intentionally changed** → name the contract change, update the test, update the production code, update any docs that reference the old contract.

If the answer is none of (1), (2), or (3), the test is not legitimately failing — stop and tell the user.

### Flaky tests

A test that fails intermittently is treated as a failing test until proven otherwise. Do not add retries, do not mark `[Fact(Skip="flaky")]` silently, do not rerun until green. State that it is flaky, propose a root cause, and wait for direction.

**Read [`docs/testing-flakiness.md`](testing-flakiness.md) before diagnosing or mitigating any flake** — it holds the root-cause taxonomy (async wait, isolation/order dependency, concurrency, resource leaks…), what mature teams do (detect → quarantine → fix → un-quarantine, with an SLA), a full audit of our existing mitigations, and the standing rule: **suppressing a flake without a root-cause ticket is prohibited, same as `[Fact(Skip=…)]`.** A filter or a retry is a quarantine with an owed fix, never a silent end-state.

**Two narrow, CI-scoped exceptions exist, each earned by a root-cause first** — neither is a licence to retry away a red test: the Vitest one-retry for a known isolation flake (§ Definition of Done), and the Playwright CI-only single retry (§ E2E, Stage 12.17). Both keep local runs at zero retries, cap the retry at one, and surface a retried pass as a flake rather than silent-green. Adding a new retry carve-out requires the same: a documented root cause proving the failure is environmental (not a real bug), CI-only scope, and a one-retry cap.

### The quarantine lifecycle (binding)

A flake moves through four states — **detect → quarantine → fix → un-quarantine** — and never skips to a silent end-state. This is what "suppressing a flake without a root-cause ticket is prohibited" means in practice.

1. **Detect.** A test fails, and the same test passes on an unchanged tree (a re-run, a different CI shard, a historical green). That is the signal. Confirm it is a flake, not a real intermittent bug, by reading the actual failure — and remember that some flakes fire **only** on the CI runner (slower, 2-core) and never locally, so *local green is not proof a flake is fixed* (see the 2026-09-18 `input-otp` case in [`testing-flakiness.md`](testing-flakiness.md) § 5).

   **The CI flaky signal (§12.19 item 5).** The CI-only retries (Playwright one, Vitest two) would otherwise green a retried pass silently — so a retry that succeeds is surfaced, by name, on the run it happened:
   - **Playwright** — `tools/e2e/report-flaky.mjs` parses the JSON report for `status: "flaky"` and writes a table to the run's **GitHub job summary** plus a `::warning::` annotation per test.
   - **Vitest** — `ProjectCeres.Client/vitest.flaky-reporter.ts` (active on CI only) flags any test whose result is `passed` but carries attached errors (Vitest's marker for passed-after-retry), to the same job summary + annotations.

   **The response rule (this is what makes the signal load-bearing).** A test that appears in the CI flaky table is never ignored:
   - **First sighting →** open a tracked `[ ]` in the active stage owing its root-cause. The retry *is* the quarantine mechanism (step 2), so the test is already non-fatal-but-visible; the owed-fix `[ ]` is the missing half.
   - **Recurrence →** root-cause it now (step 3). A test flaky across multiple runs is either a real environmental issue to fix structurally, or an early warning of a real regression that happens to pass part of the time — both demand action, not another green run.

   The per-run table does **not** aggregate frequency across runs; watching that trend is manual today. Automated cross-run counting is tracked as a Stage 16 follow-up (see [`roadmap-phase-three.md`](roadmap-phase-three.md) § 12.19).

2. **Quarantine — allowed ONLY with all four of these in the same change.** A quarantine makes the flake **non-fatal but still visible**; it is a waiting room, not a graveyard. To quarantine, you must ship together:
   - a **documented root-cause hypothesis** (what you believe is leaking/racing, and the evidence);
   - a **tracked `[ ]` line** in the active batch stage owing the real fix (a quarantine with no owed-fix checkbox is a silent suppression — forbidden, and the `suppress-without-research-gate` hook denies it);
   - an **expiry / scope** (which environment, which test, and by when it must be resolved) — the SLA below;
   - a mechanism that **keeps the test running and surfaces the outcome as a flake**, never as silent green. Acceptable mechanisms in this repo, in order of preference: the CI-only one-retry (Playwright/Vitest — a retried pass is reported as `flaky`); a narrow, commented `onUnhandledError` filter for a specific message. **Never**: `[Fact(Skip=…)]`, `it.skip`, a blanket retry, or deleting the assertion.

3. **Fix at the root**, then remove the quarantine mechanism in the same change (the filter line / the `[ ]`). Prefer a fix that makes the failure **structurally impossible** (a missing cleanup restored, a timer never armed, physical DB isolation) over one that races teardown or widens a timeout. Verify on the surface where the flake actually fires — if it is CI-only, the CI run is the gate, not a local pass.

4. **Un-quarantine.** Delete the retry/filter and confirm green on the real surface with the quarantine mechanism absent (so a clean run genuinely proves the fix, not the suppression).

**The SLA.** A quarantine is time-boxed: its owed-fix `[ ]` lives in the active stage and is resolved before that stage closes (Phase E blocks a stage-close with any unchecked `[ ]` under its heading). A quarantine that would outlive its stage is escalated to the user, not silently carried. A red build is never made green by adding a quarantine mechanism the same turn the flake is discovered *without* the four elements above — that is the exact "suppress and move on" reflex this stage (§12.19) exists to stop.

### IMPORTANT — prohibited shortcuts

Do not delete tests, do not add `[Fact(Skip="…")]`, do not comment out assertions, do not wrap failing calls in `try/catch` to silence them, and do not call `DbContext`, repositories, or services directly to set state that the feature under test was supposed to set. If a test cannot be made to pass without one of these, stop and tell the user.

### Definition of Done

Before saying "done," "ready," "complete," or "passing," run the tier-scoped pre-flight. The tier is picked by inspecting the working-tree diff (`git diff --name-only HEAD` + untracked tracked-extension files) — same scheme as `.claude/hooks/run-tests.sh` and the `/verify` slash command:

- **TIER 0** — no `.cs` / `.ts` / `.tsx` / `.csproj` / `.sln` touched (doc/config only): skip steps 1 and 2 entirely.
- **TIER F** — only `.ts` / `.tsx` under `ProjectCeres.Client/` touched: run the frontend pair in steps 1 and 2.
- **TIER B** — only `.cs` / `.csproj` under `ProjectCeres/` touched (no test-project, no `.sln`): run the backend pair in steps 1 and 2. **Exception — `ProjectCeres/Program.cs` is TIER M, not TIER B.** It is the composition root (middleware order, DI registration, environment branching); unit tests reach almost none of that, so the unit-only filter is no gate for it. Enforced in `tier-classify.py`; added 2026-08-29 after a `Program.cs`-only commit passed TIER B and left an integration test red on `main` (`dafee78c`, reverted in `19ff467a`).
- **TIER M** — mixed, OR `.sln` touched, OR `ProjectCeres.Tests/` edits, OR anything ambiguous: run both pairs.

Then:

1. **Build is clean** (no errors, no new warnings) for every command the tier required:
   - Frontend pair: `pnpm --dir ProjectCeres.Client build` — this runs `tsc -b`, which **type-checks test files too** (`.test.ts(x)` + `test-setup.ts` via the `tsconfig.test.json` project reference, added Stage 12.8.4). Before then, no command type-checked a test file, so a test asserting against a moved type stayed green. `pnpm --dir ProjectCeres.Client typecheck` runs the same `tsc -b` standalone.
   - Backend pair: `dotnet build ProjectCeres/ProjectCeres.csproj`
2. **Tests are all green** with zero skipped, unless every skip has an inline comment with a tracked issue link:
   - Frontend pair: `pnpm --dir ProjectCeres.Client test --run` (allow ONE retry on a known-isolation Vitest flake; if it fails twice, root-cause it)
   - Backend pair: `dotnet test` (no retries)
3. For each new branch in production code, name the test that exercises it. (Runs regardless of tier.)
4. If any test file was modified in this change, state which of cases (1), (2), or (3) above applied. (Runs regardless of tier.)

**Tier-up when in doubt.** Picking TIER M when uncertain is fine — picking a smaller tier than the diff warrants is not. The `/verify` slash command implements this check; reach for it instead of running the commands ad-hoc.

**IDE0001 code-style gate (Stage 9.1.6, 2026-06-15).** Redundant namespace prefixes are kept clean by `IDE0001` at `warning` severity. IDE0001 is a `dotnet format`/IDE-only analyzer — it does **not** surface at `dotnet build` even with `EnforceCodeStyleInBuild=true`, so the regression check is `dotnet format style --verify-no-changes --diagnostics IDE0001` (exit non-zero = a prefix crept back). No CI until Stage 16, so this is a manual/pre-commit gate, not a build gate.

---

## Stack

### Backend (.NET)

| Library                   | Role                                                                            |
| ------------------------- | ------------------------------------------------------------------------------- |
| **xUnit**                 | Test framework — standard for .NET, used by ASP.NET Core itself                 |
| **Moq**                   | Mocking library — isolates dependencies in unit tests                           |
| **FluentAssertions**      | Readable assertions — `result.Should().Be(100m)` over default xUnit assertions  |
| **WebApplicationFactory** | Integration test host for API controllers — boots full app in memory (Phase 2+) |

### Frontend (ProjectCeres.Client — Phase 2+)

| Library                   | Role                                                                          |
| ------------------------- | ----------------------------------------------------------------------------- |
| **Vitest**                | Test runner — shares Vite config, no separate setup. See ADR-0032.            |
| **React Testing Library** | Component-level tests — renders components and asserts on user-visible output |

Frontend tests live in `ProjectCeres.Client/src/__tests__/`. What gets tested:

- React components with non-trivial rendering logic (conditional display, derived state)
- Chart data transformation functions
- Import profile mapping logic (client-side validation)

Simple presentational components with no logic are not tested — the value is too low relative to the maintenance cost.

---

## Types of Tests

| Type        | What it tests                                                                                                       | Database? | Introduced     |
| ----------- | ------------------------------------------------------------------------------------------------------------------- | --------- | -------------- |
| Unit        | Individual calculation or business logic method in isolation                                                        | No        | Phase 1        |
| Integration | EF Core queries and service interactions against a real test database                                               | Yes       | Phase 1        |
| Analyzer    | Roslyn `DiagnosticAnalyzer` + source-generator behaviour via `Microsoft.CodeAnalysis.Testing`'s `Verifier<T>`       | No        | Phase 3 (9.5c) |
| E2E         | Full browser-driven flows                                                                                           | Yes       | Phase 3        |

E2E foundations shipped in Stage 9.11 (Playwright/TypeScript) — five auth golden-path suites running locally against a dedicated `project_ceres_e2e` database and the production-built SPA bundle. The fixture uses a real local PostgreSQL plus the EF migration runner (not Testcontainers). CI wiring is deferred to Stage 16.16 — there is no GitHub Actions workflow running the suite until then.

### Analyzer tests (Stage 9.5c, shipped 2026-05-27)

`ProjectCeres.Analyzers.Tests/` (xUnit, `net10.0`) verifies the Roslyn analyzers + source generator using the canonical `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` + `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` packages (the unsuffixed `DefaultVerifier` variant; framework-specific variants are deprecated per the Roslyn SDK). Per-analyzer fixtures cover positive case (diagnostic fires), negative case (diagnostic does not fire), and excluded-path cases (e.g. `CER004` does not fire in `ProjectCeres.Models` property initialisers, `CER001` does not fire on `PreAuthRlsScope.cs` itself). Stage 9.5c shipped the first five (`CER001`, `CER002`, `CER004`, `CER010`, `CER020`); Stage 9.5f (2026-06-09) added `CER005` (token-lookup discipline) with a 7-case matrix — 2 fires (missing `TokenLookup`; wrong-typed `TokenLookup`) + 5 exclusions (valid `byte[]` shape, non-`IUserOwned`, non-`*Token`, outside `Models`, enum), the location marker on the class identifier. Stage 9.5g (2026-06-09) added `CER006` (reverse-direction `[PreAuthScope]` marker — the mirror of `CER001`) with a 4-case matrix — 1 fires (unmarked class calls `BeginPreAuthUserScopeAsync`) + 3 exclusions (marked class, no-call, calls-other-method); the location marker sits on the **call expression** (CER006 reports at `invocation.GetLocation()`), unlike CER005's class-identifier marker. Stage 9.5i (2026-06-09) added `EmailKeysGenerator` (a code-emitting source generator, not a diagnostic) with a 4-case matrix using `CSharpSourceGeneratorTest<EmailKeysGenerator, DefaultVerifier>` (the same harness as CER020): emits the nested const class for a 3-part template; omits a const when the resx key is absent; emits a header-only shell on malformed XML; and — the user-facing payoff — a `TestCode` referencing a key the resx lacks produces a `CS0117` compile error (asserted via `ExpectedDiagnostics`). The pinned expected source is declared via `TestState.GeneratedSources.Add(...)` against a small synthetic resx, not the production 30-key file. Stage 9.5j (2026-06-10) added the first **code-fix providers** with a new `CSharpCodeFixVerifier<TAnalyzer, TCodeFix>` harness (wrapping `CSharpCodeFixTest<…, DefaultVerifier>` from `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing`), exposing `VerifyCodeFixAsync(source, fixedSource, …)` and `VerifyNoFixAsync(source, …)` (the latter sets `FixedCode == source` to assert NO fix is registered). Four cases: CER004's safe fix has three — fix applied when a `_timeProvider` field is present, fix applied when the field is a `TimeProvider` *subtype* (semantic base-type walk, not a string match), and **no fix offered when the field is absent** (the load-bearing negative assertion that pins the safety boundary); CER001's placeholder fix has one — the rewrite to `BeginPreAuthUserScopeAsync(userId, ct)` leaves three deliberate compiler errors (`CS1061` wrong-receiver + `CS0103`×2 undeclared `userId`/`ct`) pinned via `FixedState.ExpectedDiagnostics` with exact spans/args, proving the placeholder is intentional. CER010 ships no fix (a format-valid invented ticket would defeat the audit-trail rule), so it has no code-fix test.

Test source code is embedded as a string per test method with inline markup syntax: `[|...|]` for single-descriptor spans, `{|CER00X:...|}` for multi-descriptor spans. The `TestState.Sources.Add((path, content))` overload sets the file path so path-exclusion logic (`/Migrations/`, `PreAuthRlsScope.cs`) can be exercised.

Run via `dotnet test ProjectCeres.Analyzers.Tests/`. The Stop hook's tier system runs them as part of the standard `.NET` suite when `.cs` files in `ProjectCeres.Analyzers/` or `ProjectCeres.Analyzers.Tests/` change.

### E2E Tool: Playwright (TypeScript)

**Microsoft Playwright** is the chosen tool. Per [ADR-0071](decisions/ADR-0071-e2e-testing-on-playwright.md), the **TypeScript** variant ships — not the `Microsoft.Playwright` NuGet port. Tests live in `ProjectCeres.Client/e2e/` next to the React app, run via `pnpm --dir ProjectCeres.Client e2e`, exercise the production-built SPA served against a real `dotnet run` + PostgreSQL fixture.

> **Supersession (2026-05-13):** prior versions of this section pinned `Microsoft.Playwright` NuGet inside the xUnit project. That choice predated the completion of the SPA migration. ADR-0071 reopened the question, audited feature parity between the .NET and TS variants, and confirmed that the .NET port lacks UI Mode (time-travel debugging, watch mode, locator picker), the `webServer` config block (auto-boot dev server before tests), and is the slower-moving target for new features. The TS variant also shares types with the SPA DTOs, which is the regression risk the E2E suite exists to catch.

Rationale:

- TypeScript-first — tests live next to the SPA in `ProjectCeres.Client/`. DTO types imported from `src/` keep API-contract regressions inside the test surface, not at runtime.
- Native test runner with `playwright.config.ts` — `webServer` runs `tools/e2e/run-server.sh`, which builds the SPA, stages it into `ProjectCeres/wwwroot/dist`, and boots `dotnet run` under `ASPNETCORE_ENVIRONMENT=E2E` serving the production bundle (no Vite dev/preview server involved); `projects` matrix runs Chromium / Firefox / WebKit, `fullyParallel` + `sharding` work first-party.
- UI Mode (`pnpm playwright test --ui`) — time-travel scrubbing, watch mode, locator picker, DOM inspector. The single biggest DX win over the .NET variant.
- Cross-origin and cookie semantics — Phase 3 uses cookie auth (`SameSite=Lax` + CSRF token per [ADR-0063](decisions/ADR-0063-cookie-samesite-lax-with-csrf-tokens.md)). Playwright's BrowserContext model handles these without workaround.
- Trace files attach to failed CI runs — debugging across SPA + API + DB boundary is tractable from the GitHub Actions log alone.
- Official GitHub Actions integration — `npx playwright install --with-deps` is the well-trod path; sharding across runner instances is first-party. Pairs cleanly with [ADR-0070](decisions/ADR-0070-ci-cd-on-github-actions.md).

**.NET state.** The unit (Vitest) + integration (xUnit) split is by **what's tested**, not by host language. E2E tests drive the browser-on-SPA surface, which is TypeScript-native — the .NET server is exercised by the running app, but no `.cs` type is imported by the test. xUnit stays the integration-suite home; nothing in `ProjectCeres.Tests` changes.

### What Gets E2E Tested (Phase 3+)

Focus on critical user-facing flows that cross the full stack and are not covered by unit or integration tests:

> **Stage 9.11 shipped the AUTH golden paths** — login + TOTP, register + email-verify, password-reset, lockout self-service unlock, and backup-code recovery. The financial-flow bullets below are aspirational for subsequent stages.

> **Visual/styling verification must render the manifest-mode build, never the Vite dev server.** The dev server injects CSS through JavaScript, so a missing production stylesheet `<link>` looks fine in dev and breaks only in the staged bundle. The E2E `webServer` serves the production bundle, which is the only path that exercises the real asset wiring. `e2e/auth/spa-stylesheet.spec.ts` pins this for the SPA host (added at the Stage 9 close-out after the dev server had masked a whole-SPA unstyled-in-production bug — see `roadmap-phase-three.md` § Stage 9).

- Login and TOTP authentication flow
- Account creation and opening balance
- Recording a transaction and verifying it appears in the account balance
- Transfer between accounts
- Budget progress reflected after a transaction is recorded

### Running E2E locally

**Prerequisites:**

- Local PostgreSQL running with the three dev roles (same as the integration suite).
- Browser binaries installed once via `pnpm --dir ProjectCeres.Client exec playwright install firefox webkit` (chromium ships with `@playwright/test`).
- No manual DB setup — the wrapper `tools/e2e/run-server.sh` auto-creates, migrates, and wipes `project_ceres_e2e`.

**Commands:**

- `pnpm --dir ProjectCeres.Client e2e` — all five auth suites × chromium / firefox / webkit (golden config; boots the app automatically via the `webServer`).
- `pnpm --dir ProjectCeres.Client exec playwright test --config e2e/playwright.golden.config.ts --project=chromium` — fast single-browser loop.
- `pnpm --dir ProjectCeres.Client e2e:ui` — Playwright UI mode (time-travel debugging).
- `pnpm --dir ProjectCeres.Client e2e:walk` — the agent-walk route-smoke harness (separate config; needs `tools/agent-env/up.sh` to export `APP_URL`).

**Artifacts:** traces + screenshots (retain-on-failure) + HTML report under `ProjectCeres.Client/e2e/.artifacts/`; captured emails (verify / reset / unlock links) as JSON under repo-root `.e2e/emails/`.

**`retries: process.env.CI ? 1 : 0`** (Stage 12.17) — local runs keep zero retries (a flake is a failure until root-caused, per § Flaky tests); CI gets exactly one. The `ubuntu-latest` runner is CPU-starved relative to a dev machine, and the slowest browser (webkit) can slip a single sub-timeout on an otherwise-correct test — a 60s webkit `locator.click` timeout on the support-page reply test was root-caused to CI load, not a race (the Send button mounts only after the thread load resolves and takes no async disable gate, so the click has no node-swap to lose). Playwright reports a retried pass as `flaky`, not silent-green, so a degrading test stays visible and a genuinely broken one still fails both attempts.

**When it runs:** on demand + at auth-touching stage close-outs. Nothing runs it automatically yet — the Stop hook is tier-0 for `.ts`-only writes and the DoD tiers don't invoke Playwright; CI wiring lands in Stage 16.16.

---

## TDD Workflow — Required

All new service methods and business logic must follow the Red → Green → Refactor cycle:

1. **Red** — write a failing test that describes the expected behavior. The test must fail because the implementation does not exist yet, not because it is misconfigured.
2. **Green** — write the minimum implementation needed to make the test pass. No more.
3. **Refactor** — clean up the implementation without breaking the test.

The test commit must precede or accompany the implementation commit. Shipping implementation without a test is not acceptable.

### What triggers a test

- Every new service method
- Every business rule (validation, sign convention, deletion rule)
- Every financial calculation
- Every integration point with EF Core that involves a join, aggregation, or multi-step write

### What does not need a test

- Controllers — they are thin HTTP handlers with no logic
- ViewModels — plain data containers
- Migrations — correctness is verified by the integration test database

---

## What Gets Unit Tested

Priority order:

1. **Financial calculations** — net worth, account balance, savings rate, currency-scoped totals
2. **Business rules** — transfer currency match, amount positivity, deletion guards, category type enforcement
3. **Report query logic** — filters, date ranges, category and account scoping
4. **Formatting and parsing helpers** — `NumberFormatHelper` formatting and `TryParseDecimal` parsing logic

Unit tests use Moq to mock `DbContext` dependencies where needed. See [xunit-basics.md](guide/07-testing/xunit-basics.md) and [test-structure-and-patterns.md](guide/07-testing/test-structure-and-patterns.md).

### Phase 1 Unit Test Coverage (as of 2026-04-21)

| Class                             | Test file                          | What it covers                                                                    |
| --------------------------------- | ---------------------------------- | --------------------------------------------------------------------------------- |
| `BalanceCalculationTests`         | `Unit/BalanceCalculationTests.cs`  | Asset and liability balance derivation formula                                    |
| `CategoryBudgetGuardTests`        | `Unit/CategoryBudgetGuardTests.cs` | CategoryBudget expense-only enforcement                                           |
| `SavingsRateTests`                | `Unit/SavingsRateTests.cs`         | Savings rate calculation                                                          |
| `NumberFormatHelper`              | `Unit/NumberFormatHelperTests.cs`  | `FormatAmount`, `FormatInputValue`, `TryParseDecimal`, and round-trip correctness |
| `DecimalModelBinder` (via helper) | `Unit/DecimalParsingTests.cs`      | Parsing in both number format modes including invariant-input tolerance           |

---

## What Gets Integration Tested

- EF Core queries that involve joins or aggregations
- Multi-step writes that require a database transaction (e.g. account creation with opening balance)
- Service interactions where one service depends on another (e.g. `LiabilityPaymentService` → `AccountService`)
- Phase 3: multi-user data scoping — tests that confirm one user cannot access another user's data

### Phase 1 Service Coverage (as of 2026-04-18)

All Phase 1 services now have integration test coverage:

| Service                       | Test file                             |
| ----------------------------- | ------------------------------------- |
| `AccountService`              | `AccountServiceTests.cs`              |
| `TransactionService`          | `TransactionServiceTests.cs`          |
| `TransferService`             | `TransferValidationTests.cs`          |
| `CategoryService`             | `CategoryServiceTests.cs`             |
| `LiabilityPaymentService`     | `LiabilityPaymentServiceTests.cs`     |
| `BudgetService`               | `BudgetServiceTests.cs`               |
| `RecurringTransactionService` | `RecurringTransactionServiceTests.cs` |
| `ReportService`               | `ReportServiceTests.cs`               |
| `DashboardService`            | `DashboardServiceTests.cs`            |
| `SettingsService`             | `SettingsServiceTests.cs`             |
| `FileAttachmentService`       | `FileAttachmentServiceTests.cs`       |

### Phase 3 Multi-Tenancy Coverage (Stage 7, shipped 2026-05-12)

Stage 7 Commit 1 added a dedicated coverage block under `ProjectCeres.Tests/Integration/MultiTenancy/` that pins the multi-tenancy safety net. **These tests are the Stage 7 acceptance gate** — Phase 3 cannot open to invited beta users without them green.

| Test file | What it pins |
| --------- | ------------ |
| `Common/UserScopeTests.cs` | `IUserScope` AsyncLocal stack semantics: set/restore, nesting, propagation across `await` |
| `Common/UserJobRunnerTests.cs` | `IUserJobRunner.ForEachUserAsync` per-user iteration, exception isolation, scope unwinds on throw |
| `Common/CurrentUserAccessorResolutionTests.cs` | `HttpContextCurrentUserAccessor` resolution order: HTTP claim → `IUserScope.Current` → `Guid.Empty` safe default |
| `Common/IUserOwnedConformanceTests.cs` | 9 auth-internal entities implement `IUserOwned` (incl. `EmailConfirmationToken`, added 9.5b); `FailedLoginAttempt` does NOT |
| `Common/CategoriesDefaultsTests.cs` | Canonical 26-entry default-category list shape |
| `Integration/Authentication/ArchitectureTests § IgnoreQueryFilters_only_appears_in_documented_exception_paths` | Scans `ProjectCeres/**/*.cs`; fails the build if any non-comment `IgnoreQueryFilters(` call site appears outside the allow-list (9 enumerated files + the future `ProjectCeres/Admin/` namespace) |
| `Integration/Authentication/ArchitectureTests § Every_user_owned_entity_carries_a_global_query_filter` | All 22 expected entities carry a declared global query filter (Movement as TPC root + 11 finance-domain + 10 auth/scopable) |
| `Integration/Authentication/ArchitectureTests § FailedLoginAttempt_has_no_global_query_filter` | Cross-tenant retention sweep is unblocked |
| `Integration/Authentication/RegistrationSeedsCategoriesTests.cs` | Every new user gets 26 per-user categories with independent GUIDs; second seed call is idempotent |
| `Integration/MultiTenancy/GlobalQueryFilterTests.cs` | A query against `Accounts` run inside `IUserScope.EnterAs(userA)` returns only A's rows, never B's |
| `Integration/MultiTenancy/IdorIsolationTests.cs` | **The IDOR acceptance suite.** 15 cross-tenant assertions: 9 GET-by-id endpoints return **404 (not 403, not 200)**, 2 mutation endpoints return 404, 3 list endpoints exclude the other user's rows, plus the **negative-assertion test** (`Global_query_filter_alone_catches_leak_without_service_Owned`) that proves the EF global query filter catches a leak independently of any service-layer `.Owned()` discipline. The negative assertion is the load-bearing safety-net test: it issues a raw `db.Accounts.ToListAsync()` under User B's `IUserScope` and asserts User A's seeded account is invisible. |
| `Integration/Startup/EmptyDbStartupTests.cs` | The app serves `GET /api/health` after wiping user-owned business data (catches per-request hooks that crash on empty Settings/Accounts; documented limitation: WAF host boots once with the collection fixture so this does not catch true cold-boot `IHostedService.StartAsync` regressions — fresh-factory test is deferred) |
| `Integration/Authentication/CurrentUserAccessorResolutionTests § Returns_GuidEmpty_when_neither_resolves` | Pins the Stage 7 Task 9 amendment to ADR-0067 (accessor returns `Guid.Empty` rather than throwing when neither HTTP context nor `IUserScope` resolves) |

The 404-not-403 discipline matters: returning 403 leaks the row's existence to the requester, defeating the multi-tenancy isolation. Every cross-tenant assertion must be `HttpStatusCode.NotFound`. The IDOR suite's helpers (`GetAsB`, `DeleteAsB`, `PatchAsB`) attach User B's session cookie + CSRF token per-request so the same test can swap users without context bleed.

### Test-infrastructure RLS parity (Stage 9.5b, decided 2026-06-02)

`WafCollection.cs` defaults `ConnectionStrings:ApplicationConnection` to `ceres_admin` (BYPASSRLS) so legacy integration tests — which resolve `AppDbContext` from DI and do cross-user cleanup — keep working after Postgres RLS turned on. That default is a known coverage gap: under it the Stage 7.5 RLS policies are silently inert, which is how the `EmailConfirmationTokens` gap shipped in Stage 9.3 (no test could observe a missing policy).

The decision (roadmap § Stage 9.10, option (b)) is to **add a real-account capability rather than flip the global default**:

- The base factory exposes a `UseAppRoleConnection` virtual (default `false` — legacy routing unchanged). `DualContextWebApplicationFactory` overrides it to `true`, booting the app through the production DI graph wired to `ceres_app` (RLS-active). It exposes `NewAppContext(Guid actingAs)` (RLS enforced; the acting user's GUC is bound via `IUserScope` before the context resolves) and `NewAdminContext()` (BYPASSRLS, for cross-user seeding).
- **Setup discipline:** seed via `NewAdminContext`, assert via `NewAppContext(actingAs)`. Under `ceres_app`, an insert without an established user context is rejected (`42501`) and a read returns zero rows — so seeding must go through the admin context.
- `RlsParityStartupCheck` fail-closes at boot (model-derived user-owned set vs live `pg_class`), and `RlsParityMetaTests` proves the check throws on a missing policy. `RlsTestFixture` (hand-built `ceres_app` contexts) remains for the direct-wall tests under `Integration/Rls/`.
- The **full** auth-suite switch onto the app role is **Stage 9.5d** (`ProjectCeres.Tests.Integration.AppRole`). Until then, do not flip the `WafCollection` default — new RLS-sensitive tests opt in via `DualContextWebApplicationFactory`.

### AppRole suite (Stage 9.5d, shipped 2026-06-07)

`ProjectCeres.Tests/Integration/AppRole/` is a curated suite that runs the auth write-flows under the restricted `ceres_app` role (RLS active), proving the wall holds for those writes — the coverage the admin-context suite structurally cannot give. It is a **collection** (`[CollectionDefinition("AppRoleTests")]`), not a separate `.csproj`, reusing `DualContextWebApplicationFactory` (9.5b).

- **Canonical test shape:** seed via `NewAdminContext()` (BYPASSRLS), drive the flow over HTTP via `CreateClient()` (the request pipeline writes as `ceres_app`), assert via `NewAppContext(actingAs)` with BOTH a positive control (owner sees the row) and a negative control (a different user sees zero). Cleanup always via `NewAdminContext()` — cross-user deletes 42501 under `ceres_app`. `AppRoleTestBase` factors this; `AssertRlsVisibility<T>` is the positive/negative helper — **but see the caveat below before using it on a filtered entity.**
- **⚠️ `AssertRlsVisibility<T>` does not observe RLS for `IUserOwned` entities (found 2026-08-29, Stage 12.8; tracked as roadmap § 12.8.2).** It issues `Set<T>().CountAsync(predicate)` with **no `IgnoreQueryFilters()`**. Every entity RLS protects is `IUserOwned`, and those carry a global query filter — so EF excludes the foreign row *before the query reaches Postgres*, the negative control returns zero from the EF layer, and the database wall is never exercised. Demonstrated: a test written against the helper still passed with `ALTER TABLE "EmailChangeTokens" DISABLE ROW LEVEL SECURITY`. **Until § 12.8.2 lands, assert inline with `IgnoreQueryFilters()` on both halves** (`IgnoreQueryFilters` strips EF's filter only — it does not bypass RLS), and verify your test FAILS with the table's policy disabled. A negative control that passes with RLS off is not testing RLS. `EmailChangePendingUnderRlsTests` is the worked example. This is the § 9.5d hazard — "RLS policies are *silently inert*" in the admin-wired suite — partially reintroduced inside the suite built to escape it.
- **D1 fail-fast guard:** the `AppRoleFixture` collection fixture asserts `SELECT rolbypassrls FROM pg_roles WHERE rolname='ceres_app'` is false on `InitializeAsync` and throws (refusing the whole collection) if not — so the suite can't pass falsely against a misconfigured role.
- **Curated flows (11 tests):** register, login (×2: persists + the mechanism pin), logout, MFA enroll, password-reset confirm, email-confirmation verify, email-change (confirm + revoke), lockout-unlock. RLS-orthogonal tests (timing, rate-limit, CSRF-shape) are deliberately not re-run.
- **Three production gaps caught + fixed in-stage:** `EmailChangeService.ConfirmAsync`/`RevokeAsync` and `LockoutUnlockService.ConfirmAsync` looked their token up via the `ceres_app` `_db` with `IgnoreQueryFilters` on `[PreAuthCallSite]` endpoints (GUC reset → 0 rows → 401 for every user under the restricted role); fixed to the proven admin-lookup-then-`BeginPreAuthUserScopeAsync` pattern (mirrors `PasswordResetService`/`EmailConfirmationService`). Login's session-write was investigated and proven SAFE (a false positive — `SignInManager` populates `HttpContext.User` mid-request, so the GUC resolves). A completeness audit confirmed every `[PreAuthCallSite]` token-consume path is now safe — the gap class is fully closed.
- **`DisableParallelization = true`:** the collection is serialized (matching `RateLimitTests`/`MfaRateLimitTests`). These RLS-correctness tests don't need parallelism, and their concurrent load destabilizes the timing-sensitive rate-limit tests under full-suite contention.
- **Stop-hook tier:** rides the existing Tier 2 (full suite) — the AppRole files are `.cs` under `ProjectCeres.Tests/`, no tier-machinery change.

### Reviewer pipeline + Condition E3 (Stage 9.5e, shipped 2026-06-08)

Two enforcement mechanisms guarding auth / migration / `IUserOwned` diffs.

- **Condition E3 — migration-drift ship-gate.** `ProjectCeres.Tests/Unit/MigrationDriftTests.cs` asserts `DbContext.Database.HasPendingModelChanges()` is `false` — i.e. an entity-shape change must ship its migration in the same commit. It is a **Unit** test (the model + snapshot are built from the assembly; a placeholder `UseNpgsql("Host=localhost")` connection string never opens a connection — same pattern as `UserOwnedModelTests.Ctx()`). Deterministic, runs every suite run; a model/snapshot drift fails the build regardless of how the change was authored. This is the "syntactically locatable, fires every time" case that belongs in a test, not in a reviewer (per the L5 staging-ground rule).
- **3-agent reviewer pipeline (orchestrator-driven).** On a committed diff touching `ProjectCeres/Common/Authentication/**`, `ProjectCeres/Migrations/**`, or `ProjectCeres/Models/**`, the main session dispatches three review roles (`reviewer-writer` / `reviewer-security` / `reviewer-playwright-test-audit`, see `docs/agents.md`) and serializes their verdicts to `.claude/state/evidence/stage-<id>/reviewer-pipeline.json`. The **turn-end evidence-bundle hook** requires that slot for those diffs (a new `SLOT_TABLE` entry + `validateReviewerPipeline` content-check: three roles present, `diff_sha` matches HEAD, no surviving `verdict: "block"`). Hooks are read-only and cannot run agents — the hook checks the proof artifact; the orchestrator produces it. No new Stop hook (Trip-wire C — Stop-event hooks ≤10 — stays green). Bypass: `CERES_SKIP_REVIEWER_PIPELINE=1`. Spec: `docs/superpowers/specs/2026-06-08-stage-9-5e-reviewer-pipeline-design.md`.

### Phase 2 Import Coverage

> **Beta status (ADR-0078, 2026-06-29):** the import and review integration suites below run only in environments where the feature is enabled (Development/E2E/Testing). In beta/Production the API endpoints are fenced (404), so these suites are not exercised there. A dedicated `ShelvedEndpointFencingTests` asserts that the fenced endpoints (`/api/import*`, `/api/reconciliation-review*`, `/api/transfer-review*`) return 404 in the beta environment.

| Service / Class                                | Test file                                                                              |
| ---------------------------------------------- | -------------------------------------------------------------------------------------- |
| `CsvImportParser`                              | `Unit/CsvImportParserTests.cs`                                                         |
| `ExcelImportParser`                            | `Unit/ExcelImportParserTests.cs`                                                       |
| `ImportParserFactory`                          | `Unit/ImportParserFactoryTests.cs`                                                     |
| `ImportService`                                | `Unit/ImportServiceTests.cs` (unit), `Integration/ImportServiceTests.cs` (integration) |
| `ImportProfileService`                         | `Integration/ImportProfileServiceTests.cs`                                             |
| `HeaderDetectionService`                       | `Unit/HeaderDetectionServiceTests.cs`                                                  |
| Import headers API (`ImportHeadersController`) | `Integration/ImportApiTests.cs` (via `WebApplicationFactory`)                          |
| Endpoint fence (beta/Production) | `Integration/ShelvedEndpointFencingTests.cs` — asserts 404 for all fenced import + review routes |

Razor controller actions remain untested — they are thin HTTP handlers with a defined end-of-life in Phase 3; testing them is not worth the investment. API controllers in `Controllers/Api/` are integration tested using `WebApplicationFactory<Program>` (see ADR-0037).

### Integration Test Database

- Dedicated local database: `project_ceres_test`
- EF Core applies migrations at the start of each test run via `_db.Database.MigrateAsync()` (idempotent)
- Each test class wraps its writes in a transaction rolled back after the test — no data persists between tests
- Connection string stored in `appsettings.Test.json` (gitignored) or via `dotnet user-secrets`

> **Testcontainers** deferred to Phase 3 when CI/CD pipelines are introduced.

### Integration test collections — DB-per-bucket parallelism (Stage 12.18)

Integration test classes are split across **four runtime-balanced bucket collections** — `IntegrationParallel1..4` — plus a handful of purpose-specific serial collections. This superseded the single serialized `IntegrationTests` collection in Stage 12.18 (2026-09-11) to run the suite in parallel; see roadmap-phase-three.md § Stage 12.18 and the spec at `docs/superpowers/specs/2026-09-10-stage-12-18-parallel-integration-tests-design.md`.

**How isolation works.** Each bucket is pinned to its own cloned Postgres database (`project_ceres_test_1..4`); the five serial collections get their own (`_ratelimit`, `_mfaratelimit`, `_approle`, `_rls`, `_txfixture`). A distinct database name = a distinct Npgsql connection pool, so the connection-scoped RLS GUC (`app.current_user_ref` via `SET LOCAL`) and any unique-constraint / shared-read races cannot cross buckets. Transaction isolation levels do **not** solve those races — physical DB separation does.

**The mechanism (what to do when adding a test):**

- A bucketed class carries `[Collection("IntegrationParallelK")]` and inherits `IntegrationTestBase<BucketKFactory>` or `IntegrationTestBase<BucketKAuthFactory>`, taking `(BucketKFactory factory, BucketKDatabase db)` in its constructor (K = the bucket number, 1–4). Example:
  ```csharp
  [Collection("IntegrationParallel3")]
  public class SettingsApiTests(Bucket3Factory factory, Bucket3Database db)
      : IntegrationTestBase<Bucket3Factory>(factory, db)
  {
      private readonly HttpClient _client = factory.CreateClient(); // builds against project_ceres_test_3
  }
  ```
- **Use the bucket-pinned factory subclass, never the bare `TestWebApplicationFactory` / `AuthTestWebApplicationFactory` inside a bucket.** `Bucket{K}Factory.InitDbName` already resolves to the bucket DB, so the host builds against the right database even though a field initializer (`= factory.CreateClient()`) runs *before* the base constructor in C#. Injecting the bare base type would build against the legacy DB and defeat isolation.
- An **ad-hoc factory** a test constructs itself (`new SomeThrowingFactory()`) must derive from the plain base factory and override `InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallelK")` to match its class's collection.
- **Serial/role-specific collections** (`RlsTests`, `RateLimitTests`, `MfaRateLimitTests`, `AppRoleTests`, `TestDbFixtureTests`) still exist for classes needing a different factory/role wiring or the transaction-rollback `TestDbFixture`. A class using `TestDbFixture` belongs in `TestDbFixtureTests` (it targets `_txfixture`), not a parallel bucket.

**Local vs CI.** `xunit.runner.json` keeps `parallelizeTestCollections: false` by default, and `TestDatabaseRouter.CloneCount` falls back to `1` when `CERES_TEST_DB_CLONES` is unset — so a plain local `dotnet test` collapses every bucket onto the single legacy `project_ceres_test` DB and runs serially (race-free, no clone provisioning needed). Parallelism is opt-in: provision clones with `tools/ci/setup-test-db.sh --template --clones 4`, then run `dotnet test -- xUnit.ParallelizeTestCollections=true xUnit.MaxParallelThreads=4` with `CERES_TEST_DB_CLONES=4`. CI does exactly this. Each bucket carries `DisableParallelization = true` so its own classes serialize (safe pool warm-up), while the four buckets run concurrently against each other.

The real cross-class safety net remains **per-test data isolation by marker** (per-test GUID-suffixed emails / UserIds; see `feedback_filter_test_queries_by_test_data`) — the DB-per-bucket split adds physical isolation on top of it, not instead of it.

---

## BDD (Reqnroll) — Stage 12.15

**What Reqnroll is.** [Reqnroll](https://reqnroll.net) is the actively maintained successor to SpecFlow (SpecFlow itself is end-of-life). It runs Gherkin `.feature` files as executable specs on top of .NET 10, using `Reqnroll.xUnit` as the test-runner binding — so scenarios show up as ordinary xUnit tests to `dotnet test`, CI, and the Stop hook's tiering. `Reqnroll.Microsoft.Extensions.DependencyInjection` wires `[ScenarioDependencies]` so each scenario gets its own DI container, matching how the integration suite already scopes state per test.

**Where things live**, all under `ProjectCeres.Specs/`:

| Folder | Contents |
|---|---|
| `Features/*.feature` | Gherkin scenarios (Given/When/Then). Reqnroll's MSBuild generator emits a matching `*.feature.cs` next to each one — generated, do not hand-edit. |
| `Steps/*.cs` | `[Binding]` classes with `[Given]`/`[When]`/`[Then]` methods that implement the Gherkin steps by calling real HTTP endpoints through `WebApplicationFactory`, exactly like an integration test. |
| `Support/*.cs` | Reqnroll wiring — hooks and the `[ScenarioDependencies]` DI registration. |

**The harness-reuse pattern.** `ProjectCeres.Specs` takes a `ProjectReference` on `ProjectCeres.Tests`, so step definitions reuse the same `AuthTestWebApplicationFactory`, `AuthTestFixture` helpers (`RegisterUserAsync`, `LoginViaHttpAsync`, `MintCsrf`), and CSRF/session plumbing the API integration tests already use — no parallel test infrastructure. `Support/SpecsHooks.cs` defines:

```csharp
public sealed class SpecsAuthFactory : AuthTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("SpecsTests");
}

public static class SpecsDependencies
{
    [ScenarioDependencies]
    public static IServiceCollection Register()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SpecsAuthFactory>();
        return services;
    }
}
```

`SpecsAuthFactory` pins the BDD suite to its own serial collection database (`project_ceres_test_specs` under `--clones`; the legacy single DB under the N=1 local fallback) via `TestDatabaseRouter`, the same router the parallel integration buckets use (§ Integration test collections above) — so BDD scenarios get real auth (`UseTestAuthHandler=false`) without colliding with the parallel buckets or the other serial collections. `SERIAL_DB_SUFFIXES` in `tools/ci/setup-test-db.sh` includes `specs`, so `--template --clones N` provisions its clone alongside the other five.

**How to add a feature:**

1. Add a `.feature` file under `Features/` describing the behavior in Given/When/Then.
2. Run a build (or `dotnet build ProjectCeres.Specs`) so Reqnroll's generator emits the matching `.feature.cs`.
3. Add a `[Binding]` step class under `Steps/` implementing any steps that don't already exist, driving real endpoints the way `SupportTicketSteps.cs` drives `/api/support/tickets*` — inject `SpecsAuthFactory`, register/login a user via `AuthTestFixture`, and mark rows with a per-scenario GUID so assertions never see another scenario's data on the shared serial DB.
4. `dotnet test ProjectCeres.Specs` locally (falls back to the legacy DB when `CERES_TEST_DB_CLONES` is unset, same as the integration suite).

**xUnit-version coupling.** `ProjectCeres.Specs` depends on `Reqnroll.xUnit`, which requires `xunit` ≥ 2.8.1. Because `ProjectCeres.Specs` project-references `ProjectCeres.Tests`, both projects must resolve to a mutually compatible xUnit version — this is why `ProjectCeres.Tests` was bumped from its prior pin to `xunit 2.9.3` / `xunit.runner.visualstudio 2.8.2` in the same stage. Bumping xUnit again in `ProjectCeres.Tests` without checking `ProjectCeres.Specs`'s constraint (or vice versa) risks a version conflict that only shows up as a restore/build failure, not a test failure — verify both projects build after any future xUnit version change.

---

## CI/CD — Phase 3 Scope

> **Superseded 2026-09-10 for the CI shape:** the actual pipeline is documented in § Continuous Integration (Stage 12.13) below. The Testcontainers / single-job / PR-gating description here predates it and is retained only for its CD / future-hosting notes.

CI and CD are not active in Phase 1 or 2. This is an intentional deferral — the app runs locally for a single user, so automated pipelines add overhead with no benefit at this stage.

**Phase 3 state:**

- **CI service** — **GitHub Actions** per [ADR-0070](decisions/ADR-0070-ci-cd-on-github-actions.md). PR-gating workflow at `.github/workflows/ci.yml` runs `dotnet build` + `dotnet test` + `pnpm tsc --noEmit` + `pnpm build` + `pnpm test` against a `services.postgres` Postgres 16 instance.
- **CD strategy** — **GitHub Actions environments** + **OIDC** per [ADR-0070](decisions/ADR-0070-ci-cd-on-github-actions.md). Migration runs `dotnet ef database update` gated on green CI for the same commit. Pre-migration DB snapshot taken on every run, retained ≥7 days. Deploy target itself scoped to the still-open hosting-platform decision.
- **Production migration strategy** — deferred to **Stage 15** (pre-launch hardening); choice depends on hosting platform.
- **Testcontainers** — replaces the local `project_ceres_test` database in CI where a real PostgreSQL instance is not available. The `services.postgres` Postgres 16 image is the GitHub-Actions-native equivalent and is the default.

---

## Continuous Integration (Stage 12.13)

CI runs on GitHub Actions (`.github/workflows/ci.yml`) on every push to `main`
(plus a manual `workflow_dispatch` button; the `pull_request` trigger is dormant
because the project commits straight to `main`).

**CI is additive to the local Stop-hook gate, not a replacement.** The Stop hook
(`.claude/hooks/run-tests.sh`) is the fast, *tiered* inner-loop gate — it skips
(tier 0), runs unit-only (tier 1), or the full suite (tier 2) based on what a turn
changed, on the author's machine. CI is the *un-tiered, fresh-environment superset*:
it always runs everything, against a clean checkout with zero local state, and it
runs the things the Stop hook never does (E2E across three browsers, dependency +
secret scanning). The two cover each other's blind spots — the Stop hook keeps the
inner loop fast; CI proves it works somewhere other than the author's Mac (exactly
the class of bug Stage 12.12 was: a suite silently depending on a personal secrets file).

**No drift:** each CI job runs the same command the Stop hook / evidence build-matrix
already run. DB provisioning is shared via `tools/ci/setup-test-db.sh` (also used by
`tools/e2e/run-server.sh`), so "CI passes" ≡ "the suite passes on a clean machine."

**Jobs** (five, parallel, fail-fast off — one push surfaces all failures):
| Job | Runs |
|---|---|
| `dotnet-test` | full `dotnet test` (no filter) against a Postgres 16 service, **parallel-with-clones** (Stage 12.18): provisions `setup-test-db.sh --template --clones 4` then runs with `CERES_TEST_DB_CLONES=4 -- xUnit.ParallelizeTestCollections=true xUnit.MaxParallelThreads=4`; then a serial **BDD specs (Reqnroll)** step runs `ProjectCeres.Specs` against its own `project_ceres_test_specs` clone (Stage 12.15) |
| `analyzer-test` | the Roslyn analyzer suite |
| `client-test` | `pnpm build` (tsc + vite + size budget) + Vitest |
| `e2e` | Playwright sharded over chromium/firefox/webkit; traces on failure |
| `repo-hygiene` | `dotnet list package --vulnerable` + client `pnpm audit` + gitleaks + Node hook tests (`scripts/test-hooks.sh`) + roadmap consistency |

**Flaky surfacing (§12.19 item 5):** the `client-test` and `e2e` jobs allow a
CI-only retry (Vitest ×2, Playwright ×1). A retry that turns red green is not silent —
each retried-then-passed test is written by name to the run's GitHub job summary plus a
`::warning::` annotation (`vitest.flaky-reporter.ts`, `tools/e2e/report-flaky.mjs`). What
to do when a test appears there is the response rule in § The quarantine lifecycle step 1
(track it on first sighting, root-cause on recurrence). Cross-run frequency aggregation is
a Stage 16.18 follow-up.

**Secrets:** none real — fixed test-only values (the `*_dev_password` role passwords
in `scripts/setup-postgres-roles.sql` and a fixed test token-lookup secret). Real
production secrets arrive at actual Stage 16 hosting.
