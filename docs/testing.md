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

### IMPORTANT — prohibited shortcuts

Do not delete tests, do not add `[Fact(Skip="…")]`, do not comment out assertions, do not wrap failing calls in `try/catch` to silence them, and do not call `DbContext`, repositories, or services directly to set state that the feature under test was supposed to set. If a test cannot be made to pass without one of these, stop and tell the user.

### Definition of Done

Before saying "done," "ready," "complete," or "passing," run the tier-scoped pre-flight. The tier is picked by inspecting the working-tree diff (`git diff --name-only HEAD` + untracked tracked-extension files) — same scheme as `.claude/hooks/run-tests.sh` and the `/verify` slash command:

- **TIER 0** — no `.cs` / `.ts` / `.tsx` / `.csproj` / `.sln` touched (doc/config only): skip steps 1 and 2 entirely.
- **TIER F** — only `.ts` / `.tsx` under `ProjectCeres.Client/` touched: run the frontend pair in steps 1 and 2.
- **TIER B** — only `.cs` / `.csproj` under `ProjectCeres/` touched (no test-project, no `.sln`): run the backend pair in steps 1 and 2.
- **TIER M** — mixed, OR `.sln` touched, OR `ProjectCeres.Tests/` edits, OR anything ambiguous: run both pairs.

Then:

1. **Build is clean** (no errors, no new warnings) for every command the tier required:
   - Frontend pair: `pnpm --dir ProjectCeres.Client build`
   - Backend pair: `dotnet build ProjectCeres/ProjectCeres.csproj`
2. **Tests are all green** with zero skipped, unless every skip has an inline comment with a tracked issue link:
   - Frontend pair: `pnpm --dir ProjectCeres.Client test --run` (allow ONE retry on a known-isolation Vitest flake; if it fails twice, root-cause it)
   - Backend pair: `dotnet test` (no retries)
3. For each new branch in production code, name the test that exercises it. (Runs regardless of tier.)
4. If any test file was modified in this change, state which of cases (1), (2), or (3) above applied. (Runs regardless of tier.)

**Tier-up when in doubt.** Picking TIER M when uncertain is fine — picking a smaller tier than the diff warrants is not. The `/verify` slash command implements this check; reach for it instead of running the commands ad-hoc.

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

| Type        | What it tests                                                         | Database? | Introduced |
| ----------- | --------------------------------------------------------------------- | --------- | ---------- |
| Unit        | Individual calculation or business logic method in isolation          | No        | Phase 1    |
| Integration | EF Core queries and service interactions against a real test database | Yes       | Phase 1    |
| E2E         | Full browser-driven flows                                             | Yes       | Phase 3    |

E2E tests are deferred until Phase 3. The prerequisites — CI/CD pipeline, Testcontainers, and a hosted environment — do not exist until that phase. E2E must also be written after the Phase 2 React migration stabilizes, not before, to avoid investing in tests against a frontend that is about to change.

### E2E Tool: Playwright (TypeScript)

**Microsoft Playwright** is the chosen tool. Per [ADR-0071](decisions/ADR-0071-e2e-testing-on-playwright.md), the **TypeScript** variant ships — not the `Microsoft.Playwright` NuGet port. Tests live in `ProjectCeres.Client/e2e/` next to the React app, run via `pnpm --dir ProjectCeres.Client e2e`, exercise the production-built SPA served against a real `dotnet run` + PostgreSQL fixture.

> **Supersession (2026-05-13):** prior versions of this section pinned `Microsoft.Playwright` NuGet inside the xUnit project. That choice predated the completion of the SPA migration. ADR-0071 reopened the question, audited feature parity between the .NET and TS variants, and confirmed that the .NET port lacks UI Mode (time-travel debugging, watch mode, locator picker), the `webServer` config block (auto-boot dev server before tests), and is the slower-moving target for new features. The TS variant also shares types with the SPA DTOs, which is the regression risk the E2E suite exists to catch.

Rationale:

- TypeScript-first — tests live next to the SPA in `ProjectCeres.Client/`. DTO types imported from `src/` keep API-contract regressions inside the test surface, not at runtime.
- Native test runner with `playwright.config.ts` — `webServer` auto-boots `dotnet run` + Vite preview before tests, `projects` matrix runs Chromium / Firefox / WebKit, `fullyParallel` + `sharding` work first-party.
- UI Mode (`pnpm playwright test --ui`) — time-travel scrubbing, watch mode, locator picker, DOM inspector. The single biggest DX win over the .NET variant.
- Cross-origin and cookie semantics — Phase 3 uses cookie auth (`SameSite=Lax` + CSRF token per [ADR-0063](decisions/ADR-0063-cookie-samesite-lax-with-csrf-tokens.md)). Playwright's BrowserContext model handles these without workaround.
- Trace files attach to failed CI runs — debugging across SPA + API + DB boundary is tractable from the GitHub Actions log alone.
- Official GitHub Actions integration — `npx playwright install --with-deps` is the well-trod path; sharding across runner instances is first-party. Pairs cleanly with [ADR-0070](decisions/ADR-0070-ci-cd-on-github-actions.md).

**.NET state.** The unit (Vitest) + integration (xUnit) split is by **what's tested**, not by host language. E2E tests drive the browser-on-SPA surface, which is TypeScript-native — the .NET server is exercised by the running app, but no `.cs` type is imported by the test. xUnit stays the integration-suite home; nothing in `ProjectCeres.Tests` changes.

### What Gets E2E Tested (Phase 3+)

Focus on critical user-facing flows that cross the full stack and are not covered by unit or integration tests:

- Login and TOTP authentication flow
- Account creation and opening balance
- Recording a transaction and verifying it appears in the account balance
- Transfer between accounts
- Budget progress reflected after a transaction is recorded

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
| `Common/IUserOwnedConformanceTests.cs` | 8 auth-internal entities implement `IUserOwned`; `FailedLoginAttempt` does NOT |
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

### Phase 2 Import Coverage

| Service / Class                                | Test file                                                                              |
| ---------------------------------------------- | -------------------------------------------------------------------------------------- |
| `CsvImportParser`                              | `Unit/CsvImportParserTests.cs`                                                         |
| `ExcelImportParser`                            | `Unit/ExcelImportParserTests.cs`                                                       |
| `ImportParserFactory`                          | `Unit/ImportParserFactoryTests.cs`                                                     |
| `ImportService`                                | `Unit/ImportServiceTests.cs` (unit), `Integration/ImportServiceTests.cs` (integration) |
| `ImportProfileService`                         | `Integration/ImportProfileServiceTests.cs`                                             |
| `HeaderDetectionService`                       | `Unit/HeaderDetectionServiceTests.cs`                                                  |
| Import headers API (`ImportHeadersController`) | `Integration/ImportApiTests.cs` (via `WebApplicationFactory`)                          |

Razor controller actions remain untested — they are thin HTTP handlers with a defined end-of-life in Phase 3; testing them is not worth the investment. API controllers in `Controllers/Api/` are integration tested using `WebApplicationFactory<Program>` (see ADR-0037).

### Integration Test Database

- Dedicated local database: `project_ceres_test`
- EF Core applies migrations at the start of each test run via `_db.Database.MigrateAsync()` (idempotent)
- Each test class wraps its writes in a transaction rolled back after the test — no data persists between tests
- Connection string stored in `appsettings.Test.json` (gitignored) or via `dotnet user-secrets`

> **Testcontainers** deferred to Phase 3 when CI/CD pipelines are introduced.

### Test Collection Serialization (Phase 2)

All integration test classes carry `[Collection("IntegrationTests")]`. The collection is defined in `WafCollection.cs`:

```csharp
[CollectionDefinition("IntegrationTests")]
public class IntegrationCollection : ICollectionFixture<TestWebApplicationFactory> { }
```

**Why this matters:**

- xUnit runs all classes in a shared collection **sequentially on one thread**. Without this, concurrent writes from two test classes in the same test run race on `project_ceres_test` and produce non-deterministic failures.
- `TestWebApplicationFactory` overrides the connection string in `ConfigureWebHost`, pinning every WAF-based test to `project_ceres_test` regardless of what `appsettings.json` says. This prevents any integration test from accidentally hitting the dev database.
- Both `TestDbFixture`-based tests and `WebApplicationFactory`-based tests join the same collection, so the entire suite is serialized.

Add `[Collection("IntegrationTests")]` to every new integration test class. Do not create a second collection — a second collection runs in parallel with the first and reintroduces the race condition.

---

## CI/CD — Phase 3 Scope

CI and CD are not active in Phase 1 or 2. This is an intentional deferral — the app runs locally for a single user, so automated pipelines add overhead with no benefit at this stage.

**Phase 3 state:**

- **CI service** — **GitHub Actions** per [ADR-0070](decisions/ADR-0070-ci-cd-on-github-actions.md). PR-gating workflow at `.github/workflows/ci.yml` runs `dotnet build` + `dotnet test` + `pnpm tsc --noEmit` + `pnpm build` + `pnpm test` against a `services.postgres` Postgres 16 instance.
- **CD strategy** — **GitHub Actions environments** + **OIDC** per [ADR-0070](decisions/ADR-0070-ci-cd-on-github-actions.md). Migration runs `dotnet ef database update` gated on green CI for the same commit. Pre-migration DB snapshot taken on every run, retained ≥7 days. Deploy target itself scoped to the still-open hosting-platform decision.
- **Production migration strategy** — deferred to **Stage 15** (pre-launch hardening); choice depends on hosting platform.
- **Testcontainers** — replaces the local `project_ceres_test` database in CI where a real PostgreSQL instance is not available. The `services.postgres` Postgres 16 image is the GitHub-Actions-native equivalent and is the default.
