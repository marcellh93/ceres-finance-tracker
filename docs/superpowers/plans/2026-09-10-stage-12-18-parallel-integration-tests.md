# Stage 12.18 — Parallel Integration Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut the CI `dotnet-test` full-suite step (~5m40s) by running the integration suite across N=4 parallel xUnit collections, each pinned to its own isolated Postgres database, replacing the single serialized 136-file `IntegrationTests` collection.

**Architecture:** A `TestDatabaseRouter` maps each xUnit collection name to a database name and builds the three role connection strings; the WAF hierarchy reads its DB from the router instead of hardcoded constants. `setup-test-db.sh` gains a template-clone mode that migrates one template DB and clones N copies. The 136 files are re-tagged into 4 runtime-balanced bucket collections that run in parallel; distinct DB per bucket ⇒ distinct Npgsql connection pool ⇒ the connection-scoped RLS GUC can never leak across buckets.

**Tech Stack:** .NET 10 (`net10.0`), xUnit, Npgsql/EF Core 10, PostgreSQL 16, bash.

**Spec:** `docs/superpowers/specs/2026-09-10-stage-12-18-parallel-integration-tests-design.md`

## Global Constraints

- **Platform:** `net10.0`; PostgreSQL 16; three roles `ceres_app`/`ceres_admin`/`ceres_migrator` with `*_dev_password` passwords, identical across all DBs.
- **RLS security is load-bearing:** the distinct-pool-per-DB property is what prevents a cross-tenant `app.current_user_ref` GUC leak. Never introduce a shared connection string across buckets. The full RLS/security suite must stay green under parallelism.
- **The 4 already-serial collections** (`RateLimitTests`, `MfaRateLimitTests`, `AppRoleTests`, `RlsTests`) each get their own dedicated DB; their intra-serialization is deliberate and unchanged.
- **N=4** fixed (env-overridable via `CERES_TEST_DB_CLONES` / `maxParallelThreads`); **N=1 fallback** when no clone env is present → all buckets resolve the single legacy `project_ceres_test`.
- **Stay on main**, no branches/worktrees.
- **Never run `dotnet test` concurrently with the Stop hook's own run.** While iterating, use filtered runs (`--filter`); for evidence use `build-matrix.sh … --no-dotnet-test`. The Stop hook runs the full suite at turn end.
- **Connection-string format** (verbatim): `Host=localhost;Database=<db>;Username=<role>;Password=<role>_dev_password`.

---

## File Structure

| File | Responsibility |
|---|---|
| `ProjectCeres.Tests/Integration/TestDatabaseRouter.cs` (new) | Static: collection name → DB name; DB name → the 3 role connection strings; N=1 fallback logic. Single source of DB-name truth. |
| `ProjectCeres.Tests/Integration/TestDatabaseRouterTests.cs` (new) | Unit tests for the router (no DB needed). |
| `ProjectCeres.Tests/Integration/TestDbFixture.cs` (modify `:33-40`) | Replace the 3 `const` connection strings with router calls. |
| `ProjectCeres.Tests/Integration/WafCollection.cs` (modify) | WAF reads DB name from a settable property fed by the router; replace `IntegrationCollection` with N bucket collection definitions. |
| `ProjectCeres.Tests/Integration/BucketCollections.cs` (new) | The N `[CollectionDefinition("IntegrationParallelK")]` classes. |
| `tools/ci/setup-test-db.sh` (modify) | Add `--template --clones N` mode; keep single-DB mode. |
| `tools/bucket-integration-tests.sh` (new, dev tool) | Captures per-file runtimes + bin-packs the 136 files into 4 buckets; rewrites their `[Collection(...)]` attributes. |
| `ProjectCeres.Tests/xunit.runner.json` (modify) | `parallelizeTestCollections: true`, `maxParallelThreads: 4`. |
| `.github/workflows/ci.yml` (modify) | `dotnet-test` provision → `setup-test-db.sh --template --clones 4`. |
| `ProjectCeres.Tests/Integration/ParallelIsolationTests.cs` (new) | Infra self-tests (disjoint data, scoped sweep, router distinctness, RLS-under-parallelism). |
| `docs/testing.md`, `docs/runbooks/local-dev-troubleshooting.md`, `docs/roadmap-phase-three.md`, `CHANGELOG.md` | Docs + stage close. |

---

## Task 1: TestDatabaseRouter — the DB-name seam

**Files:**
- Create: `ProjectCeres.Tests/Integration/TestDatabaseRouter.cs`
- Test: `ProjectCeres.Tests/Integration/TestDatabaseRouterTests.cs`

**Interfaces:**
- Produces:
  - `static string TestDatabaseRouter.DatabaseForCollection(string collectionName)` — returns the DB name for a bucket collection. `"IntegrationParallel1"` → `project_ceres_test_1` (when clones enabled) or `project_ceres_test` (N=1 fallback). The 4 serial collections map to their own names (see step 3). Unknown → `project_ceres_test`.
  - `static (string app, string admin, string migrator) TestDatabaseRouter.ConnectionsFor(string databaseName)` — the three role connection strings for a DB name.
  - `static int TestDatabaseRouter.CloneCount` — reads `CERES_TEST_DB_CLONES` env; defaults to `1`.
  - `const string TestDatabaseRouter.LegacyDatabase = "project_ceres_test"`.

- [ ] **Step 1: Write the failing test**

```csharp
// ProjectCeres.Tests/Integration/TestDatabaseRouterTests.cs
using FluentAssertions;
using ProjectCeres.Tests.Integration;
using Xunit;

public class TestDatabaseRouterTests
{
    [Fact]
    public void ConnectionsFor_builds_the_three_role_strings_for_a_db()
    {
        var (app, admin, migrator) = TestDatabaseRouter.ConnectionsFor("project_ceres_test_2");
        app.Should().Be("Host=localhost;Database=project_ceres_test_2;Username=ceres_app;Password=ceres_app_dev_password");
        admin.Should().Be("Host=localhost;Database=project_ceres_test_2;Username=ceres_admin;Password=ceres_admin_dev_password");
        migrator.Should().Be("Host=localhost;Database=project_ceres_test_2;Username=ceres_migrator;Password=ceres_migrator_dev_password");
    }

    [Fact]
    public void DatabaseForCollection_maps_bucket_to_numbered_db_when_clones_enabled()
    {
        using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", "4");
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1").Should().Be("project_ceres_test_1");
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4").Should().Be("project_ceres_test_4");
    }

    [Fact]
    public void DatabaseForCollection_falls_back_to_legacy_db_when_no_clone_env()
    {
        using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", null);
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1").Should().Be("project_ceres_test");
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4").Should().Be("project_ceres_test");
    }

    [Fact]
    public void DatabaseForCollection_gives_each_serial_collection_its_own_db()
    {
        using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", "4");
        TestDatabaseRouter.DatabaseForCollection("RateLimitTests").Should().Be("project_ceres_test_ratelimit");
        TestDatabaseRouter.DatabaseForCollection("AppRoleTests").Should().Be("project_ceres_test_approle");
        TestDatabaseRouter.DatabaseForCollection("RlsTests").Should().Be("project_ceres_test_rls");
        TestDatabaseRouter.DatabaseForCollection("MfaRateLimitTests").Should().Be("project_ceres_test_mfaratelimit");
    }

    [Fact]
    public void DatabaseForCollection_serial_collections_ignore_clone_env_fallback_to_legacy_when_off()
    {
        using var _ = new EnvVarScope("CERES_TEST_DB_CLONES", null);
        TestDatabaseRouter.DatabaseForCollection("RateLimitTests").Should().Be("project_ceres_test");
    }
}

// Test helper: restore an env var on dispose.
public sealed class EnvVarScope : IDisposable
{
    private readonly string _name;
    private readonly string? _prev;
    public EnvVarScope(string name, string? value)
    {
        _name = name;
        _prev = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
    public void Dispose() => Environment.SetEnvironmentVariable(_name, _prev);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~TestDatabaseRouterTests"`
Expected: FAIL — `TestDatabaseRouter` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
// ProjectCeres.Tests/Integration/TestDatabaseRouter.cs
namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Single source of truth for which Postgres database a test collection uses,
/// and the three role-scoped connection strings for a database name.
///
/// Stage 12.18: the integration suite splits into N parallel bucket collections,
/// each pinned to its own database, so the connection-scoped RLS GUC
/// (app.current_user_ref) can never leak across buckets — a distinct DB name
/// yields a distinct Npgsql connection pool. When CERES_TEST_DB_CLONES is unset
/// (a plain local `dotnet test` with no provisioned clones), every collection
/// resolves the single legacy project_ceres_test and xUnit serializes them —
/// today's behaviour.
/// </summary>
public static class TestDatabaseRouter
{
    public const string LegacyDatabase = "project_ceres_test";

    private static readonly Dictionary<string, string> SerialCollectionDatabases = new()
    {
        ["RateLimitTests"] = "project_ceres_test_ratelimit",
        ["MfaRateLimitTests"] = "project_ceres_test_mfaratelimit",
        ["AppRoleTests"] = "project_ceres_test_approle",
        ["RlsTests"] = "project_ceres_test_rls",
    };

    /// <summary>Number of parallel bucket databases; 1 (fallback) when unset.</summary>
    public static int CloneCount
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("CERES_TEST_DB_CLONES");
            return int.TryParse(raw, out var n) && n >= 1 ? n : 1;
        }
    }

    public static string DatabaseForCollection(string collectionName)
    {
        // Fallback: no clones provisioned → one shared legacy DB (xUnit serializes).
        if (CloneCount <= 1) return LegacyDatabase;

        if (SerialCollectionDatabases.TryGetValue(collectionName, out var serialDb))
            return serialDb;

        // IntegrationParallelK → project_ceres_test_K
        const string prefix = "IntegrationParallel";
        if (collectionName.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(collectionName[prefix.Length..], out var k))
        {
            return $"{LegacyDatabase}_{k}";
        }

        return LegacyDatabase;
    }

    public static (string app, string admin, string migrator) ConnectionsFor(string databaseName)
    {
        string Conn(string role) =>
            $"Host=localhost;Database={databaseName};Username={role};Password={role}_dev_password";
        return (Conn("ceres_app"), Conn("ceres_admin"), Conn("ceres_migrator"));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~TestDatabaseRouterTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Tests/Integration/TestDatabaseRouter.cs ProjectCeres.Tests/Integration/TestDatabaseRouterTests.cs
git commit -m "feat(12.18): TestDatabaseRouter — collection→DB name + role connection strings"
```

---

## Task 2: Route the WAF hierarchy through the router

**Files:**
- Modify: `ProjectCeres.Tests/Integration/TestDbFixture.cs:33-40`
- Modify: `ProjectCeres.Tests/Integration/WafCollection.cs` (the `TestWebApplicationFactory` connection consts + `ConfigureWebHost`)

**Interfaces:**
- Consumes: `TestDatabaseRouter.ConnectionsFor`, `TestDatabaseRouter.DatabaseForCollection`.
- Produces: `TestWebApplicationFactory.DatabaseName` — a settable property (default `TestDatabaseRouter.LegacyDatabase`) that a bucket's collection fixture sets; the WAF builds its 3 connection strings from it.

- [ ] **Step 1: Write the failing test**

```csharp
// Append to ProjectCeres.Tests/Integration/TestDatabaseRouterTests.cs
[Fact]
public void Waf_uses_the_database_name_it_is_given_for_its_app_connection()
{
    using var factory = new TestWebApplicationFactory { DatabaseName = "project_ceres_test_3" };
    // The factory exposes its resolved app connection string for assertion.
    factory.ResolvedAppConnectionString
        .Should().Contain("Database=project_ceres_test_3")
        .And.Contain("Username=ceres_admin"); // default UseAppRoleConnection=false → admin
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~Waf_uses_the_database_name"`
Expected: FAIL — `DatabaseName` / `ResolvedAppConnectionString` do not exist.

- [ ] **Step 3: Write minimal implementation**

In `WafCollection.cs`, replace the three `private const string …ConnectionString` in `TestWebApplicationFactory` and wire them to the router:

```csharp
// TestWebApplicationFactory — replace the const AppConnectionString/AdminConnectionString/
// MigratorConnectionString block (WafCollection.cs ~:34-45) with:

/// <summary>
/// The database this factory targets. A bucket collection fixture sets this from
/// TestDatabaseRouter.DatabaseForCollection(...). Default = legacy single DB, so
/// factories built outside a bucket (ad-hoc, or under the N=1 fallback) keep
/// today's behaviour.
/// </summary>
public string DatabaseName { get; init; } = TestDatabaseRouter.LegacyDatabase;

private (string app, string admin, string migrator) Conns
    => TestDatabaseRouter.ConnectionsFor(DatabaseName);

// For test assertions only — the app connection string actually wired below.
internal string ResolvedAppConnectionString =>
    UseAppRoleConnection ? Conns.app : Conns.admin;
```

Then in `ConfigureWebHost`, replace the `UseSetting("ConnectionStrings:...")` lines that referenced the old consts:

```csharp
builder.UseSetting("ConnectionStrings:ApplicationConnection",
    UseAppRoleConnection ? Conns.app : Conns.admin);
builder.UseSetting("ConnectionStrings:AdminConnection",       Conns.admin);
builder.UseSetting("ConnectionStrings:MigrationConnection",   Conns.migrator);
```

In `TestDbFixture.cs`, replace the three `internal const string` (`:33-40`) with router-backed statics (they are used by AppRole tests directly, so keep the same names):

```csharp
// TestDbFixture.cs — replace the three internal const strings (:33-40) with:
internal static string AppConnectionString => TestDatabaseRouter.ConnectionsFor(DatabaseName).app;
internal static string AdminConnectionString => TestDatabaseRouter.ConnectionsFor(DatabaseName).admin;
internal static string MigratorConnectionString => TestDatabaseRouter.ConnectionsFor(DatabaseName).migrator;

// TestDbFixture is used by the AppRoleTests collection; its DB is that collection's.
internal static string DatabaseName => TestDatabaseRouter.DatabaseForCollection("AppRoleTests");
```

Note: `const` → `static` property means any `switch`/attribute use of these must still compile (they are referenced as expressions, not constant patterns — verify with a build).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj` then `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~Waf_uses_the_database_name"`
Expected: build succeeds (the const→static change compiles everywhere), test PASSES.

- [ ] **Step 5: Sanity — the existing suite still passes under N=1 fallback**

Run (no clone env, so router returns legacy DB — today's behaviour): `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ProjectCeres.Tests.Integration.Configuration"`
Expected: PASS — a small integration slice proves the router seam didn't break connection wiring.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Tests/Integration/TestDbFixture.cs ProjectCeres.Tests/Integration/WafCollection.cs ProjectCeres.Tests/Integration/TestDatabaseRouterTests.cs
git commit -m "feat(12.18): WAF + TestDbFixture read DB name from the router (N=1 fallback intact)"
```

---

## Task 3: setup-test-db.sh — template-clone mode

**Files:**
- Modify: `tools/ci/setup-test-db.sh`
- Test: manual local run (bash; no xUnit harness).

**Interfaces:**
- Produces: `setup-test-db.sh --template --clones N` provisions `project_ceres_test_template` + `project_ceres_test_1..N` + the 4 serial DBs. `setup-test-db.sh <db-name>` (existing) unchanged.

- [ ] **Step 1: Write the change**

Restructure the script to branch on `--template`. Keep the existing single-DB path verbatim as a function `provision_one <db>`:

```bash
#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
log() { echo "[setup-test-db] $*" >&2; }

provision_roles() {   # $1 = db
  log "applying setup-postgres-roles.sql to $1"
  psql -d "$1" -v ON_ERROR_STOP=1 -f "$REPO_ROOT/scripts/setup-postgres-roles.sql" >/dev/null
}

migrate_db() {        # $1 = db
  local conn="Host=localhost;Database=${1};Username=ceres_migrator;Password=ceres_migrator_dev_password"
  log "migrating $1"
  dotnet ef database update --project "$REPO_ROOT/ProjectCeres" \
    --context AppDbContext --connection "$conn"
}

create_if_absent() {  # $1 = db
  if ! psql -lqt | cut -d '|' -f1 | grep -qw "$1"; then
    log "creating database $1"; createdb "$1"
  fi
}

provision_one() {     # $1 = db  (the existing single-DB behaviour)
  create_if_absent "$1"
  provision_roles "$1"
  migrate_db "$1"
  log "done: $1 provisioned + migrated"
}

clone_from_template() {   # $1 = target db, $2 = template db
  # Drop any stale copy so the clone is deterministic run-to-run.
  psql -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$1\";" >/dev/null
  log "cloning $1 from template $2"
  psql -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE \"$1\" TEMPLATE \"$2\";" >/dev/null
  # TEMPLATE copies schema + data + ownership, but re-apply grants to be safe
  # (role grants are per-database and not guaranteed to survive every PG minor).
  provision_roles "$1"
}

if [[ "${1:-}" == "--template" ]]; then
  shift
  clones=1
  if [[ "${1:-}" == "--clones" ]]; then clones="${2:?--clones needs a count}"; fi

  TEMPLATE="project_ceres_test_template"
  # 1. Build the template once (idempotent).
  provision_one "$TEMPLATE"
  # 2. Make it a real template + forbid connections so CREATE DATABASE ... TEMPLATE
  #    is guaranteed to see zero other sessions (PG16 § 23.3).
  psql -d postgres -v ON_ERROR_STOP=1 -c \
    "UPDATE pg_database SET datistemplate=true, datallowconn=false WHERE datname='$TEMPLATE';" >/dev/null

  # 3. Clone the N bucket DBs + the 4 serial DBs.
  for k in $(seq 1 "$clones"); do clone_from_template "project_ceres_test_${k}" "$TEMPLATE"; done
  for s in ratelimit mfaratelimit approle rls; do
    clone_from_template "project_ceres_test_${s}" "$TEMPLATE"
  done
  log "done: template + ${clones} bucket DBs + 4 serial DBs provisioned"
else
  # Legacy single-DB mode (E2E uses this: setup-test-db.sh project_ceres_e2e).
  provision_one "${1:?usage: setup-test-db.sh <db-name>  |  --template --clones N}"
fi
```

- [ ] **Step 2: Run locally to verify it provisions**

Run: `CERES_TEST_DB_CLONES=4 tools/ci/setup-test-db.sh --template --clones 4`
Then verify: `psql -lqt | cut -d '|' -f1 | grep -E 'project_ceres_test_(template|[1-4]|ratelimit|mfaratelimit|approle|rls)' | sort`
Expected: 10 databases listed (template + 4 buckets + 4 serial).

- [ ] **Step 3: Verify the legacy single-DB mode still works**

Run: `tools/ci/setup-test-db.sh project_ceres_e2e`
Expected: `done: project_ceres_e2e provisioned + migrated` (E2E path unbroken).

- [ ] **Step 4: Verify a cloned DB has the schema (proves TEMPLATE copied it)**

Run: `psql -d project_ceres_test_2 -c '\dt' | grep -c AspNetUsers`
Expected: `1` (the migrated schema is present in the clone without a per-clone migration).

- [ ] **Step 5: Commit**

```bash
git add tools/ci/setup-test-db.sh
git commit -m "feat(12.18): setup-test-db.sh --template --clones N (one migration, N clones)"
```

---

## Task 4: Bucket assignment tool + capture runtimes

**Files:**
- Create: `tools/bucket-integration-tests.sh`
- (data) reads a `dotnet test` trx/console timing dump

**Interfaces:**
- Produces: a deterministic mapping of the 136 `[Collection("IntegrationTests")]` files → `IntegrationParallel1..4`, written by rewriting each file's attribute. Serial-collection files untouched.

- [ ] **Step 1: Capture per-file runtimes**

Run the full suite once (backgrounded per the no-concurrent-run rule; this is the one full run this task needs):

```bash
nohup dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj \
  --logger "trx;LogFileName=timings.trx" > /tmp/dt-timings.log 2>&1 &
```
Wait for completion (poll `/tmp/dt-timings.log` for `Passed!`). The `.trx` has per-test durations; aggregate to per-file (sum durations of tests whose class is in that file).

- [ ] **Step 2: Write the bucketer**

```bash
#!/usr/bin/env bash
# tools/bucket-integration-tests.sh — greedy longest-processing-time bin-pack of the
# IntegrationTests files into N buckets balanced by runtime, then rewrite each file's
# [Collection("IntegrationTests")] to its bucket. Serial collections are never touched.
set -euo pipefail
N="${1:-4}"
TRX="${2:?usage: bucket-integration-tests.sh <N> <timings.trx>}"
ROOT="ProjectCeres.Tests/Integration"

# 1. list the IntegrationTests files
mapfile -t FILES < <(grep -rlF '[Collection("IntegrationTests")]' "$ROOT" --include='*.cs' | sort)

# 2. per-file duration (ms) from the trx — parse <UnitTestResult testName duration>,
#    map testName's class → file via `grep -l "class <Class>"`. Emit "file<TAB>ms".
#    (Implementation: a short awk/xmllint pass; durations that can't be matched default
#     to the median so an un-timed file still gets placed.)
# 3. greedy LPT: sort files desc by ms; assign each to the currently-lightest bucket.
# 4. rewrite: for each file in bucket k, sed-replace the attribute.
for f in "${FILES[@]}"; do
  k=$(next_bucket)   # from the LPT assignment computed above
  sed -i '' "s/\[Collection(\"IntegrationTests\")\]/[Collection(\"IntegrationParallel${k}\")]/" "$f"
done
echo "rebucketed ${#FILES[@]} files into $N buckets"
```

(The awk/xmllint parsing and the `next_bucket`/LPT loop are written out fully in the tool; the plan's executor implements the greedy assignment — sort files by descending ms, push each onto a min-heap-of-bucket-totals. If the `.trx` parse yields no timing for a file, assign the run's median so placement is still deterministic.)

- [ ] **Step 3: Run the bucketer**

Run: `bash tools/bucket-integration-tests.sh 4 /path/to/timings.trx`
Expected: `rebucketed 136 files into 4 buckets`. Verify: `grep -rhoE 'IntegrationParallel[1-4]' ProjectCeres.Tests/Integration --include='*.cs' | sort | uniq -c` shows a roughly even count per bucket.

- [ ] **Step 4: Commit the tool + the rebucketed attributes**

```bash
git add tools/bucket-integration-tests.sh ProjectCeres.Tests/Integration
git commit -m "feat(12.18): bin-pack 136 integration files into 4 runtime-balanced buckets"
```

---

## Task 5: Bucket collection definitions + retire the mega-collection

**Files:**
- Create: `ProjectCeres.Tests/Integration/BucketCollections.cs`
- Modify: `ProjectCeres.Tests/Integration/WafCollection.cs` (remove `IntegrationCollection`)

**Interfaces:**
- Consumes: `TestWebApplicationFactory.DatabaseName`, `TestDatabaseRouter.DatabaseForCollection`.
- Produces: 4 `[CollectionDefinition("IntegrationParallelK")]` classes, each with `ICollectionFixture<>` over a per-bucket sweeping WAF whose `DatabaseName` is the bucket's DB.

- [ ] **Step 1: Write the bucket collections + per-bucket factories**

```csharp
// ProjectCeres.Tests/Integration/BucketCollections.cs
namespace ProjectCeres.Tests.Integration;

// One sweeping factory subclass per bucket, each pinned to its bucket DB via the
// router. Sweeping is inherited from SweepingTestWebApplicationFactory; because the
// factory's DI resolves AppDbContext from DatabaseName, the sweep scopes to THIS
// bucket's DB automatically (Stage 12.18 sweep-scoping).
public sealed class Bucket1Factory : SweepingTestWebApplicationFactory
{ public Bucket1Factory() : base() { } public override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1"); }
// … Bucket2Factory / Bucket3Factory / Bucket4Factory identically for 2/3/4.

[CollectionDefinition("IntegrationParallel1")]
public class IntegrationParallel1Collection : ICollectionFixture<Bucket1Factory> { }
[CollectionDefinition("IntegrationParallel2")]
public class IntegrationParallel2Collection : ICollectionFixture<Bucket2Factory> { }
[CollectionDefinition("IntegrationParallel3")]
public class IntegrationParallel3Collection : ICollectionFixture<Bucket3Factory> { }
[CollectionDefinition("IntegrationParallel4")]
public class IntegrationParallel4Collection : ICollectionFixture<Bucket4Factory> { }
```

Because `DatabaseName` is an `init`-only property (Task 2), add a `protected virtual string InitDbName` to `TestWebApplicationFactory` that the ctor uses to set `DatabaseName`, so a subclass can override the DB without a settable property leaking. Adjust Task 2's property to:

```csharp
protected virtual string InitDbName => TestDatabaseRouter.LegacyDatabase;
public string DatabaseName { get; }
protected TestWebApplicationFactory() { DatabaseName = InitDbName; }
```

- [ ] **Step 2: Remove the old mega-collection**

Delete the `IntegrationCollection` `[CollectionDefinition("IntegrationTests")]` class from `WafCollection.cs` (its fixtures `SweepingTestWebApplicationFactory` etc. stay — the bucket factories subclass them). No file should still reference `"IntegrationTests"` after Task 4's rewrite.

- [ ] **Step 3: Verify it compiles and no orphan collection remains**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj` and `! grep -rF '[Collection("IntegrationTests")]' ProjectCeres.Tests --include='*.cs'`
Expected: build succeeds; grep finds nothing (exit 0 from the `!`).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Integration/BucketCollections.cs ProjectCeres.Tests/Integration/WafCollection.cs
git commit -m "feat(12.18): 4 bucket collections pinned to per-bucket DBs; retire mega-collection"
```

---

## Task 6: xunit.runner.json — turn on collection parallelism

**Files:**
- Modify: `ProjectCeres.Tests/xunit.runner.json`

- [ ] **Step 1: Edit the config**

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "diagnosticMessages": true,
  "longRunningTestSeconds": 30,
  "parallelizeTestCollections": true,
  "maxParallelThreads": 4
}
```

- [ ] **Step 2: Verify parallel execution locally (provisioned)**

Run: `CERES_TEST_DB_CLONES=4 tools/ci/setup-test-db.sh --template --clones 4` then
`CERES_TEST_DB_CLONES=4 dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj`
Expected: full suite PASSES; wall-clock lower than the serial baseline (note the number for the success criterion).

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/xunit.runner.json
git commit -m "feat(12.18): enable xUnit collection parallelism (maxParallelThreads 4)"
```

---

## Task 7: Infra self-tests (isolation + sweep-scoping + RLS-under-parallelism)

**Files:**
- Create: `ProjectCeres.Tests/Integration/ParallelIsolationTests.cs`

**Interfaces:**
- Consumes: `TestDatabaseRouter`, two bucket factories.

- [ ] **Step 1: Write the failing tests**

```csharp
// ProjectCeres.Tests/Integration/ParallelIsolationTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using Xunit;

namespace ProjectCeres.Tests.Integration;

// Not in a bucket collection — this asserts ACROSS buckets, so it builds its own
// factories. Requires CERES_TEST_DB_CLONES>=2 (skips cleanly under the N=1 fallback).
public class ParallelIsolationTests
{
    private static bool ClonesEnabled => TestDatabaseRouter.CloneCount >= 2;

    [Fact]
    public void Two_buckets_resolve_distinct_databases()
    {
        if (!ClonesEnabled) return; // N=1 fallback: nothing to isolate
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1")
            .Should().NotBe(TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2"));
    }

    [Fact]
    public async Task A_row_written_in_bucket1_is_absent_in_bucket2()
    {
        if (!ClonesEnabled) return;
        var marker = $"iso-{Guid.NewGuid():N}@bucket-test.local";
        await using var b1 = new Bucket1Factory();
        await using var b2 = new Bucket2Factory();

        await AuthTestFixture.RegisterUserAsync(b1, marker);

        using var scope2 = b2.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var seenInB2 = await db2.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.Email == marker);
        seenInB2.Should().BeFalse("bucket 2 has its own database; bucket 1's user must not appear");
    }
}
```

- [ ] **Step 2: Run to verify they fail then pass**

Run (provisioned): `CERES_TEST_DB_CLONES=4 dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ParallelIsolationTests"`
Expected: PASS (distinct DBs; cross-bucket invisibility). Under no env: both return early (fallback), still green.

- [ ] **Step 3: RLS-under-parallelism check — run the security suite provisioned**

Run: `CERES_TEST_DB_CLONES=4 dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~Integration.Rls|FullyQualifiedName~AppRole"`
Expected: PASS — the RLS/AppRole collections on their own DBs show no cross-tenant leak under parallelism. This is the load-bearing safety assertion.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Integration/ParallelIsolationTests.cs
git commit -m "test(12.18): cross-bucket isolation + RLS-under-parallelism self-tests"
```

---

## Task 8: CI wiring + docs + stage close

**Files:**
- Modify: `.github/workflows/ci.yml` (`dotnet-test` provision step)
- Modify: `docs/testing.md`, `docs/runbooks/local-dev-troubleshooting.md`
- Modify: `docs/roadmap-phase-three.md`, `CHANGELOG.md`

- [ ] **Step 1: CI provision + env**

In `ci.yml` `dotnet-test`, change the provision step to build the clones and set the env for the test run:

```yaml
      - name: Provision + migrate the test databases
        run: |
          which dotnet-ef && dotnet-ef --version
          tools/ci/setup-test-db.sh --template --clones 4
          tools/ci/setup-test-db.sh project_ceres_e2e
        env:
          PGHOST: localhost
          PGUSER: postgres
          PGPASSWORD: postgres
      # … Build step unchanged …
      - name: Full test suite
        run: dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --no-build
        env:
          CERES_TEST_DB_CLONES: "4"
```

(`maxParallelThreads: 4` is already in `xunit.runner.json` from Task 6; the env var is what flips the router out of fallback.)

- [ ] **Step 2: Validate YAML + docs**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('YAML OK')"`
Add to `docs/testing.md` a "Parallel integration tests (Stage 12.18)" subsection (N=4 buckets, `--template --clones`, the N=1 fallback, the RLS-pool-isolation rationale) and to `docs/runbooks/local-dev-troubleshooting.md` the local provision command (`CERES_TEST_DB_CLONES=4 tools/ci/setup-test-db.sh --template --clones 4`).

- [ ] **Step 3: Push and measure the real speedup (honest limit)**

The wall-clock win only exists on the CI runner. Push, then via `gh` (per CLAUDE.md § Handling CI failures) watch the run and compare the `dotnet-test` **Full test suite** step duration against the ~5m40s baseline:

```bash
git push origin main
# watch, then:
gh run view <id> --json jobs --jq '.jobs[] | select(.name=="dotnet-test") | .steps[] | select(.name=="Full test suite") | "\(.startedAt) \(.completedAt)"'
```
Record the before/after in the roadmap entry. If a bucket races (cross-bucket failure) or the RLS suite goes red, that is a real isolation defect — root-cause, do not retry away.

- [ ] **Step 4: Close the stage**

Mark §12.18 done in `docs/roadmap-phase-three.md` with the measured before/after duration; run `sync-docs` + `changelog-sync`; add the CHANGELOG entry under [Unreleased] → Phase 3 → Tests.

```bash
git add .github/workflows/ci.yml docs/testing.md docs/runbooks/local-dev-troubleshooting.md docs/roadmap-phase-three.md CHANGELOG.md
git commit -m "ci(12.18): run integration suite across 4 bucket DBs; docs + stage close"
git push origin main
```

---

## Self-Review

**Spec coverage:** Router seam + N=1 fallback (Task 1-2 ✓); template-clone provisioning with `datallowconn=false` (Task 3 ✓); runtime-balanced bucketing of 136 files (Task 4 ✓); N bucket collections replacing the mega-collection (Task 5 ✓); sweep-scoping — falls out of the router change, asserted (Task 5 factory + Task 7 ✓); `xunit.runner.json` parallelism (Task 6 ✓); CI + docs wiring (Task 8 ✓); infra self-tests incl. RLS-under-parallelism (Task 7 ✓); 4 serial collections each get their own DB (Task 1 map + Task 3 clone loop ✓); measured-speedup verification via gh (Task 8 step 3 ✓).

**Placeholder scan:** Task 4's bucketer leaves the `.trx`-parse/LPT loop described rather than fully coded — flagged explicitly as the one place the executor writes the parsing from the described algorithm (deterministic greedy LPT, median for un-timed files). Every other step has literal code.

**Type consistency:** `DatabaseName` (init/get) ↔ `InitDbName` (protected virtual) reconciled in Task 5 step 1 (Task 2's settable form is superseded there — the executor uses the `InitDbName`-ctor form). `TestDatabaseRouter.ConnectionsFor`/`DatabaseForCollection`/`CloneCount` signatures are consistent across Tasks 1, 2, 5, 7. Serial-collection DB names (`_ratelimit`/`_mfaratelimit`/`_approle`/`_rls`) match between Task 1's map and Task 3's clone loop.

**One reconciliation note for the executor:** Task 2 introduces `DatabaseName` as `{ get; init; }` for its isolated test; Task 5 changes it to `{ get; }` set from `InitDbName` in the ctor so bucket subclasses can override. Implement Task 2 with the `init` form to pass its test, then adopt the `InitDbName` form in Task 5 — or implement the `InitDbName` form from the start and adapt Task 2's test to construct a subclass. The latter is cleaner; either is correct.
