# Stage 13.8 — Full Data Export (GDPR Right of Access / Portability) — Design

**Status:** Draft for review (2026-09-19)
**Stage:** 13.8 (Phase 3 — GDPR baseline)
**Depends on:** ADR-0067 (background-job user scope), Stage 6.15 (`TokenLookupHasher`), Stage 7.5 (RLS wall + `AdminDbContext`), Stage 12.10 (`SweepSessions` cron pattern), Stage 12.15 (Reqnroll BDD).
**Unblocks / de-risks:** Stage 13.9 (right-to-erasure) reuses the user-content enumeration seam established here.

---

## 1. Purpose

Give a user a downloadable archive of their own data, satisfying GDPR **Right of Access** (Art. 15) and **Right to Data Portability** (Art. 20). The archive is assembled asynchronously and delivered via a one-time, expiring download link emailed to the user.

This is categorically distinct from the existing synchronous per-resource CSV exports (`GET /api/movements/export.csv`, the `/api/reports/*` CSVs), which export a filtered *slice* of one resource on demand. This is the whole-account archive.

## 2. Decisions locked in brainstorming (2026-09-19)

| # | Decision | Rationale |
|---|---|---|
| D1 | **Async via durable job row + poll-drain cron worker** (not in-process, not fire-and-forget). | Consistent with the recorded external-cron scheduler decision; runs once regardless of cloud instance count; survives restarts. Synchronous rejected (HTTP worker exhaustion, `planning-phase3.md`). |
| D2 | **`ExportJob` is a user-owned entity** (joins the RLS wall; 5-registry rule). | The download hands a user their entire dataset — ownership must rest on the database wall, not a hand-written filter. |
| D3 | **Download link = stored-token pattern**, reusing `TokenLookupHasher`. | Proven idiom (password-reset / email-change / lockout-unlock); makes single-use trivial (`ConsumedAt` stamp). Stateless-signed-URL rejected (can't be single-use without a DB check anyway). |
| D4 | **Export contents = financial data + attachments + profile; security/audit logs excluded.** | Serves access + portability with the data a user means by "my data"; security logs sit under a separate legal basis. |
| D5 | **Module = `api/profile`** (self-service controller); **action = `export`**. | Single-responsibility controller for the user's own account lifecycle (export now, erasure next). Named `profile` — NOT `account` — to avoid a one-character collision with `api/accounts` (financial accounts). |
| D6 | **Download requires login AND a valid token.** | The link lands in an email inbox (forwardable / breachable). Token proves *which* export; session proves *who's asking*. |
| D7 | **A single-sourced user-content entity list** is the seam 13.9 erasure reuses. | Export *reads* over it, erasure *deletes* over it — single-sourcing prevents drift (an entity in export but not erasure = a retention bug). Derives from `UserOwnedModel.FinanceTables(model)`. |
| D8 | **Format is a request parameter (default `zip`), not part of the route.** | Keeps the `export` action stable as formats grow (xlsx/json later become allowed `format` values, not new endpoints). Only `zip` ships in 13.8. |

## 3. API surface

New `ProfileApiController` at `[Route("api/profile")]`.

### `POST /api/profile/export`
- **Auth:** `[Authorize]` + `[RequireRecentAuth]` (reauth within the last 5 min).
- **Rate limit:** 1 per 24h per user — new `ProfileExportByUser` policy following the existing `*ByUser` partition idiom (`AuthReauthByUser` / `EmailByUser`). Returns `429` with `Retry-After` when exceeded.
- **Body (optional):** `{ "format": "zip" }`. Default `zip`. `13.8` accepts only `zip`; any other value → `422` (unsupported format) via the ModelState/`UnprocessableEntity` convention.
- **Behaviour:** if the user has a `Pending` or `Processing` job, return *that* job (idempotent — no duplicate). Otherwise create a new `ExportJob` (status `Pending`) and return it.
- **Success:** `202 Accepted` with the api-contract.md §Status-Codes shape:
  ```json
  { "data": { "jobId": "…", "message": "Export started. You will be notified by email when ready." } }
  ```

### `GET /api/profile/export/download?token=<raw>`
- **Auth:** `[Authorize]` (login required — D6). NOT `[RequireRecentAuth]` (the token is the second factor; forcing reauth on a click-through email link is hostile).
- **Behaviour:** compute `TokenLookupHasher.ComputeLookup(rawToken)`, look the job up by `TokenLookup` **under the requesting user's own scope** (the RLS wall guarantees a foreign job is invisible → 404). Validate: job belongs to the current user, status `Ready`, not expired (≤24h since ready), `ConsumedAt` null. On success: stream the ZIP as a file download, stamp `ConsumedAt`.
- **Status codes:** `200` file stream on success; `401` if not logged in; `404` if the token matches no job in the user's scope (don't reveal existence); `410 Gone` if expired or already consumed.

## 4. Data model — `ExportJob` (user-owned)

```
ExportJob : IUserOwned
  Id             Guid      (PK)
  UserId         Guid      (RLS discriminator)
  Status         ExportJobStatus enum: Pending | Processing | Ready | Failed
  Format         ExportFormat enum: Zip   (only value in 13.8)
  RequestedAt    DateTimeOffset
  ReadyAt        DateTimeOffset?           (set when the ZIP is built)
  ExpiresAt      DateTimeOffset?           (ReadyAt + 24h)
  ConsumedAt     DateTimeOffset?           (single-use stamp)
  FailureCount   int                       (retry budget)
  StoredPath     string?                   (filesystem path to the ZIP; never a DB blob)
  TokenLookup    byte[]                    (HMAC fingerprint; unique index; empty until Ready)
  EmailedAt      DateTimeOffset?           (idempotency for the ready-email)
```

- **No raw token stored** — only the `TokenLookup` HMAC fingerprint (Stage 6.15 pattern). The raw token exists only in the emailed URL.
- **`StoredPath`** points at a file on the export directory (mirrors the attachment convention: files on the filesystem, never BLOBs). Separate from any user-facing name.
- **5-registry rule (D2):** `DbSet<ExportJob>` on `AppDbContext`; `OnModelCreating` global filter + unique index on `TokenLookup`; membership in the RLS-table set (`UserOwnedModel` / `UserOwnedTables.All`); the `AddRowLevelSecurityPolicies`-style RLS migration enabling `user_isolation`; DI for `ExportJobService`. The `verify-stage-completeness` gate enforces completeness.

## 5. Export contents (D4 / D7)

The ZIP contains:
- **One CSV per user-content entity** — enumerated from the single-sourced `UserContentEntities` list (§7), built via `CsvFormattingHelper` (UTF-8 BOM for Excel; `=@+-` injection escaping). Entities: accounts, transactions, transfers, categories, category-budgets, goal-budgets, saved-reports, liability-payments, recurring-transactions, support tickets + messages — i.e. the full `UserOwnedModel.FinanceTables(model)` set (RLS tables minus auth-internal). If a future entity must be withheld from export despite being user-content, it is named in an explicit exclusion constant with a comment — there is none in 13.8.
- **`profile.csv`** — the user's own profile fields (display name, email) + their `Settings` row.
- **Attachment files** — the actual files referenced by `TransactionAttachment` / `TransferAttachment` / support-message attachments, pulled from the filesystem by `StoredPath`, placed under an `attachments/` folder in the ZIP.
- **`manifest.txt`** — lists every file included + the export timestamp. Doubles as the record referenced by the audit-log row.

**Explicitly excluded:** `AuditLog`, `FailedLoginAttempt`, `UserSession`, blocked-IP rows, and any auth-internal table (`UserOwnedModel.RlsTables` minus `FinanceTables`). These sit under a separate (security) legal basis and are not part of a self-service data dump.

## 6. Components (single-responsibility units)

| Unit | Responsibility | Reuse |
|---|---|---|
| `ExportJob` + migration | Durable, user-owned job row | New (5-registry) |
| `ExportJobService` | Create (dedupe + rate-limit check), read the caller's own job, look up by token under user scope | New |
| `UserContentEntities` | The single-sourced "what is user content" list (§7) | New — the 13.9 seam |
| `DataExportBuilder` | Enumerate → CSVs + attachment files + manifest → ZIP on filesystem | New; uses `CsvFormattingHelper`, `System.IO.Compression` |
| `ExportJobWorker` | Cron entry: poll-drain `Pending`, build, tokenize, email, cleanup expired | New; `SweepSessions` + `AdminDbContext` pattern |
| `ProfileApiController` | `POST /export`, `GET /export/download` | New |
| Email `GdprExportReady` | Ready notice + download link (EN + ES) | New `EmailTemplateKey` + resx pair |
| `/settings/account` SPA page | Export button + async "we'll email you" confirmation (Sonner toast) | New page; existing `button` + `sonner` primitives |

### Two access paths (made explicit)
- The **worker** enumerates `Pending` jobs **across all users** through `AdminDbContext` (BYPASSRLS) — a cron job acts as no single user (SweepSessions pattern).
- The **download endpoint** reads **one job under the caller's own user scope** (RLS-enforced) — foreign job → 404.

## 7. The `UserContentEntities` seam (D7)

A single static list (or model-derived function) naming the entities that constitute "the user's own content." `DataExportBuilder` iterates it to produce CSVs; Stage 13.9's erasure will iterate the **same** list to delete. Derives from `UserOwnedModel.FinanceTables(model)` so that adding a user-content table automatically flows into both export and erasure. A negative-assertion test pins that no security/auth-internal table appears in the list.

## 8. Worker lifecycle & error handling

- **Trigger:** `dotnet run --project ProjectCeres -- --run-export-jobs`, dispatched in `Program.cs` alongside `--sweep-sessions`. Cloud scheduler invokes it on a timer (e.g. every 5 min); export readiness is bounded by that interval (acceptable for an emailed export).
- **Claim:** worker selects `Pending` rows, flips each to `Processing` (so a concurrent run can't double-build).
- **Build success:** write ZIP to `StoredPath`, generate raw token + store `TokenLookup`, set `Status=Ready`, `ReadyAt`, `ExpiresAt=ReadyAt+24h`, queue the `GdprExportReady` email, stamp `EmailedAt`.
- **Build failure:** increment `FailureCount`; retry up to 3 runs; on exhaustion set `Status=Failed` and email a "please retry" notice. Delete any partial ZIP (never serve a partial export).
- **Crash after build, before email:** a re-run detects `Status=Ready` with `EmailedAt=null` and re-sends (idempotent).
- **Cleanup:** the same worker deletes the ZIP file for jobs past `ExpiresAt` or already `ConsumedAt`, and nulls `StoredPath`.

## 9. Frontend (`/settings/account`)

- New SPA route `<Route path="settings/account" element={<Account />} />` (sibling of `settings/sessions`), routed through `frontend-orchestrator` at build time.
- The page hosts a **"Export my data"** button (existing `<Button>` primitive). Clicking it: reauth step-up if needed (the existing `StepUpProvider` / `useStepUp` auto-replay), `POST /api/profile/export`, then a Sonner `toast()` confirming "Export started — we'll email you when it's ready." No spinner-until-done (it's async).
- The button meets the 44×44px mobile touch-target (app-wide `Button` `max-sm:h-11`); the confirmation copy wraps cleanly at 375px (roadmap 13.8 verification item).
- The page is the shared home for 13.9's erasure section (built here, extended there).

## 10. Testing (ship-gate — negative assertions per `feedback_test_edge_cases_as_ship_gate`)

Integration:
- Rate limit: 2nd `POST /export` within 24h → `429`.
- Dedupe: a 2nd `POST` while `Pending` returns the same job, no new row.
- `POST` without recent auth → `401 REAUTH_REQUIRED`.
- Download with a **consumed** token → `410` (NOT the ZIP).
- Download with **another user's** token → `404` (RLS invisibility; not 403).
- Download with a valid token but **not logged in** → `401` (D6 — the dual gate).
- Download after **expiry** → `410`.
- Unsupported `format` → `422`.

Builder / content:
- ZIP **excludes** `AuditLog` / `FailedLoginAttempt` / `UserSession` (negative assertion pinning the D4 content boundary).
- CSV cells beginning `=`/`+`/`-`/`@` are escaped (injection); files carry the UTF-8 BOM.
- Builder enumerates from `UserContentEntities` — a test asserts every `FinanceTables` entity is represented, so a new user-content table is caught.

Worker:
- A `Failed`-then-retried job; a crash-after-build re-run re-emails without rebuilding (idempotency).

BDD (Reqnroll, per 12.15):
- Golden path: request → worker runs → email link → download succeeds → second download blocked (410).

## 11. Out of scope (deferred)
- Formats other than `zip` (the `format` param reserves the seam).
- Erasure (13.9) — reuses `UserContentEntities` and the `/settings/account` page.
- A user-facing job-status polling UI — the delivery channel is email, not an in-app progress bar.

## 12. Docs to sync on completion
- `api-contract.md` — the two `api/profile/export*` endpoints + codes.
- `models.md` — `ExportJob` entity + retention (its ZIP file follows the 24h expiry; the row itself follows normal user-owned lifecycle).
- `security-model.md` — the download dual-gate (token + session) + the export-directory file handling.
- `design-system.md` — only if a new primitive/recipe is introduced (not expected; reuses button + sonner).
- `roadmap-phase-three.md` §13.8 checklist.
