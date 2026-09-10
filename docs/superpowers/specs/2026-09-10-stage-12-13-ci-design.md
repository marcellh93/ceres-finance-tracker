# Stage 12.13 — CI pipeline (GitHub Actions) — Design

**Status:** Draft for review · **Date:** 2026-09-10 · **Stage:** 12.13 (accelerated from Stage 16.8 / 16.9 / 16.16)

## Goal

A GitHub Actions CI pipeline that runs the project's full test + quality gate on a **fresh, stateless environment** on every push to `main`. CI is the un-tiered, environment-independent **superset** of the local Stop-hook test gate — it never skips, and it proves the suite passes somewhere other than the author's machine. It does **not** replace the Stop hook (which stays fast + tiered for the inner loop).

## Why now (acceleration rationale)

CI was scheduled at Stage 16 (16.8 dependency scan, 16.9 secret scan, 16.16 E2E-in-CI), the last Phase-3 stage. Pulled forward on the user's direction (2026-09-10) as its own stage §12.13. Governed by [ADR-0070](../../decisions/ADR-0070-ci-cd-on-github-actions.md) (CI/CD on GitHub Actions) and [ADR-0071](../../decisions/ADR-0071-e2e-testing-on-playwright.md) (E2E on Playwright). The Playwright config was already built CI-aware (`workers: 1` locally; sharding "owned by the Stage 16.16 CI bring-up") — this stage is that bring-up.

## Constraints (from the project)

- **Stay on main, no branches/PRs** (`feedback_stay_on_main`). CI triggers on push to `main`; the `pull_request` trigger is present but dormant (fires only if a PR is ever opened); `workflow_dispatch` gives a manual "Run" button.
- **.NET 10** (`net10.0`); **pnpm pinned to 10.33.2** (`packageManager` in `ProjectCeres.Client/package.json`); Node 22 LTS in CI.
- **No secrets in the workflow.** No GitHub Secrets and no committed secret literal. The integration suite already supplies its own token-lookup secret via `WafCollection.cs` `UseSetting` (the §12.12 fix), so `dotnet-test` needs only a reachable Postgres; the E2E job's `run-server.sh` generates a fresh one with `openssl rand -base64 32`. DB access uses the `*_dev_password` role passwords already committed in `scripts/setup-postgres-roles.sql`. Resend is not needed (Dev/Test → `LogOnlyEmailService`). (This replaced an earlier plan to embed a fixed base64 token literal — gitleaks, the project's own scanner, correctly flagged it as secret-shaped; self-supplying/generating is both cleaner and scanner-clean.)
- **No drift.** Each job runs the *same commands* the Stop hook (`run-tests.sh`) and the evidence build-matrix (`tools/agent-env/build-matrix.sh`) already run. CI = their un-tiered superset, not a second definition of "passing."
- **The marker/status roadmap-consistency hook must stay green** through the bookkeeping edits.

## Architecture

One workflow file: **`.github/workflows/ci.yml`**.

**Triggers:**
```yaml
on:
  push: { branches: [main] }
  pull_request:               # dormant — no PR workflow today, armed for the future
  workflow_dispatch:          # manual run button
```

**Concurrency guard** — supersede in-flight runs on rapid pushes to the same ref, without hiding failures within a run:
```yaml
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true
```

**Five parallel jobs, `strategy.fail-fast: false`** (so one failure never cancels the others — every push surfaces ALL failures), each on `ubuntu-latest`:

### Job 1 — `dotnet-test`
- **Postgres 16 service container** (`services.postgres`), health-checked.
- Provision step: run the **shared DB-setup script** (see § Shared DB setup) against `project_ceres_test`.
- `dotnet build` (Release or Debug — Debug, to match the Stop hook / build-matrix).
- **Full** `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj` (no filter) — the un-tiered tier-2.
- Env vars: `Authentication__TokenLookupSecret__Secret` (fixed test value), and the `ceres_*` connection strings the fixture expects are supplied by `WafCollection.cs` itself (it hardcodes `localhost` + `*_dev_password`), so only the secret + a reachable Postgres on `localhost:5432` are needed.

### Job 2 — `analyzer-test`
- No DB. `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`.
- Note the known transient: the `Microsoft.CodeAnalysis.Testing` harness fetches a ref-pack into a temp cache; a cold runner is the first fetch, so this job restores NuGet before running (see `project_analyzer_tests_refpack_flake`).

### Job 3 — `client-test`
- No DB. `pnpm/action-setup` (version from `packageManager`) + Node 22 + pnpm store cache.
- `pnpm --dir ProjectCeres.Client install --frozen-lockfile` → `pnpm --dir ProjectCeres.Client build` (tsc + vite + `check-size`) → `pnpm --dir ProjectCeres.Client test` (Vitest).

### Job 4 — `e2e`
- **Postgres 16 service container.**
- Provision `project_ceres_e2e` via the shared DB-setup script (the E2E DB is separate from the test DB — `run-server.sh` uses `project_ceres_e2e`).
- pnpm install + **Playwright browser cache** (`actions/cache` keyed on the Playwright version) + `pnpm exec playwright install --with-deps`.
- **Sharded across browsers**: `strategy.matrix.project: [chromium, firefox, webkit]`, running `playwright test --config e2e/playwright.golden.config.ts --project=<browser>`. The golden config's `webServer` runs `tools/e2e/run-server.sh` (builds+stages the SPA, migrates the e2e DB, boots `dotnet run` under `ASPNETCORE_ENVIRONMENT=E2E`). `workers:1` stays for determinism; the *matrix* is the parallelism.
- On failure: upload `e2e/.artifacts/` (trace + HTML report) via `actions/upload-artifact` with `if: failure()`.

### Job 5 — `repo-hygiene`
- `dotnet list package --vulnerable --include-transitive` → fail the job if any vulnerable package is reported (grep the output; non-zero on a match).
- **Secret scan:** `gitleaks` (via its GitHub Action) over the repo — chosen over GitHub-native secret scanning because native scanning needs repo-settings toggles the user would have to enable, whereas gitleaks runs self-contained in the workflow.
- **Node hook self-tests:** `node --test .claude/hooks/__tests__/` (the hook suite, incl. `roadmap-consistency-check`).
- **Roadmap consistency:** run `roadmap-consistency-check.js` against each `docs/roadmap-phase-*.md` and fail on any finding (the same check the PostToolUse hook runs locally).

## Shared DB setup (single source of truth)

The createdb → `setup-postgres-roles.sql` → `dotnet ef database update --connection <migrator>` sequence is currently inline in `tools/e2e/run-server.sh`. To avoid CI and local drifting, extract the DB bootstrap into a small reusable shell script — **`tools/ci/setup-test-db.sh <db-name>`** — that:
1. `createdb <db>` if absent,
2. applies `scripts/setup-postgres-roles.sql`,
3. runs `dotnet ef database update --project ProjectCeres --context AppDbContext --connection "Host=…;Database=<db>;Username=ceres_migrator;Password=ceres_migrator_dev_password"`.

`run-server.sh` is refactored to call this script for its own DB bootstrap (keeping its guarded-wipe step), so there is exactly one definition of "how a Ceres database is provisioned." CI's `dotnet-test` calls it with `project_ceres_test`; `e2e` calls it with `project_ceres_e2e`.

**Rationale:** this is the no-drift guarantee made concrete. A future change to the role model or migration invocation lives in one file that both CI and local E2E consume.

## Components / files

| File | Responsibility |
|---|---|
| `.github/workflows/ci.yml` (new) | The five-job workflow. |
| `tools/ci/setup-test-db.sh` (new) | Shared createdb + roles + migrate for a named DB. |
| `tools/e2e/run-server.sh` (modified) | Refactored to call `setup-test-db.sh` for its bootstrap; guarded-wipe unchanged. |
| `docs/testing.md` (modified) | New § CI: what runs, where, and the "additive to the Stop hook" model. |
| `docs/roadmap-phase-three.md` (modified) | New §12.13 stage (Done on merge); Stage 16.8/16.9/16.16 items → `[→]` pointing to §12.13. |
| `CHANGELOG.md` (modified) | Tooling/CI entry under [Unreleased]. |

## Data flow (a push to main)

1. Push to `main` → workflow triggers; concurrency guard cancels any superseded run.
2. Five jobs start in parallel. DB-needing jobs spin up their own Postgres service + provision their own DB via `setup-test-db.sh` (isolated per job — no cross-job collision; the "never run dotnet test concurrently on the shared DB" rule is a *local* concern and does not apply, since each CI job has its own database).
3. Each job runs its command set; failures upload artifacts (E2E) and report per-job.
4. The workflow is green only if all five jobs pass. Author sees every failure from one push (fail-fast off).

## Error handling / known snags (first-run iteration)

I can author correct YAML and dry-run the shell locally, but a **real Actions run only happens on push** — I cannot watch a remote run from here. Expected first-run iteration points, in likelihood order:
- **Playwright cache key** — must key on the resolved Playwright version, else stale/cold cache.
- **Postgres service readiness** — the health-check must gate the provision step (`pg_isready` loop or the service `options` health check).
- **Path casing** — the `'"SupportTickets"'::regclass` pattern and file paths are case-sensitive on Linux runners (macOS dev is case-insensitive); a mis-cased path passes locally, fails in CI. (This is exactly a class of bug CI exists to catch.)
- **`--frozen-lockfile`** drift — if the committed lockfile is stale, install fails; surfaces a real problem.
- **`dotnet ef` tool availability** — CI must `dotnet tool restore` or install `dotnet-ef` before `database update`.

The recovery loop is explicit: ship the workflow → user pushes → we read the Actions log → fix → repeat. This is stated so the "done" bar for §12.13 is "the workflow file is correct and the commands match local," with a follow-up `[ ]` for "first green run on GitHub" that only the user's push can satisfy.

## Testing (how we verify the CI itself)

- **YAML validity:** parse `ci.yml` (a YAML linter / `node -e` yaml parse) — no syntax errors.
- **Shell script:** `tools/ci/setup-test-db.sh` runs locally against a throwaway DB and produces a migrated schema; `run-server.sh` still works after the refactor (run the E2E suite locally once, unchanged green).
- **Command parity:** each CI job's command is the same string the Stop hook / build-matrix uses (asserted by reading both, documented in the plan).
- **First green run:** a `[ ]` owed at §12.13 that only the user's push satisfies (honest limitation, tracked, not faked).

## Out of scope (explicit)

- **CD / deploy** — no deployment step; that stays at Stage 16.11.
- **Docker** — CI runs on native runners; it does not build or consume the container image (§12.14 is independent, per the user's "both now, but independent" choice).
- **Real production secrets / non-dev values** — fixed test-only values only; real secrets come at actual Stage 16 hosting.
- **Matrix over OSes / .NET versions** — single ubuntu-latest, single .NET 10. YAGNI for a solo single-target project.
- **Branch protection rules** — requires repo settings + the branch workflow the project doesn't use.
