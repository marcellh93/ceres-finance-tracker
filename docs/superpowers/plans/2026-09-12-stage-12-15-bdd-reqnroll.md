# Stage 12.15 — BDD with Reqnroll Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add executable-specification (BDD) coverage via a new `ProjectCeres.Specs` project (xUnit + Reqnroll) whose Gherkin `.feature` files document a real business rule (support-ticket lifecycle) and run against the existing integration-test harness.

**Architecture:** `ProjectCeres.Specs` references `ProjectCeres.Tests` to reuse `AuthTestWebApplicationFactory`; a `[ScenarioDependencies]` DI method registers a `SpecsAuthFactory` pinned to its own serial DB (`project_ceres_test_specs`), preserving §12.18 isolation. Reqnroll 3.3.4 requires xUnit ≥ 2.8.1, so `ProjectCeres.Tests` is bumped 2.5.3 → 2.9.3 first (gated by a full green suite run).

**Tech Stack:** .NET 10; Reqnroll.xUnit 3.3.4 + Reqnroll.Microsoft.Extensions.DependencyInjection 3.3.4; xunit 2.9.3 + xunit.runner.visualstudio 2.8.2; PostgreSQL (existing harness).

**Spec:** `docs/superpowers/specs/2026-09-12-stage-12-15-bdd-reqnroll-design.md`

## Global Constraints

- **Option B:** bump `ProjectCeres.Tests` to `xunit` **2.9.3** + `xunit.runner.visualstudio` **2.8.2**. Required because `ProjectCeres.Specs` references `ProjectCeres.Tests` and Reqnroll.xUnit 3.3.4 floors `xunit.core >= 2.8.1`. Do NOT bump `ProjectCeres.Analyzers.Tests` (independent, already 2.7.0) unless trivially clean.
- Reqnroll packages pinned to **3.3.4** (both `Reqnroll.xUnit` and `Reqnroll.Microsoft.Extensions.DependencyInjection`).
- The Specs project reuses the harness — no reimplementation of auth/DB setup. Its factory derives from `AuthTestWebApplicationFactory` (support-ticket needs real auth), pinned to `project_ceres_test_specs`.
- DB isolation: add `["SpecsTests"] = "project_ceres_test_specs"` to `TestDatabaseRouter.SerialCollectionDatabases`, and `specs` to `SERIAL_DB_SUFFIXES` in `tools/ci/setup-test-db.sh`. Under N=1 local fallback everything collapses to the legacy DB (zero local setup).
- Per-scenario data marker-isolated (`feedback_filter_test_queries_by_test_data`).
- Stay on `main`, no branches. No `Co-Authored-By`. gitleaks Stop-hook active — no secret literals. Never run `dotnet test` concurrently; the full suite runs ONCE per turn, backgrounded.
- Do not weaken/suppress to make things pass (the `suppress-without-research-gate` hook enforces this). Do not skip tests.

---

### Task 1: Bump ProjectCeres.Tests to xUnit 2.9.3 (the gating step)

**Files:**
- Modify: `ProjectCeres.Tests/ProjectCeres.Tests.csproj:21-22`

This is the load-bearing prerequisite: everything downstream references this project. Its own deliverable is "the whole existing suite still green on the new xUnit," so it is its own task with its own full-suite verification.

- [ ] **Step 1: Bump the two package versions**

In `ProjectCeres.Tests/ProjectCeres.Tests.csproj`, change:
```xml
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
```
to:
```xml
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
```

- [ ] **Step 2: Restore + build the test project**

Run: `dotnet restore ProjectCeres.Tests/ProjectCeres.Tests.csproj && dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj`
Expected: `Build succeeded`, 0 errors, no NEW warnings introduced by the bump (CS/xUnit-analyzer). If the xUnit analyzers (xUnit2xxx) surface new warnings, read each — fix real ones, do not suppress.

- [ ] **Step 3: Run the FULL suite (the gate) — backgrounded, once**

Run: `nohup dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --no-build > /tmp/dt-xunit-bump.log 2>&1 &` then wait for completion (do not run concurrently).
Expected: `Passed! - Failed: 0, Passed: 1393` (or the current total), 0 failures. This proves the 2.5→2.9 bump broke nothing before anything builds on it. If any test fails, root-cause it (a genuine xUnit-behavior change) — do not proceed to Task 2 with a red suite.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/ProjectCeres.Tests.csproj
git commit -m "build(12.15): bump ProjectCeres.Tests to xUnit 2.9.3 (Reqnroll floor); full suite green"
```

---

### Task 2: Serial DB for the Specs project (router + provisioning)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/TestDatabaseRouter.cs:19-26`
- Modify: `tools/ci/setup-test-db.sh:27`
- Test: `ProjectCeres.Tests/Integration/TestDatabaseRouterTests.cs` (add a case)

**Interfaces:**
- Consumes: `TestDatabaseRouter.SerialCollectionDatabases` (dict), `DatabaseForCollection(name)`.
- Produces: `DatabaseForCollection("SpecsTests")` → `"project_ceres_test_specs"` under clones, `LegacyDatabase` under N=1. `setup-test-db.sh --template --clones N` provisions `project_ceres_test_specs`.

- [ ] **Step 1: Write the failing test**

In `TestDatabaseRouterTests.cs`, add to the serial-collections test (or a new `[Fact]`):
```csharp
[Fact]
public void DatabaseForCollection_gives_the_specs_project_its_own_db()
{
    using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", "4");
    TestDatabaseRouter.DatabaseForCollection("SpecsTests").Should().Be("project_ceres_test_specs");
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~DatabaseForCollection_gives_the_specs_project_its_own_db"`
Expected: FAIL — `DatabaseForCollection("SpecsTests")` currently returns `project_ceres_test` (legacy fallback for an unknown serial name).

- [ ] **Step 3: Add the router entry**

In `TestDatabaseRouter.cs`, add to `SerialCollectionDatabases`:
```csharp
        ["TestDbFixtureTests"] = "project_ceres_test_txfixture",
        ["SpecsTests"] = "project_ceres_test_specs",
```

- [ ] **Step 4: Add the provisioning suffix**

In `tools/ci/setup-test-db.sh`, change:
```bash
SERIAL_DB_SUFFIXES=(ratelimit mfaratelimit approle rls txfixture)
```
to:
```bash
SERIAL_DB_SUFFIXES=(ratelimit mfaratelimit approle rls txfixture specs)
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~DatabaseForCollection_gives_the_specs_project_its_own_db"`
Expected: PASS.

- [ ] **Step 6: Provision the specs DB locally (for later tasks) + confirm the script works**

Run: `tools/ci/setup-test-db.sh --template --clones 4 2>&1 | tail -3` (needs local Postgres)
Expected: the "done" line now reports 6 serial DBs; `psql -lqt | grep project_ceres_test_specs` shows it exists. (If Postgres isn't available locally, note it — the CI run in Task 5 is the real proof.)

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres.Tests/Integration/TestDatabaseRouter.cs ProjectCeres.Tests/Integration/TestDatabaseRouterTests.cs tools/ci/setup-test-db.sh
git commit -m "feat(12.15): dedicated project_ceres_test_specs serial DB for the BDD project"
```

---

### Task 3: The ProjectCeres.Specs project + harness DI bridge

**Files:**
- Create: `ProjectCeres.Specs/ProjectCeres.Specs.csproj`
- Create: `ProjectCeres.Specs/Support/SpecsHooks.cs`
- Modify: `ProjectCeres.sln` (add the project)

**Interfaces:**
- Consumes: `AuthTestWebApplicationFactory` (from `ProjectCeres.Tests`), `TestDatabaseRouter.DatabaseForCollection("SpecsTests")` (Task 2).
- Produces: a buildable `ProjectCeres.Specs` assembly; `SpecsAuthFactory` (pinned to the specs DB) available via `[ScenarioDependencies]` DI for step classes.

- [ ] **Step 1: Create the csproj**

`ProjectCeres.Specs/ProjectCeres.Specs.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Reqnroll.xUnit" Version="3.3.4" />
    <PackageReference Include="Reqnroll.Microsoft.Extensions.DependencyInjection" Version="3.3.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ProjectCeres.Tests\ProjectCeres.Tests.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the DI bridge + pinned factory**

`ProjectCeres.Specs/Support/SpecsHooks.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Tests.Integration;
using Reqnroll.Microsoft.Extensions.DependencyInjection;

namespace ProjectCeres.Specs.Support;

/// <summary>
/// Auth-enabled WAF pinned to the BDD project's own serial DB (project_ceres_test_specs
/// under clones; legacy under the N=1 fallback), so scenarios reuse the real harness while
/// preserving §12.18 per-run isolation. Support-ticket flows exercise the real auth
/// pipeline, hence AuthTestWebApplicationFactory rather than the plain factory.
/// </summary>
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

- [ ] **Step 3: Add the project to the solution**

Run: `dotnet sln ProjectCeres.sln add ProjectCeres.Specs/ProjectCeres.Specs.csproj`
Expected: "Project ... added to the solution."

- [ ] **Step 4: Build the Specs project**

Run: `dotnet build ProjectCeres.Specs/ProjectCeres.Specs.csproj`
Expected: `Build succeeded`, 0 errors (no `NU1107` — Task 1's bump resolved the xUnit conflict). Reqnroll's generator runs with no feature files yet, which is fine.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Specs/ProjectCeres.Specs.csproj ProjectCeres.Specs/Support/SpecsHooks.cs ProjectCeres.sln
git commit -m "feat(12.15): ProjectCeres.Specs project + Reqnroll harness DI bridge (SpecsAuthFactory)"
```

---

### Task 4: The support-ticket-lifecycle feature + steps

**Files:**
- Create: `ProjectCeres.Specs/Features/SupportTicketLifecycle.feature`
- Create: `ProjectCeres.Specs/Steps/SupportTicketSteps.cs`

**Interfaces:**
- Consumes: `SpecsAuthFactory` (Task 3, via ctor injection), `AuthTestFixture` (existing helpers: `RegisterUserAsync`, the CSRF/login helpers), the support endpoints (`POST /api/support/tickets`, the reply + admin-reply endpoints).

- [ ] **Step 1: Write the feature**

`ProjectCeres.Specs/Features/SupportTicketLifecycle.feature`:
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

- [ ] **Step 2: Write the step definitions**

`ProjectCeres.Specs/Steps/SupportTicketSteps.cs` — bind each step, driving the REAL endpoints via the pinned factory + AuthTestFixture. Read the existing `SupportConversationApiTests` and `SupportAdminApiTests` first for the exact endpoint paths, request shapes, auth/CSRF helper calls, and the status enum values; mirror those calls. Marker-isolate the ticket (a per-scenario GUID in the subject). Skeleton:
```csharp
using FluentAssertions;
using ProjectCeres.Specs.Support;
using ProjectCeres.Tests.Integration.Authentication;
using Reqnroll;

namespace ProjectCeres.Specs.Steps;

[Binding]
public sealed class SupportTicketSteps
{
    private readonly SpecsAuthFactory _factory;
    // per-scenario state: the signed-in user, the ticket id, the last response.

    public SupportTicketSteps(SpecsAuthFactory factory) => _factory = factory;

    [Given("a signed-in user")]
    public async Task GivenASignedInUser() { /* AuthTestFixture.RegisterUserAsync + login → cookie */ }

    [Given("the user has an open support ticket")]
    public async Task GivenAnOpenTicket() { /* POST /api/support/tickets with a GUID-marked subject; capture id */ }

    [When("the user replies to the ticket")]
    public async Task WhenUserReplies() { /* POST the user reply endpoint */ }

    [When("an operator replies to the ticket")]
    public async Task WhenOperatorReplies() { /* POST the admin reply endpoint (RequireAdmin path) */ }

    [Then("the ticket status is \"(.*)\"")]
    public async Task ThenStatusIs(string expected) { /* GET the ticket; assert status == expected */ }
}
```
Fill each body from the existing tests' proven call sequences — do not invent endpoint shapes; reuse what `SupportConversationApiTests`/`SupportAdminApiTests` already do.

- [ ] **Step 3: Run the scenarios (local N=1 → legacy DB)**

Run: `dotnet test ProjectCeres.Specs/ProjectCeres.Specs.csproj` (needs local Postgres with the legacy `project_ceres_test` DB migrated).
Expected: `Passed! - Failed: 0, Passed: 2` (the two scenarios). If a step is ambiguous/undefined, Reqnroll reports it — fix the binding, do not skip.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Specs/Features/SupportTicketLifecycle.feature ProjectCeres.Specs/Steps/SupportTicketSteps.cs
git commit -m "test(12.15): support-ticket-lifecycle BDD feature + step definitions"
```

---

### Task 5: CI wiring + docs (stage close)

**Files:**
- Modify: `.github/workflows/ci.yml` (dotnet-test job)
- Modify: `docs/testing.md` (new § BDD)
- Modify: `docs/roadmap-phase-three.md` (§12.15 Done)
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add the Specs build + test step to the dotnet-test job**

After the "Full test suite" step in `ci.yml`'s `dotnet-test` job, add:
```yaml
      - name: BDD specs (Reqnroll)
        # Reuses the provisioned Postgres + the project_ceres_test_specs clone
        # (setup-test-db.sh --template provisions it). Serial — no parallel override.
        run: |
          dotnet build ProjectCeres.Specs/ProjectCeres.Specs.csproj --no-restore
          dotnet test ProjectCeres.Specs/ProjectCeres.Specs.csproj --no-build
        env:
          CERES_TEST_DB_CLONES: "4"
```
(Confirm restore covers Specs — the job's earlier `dotnet build ProjectCeres.Tests` restores its graph; add a `dotnet restore ProjectCeres.Specs/ProjectCeres.Specs.csproj` before build if the `--no-restore` fails on a clean runner.)

- [ ] **Step 2: docs/testing.md § BDD**

Add a section: what Reqnroll is (maintained SpecFlow successor, .NET 10), where features/steps/support live, the `[ScenarioDependencies]` + `SpecsAuthFactory`-pinned-DB reuse pattern, how to add a feature, and the xUnit-version coupling note (Specs forces `ProjectCeres.Tests` ≥ 2.8.1).

- [ ] **Step 3: roadmap §12.15 Done**

Add a `## Stage 12.15` section (mirror §12.14's Done format): status, what/why, `[x]` checklist (xUnit bump + suite green; specs DB; project + DI bridge; feature + steps green; CI step; hook auto-pickup). Note the CI→Docker→BDD sequence is now complete.

- [ ] **Step 4: roadmap-consistency check**

Run: `node -e "const fs=require('fs');const {scan}=require('./.claude/hooks/roadmap-consistency-check.js');const g=scan(fs.readFileSync('docs/roadmap-phase-three.md','utf8'));if(g.length){g.forEach(x=>console.error(x));process.exit(1)}else{console.log('consistent')}"`
Expected: `consistent`.

- [ ] **Step 5: CHANGELOG entry** — a Tests/Added entry for the BDD project under Unreleased/Phase 3.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/ci.yml docs/testing.md docs/roadmap-phase-three.md CHANGELOG.md
git commit -m "docs+ci(12.15): run BDD specs in CI; testing.md BDD section; mark stage Done"
```

- [ ] **Step 7: Full-family verification** — push, watch CI to completion via `gh` (all jobs green, incl. the new BDD specs step), and confirm the whole suite + specs are green on a clean runner. Do NOT report done until the watched CI run is green.

---

## Notes for the executor

- **Task 1 gates everything.** If the xUnit bump reddens the suite, stop and root-cause before Task 2.
- **Postgres availability:** Tasks 2 Step 6, 4 Step 3 need a local Postgres. If unavailable, write+commit the code, mark those verification steps blocked with the exact commands, and rely on Task 5's CI run — do not fabricate a pass.
- **Reqnroll endpoint shapes:** Task 4 must read `SupportConversationApiTests` + `SupportAdminApiTests` for the real call sequences. Do not invent endpoint paths, request bodies, or status values.
- **Stage close (Phase E)** after Task 5: `sync-docs` + `changelog-sync` fired, zero unchecked `[ ]` under §12.15, then delete the SDD workspace.
