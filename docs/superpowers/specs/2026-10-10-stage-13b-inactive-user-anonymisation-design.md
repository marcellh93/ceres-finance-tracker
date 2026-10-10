# Stage 13.b — Inactive-user anonymisation sweep (design)

**Date:** 2026-10-10
**Status:** Design approved; spec under user review.
**Roadmap:** `docs/roadmap-phase-three.md` § Stage 13.b (🔓 unlocked 2026-10-06, Decision C).
**Policy source:** `docs/security-model.md` § Data Retention — inactive-account policy.

## 1. Scope

Automatically handle accounts that have gone **inactive** (no login for a defined
period), as a standing storage-limitation duty under GDPR Art. 5(1)(e):

- **12 months, no login →** send one "you've been inactive" warning email.
- **18 months, no login →** anonymise the account: run the shared statutory scrub
  (anonymise financial records in place, redact support history, delete
  non-statutory throwaway data) + scrub the profile identity.

"No login" means **actual authentication only** — password, TOTP, or backup code.
Staying signed in via a persistent cookie does **not** reset the clock (the policy
wording is "no *login*"; the known trade-off is recorded in § 8).

### Explicitly out of scope

- **User-requested erasure (Stage 13.9).** Already shipped. A sibling flow, not this
  stage. Inactivity anonymisation is a *controller* duty, not an Art. 17 request —
  the legal distinction drives §§ 4 and 5.
- **Account-closure archive lifecycle (ADR-0029 `CustomerArchive`).** Split out by
  Decision C (user-ratified, 2026-10-06). Its receiving home already exists: the
  `legal.md` GDPR-checklist line "CustomerArchive background job implemented" stays
  `[ ]` as the tripwire, and no account-closure trigger exists in code today. Scope it
  to that account-closure stage, not 13.b. Do not pull it back in.
- **Cron *registration*.** The `--run-*` flag ships here; the scheduled trigger is
  wired at hosting time per the Stage-16 deferred-cron convention.

## 2. Measuring inactivity — the login timestamp

Today `ApplicationUser` carries only `CreatedAt`, `SealedAt?`, `ErasedAt?`
(verified 2026-10-10). `UserSession.LastUsedAt` is debounced and swept at 90 days, so
it cannot measure a 12-month gap. Two new columns on `ApplicationUser`:

| Column | Type | Behaviour |
|---|---|---|
| `LastLoginAt` | `DateTime` (non-null) | Stamped at every login-success point. |
| `InactivityWarningSentAt` | `DateTime?` (nullable) | Set when the 12-month warning is sent; **cleared to `null` at every login** (same code site as the `LastLoginAt` stamp). |

### Stamp points

All three login-success sites in `ProjectCeres/Controllers/Api/AuthController.cs`:

- `Login` (password) — the `AuditLogAction.LoginSucceeded` site (~line 376).
- `LoginTotp` — the `LoginSucceededMfa` site (~line 469).
- backup-code login — the `LoginSucceededBackupCode` site (~line 506).

Each site: set `LastLoginAt = timeProvider.GetUtcNow().UtcDateTime` and
`InactivityWarningSentAt = null`. `AuthController` already injects `TimeProvider`, so
CER004 is satisfied without a new dependency. A single private helper on the controller
(e.g. `StampLogin(ApplicationUser user)`) keeps the three sites in lockstep.

### Backfill (Decision 2 → A: seed "now")

New EF migration mirroring `20260509125640_AddCreatedAtToApplicationUser` (timestamp
column on `AspNetUsers` via `defaultValueSql: "current_timestamp"`):

- `LastLoginAt` — non-null, `defaultValueSql: "current_timestamp"`. Existing users are
  seeded to "now" at migration time → everyone gets a fresh 18-month lease at launch,
  nobody is wrongly flagged on day one. The column becomes fully accurate for each user
  the next time they log in.
- `InactivityWarningSentAt` — nullable, no default (starts `null`).

The C# property initialiser for `LastLoginAt`, if any default is given, needs
`[AllowsWallClock("entity property initialiser; EF Core materializer cannot inject
TimeProvider")]` — the same treatment `CreatedAt` carries. Preferred: **no C# default**
on `LastLoginAt` (the DB default covers existing rows; the login path always sets it for
new activity, and registration sets it explicitly), avoiding the attribute entirely.
Registration (`AuthController` → `Registered`) sets `LastLoginAt` at account creation so
a never-logged-in new account is not instantly "inactive".

## 3. The shared scrubber (Decision 3 → A: extract)

The statutory scrub steps already exist in `ProjectCeres/Services/ErasureExecutor.cs`
and are tested. They take only a `userId` and read nothing erasure-specific — the
coupling to the erasure request is confined to the top (reads a `Sealed`
`ErasureRequest`) and the tail (`GdprErasureCompleted` audit + `ErasedEmailHold` +
status flip). The lane methods (`AnonymiseStatutoryAsync`, `RedactSupportAsync`,
`PurgeAsync`, `DeleteExportJobAsync`, `AnonymiseAccountAsync`) are already
erasure-agnostic.

**Extract** the lane methods into a new `UserAnonymiser` service (one job: given a
`userId`, run the scrub lanes). Two callers:

| Caller | Shared scrub | Its own tail |
|---|---|---|
| `ErasureExecutor` (existing) | `UserAnonymiser` | writes `GdprErasureCompleted`, sets `ErasedEmailHold` (30-day re-registration block), flips request status — **unchanged behaviour**. |
| Inactivity sweep (new) | `UserAnonymiser` | writes `InactivityAnonymised`, scrubs identity, **no `GdprErasureCompleted`, no `ErasedEmailHold`** (Decision 6). |

**Hard rule:** `GdprErasureCompleted` (the Art. 17 record) is written **only** by the
erasure tail, never by inactivity — inactivity cleanup is legally a different act.

The extraction is a safe refactor: `ErasureExecutor`'s existing tests cover the moved
steps. A regression test pins that the erasure tail still writes its own record
post-extraction (§ 6).

`UserAnonymiser` registers via the Scrutor scan convention (interface + implementation
in a scanned namespace) — no manual `Program.cs` line. Its `IgnoreQueryFilters()` usage
(inherited from the moved lanes, through `AdminDbContext`) stays inside the allow-list;
the allow-list entry moves from `ErasureExecutor.cs` to `UserAnonymiser.cs` (or both are
listed) — see § 5.

## 4. The sweep (`InactivitySweep`)

A new flat cross-tenant tool under `ProjectCeres/Tools/`, modelled on `RetentionPurge` /
`SweepSessions` / `ErasureWorker`. Runs through `AdminDbContext` (BYPASSRLS +
`[RequiresAdminContext]`); a RLS-scoped context would see no cross-tenant rows.

### CLI entry

`Program.cs` CLI block (~line 989–1022), following `args[0] == "--run-*"`:

```
if (args.Length > 0 && args[0] == "--run-inactivity-sweep")
```

### Horizons

Named constants, mirroring `RetentionPurge`'s horizon constants:
`WarningHorizon = 12 months`, `AnonymiseHorizon = 18 months` (as `TimeSpan`/month
arithmetic consistent with the existing purge horizons). Boundary-tested in § 6.

### Eligible query + the two groups

One flat query over all users, read via `AdminDbContext` + `IgnoreQueryFilters()`:

- **Safety guard (Decision 6b):** exclude any account with `SealedAt != null` **or**
  `ErasedAt != null` — never collide with an in-flight or completed erasure. Mirrors the
  `AuthController` login-refusal guards.
- **Warning group:** `LastLoginAt <= now - WarningHorizon` **and**
  `LastLoginAt > now - AnonymiseHorizon` **and** `InactivityWarningSentAt == null`.
  Action: send the warning email (§ 5), then set `InactivityWarningSentAt = now`.
- **Anonymise group:** `LastLoginAt <= now - AnonymiseHorizon`. Action: per-user loop
  calling `UserAnonymiser` + the inactivity tail (§ 3).

The warning fires **once** because sending it stamps `InactivityWarningSentAt`; a user
who returns has it cleared at the login path (§ 2) and is correctly re-warned on a later
lapse. (The reset lives in the login path, not the sweep — the sweep never clears it.)

### Shape

The *find* arm is a flat query. The *anonymise* arm is a per-user loop (each user is a
set of row mutations, not a flat DELETE), mirroring `ErasureWorker`'s enumerate-then-
per-user-executor shape. A failure anonymising one user is logged and the loop
continues (one bad row must not abort the sweep); the user remains eligible next run
(idempotent — a second pass over an already-scrubbed user is a no-op, matching the
erasure worker's crash-safety property).

## 5. The warning email + registries

### Email

One new template, delivered by a new `InactivityWarningNotificationService` copied
verbatim in shape from `NewSessionNotificationService`:

- Recipient resolved server-side via `IEmailRecipientResolver` (reads
  `ApplicationUser.Email`; never a payload).
- Failures logged and swallowed (a mail outage must not abort the sweep). **But:** unlike
  the login-path alert, a swallowed failure here means `InactivityWarningSentAt` must
  **not** be stamped if the send failed — so the stamp happens only on a successful
  `SendAsync`, leaving a failed user eligible to retry next run.
- **No opt-out consulted (Decision 5 → A).** It is a legal-basis notice preventing
  silent data loss — same posture as `NewSessionNotificationService`. The service header
  documents this and notes: if a notification-preferences surface is ever built, this
  notice stays exempt.

Content (EN + ES): why they received it (long inactivity), what happens if ignored
(account anonymised at the 18-month mark), the one action to stop it (log in).

### Registry burden (source: `verify-stage-completeness/SKILL.md` § 1)

| Registry | Addition |
|---|---|
| `EmailTemplateKey` enum | `InactivityWarning` (with a `// wired in Stage 13.b` comment). |
| `Resources/EmailsResource.en.resx` | 3-key contract: `InactivityWarning.Subject` / `.BodyText` / `.BodyHtml`. |
| `Resources/EmailsResource.es.resx` | same 3 keys, ES. |
| `AuditLogAction` enum | `InactivityWarningSent`, `InactivityAnonymised` (with `// wired in Stage 13.b` comments). |
| `ArchitectureTests.AuditLogAction_enum_values_match_documented_set` (`expected` array ~line 617) | both new values added. |
| `ArchitectureTests.IgnoreQueryFilters_only_appears_in_documented_exception_paths` allow-list (~line 784) | add `ProjectCeres/Tools/InactivitySweep.cs` and (if it calls `IgnoreQueryFilters` directly) `ProjectCeres/Services/UserAnonymiser.cs`, each with a trailing justification comment. Move/duplicate the existing `ErasureExecutor.cs` entry as the lanes relocate. |

**No new IUserOwned entity** — 13.b mutates existing rows and adds two columns to the
non-IUserOwned `ApplicationUser`. The DbSet / OnModelCreating / RLS-migration / DI
five-registry chain does **not** fire. (Confirmed: the "warned at" state is a column,
not an entity — Decision 4.)

## 6. Tests (ship-gate)

Following the project's negative-assertion discipline (`feedback_test_edge_cases_as_ship_gate`):

- **Boundary — warning:** a user at `WarningHorizon + 1 day` is warned; at
  `WarningHorizon - 1 day` is left alone.
- **Boundary — anonymise:** a user at `AnonymiseHorizon + 1 day` is anonymised; just
  inside is left alone.
- **Warn-once:** two consecutive sweeps send the warning exactly once (negative:
  second sweep sends nothing for that user).
- **Reset-on-login:** a warned user who logs in has `LastLoginAt` refreshed and
  `InactivityWarningSentAt` cleared; a later lapse re-warns (not silently skipped).
- **Skip guard (negative):** an account with `SealedAt` set — and separately one with
  `ErasedAt` set — is never processed by either group.
- **Legal line (negative):** inactivity anonymisation runs the scrub **but writes no
  `GdprErasureCompleted` and creates no `ErasedEmailHold`**; it writes exactly one
  `InactivityAnonymised` audit row.
- **Erasure regression:** post-extraction, the erasure flow still writes its own tail
  (`GdprErasureCompleted` + `ErasedEmailHold`) unchanged — pins the refactor.
- **Warning send-failure (negative):** a failed `SendAsync` does **not** stamp
  `InactivityWarningSentAt` (user stays eligible to retry).
- **Backfill:** `LastLoginAt` is non-null for all existing rows after the migration.
- **Idempotency:** a second anonymise pass over an already-scrubbed user is a no-op.

## 7. Files touched (summary)

- `ProjectCeres/Models/ApplicationUser.cs` — two columns.
- `ProjectCeres/Migrations/<new>` — add columns + backfill.
- `ProjectCeres/Controllers/Api/AuthController.cs` — `StampLogin` helper at 3 sites + registration.
- `ProjectCeres/Services/UserAnonymiser.cs` — **new**, extracted lanes.
- `ProjectCeres/Services/ErasureExecutor.cs` — call `UserAnonymiser`; tail unchanged.
- `ProjectCeres/Tools/InactivitySweep.cs` — **new**.
- `ProjectCeres/Program.cs` — `--run-inactivity-sweep` CLI dispatch.
- `ProjectCeres/Common/Email/InactivityWarningNotificationService.cs` — **new**.
- `ProjectCeres/Common/Email/EmailTemplateKey.cs` — `InactivityWarning`.
- `ProjectCeres/Resources/EmailsResource.{en,es}.resx` — 3 keys each.
- `ProjectCeres/Models/AuditLog.cs` — two `AuditLogAction` values.
- `ProjectCeres.Tests/.../ArchitectureTests.cs` — documented-set + allow-list.
- `ProjectCeres.Tests/...` — the test suite in § 6.
- Docs: `security-model.md` (mark the inactivity flow built), roadmap line
  "Inactive user records…" → `[x]` + Stage 13.b sub-items, `planning-resolved.md` note,
  likely a new ADR for the reuse-vs-extract + no-hold decisions.

## 8. Decision 1 trade-off (recorded rationale)

This section records *why* the clock is login-only, so the next reader does not mistake
it for an oversight. It is the documented consequence of Decision 1 (user-ratified), not
a shelved defect.

Login-only means a user who stays signed in via a persistent "remember me" cookie for
12+ months without re-authenticating is treated as inactive — warned, then anonymised —
despite real use. This is the intended reading of the policy's literal "no *login*"
wording (Decision 1 = A, chosen over the any-activity and hybrid options).

Should the policy's intent ever be revised to count persistent-cookie use as activity,
the implementation path is small: also refresh `LastLoginAt` on a persistent-cookie
rotation (that path already resolves the user by session id — a one-line addition at
`PersistentCookieRotationMiddleware`). That is a future policy change, not work this
stage leaves unfinished.
