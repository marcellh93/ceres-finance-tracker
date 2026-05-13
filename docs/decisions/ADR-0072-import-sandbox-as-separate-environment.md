# ADR-0072 — Import Sandbox as a Separate Environment (and Stage 11.5 Placement)

**Status:** Accepted (Phase 3, Batch 4)

**Date:** 2026-05-13

**Supersedes:** None. This ADR records a new decision; no prior decision is overturned.

## Context

The CSV/XLSX import pipeline has known parser bugs (transfer detection, liability-payment routing, multi-bank format quirks). The developer cannot iterate on parser fixes against their real account because:

- A bad parse contaminates real financial data.
- The bugs only surface after parsed rows interact with reports, balances, budgets, and dashboards — so per-parser unit tests are insufficient. The full app must execute against the data.
- The existing `ImportStagedTransaction` / `ImportStagedTransfer` / `ImportTransferExclusion` tables are a reconciliation-review queue (they record import rows that matched a pre-existing transaction), not a preview pipeline. They do not stage transfers as transfers or liability payments at all, and they do not let the developer iterate on a parse-and-discard loop.

This blocks meaningful parser development. The developer needs a controlled environment where:

1. The full app runs against fake bank data exactly as it would against real data.
2. There is zero risk of fake rows leaking into the real account, even through human error.
3. The data is **committed** to real tables in that environment (so reports, balances, budgets all execute the production code paths), not held in a preview-only structure.
4. Cleanup supports both **bulk wipe** ("delete every row from this import") and **selective deletion** ("multi-select 12 of these 47 rows and delete only those") — so a parser-iteration cycle is not gated on manually deleting rows one at a time.
5. The pattern is maintainable years later by a hired engineer who didn't write it. No "remember to flip the switch" discipline. Every safety property is a build-time check, integration test, or framework primitive — not a habit.

A research pass (2026-05-13) examined the patterns mature ETL teams use for this problem. Six candidate patterns emerged:

- **`IsTestData` boolean on every table** — antipattern; flag leaks into every query, every report has to remember it. Rejected.
- **Runtime "act as test user" switch inside one app** — single login, one click to enter/exit, banner when active. Initially appealing but failure-prone: forgetting to flip the switch contaminates real data, and the failure mode is silent until you notice the wrong row in a real report.
- **Separate-build pattern (multi-environment)** — two runs of the same compiled app, two `ASPNETCORE_ENVIRONMENT` values, two databases, two ports. Boundary is physical (which URL/process), not runtime. Forgetting is impossible.
- **Schema-per-tenant** — separate schemas inside one database. Forces dual migration paths and drifts the moment any migration is added. Rejected.
- **Separate database, same connection-string mechanics** — what the multi-environment pattern degenerates to once the launch profile and config are in place. The standard ASP.NET Core idiom.
- **Snapshot + restore** — `pg_dump` / `pg_restore` for "reset to a known good state." Complementary, not a replacement.

Two clarifications during brainstorming pinned the decision:

1. **"Tenant" was a term mismatch.** The research called the runtime-switch pattern "sandbox tenant." In Project Ceres vocabulary that is **multi-user** (multiple `AspNetUsers` rows isolated by the wall — Stage 7.5). The correct industry meaning of multi-tenant is **multi-environment** (physically separated deployments per customer/region, as the developer's day-job company operates). The sandbox is multi-environment, not multi-user. Once the term was untangled, the right answer was obvious.
2. **The boundary must be physical, not runtime.** The developer explicitly identified the failure mode of the runtime-switch pattern: forgetting to flip the switch. A physical separation (different process, different database, different URL) makes this impossible.

The import-batch identifier (a separate but complementary design choice) emerged during the same brainstorm. Every import (CSV or XLSX) mints a new `ImportBatch` row recording the run; every produced row (`Transaction`, `Transfer`, `LiabilityPayment`) carries a nullable FK back to it. This is the standard import-lineage pattern in dbt (`invocation_id`), Airflow (`run_id`), Hangfire (`JobId`). It exists in both environments because it is not a sandbox feature — it is a feature of the import pipeline. The sandbox is its first consumer; the eventual real-user "undo my import" feature is its second.

## Decision

The Phase 3 import sandbox ships as **two coordinated design choices and one sequencing choice**:

### Decision 1 — Architecture pattern: multi-environment separation

The sandbox is a separate run of the same application, against a separate PostgreSQL database, started with a separate launch profile. Specifically:

- **A new ASP.NET Core environment**, `ASPNETCORE_ENVIRONMENT=Sandbox`, sits alongside the existing `Development` and `Production`. The standard cascading-config mechanism loads `appsettings.json` then layers `appsettings.Sandbox.json` on top of it.
- **A new launch profile** in `Properties/launchSettings.json` (`Sandbox`), runnable via `dotnet run --launch-profile Sandbox`, sets the environment variable and runs the app on a port distinct from `Development`.
- **A new PostgreSQL database** named `project_ceres_sandbox`, created with `createdb`, holds sandbox state. The sandbox connection string in `appsettings.Sandbox.json` points at it.
- **EF Core migrations** are applied to both databases via a single `scripts/migrate-all.sh` script. Schema drift is impossible by design — both databases share the same `__EFMigrationsHistory` source, and the script fails loudly on any per-database error.
- **A startup fail-fast check** refuses to start the sandbox build if the connection-string `Database=` value does not match a `*_sandbox` regex. This catches the "I accidentally pointed sandbox config at the real database" footgun.
- **The admin API endpoints** (`/api/admin/import-batches/*`) register conditionally: `if (env.EnvironmentName == "Sandbox" || env.IsDevelopment())`. In production they 404. An integration test asserts the 404 to prevent drift.
- **A seed runner** (`IHostedService` gated to the sandbox environment) creates a single sandbox user, a fixed set of test bank accounts (Test Checking EUR, Test Checking USD, Test Savings, Test Credit Card), and the default Project Ceres category set on first sandbox startup. Deterministic. Same code as the registration flow, so no parallel seed code to maintain.

The decision rejects:

- The runtime-switch pattern (failure-prone — human discipline must not be the safety boundary).
- The `IsTestData` flag pattern (OCP violation — every query must remember it).
- Schema-per-tenant inside one database (migration drift, no operational gain over a separate database).

The decision aligns with:

- Twelve-Factor App § Dev/prod parity (same code, same backing service type/version, differ only via config).
- Twelve-Factor App § Backing services (the connection-string locator is the only thing that differs).
- ASP.NET Core's first-class environment + launch-profile primitives.

### Decision 2 — Complementary primitive: the import-batch identifier

A new `ImportBatch` entity records every import run:

```
ImportBatch {
  Id Guid,
  UserId Guid,              // who ran the import
  StartedAt DateTime,
  SourceFileName string,
  ParserVersion string?,
  RowsTotal int,
  RowsImported int,
  Status enum               // InProgress | Succeeded | Failed | Aborted
}
```

The three importable movement tables (`Transactions`, `Transfers`, `LiabilityPayments`) gain a nullable `ImportBatchId Guid?` foreign key. Existing rows have `null`; no backfill migration.

This primitive exists in **both environments**. In the sandbox, it enables per-batch bulk wipe (`DELETE WHERE ImportBatchId = X`). In production, it is the substrate for the eventual user-facing "undo my import" feature. No code is sandbox-only.

### Decision 3 — Sequencing: Stage 11.5, after the SPA migration completes

The sandbox + admin tooling lands as **Stage 11.5**, slotted between Stage 11 (Razor + URL cleanup) and Stage 12 (Sessions + Support SPA pages).

Sequencing alternatives considered and rejected:

- **Stage 9.5 (right after the SPA login UI ships).** Earliest possible date the admin SPA page is reachable. Rejected because Stages 9, 10, 11 still interleave Razor and SPA work; the admin page would land into a not-yet-fully-migrated SPA surface and might need rework once Stage 11 completes the cleanup.
- **Sub-stage of Stage 9 itself.** Enlarges Stage 9's scope mid-flight; Stage 9 is already large (login, registration, password reset, email change, MFA enrollment, lockout unlock — all the auth SPA pages).
- **Earlier than Stage 9.** Cannot work: the admin page lives in the React SPA, which is not reachable by an authenticated user until Stage 9 ships the login UI.
- **Later than Stage 11.5 (e.g. Phase 4).** Leaves parser-bug hunting blocked through Stage 12, Stage 13, Stage 14, Stage 15, Stage 16 — every Phase 3 stage that follows. Unacceptable cost.

Stage 11.5 trades: parser-bug hunting stays blocked through Stages 9, 10, 11. The developer accepts this cost in exchange for the sandbox landing into a fully clean SPA-only surface with no Razor/SPA boundary tension.

## Rationale

Three deciding factors:

1. **Silent failure is more dangerous than loud failure.** This is the same principle that drove ADR-0065's choice of EF query filters (loud failure on miss) over no defence (silent leak), and ADR-0068's RLS layer (loud failure on raw-SQL bypass). The runtime-switch pattern's failure mode (forget to flip → contaminate real data) is silent. The multi-environment pattern's failure mode (point sandbox config at real DB) is loud — fail-fast at startup catches it. Multi-environment wins on the same principle.

2. **The boundary should match the threat.** The threat is "I, the developer, accidentally do test work against real data." The boundary that prevents this is the boundary I have to actively cross. A different URL is such a boundary; a switch on the same URL is not. The research is consistent on this: every mature dev environment in industry is physically separated, not runtime-toggled, because runtime toggles fail on the exact failure mode they exist to prevent.

3. **Years of maintenance over months of effort.** A future engineer joining the project should find this set of pieces obvious. ASP.NET Core environments, EF migrations, a foreign key, an admin SPA route — every piece is a framework primitive someone hired in two years has seen before. The runtime-switch pattern, by contrast, would require maintaining a custom `ICurrentUserAccessor` override, a banner, a session-stored override flag, an audit trail of who-toggled-when. All of that is sandbox-only code that bit-rots.

The deciding factor for sequencing (Stage 11.5 specifically) is that the admin page lives in the SPA, and the SPA migration completes at Stage 11. Landing the admin page into a surface that is still half-Razor would force rework once Stage 11 cleaned the surface.

## Consequences

- A new ADR-0072 entry (this file) joins the decisions directory.
- **`docs/planning-phase3.md`** gains a one-paragraph entry describing the import sandbox + admin tooling under planned features, cross-referencing this ADR for the design rationale, sitting in the Batch where Stage 11.5 lives.
- **`docs/roadmap-phase-three.md`** gains a new Stage 11.5 section between Stage 11 and Stage 12 with: status `❌ Pending`, sub-stages table (launch profile, `appsettings.Sandbox.json`, `ImportBatch` entity + FK, admin SPA route, seed runner, fail-fast startup check, migrate-all script), and verification checklist (separate database boots, schema drift catches at startup, admin endpoints 404 in production, batch wipe is atomic, multi-select delete is atomic, seed runner is idempotent and gated, etc.).
- **`docs/planning-resolved.md`** gains a resolved entry: "Q: Where should the developer-facing import sandbox + admin tooling slot in the roadmap, and what architecture pattern should it use? → A: Stage 11.5 (post-SPA-migration), separate-environment pattern (separate database + separate launch profile), import-batch identifier as a complementary primitive shared with the future real-user undo feature. See ADR-0072."
- Two architectural primitives ship as part of Stage 11.5:
  - A new `Sandbox` ASP.NET Core environment with its own `appsettings.Sandbox.json` and `launchSettings.json` profile.
  - A new `ImportBatch` aggregate with `ImportBatchId` FKs on `Transactions`, `Transfers`, `LiabilityPayments`.
- The wall (Stage 7.5, ADR-0068) is **not load-bearing** for the sandbox. The database boundary is the isolation. The wall continues to do its own separate job (protecting real end-users from each other within the production database).
- The eventual real-user-facing "undo my import" feature (Phase 4+) builds on the same `ImportBatch` primitive. No additional schema work needed when that feature ships.
- The reconciliation-review tables (`ImportStagedTransaction` et al.) are **not changed** by this ADR. They remain a separate concern (matching imported rows against existing transactions) and do not become the sandbox.
- Parser-bug hunting remains blocked through Stages 9, 10, 11. The developer accepts this cost; it is not a side-effect to mitigate.

## Cross-references

- ADR-0065 — EF Core Global Query Filters with Explicit Redundancy and Admin-Only Bypass (the silent-vs-loud-failure principle that motivates Decision 1).
- ADR-0068 — PostgreSQL Row-Level Security as Phase 3 Defence in Depth (the wall — separate concern, runs inside production, not load-bearing for sandbox isolation).
- `docs/planning-phase3.md` — gains a planned-features entry referencing this ADR.
- `docs/roadmap-phase-three.md` § Stage 11.5 — the implementation stage.
- `docs/planning-resolved.md` — gains a resolved entry referencing this ADR.
- [Microsoft Learn — ASP.NET Core runtime environments](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments) — the framework primitive Decision 1 uses.
- [Microsoft Learn — Applying Migrations (EF Core)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) — the `--connection` flag that drives multi-database migrations.
- [12factor.net — Dev/prod parity](https://12factor.net/dev-prod-parity) — the principle Decision 1 follows.
- [dbt docs — invocation_id](https://docs.getdbt.com/reference/dbt-jinja-functions/invocation_id) — the import-lineage pattern Decision 2 mirrors.
