# Stage 12.10 — Session Lifecycle: Expiry Filter, Login Dedup, Retention Sweep

**Status:** Design approved (2026-08-29). Ready for implementation planning.

## Problem

The active-sessions list (`/settings/sessions`) shows stale and duplicate sessions:

1. **Expired sessions still appear.** `SessionService.GetActiveAsync` filters only `RevokedAt == null`. A `UserSession` row has no expiry concept, so a session whose cookie died (ephemeral: 30-min sliding window elapsed) is still `RevokedAt == null` in the database and shows in the list as "active" — offering Revoke / Block-IP buttons on a session that is already dead.
2. **Rows pile up, one per login.** A new `UserSession` row is created on **every** login (`AuthController.IssueSessionAndCookiesAsync`), with no dedup — even from the same browser at the same IP. `security-model.md` § Sessions documents the intent as "one row per active session per device," but the code does not enforce it, so repeated logins in one browser accumulate rows.

Both symptoms share one root cause: the list shows every non-revoked row, with no notion of expiry, and login never reuses an existing device's session.

## Non-goals

- No change to the cookie authentication mechanism, the `sid` claim, the security stamp, or `SessionRevocationValidator`.
- No new HTTP endpoint. The sweep is a cron-invoked CLI one-shot.
- No change to the persistent "remember me" rotation (`PersistentCookieRotationMiddleware`) beyond consuming the shared lifetime constant.
- Retention horizon is **90 days**, already fixed by `security-model.md` § Retention Policy (revoked `UserSession` rows + User-Agent strings, 90 days). Not re-litigated here.

## Design

Three independent parts, each with one job.

### Part A — Read-time expiry filter (fixes the visible bug)

`SessionService.GetActiveAsync` gains an expiry predicate. A session is shown iff:

- `RevokedAt == null`, AND
- **ephemeral** (`IsPersistent == false`): `LastUsedAt > now − 30 min` (the sliding-cookie window), OR
- **persistent** (`IsPersistent == true`): `LastUsedAt > now − 30 days` (the persistent-cookie `Expires`).

`LastUsedAt` is a reliable "last activity" signal: `SessionRevocationValidator` bumps it on every authenticated request (debounced to one write per 60 s). The 60 s debounce means `LastUsedAt` can lag true last activity by up to ~1 minute, so the boundary is fuzzy by that much — a session at the very edge of the 30-min window could show as live ~1 min longer than strictly accurate. This slack is acceptable (it only ever errs toward showing a very-recently-active session, never toward hiding a live one) and is not a defect. No data is mutated by this part — it is a pure query change. Revoke / Block-IP buttons therefore only ever appear on genuinely-live sessions.

### Part B — Dedup on login (stops the pile-up at the source)

In `AuthController.IssueSessionAndCookiesAsync`, **before** adding the new `UserSession` row, revoke any live duplicate for the same device:

```
UPDATE UserSessions
SET RevokedAt = now
WHERE UserId = user.Id
  AND IsPersistent = false
  AND RevokedAt IS NULL
  AND UserAgent = <this UA>
  AND IpCreatedAt = <this IP>
  AND Id <> <newSessionId>          -- never self-revoke the row about to be created
```

Then the existing new-row insert runs unchanged. Net effect: one live ephemeral row per (UserAgent, IP). A different browser or a different IP always produces a distinct session, so the audit trail and the future new-session-from-new-IP alert (Stage 12.5.3) stay honest.

**Why B1 (revoke-the-old-duplicate) and not row-reuse:** the `sessionId` is generated and stashed in `HttpContext.Items[PendingSessionItemKey]` **before** `PasswordSignInAsync`, and the claims factory bakes it into the cookie's `sid` claim. By the time `IssueSessionAndCookiesAsync` runs, the just-issued cookie already carries the *new* `sid`. Reusing an old row's id would require moving `sessionId` generation earlier across a semaphore boundary and rewiring the pending-item plumbing at three call sites (login-no-MFA, login-TOTP, backup-code). Revoking the old duplicate achieves the same one-row-per-device outcome with a single `ExecuteUpdateAsync` inside the existing login flow.

**Safety against `SessionRevocationValidator`:** the validator rejects a request whose `sid` points at a `RevokedAt != null` row. The revoke here targets only the OLD row(s) (`Id <> newSessionId`); the new row is inserted unrevoked and carries the `sid` the new cookie holds. The just-logged-in user is unaffected. Confirmed by reading `SessionRevocationValidator.cs:42`.

The superseded row is revoked (ExecuteUpdateAsync, its own statement) before the new row is inserted; there is no enclosing transaction, so a rare failure between the two would leave the old session revoked and the new one absent — the user simply retries the login, which re-dedups idempotently. The superseded row survives as `RevokedAt != null` (audit intact), drops off the list immediately (Part A), and is reclaimed by the sweep at 90 days (Part C).

The persistent-rotation path (`PersistentCookieRotationMiddleware`) already revokes the old row on each rotation, so it needs no dedup — only the shared-constant change (below).

### Part C — Retention sweep (bounds the table)

A **flat cross-tenant `DELETE`** on `UserSessions`:

```
DELETE FROM UserSessions
WHERE (RevokedAt IS NOT NULL AND RevokedAt < now − 90 days)
   OR (RevokedAt IS NULL AND LastUsedAt < now − 90 days)
```

- The first clause is the documented "revoked rows, 90 days" retention. Deleting the row deletes its `UserAgent`, so the two `security-model.md` § Retention lines (revoked sessions + UA strings) are satisfied by one sweep.
- The second clause reclaims any never-revoked row — ephemeral or persistent — past the same horizon, regardless of `IsPersistent`. This matters for an abandoned "remember me" row: it is never revoked (rotation only fires on a return visit), so a clause scoped to `IsPersistent = false` would never sweep it, violating the 90-day retention. Persistent rows still inside their 30-day window are untouched (`LastUsedAt` is recent).

**Cross-tenant path — verified against Stage 7.5 conventions:**

- The sweep runs through **`AdminDbContext`** (Postgres role `ceres_admin`, `BYPASSRLS`), NOT `AppDbContext`/`ceres_app`. Under `ceres_app` the RLS policy scopes every read/write to the current user's GUC, so a cross-tenant DELETE would delete nothing. This is the same reason `UserJobRunner` and the `FailedLoginAttempt` purge use the admin path.
- It is a **flat DELETE**, not a per-user fan-out. It does NOT route through `IUserJobRunner.ForEachUserAsync` or `BackgroundJobScope` (those are for per-user work in a user scope). One `ExecuteDeleteAsync` statement over all rows. This matches the documented `FailedLoginAttempt` "1-year flat cross-tenant DELETE" shape.
- The sweep type must carry `[RequiresAdminContext]` (pinned by `AdminContextDisciplineTests` — every AdminDbContext consumer declares it). If the implementation uses `IgnoreQueryFilters()`, that call site must be added to the `ArchitectureTests` IgnoreQueryFilters allow-list in the same commit. (With `AdminDbContext` + a flat `ExecuteDeleteAsync` filtered on the retention predicate, `IgnoreQueryFilters()` is used for defence-in-depth on the EF-filter side, mirroring `UserJobRunner.cs:26`.)

**Invocation — cron-driven CLI one-shot, mirroring `--seed-dev-user`:**

```
dotnet run --project ProjectCeres -- --sweep-sessions
```

Dispatched from `Program.cs` before `builder.Build()` runs the web host (same location and shape as the `--seed-dev-user` branch): build services, resolve the sweep, run the DELETE, `Environment.Exit(code)`. Cron on the VPS invokes it daily. This matches the established pattern (the app deliberately registers **no** `IHostedService` that touches user tables — see roadmap Stage 9.5h close-out — so an in-process timer is out). The audit-log purge (roadmap Stage 13.6) can reuse this exact cron-command mechanism.

Exit codes: 0 on success (report rows deleted via structured log), non-zero on failure so cron alerting fires. The DELETE is a single statement (atomic, idempotent — safe to run repeatedly).

### Shared lifetime constants (folds A, B, C, and the cookie config into one source of truth)

The 30-minute sliding window and 30-day persistent lifetime are currently **inline literals** in `Program.cs` (`ExpireTimeSpan = FromMinutes(30)`) and `AuthController.cs` (`AddDays(30)`) — they are NOT reusable constants today. Add them to `SessionConstants`:

```csharp
public static readonly TimeSpan EphemeralSlidingWindow = TimeSpan.FromMinutes(30);
public static readonly TimeSpan PersistentLifetime = TimeSpan.FromDays(30);
public static readonly TimeSpan RetentionHorizon = TimeSpan.FromDays(90);
```

Then `Program.cs` cookie config, `AuthController` persistent-cookie `Expires`, the Part A filter, and the Part C sweep all consume these. This prevents the drift where someone changes the cookie window but not the filter (which would re-open exactly this bug).

## Data flow

- **Login:** generate `sessionId` → stash in `HttpContext.Items` → `PasswordSignInAsync` (bakes `sid` into cookie) → `IssueSessionAndCookiesAsync`: **[NEW] revoke same-device duplicates** → add new row → SaveChanges → antiforgery tokens.
- **List:** `GetActiveAsync` → `[NEW] RevokedAt==null AND (live-by-expiry predicate)` → project to `SessionDto`.
- **Sweep (cron):** `--sweep-sessions` → AdminDbContext → `ExecuteDeleteAsync(retention predicate)` → log count → exit.

## Error handling

- Part A: no failure mode (read-only).
- Part B: the revoke (`ExecuteUpdateAsync`) and the new-row insert are separate statements with no enclosing transaction; a rare failure between them leaves the old row revoked and no new row, self-recovered by the user's next login attempt (idempotent re-dedup). The `Id <> newSessionId` guard prevents self-revocation.
- Part C: single-statement DELETE, atomic and idempotent; non-zero exit on failure for cron alerting; never partial-commits.

## Testing

- **Part A (integration, real DB, per-test marker):** expired ephemeral (LastUsedAt backdated >30 min) absent; live ephemeral present; persistent within 30 days present; persistent past 30 days absent; revoked always absent.
- **Part B (integration):** same UA+IP login twice → first row `RevokedAt != null`, exactly one live row; different IP → two live rows; different UA → two live rows; the revoked old row still exists (audit intact, not deleted); the new session's own row is never revoked.
- **Part C (integration):** seed rows at 91 vs 89 days (revoked + expired-ephemeral + abandoned-persistent) → only >90-day rows deleted regardless of `IsPersistent`, live-persistent-within-window survives; a thin test that `--sweep-sessions` dispatches to the sweep logic and exits 0.
- **Architecture:** the sweep type carries `[RequiresAdminContext]` (pinned by `AdminContextDisciplineTests`); its `IgnoreQueryFilters()` call site (if any) is on the `ArchitectureTests` allow-list.

## Verify-against-codebase corrections folded in

1. Lifetime durations are inline literals, not constants — the spec adds them to `SessionConstants` (§ Shared lifetime constants).
2. Part B guards `Id <> newSessionId` and is confirmed safe against `SessionRevocationValidator` (§ Part B).
3. Part C uses `AdminDbContext` (BYPASSRLS) + a flat `ExecuteDeleteAsync`, NOT `IUserJobRunner`/`BackgroundJobScope`; carries `[RequiresAdminContext]`; `AppDbContext` would delete nothing (§ Part C).

## Docs to sync at close-out

- `models.md` § UserSession — note the expiry semantics + retention sweep.
- `security-model.md` § Sessions / § Retention — the "one row per device" intent now enforced by dedup; the sweep names the mechanism behind the 90-day retention line.
- `roadmap-phase-three.md` — a Stage 12.10 section; add the cron registration to Stage 16 hosting; note the audit-log purge (13.6) can reuse the `--sweep-*` cron-command pattern.
- `SessionConstants` change is internal; no api-contract change (no new endpoint).
