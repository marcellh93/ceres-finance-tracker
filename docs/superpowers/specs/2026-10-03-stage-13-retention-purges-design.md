# Stage 13 — Retention Purge Jobs — Design

**Date:** 2026-10-03
**Phase:** 3 (Hosted Beta)
**Roadmap:** `docs/roadmap-phase-three.md` § Stage 13 (GDPR baseline) — Retention policy checklist
**Status:** Design approved; pending implementation plan.

## Goal

Enforce the already-decided GDPR data-retention periods with automated deletion
jobs, by replicating the established `SweepSessions` external-cron pattern. Build
the purge logic + entrypoint + tests now; the real daily cron *schedule* is wired
at Stage 16 (hosting), exactly as `SweepSessions` was (shipped in Stage 12.10, its
cron registration deferred to Stage 16).

## Scope

### In scope (this slice)

Four flat, cross-tenant, time-keyed purges — each a copy of the `SweepSessions`
shape (`ProjectCeres/Tools/SweepSessions.cs`):

| Purge | Entity (model file) | DbSet | Predicate | Period | Source |
|---|---|---|---|---|---|
| Audit log | `AuditLog` (`Models/AuditLog.cs`, `IUserOwned`) | `AuditLogs` | `OccurredAt < now − 12mo` | 12 months | roadmap 13.6; `security-model.md` § Retention (PCI/SOC2/ISO 12-mo floor) |
| Failed logins | `FailedLoginAttempt` (`Models/FailedLoginAttempt.cs`, no RLS filter) | `FailedLoginAttempts` | `OccurredAt < now − 1yr` | 1 year | roadmap 13.7 |
| Soft-deleted reports | `SavedReport` (`Models/SavedReport.cs`, `IUserOwned`) | `SavedReports` | `DeletedAt != null && DeletedAt < now − 90d` | 90 days | roadmap 13.5 |
| Soft-deleted import profiles | `ImportProfile` (class in `Models/CsvImportProfile.cs`, `IUserOwned`) | `ImportProfiles` | `DeletedAt != null && DeletedAt < now − 90d` | 90 days | roadmap 13.5 |

> **Note — the ImportProfile purge already covers every format (CSV and Excel).**
> `ImportProfile` is format-*agnostic* despite the `CsvImportProfile.cs` filename: it
> carries a `Format` column (`ImportFormat.Csv`/`.Excel`) + a `SheetName` field, so an
> Excel profile is just an `ImportProfile` row with `Format = Excel` — the same table,
> same `DeletedAt` column. There is no separate `ExcelImportProfile` entity and none is
> needed; this single purge sweeps all formats, present and future. (Aside, not in scope
> here: the `ImportProfile` entity is shelved-import machinery per ADR-0078 and may become
> dead when the template import in Stage 15.10 ships — the template approach drops saved
> column-mapping profiles. The purge stays correct regardless as long as the table exists.)

The periods are fixed by prior decision (`planning-resolved.md` 2026-09-19,
`security-model.md` § Data Retention, `legal.md` § Data Retention Policy) — this
spec does not revisit them.

### Out of scope

- **Inactive-user archival** (roadmap "Inactive user records: archived after a
  defined period") — split to its own stage. It is underspecified and the two
  governing docs conflict: `security-model.md` (12-mo warning / 18-mo anonymise)
  vs `legal.md` (30-day grace / 150-day archive / 180-day delete, tied to an
  ADR-0029 `CustomerArchive` entity that does not exist). It is a materially larger
  build (new entity + registries + filesystem archive + deletion job) resting on a
  policy contradiction only the user can resolve. `legal.md` already homes
  `CustomerArchive` as its own GDPR-checklist item.
- **The real cron schedule** — deferred to Stage 16 (add a `[ ]` cron-registration
  row there next to `--sweep-sessions`).
- **A deletion audit record per run** — not in v1 (see Decisions).
- The non-code Stage 13 items (privacy policy, cookie consent, RoPA, DPA, breach
  runbook, DPIA) — separate slices.

## Decisions (settled in brainstorm)

1. **One bundled entrypoint** `--run-retention-purge` runs all four purges in
   sequence (each independently testable). One Stage-16 cron row. (Docs lean this
   way — roadmap §16 notes the audit-log purge "reuses this same cron mechanism".)
2. **No deletion audit record in v1.** Each purge logs its deleted count to the
   application log (like `SweepSessions`). No `AuditLog` row, so **no new
   `AuditLogAction` enum value** and no documented-set-test churn. A cross-tenant
   system purge also has no natural `UserId` to log under. Revisit if a compliance
   review requires a formal trail.
3. **`AdminDbContext`** (role `ceres_admin`, BYPASSRLS) for all four — these are
   cross-tenant time-keyed deletes; `AppDbContext`/`ceres_app` would RLS-scope the
   delete to nobody (empty GUC → fail-closed → delete nothing).

## Architecture

New static class **`RetentionPurge`** in `ProjectCeres/Tools/`, decorated
`[RequiresAdminContext]`, namespace `ProjectCeres.Tools` — mirroring `SweepSessions`.

- **Four purge methods**, each `static async Task<int> Purge<X>Async(AdminDbContext
  db, TimeProvider clock, CancellationToken ct)`, computing
  `cutoff = clock.GetUtcNow().UtcDateTime − <period>` and doing one flat
  `db.<Set>.IgnoreQueryFilters().Where(<predicate>).ExecuteDeleteAsync(ct)`,
  returning the deleted count. `IgnoreQueryFilters()` is required for the three
  `IUserOwned` entities (cross-tenant); harmless for `FailedLoginAttempt` (no
  filter) but kept for consistency.
- **`RunAsync(WebApplicationBuilder builder)`** entrypoint: `builder.Build()` →
  `CreateScope()` → resolve `AdminDbContext` + `TimeProvider` + `ILogger<Program>`
  → call all four methods → log each count → return 0.
- **Dispatch:** add to the `Program.cs` arg block (after service registration,
  before the final `builder.Build()`), alongside `--sweep-sessions`/`--run-export-jobs`:
  `if (args.Length > 0 && args[0] == "--run-retention-purge") Environment.Exit(await ProjectCeres.Tools.RetentionPurge.RunAsync(builder));`
- **Clock:** `TimeProvider` injected (CER004 — no `DateTime.UtcNow` in production code).

## Convention obligations (build requirements, not choices)

- **Add `ProjectCeres/Tools/RetentionPurge.cs` to the `IgnoreQueryFilters`
  allow-list** HashSet in `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`
  (~line 783), with a justifying comment, or
  `IgnoreQueryFilters_only_appears_in_documented_exception_paths` fails.
- **`[RequiresAdminContext]`** marker on the class (enforced by
  `AdminContextDisciplineTests`).
- **Fix the wrong roadmap line:** roadmap 13.5 says CsvImportProfile 90-day purge
  is "already implemented; verify" — verified FALSE (no purge exists anywhere; what
  exists is a 90-day *display window* for the restore UI, the inverse). Correct the
  line to reflect it is a new build.

## Testing (tests-as-ship-gate)

Follow the `SessionRetentionSweepTests` pattern
(`ProjectCeres.Tests/Integration/Authentication/`): `[Collection("AppRoleTests")]`,
seed via an admin context, assert via an admin context.

Per purge, a test that:
- Seeds rows with a **unique per-test marker** in a column (shared sequential DB
  discipline — never assert on unmarked global state).
- Runs the purge method with the injected `TimeProvider`.
- Asserts deletion filtered by the marker, with **boundary coverage**: a row just
  past the horizon (e.g. `−(period + 1 day)`) is deleted; a row just inside
  (`−(period − 1 day)`) is kept.
- For the two soft-delete purges: also assert a row with `DeletedAt == null` (never
  soft-deleted) is **never** touched regardless of age (negative assertion — the
  purge only removes aged *soft-deleted* rows).

Tests live under `ProjectCeres.Tests/Integration/` (a `Retention`/`Profile` sibling,
mirroring where each entity lives).

## Definition of Done

- `RetentionPurge` with four methods + `--run-retention-purge` entrypoint; `dotnet build` clean.
- Four purge tests (with boundary + soft-delete-null negative assertions) green.
- `RetentionPurge.cs` added to the `IgnoreQueryFilters` allow-list; `ArchitectureTests` green.
- Roadmap 13.5 "already implemented" line corrected; the four retention `[ ]` items
  ticked where covered; a Stage 16 `[ ]` cron-registration row added for
  `--run-retention-purge`.
- Full `dotnet test` green.

## Open implementation detail (the plan decides; shape is fixed)

- Method naming + whether to share a tiny private helper for the two
  `DeletedAt < cutoff` soft-delete purges vs. keep all four fully independent for
  test clarity — the plan decides; the shape is fixed.

## Deferred out of this slice

- **Inactive-user archival** → deferred to Stage 13.b (a new locked roadmap stub —
  see `roadmap-phase-three.md` § Stage 13.b). **Reason:** cannot be built
  correctly now — it is underspecified and the governing docs conflict
  (`security-model.md` 12-mo/18-mo anonymise vs `legal.md` 30/150/180-day
  `CustomerArchive`), and choosing the authoritative policy is a user/counsel
  decision (ask-before-deviating-from-docs). It is also a larger build than a flat
  purge. **Tripwire:** the new stub holds the `[ ]` + its open-question list; the
  original Stage 13 retention line points at the stub so a stage-close audit
  re-surfaces it.
