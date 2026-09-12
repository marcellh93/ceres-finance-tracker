# Stage 12.15 — BDD with Reqnroll: design

**Status:** approved (2026-09-12), pending implementation plan.
**Feasibility:** proven by a throwaway spike (2026-09-12) — Reqnroll ran one scenario end-to-end against the real `TestWebApplicationFactory` via `[ScenarioDependencies]` DI on .NET 10.
**Decision (user, 2026-09-12):** Option B — bump to the latest stack (xUnit 2.9.3 + Reqnroll 3.3.4), rather than pinning an older Reqnroll (2.3.0) to keep xUnit 2.5.3.

## Goal

Add executable specification (BDD) coverage to Project Ceres via [Reqnroll](https://reqnroll.net) — a new `ProjectCeres.Specs` project whose Gherkin `.feature` files are living documentation of a real business rule, executed against the existing integration-test harness. Reqnroll (not SpecFlow, which is discontinued and has no .NET 10 support) is the maintained successor.

## Why BDD here, and why one feature

We already have strong xUnit integration + Playwright E2E coverage, so BDD is not for *more* coverage — it is for **readable, business-facing specification** of a domain rule that a non-engineer can review. The value is the Gherkin given/when/then as documentation, so the starting feature is a genuine multi-step business rule, not a CRUD smoke test. Ship **one** representative feature (support-ticket lifecycle); a second can follow once the in-tree pattern is proven. YAGNI on more.

## The stack decision (Option B) and its unavoidable consequence

- `ProjectCeres.Specs` references `Reqnroll.xUnit` 3.3.4 + `Reqnroll.Microsoft.Extensions.DependencyInjection` 3.3.4.
- Reqnroll.xUnit 3.3.4 requires `xunit.core >= 2.8.1`. `ProjectCeres.Specs` references `ProjectCeres.Tests` (to reuse the harness), which transitively pins xUnit. So **`ProjectCeres.Tests` must be bumped xUnit 2.5.3 → 2.9.3** (and `xunit.runner.visualstudio` to a matching 2.x), or the `NU1107` version conflict the spike hit returns.
- **Scope of the bump:** only `ProjectCeres.Tests` is required (it is the referenced project). `ProjectCeres.Analyzers.Tests` is already on xUnit 2.7.0, is independent (does not reference Specs), and does not *need* bumping — aligning it to 2.9.3 is optional consistency, done only if trivially clean.
- **This bump touches the whole 1393-test suite's framework version.** 2.5→2.9 is same-major with no known breaking API changes, but the bump is verified by a **full green suite run as its own gating step** before anything builds on it. Not assumed.

## Project structure

- New `ProjectCeres.Specs/ProjectCeres.Specs.csproj` (net10.0), added to `ProjectCeres.sln`.
  - Packages: `Microsoft.NET.Test.Sdk` 17.8.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 2.9.x, `Reqnroll.xUnit` 3.3.4, `Reqnroll.Microsoft.Extensions.DependencyInjection` 3.3.4.
  - `ProjectReference` → `ProjectCeres.Tests` (reuses `TestWebApplicationFactory` / `AuthTestWebApplicationFactory` / `AuthTestFixture`).
- Layout: `Features/*.feature` (Gherkin), `Steps/*.cs` (step bindings), `Support/*.cs` (the `[ScenarioDependencies]` registration + the pinned factory).

## Harness reuse + DB isolation (the §12.18-aware part)

Reqnroll scenarios do not inherit `IntegrationTestBase`, so they must reuse the harness while preserving per-run DB isolation the way §12.18 established:

- Add a serial-collection DB for the Specs project:
  - `TestDatabaseRouter.SerialCollectionDatabases` gains `["SpecsTests"] = "project_ceres_test_specs"`.
  - `tools/ci/setup-test-db.sh` `SERIAL_DB_SUFFIXES` gains `specs` → clones `project_ceres_test_specs`.
- A `SpecsAuthFactory : AuthTestWebApplicationFactory` overrides `InitDbName => TestDatabaseRouter.DatabaseForCollection("SpecsTests")` — same pattern as the bucket factories. The support-ticket feature needs the real auth pipeline, so it derives from the **auth** factory, not the plain one.
- `[ScenarioDependencies]` registers `SpecsAuthFactory` as a singleton in the returned `IServiceCollection`; step classes receive it via constructor injection (spike-proven).
- Under the local N=1 fallback (`CloneCount < 2`), `DatabaseForCollection("SpecsTests")` collapses to the legacy DB — same as every serial collection — so a plain local `dotnet test` needs no clone provisioning.

## The starting feature — support-ticket lifecycle

`Features/SupportTicketLifecycle.feature`, exercising the real state machine (§12.6):

```gherkin
Feature: Support ticket lifecycle
  A support ticket's status reflects who holds the conversation.

  Background:
    Given a signed-in user

  Scenario: A user reply keeps the ticket Open
    Given the user has an open support ticket
    When the user replies to the ticket
    Then the ticket status is "Open"

  Scenario: An operator reply moves the ticket to Pending
    Given the user has an open support ticket
    When an operator replies to the ticket
    Then the ticket status is "Pending"
```

Step definitions drive the real HTTP endpoints via the pinned factory's `HttpClient` + `AuthTestFixture` for the signed-in user — the same calls the existing `SupportConversationApiTests` / `SupportAdminApiTests` make, expressed as Gherkin. Per-scenario data is marker-isolated (per `feedback_filter_test_queries_by_test_data`).

## CI wiring

`ci.yml` `dotnet-test` job runs specific `.csproj` files, so Specs needs its own run:
- Add a build + test step after the main suite: `dotnet build ProjectCeres.Specs/ProjectCeres.Specs.csproj` then `dotnet test ProjectCeres.Specs/ProjectCeres.Specs.csproj`.
- Reuses the job's already-provisioned Postgres + `--template --clones` DBs (the `specs` serial DB is now provisioned by the setup-test-db change).
- Runs serial (one BDD project) — no `CERES_TEST_DB_CLONES` / parallel override needed for it.

## Stop-hook wiring

`run-tests.sh` runs bare `dotnet test` (whole solution) on tier 2, so once `ProjectCeres.Specs` is in `ProjectCeres.sln` it is **picked up automatically** — no hook edit. Verify the added scenarios don't materially slow tier 2 (one feature ≈ 2 scenarios ≈ seconds).

## Docs

- `docs/testing.md` — new § BDD (Reqnroll): what it is, where features/steps live, the harness-reuse + pinned-DB pattern, how to add a feature, the xUnit-version coupling note.
- `docs/roadmap-phase-three.md` — §12.15 marked Done.
- `CHANGELOG.md` — Tests entry.

## Error handling / correctness guards

- The xUnit 2.5.3→2.9.3 bump is gated by a full green suite run (its own plan task) before the Specs project is built on top.
- Reqnroll's source generator must emit bindings — a generation failure surfaces at the Specs build step.
- The `project_ceres_test_specs` DB must be provisioned or scenarios fail to connect — covered by the setup-test-db + router change, verified by the CI run and a local clones run.

## Testing (the stage's proof)

1. Full existing suite green after the xUnit bump (1393/1393) — gates everything.
2. The support-ticket scenarios pass locally (N=1 fallback → legacy DB) and under clones (own `_specs` DB).
3. CI `dotnet-test` job runs the Specs project green.
4. Stop hook picks up Specs automatically (whole-solution `dotnet test`).

## Out of scope (YAGNI)

- More than the one starting feature (a second follows once the pattern's proven).
- A separate CI job for Specs (folded into `dotnet-test`).
- Parallelizing the Specs project (one feature, serial is fine).
- Bumping `ProjectCeres.Analyzers.Tests` (independent; only if trivially clean).

## Files

- Create: `ProjectCeres.Specs/ProjectCeres.Specs.csproj`, `Features/SupportTicketLifecycle.feature`, `Steps/SupportTicketSteps.cs`, `Support/SpecsHooks.cs` (ScenarioDependencies + `SpecsAuthFactory`).
- Modify: `ProjectCeres.sln` (add project); `ProjectCeres.Tests/ProjectCeres.Tests.csproj` (xUnit 2.5.3→2.9.3); `ProjectCeres.Tests/Integration/TestDatabaseRouter.cs` (SpecsTests entry); `tools/ci/setup-test-db.sh` (specs suffix); `.github/workflows/ci.yml` (Specs build+test step); `docs/testing.md`, `docs/roadmap-phase-three.md`, `CHANGELOG.md`.
