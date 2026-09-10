# Stage 12.18 — Parallel integration tests via DB-per-collection — Design

**Status:** Draft for review · **Date:** 2026-09-10 · **Stage:** 12.18 (Phase 3 test-infra)

## Goal

Cut the CI `dotnet-test` full-suite step (currently ~5m40s, the pipeline's longest
job) by running the integration suite in parallel. Today all 136 integration test
files share a single serialized xUnit collection; this stage splits them into N
balanced collections, each pinned to its own isolated Postgres database, so the N
collections run concurrently while files within a collection still serialize.

## Why the suite is serial today (and why the DB can't "just sort it out")

The single `IntegrationTests` collection (`WafCollection.cs:422`) exists because
three failure modes appear when integration tests share one `project_ceres_test`
database — and none is the kind Postgres isolation levels resolve:

1. **Unique-constraint collisions on shared identity.** `Settings.UserId` is
   `UNIQUE` (`AppDbContext.cs:380`). Two tests creating settings for overlapping
   users, or a leftover row surviving into the next test, throws `duplicate key
   value violates unique constraint "IX_Settings_UserId"`. A higher isolation
   level makes one transaction *abort with a serialization error*, not both
   succeed — worse for a test, not better.
2. **Read assertions over a shared table.** Tests assert counts/contents of
   `Accounts`, `Categories`, etc. Another test's committed rows are also present.
   The project already mandates per-test-marker filtering
   (`feedback_filter_test_queries_by_test_data`), but even perfect filtering
   can't stop a parallel commit from changing what a `COUNT(*)` or the user-sweep
   sees mid-assertion.
3. **The RLS GUC is connection-scoped (the crux).** Row-level security sets a
   Postgres session variable — `SET LOCAL app.current_user_ref = '<uuid>'` — on
   the connection inside a transaction (`PreAuthRlsScope.cs`), and RLS policies
   filter every row against it. With Npgsql connection pooling, a parallel worker
   borrowing a pooled connection whose GUC another worker set would read *as that
   user* — a cross-tenant session-state leak the database cannot arbitrate.

**Lock ordering is the wrong tool** — that solves deadlocks (opposite-order row
locking), which is not the problem. The current single serialized collection is
already a process-level mutex; it is correct but it is the thing making the suite
slow. The standard fix is **isolation-by-database**: give each parallel unit its
own DB, so unique constraints, read sets, and — critically — connection pools are
all separate. Cross-tenant GUC leak becomes *structurally unrepresentable*, not
merely unlikely.

## Research findings (authoritative)

- **xUnit parallelism is per-collection, threads within one process, with no
  stable worker ordinal.** Parallel collections run on thread-pool threads; xUnit
  exposes no `worker 0..N-1` id to a test. So "one DB per worker" is not directly
  expressible — isolation must be keyed on the *collection*, which xUnit does give
  each collection fixture at construction.
  ([xUnit parallelism docs](https://xunit.net/docs/running-tests-in-parallel).)
- **`CREATE DATABASE … TEMPLATE` requires no other connection to the template
  during the copy.** Cloning is a fast file copy, but the template must be idle.
  Setting `datallowconn = false` on the template prevents new connections and thus
  guarantees the "no other connections" precondition — the practical pattern for
  parallel test-DB provisioning.
  ([PostgreSQL 16 § 23.3 Template Databases](https://www.postgresql.org/docs/16/manage-ag-templatedbs.html).)
- **Npgsql connection pools are keyed by connection string.** A distinct database
  name yields a distinct pool, so per-DB isolation also isolates the pool — the
  property that closes the RLS-GUC leak.

## Architecture

The single `IntegrationTests` collection → **N balanced bucket collections**
`IntegrationParallel1..N`, each pinned to its own database
`project_ceres_test_1..N`. xUnit runs the N buckets in parallel (one thread each);
files within a bucket serialize (preserving the intra-DB no-race guarantee). N
defaults to the CI core count (4), overridable by env var.

Only the **database name** varies per bucket. The three roles
(`ceres_app`/`ceres_admin`/`ceres_migrator`) and their `_dev_password` passwords
are identical across all N DBs — they are per-DB grants applied at provision time.

The N databases are provisioned **up-front, before any test connects**: build one
migrated `project_ceres_test_template`, mark it `datallowconn=false`, then
`CREATE DATABASE project_ceres_test_k TEMPLATE project_ceres_test_template` per
bucket (a file copy, not N migrations), re-applying `setup-postgres-roles.sql` to
each clone (grants are not reliably copied by `TEMPLATE`).

The four already-serial collections (`RateLimitTests`, `MfaRateLimitTests`,
`AppRoleTests`, `RlsTests` — 26 files total) each get their own dedicated DB too,
so nothing shares. Their intra-serialization is deliberate and unchanged.

## Components

| Component | Responsibility |
|---|---|
| `TestDatabaseRouter` (new, `ProjectCeres.Tests/Integration/`) | Owns `collection name → database name` and builds the three role connection strings for a DB name. Replaces today's hardcoded `const … Database=project_ceres_test` in the WAF hierarchy. |
| N bucket collection definitions (new) | `IntegrationParallel1Collection … IntegrationParallelNCollection`, each an `ICollectionFixture<>` over a WAF parameterized by DB name. Generated, not hand-written. Replace the single `IntegrationCollection`. |
| 136 `[Collection("IntegrationTests")]` → bucket attribute (mechanical) | Each file's attribute becomes its assigned bucket, balanced by **runtime** (greedy bin-pack from captured per-file durations), not file count. |
| `tools/ci/setup-test-db.sh` (extended) | New `--template --clones N` mode: migrate template once, `datallowconn=false`, clone N + re-grant. Existing single-DB mode (for `project_ceres_e2e`) unchanged. |
| `xunit.runner.json` | Add `"parallelizeTestCollections": true` + `"maxParallelThreads": <N>`. |
| WAF hierarchy (`TestWebApplicationFactory`, `TestDbFixture`, `RateLimitedAuthTestWebApplicationFactory`) | Ask `TestDatabaseRouter` for connection strings instead of holding constants. The collection fixture carries the DB name; the WAF reads it — no `AsyncLocal`, no worker-ordinal. **The collection is the key.** |

The load-bearing seam is `TestDatabaseRouter`; everything else is mechanical once
it owns DB-name resolution. The connection-string constants are centralized in a
few files (`TestDbFixture.cs`, `WafCollection.cs`, referenced by a handful of
tests), so the seam is bounded.

## Data flow

**Provision (once, before tests):** `setup-test-db.sh --template --clones N` →
`project_ceres_test_template` (createdb → roles → `dotnet ef database update` →
RLS) → `ALTER DATABASE …_template WITH ALLOW_CONNECTIONS false` → loop
`CREATE DATABASE project_ceres_test_k TEMPLATE …_template` then re-apply
`setup-postgres-roles.sql` to each clone. Net: one migration + N cheap clones.

**Run:** xUnit starts N bucket collections in parallel. Each bucket's WAF resolves
its role connection strings from `TestDatabaseRouter` for *its* DB. Distinct DB ⇒
distinct Npgsql pool ⇒ the `SET LOCAL app.current_user_ref` GUC can never cross
buckets. Within a bucket, files serialize (today's guarantee); intra-DB GUC
discipline is unchanged.

## RLS safety (security-critical)

Isolation-by-database makes cross-tenant GUC leak **unrepresentable**: bucket 3's
pooled connection is in bucket 3's pool (keyed on `Database=project_ceres_test_3`)
and can never be handed to bucket 1. This is stronger than the current
serialization, which merely avoids concurrency; here concurrency is safe by
construction. The RLS security tests (`Integration/Rls/`, `AppRole/`) keep their
own dedicated DBs and are unaffected.

## The sweep-teardown interaction (highest-risk)

`SweepingTestWebApplicationFactory` deletes abandoned test users on collection
teardown (`WafCollection.cs:429`). Today it sweeps `project_ceres_test`. Each
bucket's sweeping factory must scope its `DELETE` to *its own* DB — which falls
out automatically once the WAF's connection strings come from the router (the
sweep runs through the same scoped `AdminDbContext`). The rule *"ad-hoc factories
must never sweep"* (`project_test_db_orphan_leak`) stays intact. A new test
asserts the sweep touches only its bucket's DB.

## Error handling / fallback

- **No `--clones`/`maxParallelThreads` env → N=1 fallback.** The router maps all
  buckets to the single legacy `project_ceres_test` and xUnit serializes them —
  today's behavior — so a plain `dotnet test` against an un-cloned local DB still
  works. No developer's existing workflow breaks.
- **A clone fails (template busy / half-provisioned) → the provision script fails
  loudly before any test runs**, never a silently partial DB set.

## CI & local wiring

- **CI (`ci.yml` `dotnet-test`):** provision step becomes `setup-test-db.sh
  --template --clones 4` (plus the unchanged `project_ceres_e2e` line for the
  guard test). `xunit.runner.json` sets `maxParallelThreads: 4` (the GitHub runner
  has 4 cores). The `dotnet test` command is byte-for-byte unchanged — parallelism
  is entirely config- and provision-driven.
- **Local:** same `--template --clones 4` against a local Postgres; a developer
  with only the legacy single DB hits the N=1 fallback. `docs/testing.md` and
  `docs/runbooks/local-dev-troubleshooting.md` get the new provision command.

## Testing the infrastructure itself

1. Two buckets see **disjoint** data (seed in bucket A, absent in bucket B).
2. A bucket's sweep leaves other buckets' rows **intact**.
3. `TestDatabaseRouter` resolves **distinct** DBs per collection and identical
   roles/passwords.
4. N=1 fallback: with no clone env, all buckets resolve the single legacy DB.
5. The full RLS security suite stays green under parallelism (no cross-tenant
   leak) — the load-bearing safety assertion.

## Success criterion

The CI `dotnet-test` full-suite step drops materially from ~5m40s — target
approaching `5m40s ÷ min(N, effective-parallelism)`, bounded by the slowest bucket
and the 4-core runner — with zero cross-bucket data leaks and every RLS/security
test still green. Provision overhead stays low (one migration + N clones, not N
migrations).

## Out of scope (YAGNI)

- **Dynamic N per machine core-count** — fixed N=4, env-overridable, is enough.
- **DB-per-test-class** — rejected for clone/pool churn (~200 cycles/run).
- **Parallelizing E2E / Vitest** — E2E stays `workers:1` (§12.17 tracks its
  retry policy); Vitest already parallelizes at `maxWorkers:4`.
- **Re-architecting the four already-serial special collections** beyond giving
  each its own DB — their intra-serialization is deliberate.
