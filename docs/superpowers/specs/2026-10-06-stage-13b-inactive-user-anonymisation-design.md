# Stage 13.b — Inactive-User Anonymisation Sweep — Design

**Date:** 2026-10-06
**Phase:** 3 (Hosted Beta)
**Roadmap:** `docs/roadmap-phase-three.md` § Stage 13.b (🔓 unlocked 2026-10-06, Decision C)
**Status:** Design approved; pending implementation plan.

## Goal

Automatically handle accounts that have gone **inactive** (no activity for a defined
period), per `security-model.md` § inactive-account policy: at 12 months of
inactivity send a warning email; at 18 months anonymise the user-controlled profile
and delete non-statutory data, with statutory financial records anonymise-retained.
Backend + one migration; no frontend. This is the **inactivity** flow only — the
account-*closure* archive lifecycle (ADR-0029 `CustomerArchive`) is a separate
deferred stage (Decision C, `planning-resolved.md`).

## Scope boundary (what this is NOT)

- NOT the user-*requested* GDPR erasure flow (shipped Stage 13.9). Different trigger
  (a request vs. inactivity), different ceremony (no re-registration block here).
- NOT account-closure / archive (ADR-0029). No `ClosureType`, no `CustomerArchive`,
  no archive file, no restoration service. That lifecycle is deferred — see the
  Stage 13.b roadmap deferral line.

## Context established by pre-design research + verify-against-codebase

- **No activity signal exists today.** `ApplicationUser` has only `CreatedAt`,
  `SealedAt`, `ErasedAt` (`ApplicationUser.cs`). `UserSession.LastUsedAt` is debounced
  AND swept at 90 days (`SweepSessions.cs`), so it cannot measure 12–18-month
  inactivity. A new durable timestamp on the user is the load-bearing prerequisite.
- **`ErasureExecutor` already implements the data operation this needs.** Its three
  private lanes — `AnonymiseStatutoryAsync` / `RedactSupportAsync` / `PurgeAsync`,
  called from `ExecuteAsync` (`ErasureExecutor.cs:72-74`) — do exactly "anonymise
  statutory financial rows in place, redact support, purge the rest", with the lane
  split single-sourced in `ErasureLanes` / `StatutoryRetentionSet`. It is
  `[RequiresAdminContext]` with an `AdminDbContext` constructor.
- **Sweep/cron convention** (`RetentionPurge.cs`, `SweepSessions.cs`): a
  `[RequiresAdminContext]` static class, flat cross-tenant `AdminDbContext` +
  `IgnoreQueryFilters`, dispatched via a `--run-*` arg in `Program.cs`. Cron
  *registration* is deferred to Stage 16 (same as `--sweep-sessions` /
  `--run-retention-purge`).
- **`ApplicationUser` is the identity root, NOT `IUserOwned`** — so the 5-registry /
  RLS-table rule does NOT apply to the new columns. The migration + new audit action
  still trigger the 3-reviewer pipeline (migration + security-sensitive) per the
  evidence rules.
- **Enums are flat and append-only**: `AuditLogAction` (`AuditLog.cs`, ends at
  `GdprErasureCompleted`) and `EmailTemplateKey` (`EmailTemplateKey.cs`).

## Design decisions (all user-approved in the brainstorm)

| # | Decision | Rationale |
|---|---|---|
| D1 | **Measure inactivity via a new `LastActivityAt` (UTC) on `ApplicationUser`**, stamped at fresh login (`AuthController`) AND persistent-cookie rotation (`PersistentCookieRotationMiddleware`). Existing users backfilled to the migration timestamp. | A remember-me user who keeps using the app without re-authenticating must not be read as inactive. Named `LastActivityAt` (not `LastLoginAt`) because it tracks more than logins — honest naming. |
| D2 | **Extract the three-lane anonymisation walk from `ErasureExecutor` into a shared, trigger-agnostic service** (`[RequiresAdminContext]`, `AdminDbContext`). An interface supplies per-trigger *ceremony*; erasure and inactivity are two implementations. | The data operation is a legal-correctness invariant that must stay byte-identical for both triggers → single-sourced. Only the ceremony (audit action, email, re-reg block, reversibility) differs. |
| D3 | **12mo → one warning email; fully reversible until 18mo; any activity resets the streak; 18mo → anonymise.** No seal/grace middle state. | The policy names exactly two events with nothing between; a seal step is the deferred *closure* mechanism. Minimal tracked state; a returning user loses nothing. |
| D4 | **Warning-sent state = new `InactivityWarnedAt` (UTC, nullable) on `ApplicationUser`.** The sweep sends the warning only if it's null OR older than `LastActivityAt`. | One column read per candidate (cheaper than an audit-log query); naturally re-arms when activity updates `LastActivityAt`. |
| D5 | **Ceremony = new `AuditLogAction.InactivityAnonymised` + a new "inactive account anonymised" email (EN+ES); NO re-registration block.** The audit row pseudonymises the user id the same way `GdprErasureCompleted` does. | A distinct, accurate audit action + notice; the re-reg block is erasure-specific. The user is being anonymised, so the audit row must not store raw identity. |

## Architecture

### The shared anonymisation service (D2)

Extract the data walk into a new service — proposed `UserAnonymisationService`
(`[RequiresAdminContext]`, ctor `AdminDbContext`) — exposing one method:

```
Task AnonymiseUserDataAsync(Guid userId, string pseudonymToken, CancellationToken ct)
```

It runs the three lanes (statutory anonymise / support redact / purge) **only** — no
audit row, no email, no status flip, no email-hold. It is the moved bodies of
`AnonymiseStatutoryAsync` + `RedactSupportAsync` + `PurgeAsync` + `AnonymiseAccountAsync`
+ `DeleteExportJobAsync` (the pure data steps), with the lane classification still
read from `ErasureLanes`/`StatutoryRetentionSet`.

`ErasureExecutor` is refactored to **call** this service for the data work and keep
only its erasure ceremony: reading the `ErasureRequest`, writing `GdprErasureCompleted`,
`RecordEmailHoldAsync` (the re-reg block), the erasure email. **Behaviour-preserving:
the Stage 13.9 erasure test suite is the regression guard and must stay green.**

Per-trigger ceremony is expressed through a small interface (shape decided at plan
time), e.g. each trigger supplies its audit action + email + post-data steps; the
shared service stays ceremony-free.

### The inactivity sweep (D3)

A new `[RequiresAdminContext]` tool — proposed `InactiveUserSweep` — mirroring
`RetentionPurge`/`SweepSessions`: `AdminDbContext`, `TimeProvider`, flat cross-tenant,
dispatched via `--run-inactive-sweep` in `Program.cs`. Per eligible user (compared
against `now`):

- `LastActivityAt <= now − 12mo` AND (`InactivityWarnedAt` is null OR
  `InactivityWarnedAt < LastActivityAt`) → send the warning email, set
  `InactivityWarnedAt = now`.
- `LastActivityAt <= now − 18mo` → call `UserAnonymisationService.AnonymiseUserDataAsync`
  with a fresh pseudonym, write `AuditLogAction.InactivityAnonymised` (pseudonymised
  user id in entityType, as `GdprErasureCompleted` does), send the anonymised-notice
  email. NO re-reg block.
- Exclusions: already-anonymised users (`ErasedAt` set / already inactivity-anonymised),
  sealed accounts — skipped. (Exact exclusion predicate confirmed at plan time against
  the real flags.)

Reversibility (D3) is automatic: a login or cookie rotation updates `LastActivityAt`,
which both removes the user from the ≥12mo/≥18mo buckets and (via `InactivityWarnedAt <
LastActivityAt`) re-arms the warning.

### Activity stamping (D1)

Stamp `LastActivityAt = now` at:
- `AuthController` successful login (beside the `LoginSucceeded` audit write).
- `PersistentCookieRotationMiddleware` on a successful remember-me rotation.
Login-triggered only (no per-request write), so no debounce needed. (If profiling
later shows cookie rotation is hot, add the same ≥1-day debounce `UserSession` uses —
noted, not built.)

### New state + registrations

- Migration: `ApplicationUser.LastActivityAt` (DateTime, NOT NULL, backfill existing
  rows to the migration timestamp) + `InactivityWarnedAt` (DateTime?, null).
- `AuditLogAction.InactivityAnonymised` (append, "wired in Stage 13.b").
- `EmailTemplateKey`: two new keys (inactivity warning + inactivity anonymised notice),
  each with EN+ES resx entries + composer arm (the `GdprErasureInitiated`/`GdprExportReady`
  pattern).
- `--run-inactive-sweep` dispatch in `Program.cs` (cron registration deferred to Stage 16).

## Binding requirements (from the docs)

- **Statutory override is non-negotiable** (`legal.md`, `StatutoryRetentionSet`): tax/
  accounting records are anonymise-retained for the statutory period (Ley General
  Tributaria / Código de Comercio), never hard-deleted — even for an inactive user.
  Inherited from the shared service; a test must assert statutory rows survive.
- **No erasure-flow regression**: the extraction must keep `ErasureExecutor`'s observable
  behaviour identical; the Stage 13.9 erasure suite is the guard.
- **Privacy-policy disclosure**: the 12mo/18mo windows must be stated in the privacy
  policy before this ships (per `security-model.md` + the retention-disclosure rule).
  The policy pages are a `[~]` placeholder draft pending counsel — flag, don't block.
- **Cross-tenant discipline**: sweep + shared service run through `AdminDbContext`
  (BYPASSRLS) + `IgnoreQueryFilters`, carry `[RequiresAdminContext]` (ArchitectureTests
  enforce the marker).

## Testing (tests-as-ship-gate)

- **Boundary coverage, both horizons** (the `RetentionPurgeTests` idiom): just-under-12mo
  → no warning; just-over-12mo → warning sent once; just-under-18mo → no anonymise;
  just-over-18mo → anonymised. Marker-filtered, fixture-seeded `LastActivityAt`.
- **Warn-once-per-streak**: a second sweep after a warning does not re-send;
  `InactivityWarnedAt` gates it.
- **Reversibility**: activity after a warning (updates `LastActivityAt`) re-arms the
  warning AND moves the user out of the anonymise bucket.
- **Statutory survival**: after an 18mo anonymise, statutory financial rows still exist
  (anonymised), non-statutory rows gone — the negative assertion.
- **Extraction regression**: the full Stage 13.9 erasure suite stays green; add a test
  that `ErasureExecutor` still writes `GdprErasureCompleted` + the re-reg hold (proving
  the ceremony stayed behind).
- **Ceremony separation**: an inactivity anonymise writes `InactivityAnonymised` (not
  `GdprErasureCompleted`) and does NOT create a re-registration hold.
- **Activity stamping**: a successful login and a cookie rotation each update
  `LastActivityAt`.

## Definition of Done

- `LastActivityAt` + `InactivityWarnedAt` on `ApplicationUser`; migration backfills
  existing users to the migration date; stamped at login + cookie rotation.
- Shared `UserAnonymisationService` extracted; `ErasureExecutor` refactored to call it
  with erasure behaviour unchanged (suite green).
- `InactiveUserSweep` + `--run-inactive-sweep`: 12mo warn-once, 18mo anonymise,
  reversible until 18mo, statutory rows retained.
- `AuditLogAction.InactivityAnonymised` (pseudonymised id) + 2 email templates (EN+ES).
- All tests above green; `dotnet build` + `dotnet test` exit 0.
- Roadmap Stage 13.b items ticked for the automated pieces; cron registration left as
  the Stage-16 `[ ]`; privacy-policy disclosure flagged as counsel-dependent.

## Open implementation details (the plan decides; shapes are fixed)

- The exact per-trigger ceremony interface shape (strategy object vs. two callers of a
  ceremony-free service) — plan-time, both satisfy D2.
- The precise exclusion predicate (already-anonymised / sealed / never-active users).
- Whether cookie-rotation stamping needs the ≥1-day debounce (measure first).
- Pseudonym generation reuse (`ErasurePseudonym.Compute` or equivalent) for the
  inactivity audit row.
