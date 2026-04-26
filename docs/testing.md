# Project Ceres — Testing Strategy

> **Diataxis type:** Reference — defines the testing approach, TDD workflow, and CI/CD scope for all phases.

---

## Stack

### Backend (.NET)

| Library | Role |
|---------|------|
| **xUnit** | Test framework — standard for .NET, used by ASP.NET Core itself |
| **Moq** | Mocking library — isolates dependencies in unit tests |
| **FluentAssertions** | Readable assertions — `result.Should().Be(100m)` over default xUnit assertions |
| **WebApplicationFactory** | Integration test host for API controllers — boots full app in memory (Phase 2+) |

### Frontend (ProjectCeres.Client — Phase 2+)

| Library | Role |
|---------|------|
| **Vitest** | Test runner — shares Vite config, no separate setup. See ADR-0032. |
| **React Testing Library** | Component-level tests — renders components and asserts on user-visible output |

Frontend tests live in `ProjectCeres.Client/src/__tests__/`. What gets tested:
- React components with non-trivial rendering logic (conditional display, derived state)
- Chart data transformation functions
- Import profile mapping logic (client-side validation)

Simple presentational components with no logic are not tested — the value is too low relative to the maintenance cost.

---

## Types of Tests

| Type | What it tests | Database? | Introduced |
|------|--------------|-----------|------------|
| Unit | Individual calculation or business logic method in isolation | No | Phase 1 |
| Integration | EF Core queries and service interactions against a real test database | Yes | Phase 1 |
| E2E | Full browser-driven flows | Yes | Phase 3 |

E2E tests are deferred until Phase 3. The prerequisites — CI/CD pipeline, Testcontainers, and a hosted environment — do not exist until that phase. E2E must also be written after the Phase 2 React migration stabilizes, not before, to avoid investing in tests against a frontend that is about to change.

### E2E Tool: Playwright

**Microsoft Playwright** (`Microsoft.Playwright` NuGet package) is the chosen tool. Rationale:

- Native .NET/C# support — tests live in the existing xUnit project, no second test stack or language
- Auto-wait eliminates manual `sleep` calls; parallel execution is built in
- Trace viewer and screenshot/video on failure simplify debugging CI failures
- Pairs naturally with Testcontainers: spin up the database in a container, host the app in-process, drive it with Playwright, tear down
- Maintained by Microsoft — aligns with the .NET-first stack

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

| Class | Test file | What it covers |
|-------|-----------|----------------|
| `BalanceCalculationTests` | `Unit/BalanceCalculationTests.cs` | Asset and liability balance derivation formula |
| `CategoryBudgetGuardTests` | `Unit/CategoryBudgetGuardTests.cs` | CategoryBudget expense-only enforcement |
| `SavingsRateTests` | `Unit/SavingsRateTests.cs` | Savings rate calculation |
| `NumberFormatHelper` | `Unit/NumberFormatHelperTests.cs` | `FormatAmount`, `FormatInputValue`, `TryParseDecimal`, and round-trip correctness |
| `DecimalModelBinder` (via helper) | `Unit/DecimalParsingTests.cs` | Parsing in both number format modes including invariant-input tolerance |

---

## What Gets Integration Tested

- EF Core queries that involve joins or aggregations
- Multi-step writes that require a database transaction (e.g. account creation with opening balance)
- Service interactions where one service depends on another (e.g. `LiabilityPaymentService` → `AccountService`)
- Phase 3: multi-user data scoping — tests that confirm one user cannot access another user's data

### Phase 1 Service Coverage (as of 2026-04-18)

All Phase 1 services now have integration test coverage:

| Service | Test file |
|---------|-----------|
| `AccountService` | `AccountServiceTests.cs` |
| `TransactionService` | `TransactionServiceTests.cs` |
| `TransferService` | `TransferValidationTests.cs` |
| `CategoryService` | `CategoryServiceTests.cs` |
| `LiabilityPaymentService` | `LiabilityPaymentServiceTests.cs` |
| `BudgetService` | `BudgetServiceTests.cs` |
| `RecurringTransactionService` | `RecurringTransactionServiceTests.cs` |
| `ReportService` | `ReportServiceTests.cs` |
| `DashboardService` | `DashboardServiceTests.cs` |
| `SettingsService` | `SettingsServiceTests.cs` |
| `FileAttachmentService` | `FileAttachmentServiceTests.cs` |

### Phase 2 Import Coverage

| Service / Class | Test file |
|---------|-----------|
| `CsvImportParser`     | `Unit/CsvImportParserTests.cs`          |
| `ExcelImportParser`   | `Unit/ExcelImportParserTests.cs`        |
| `ImportParserFactory` | `Unit/ImportParserFactoryTests.cs`      |
| `ImportService`       | `Unit/ImportServiceTests.cs` (unit), `Integration/ImportServiceTests.cs` (integration) |
| `ImportProfileService`| `Integration/ImportProfileServiceTests.cs` |
| `HeaderDetectionService` | `Unit/HeaderDetectionServiceTests.cs` |
| Import headers API (`ImportHeadersController`) | `Integration/ImportApiTests.cs` (via `WebApplicationFactory`) |

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

**Phase 3 open questions (see [planning.md](planning.md#open-questions--decisions)):**

- CI service — no provider chosen (GitHub Actions is the leading candidate). Required for automated build verification and vulnerability scanning before hosting.
- CD strategy — no deployment pipeline designed. Must define: what triggers a deploy (merge to main, tag, manual), whether a staging environment exists, how database migrations run in the pipeline, and whether rollback is supported.
- Production migration strategy — `dotnet ef database update` vs. pre-deploy CI/CD step vs. reviewed SQL scripts. Option 2 or 3 recommended.
- Testcontainers — replaces the local `project_ceres_test` database in CI where a real PostgreSQL instance is not available.
