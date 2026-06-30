# Stage 12 — Sessions + Support SPA pages (core) — Design

**Date:** 2026-06-30
**Stage:** 12 (roadmap-phase-three.md → `## Stage 12 — Sessions + Support SPA pages`)
**Status:** Design approved 2026-06-30 — pending spec review, then writing-plans.

## Goal

Users can: re-confirm their identity for sensitive actions (a shared reauth dialog), review and revoke their active sessions and block IPs, complete an email change via the pages behind links the app already emails, and submit support tickets with optional screenshot attachments. Build order is dependency-driven: **reauth dialog → sessions → email-change pages → support**.

Heavily `ProjectCeres.Client/` (4 SPA surfaces + 1 shared dialog) → the build routes through `frontend-orchestrator`; tokens/recipes from `docs/design-system.md`, no hard-coded values.

## Scope decisions (locked during brainstorm, 2026-06-30)

**In scope (Stage 12 core), one spec, four dependency-ordered commits:**
1. Reauthentication dialog (12.9) — the shared dependency.
2. Sessions list + revoke + block-IP (12.1–12.3), **minus** the IP-anchor toggle.
3. Email-change SPA pages (12.8).
4. Support page + `SupportTicket` + `SupportTicketAttachment` + admin-notify email (12.4–12.7), **admin-notify EMAIL only** (no admin list UI).

**Deferred (parked in `planning-future.md` + receiving-stage `[ ]`, per `feedback_deferral_requires_receiving_stage_checkbox`):**
- **Per-session IP-anchor toggle** — the roadmap wrongly assumed `UserSession` has the field; it does NOT (fields: `Id, UserId, PersistentTokenHash, IpCreatedAt, UserAgent, CreatedAt, LastUsedAt, RevokedAt, IsPersistent, UsedBackupCodeAtLogin`). It needs a new column + new enforcement + a self-lockout-for-mobile design.
- **Admin ticket-list UI** — needs a roles / admin-identity system the app has zero of today.
- **New-session-from-new-IP alert email** — needs comparison-granularity (exact/subnet/geo) + first-login-suppression design to avoid alert fatigue.

## Key facts established by pre-design research (2026-06-30)

- **Reauth server side is fully shipped (Stage 6c.2):** `[RequireRecentAuth]` + `RecentAuth` policy + custom `IAuthorizationMiddlewareResultHandler`, 5-min window, `401 REAUTH_REQUIRED`, `POST /api/auth/reauth` accepting `{password}` (non-MFA) or `{totpCode}` (MFA, branch on `user.TwoFactorEnabled`), `sid` preserved across refresh. 12.9 is purely the SPA dialog + wiring the `useStepUp` stub (`src/app/auth/use-step-up.ts`, currently rethrows).
- **Email-change API fully shipped (Stage 6.12):** `POST /api/auth/email-change/request` (`[RequireRecentAuth]`) + `/confirm` + `/revoke` (anonymous, token-is-auth). Emitted URLs are prefix-free post-Stage-11: `/email-change/confirm#token=…`, `/email-change/revoke#token=…`. The pages currently dead-end (FIXME at `EmailChangeService.cs:208`). 12.8 is purely the SPA pages.
- **No sessions API exists** — session lifecycle lives in `AuthController` (create at login, revoke at logout) + `UserBlockedIpMiddleware` (enforcement). 12.1–12.3 needs a new controller.
- **`SupportTicket` is greenfield;** the attachment pattern (`TransactionAttachment` + `FileAttachmentService`) is fully reusable.
- **Status codes (api-contract):** create → `201 Created` + `{ id }` + `Location`; delete → `204`; validation → `422` `{error:{code,message,details[]}}`; reauth-needed / invalid-token → `401`; rate-limited → `429` + `Retry-After`.

---

## Commit 1 — Reauthentication dialog (12.9, shared dependency)

Built first: the sessions list (12.1) and email-change request form (12.8) are both `[RequireRecentAuth]`-gated and need this dialog to be usable.

**Behavior (auto-replay, locked):**
- `lib/api-client.ts` already throws `ReauthRequiredError` on a `401 REAUTH_REQUIRED` envelope.
- `useStepUp` (rewrite the stub at `src/app/auth/use-step-up.ts`) becomes the driver: wrap an async action; on `ReauthRequiredError`, **capture the action**, open `<ReauthenticationDialog>`, await success, then **re-run the original action automatically** and return its result to the caller.
- `<ReauthenticationDialog>` collects a **password** (non-MFA) or **TOTP code** (MFA) — field chosen by reading `TwoFactorEnabled` from the auth context (`/api/auth/me`). Submits `POST /api/auth/reauth` with `{password}` or `{totpCode}`. CSRF via `ensureCsrf()` + `X-XSRF-TOKEN`.

**Edge cases (must handle):**
- **Cancel** — action abandoned cleanly; no retry, surfaced to the caller as a benign "cancelled" (no crash, no toast-as-error).
- **Wrong password** — server counts it toward lockout (already enforced); dialog shows the 401/422 envelope message and allows retry; a 429 shows the rate-limit message.
- **Wrong TOTP** — does not poison lockout (server uses `VerifyTwoFactorTokenAsync`); dialog shows error + retry.
- **Self-logout mid-replay** — when the replayed action is "revoke current session" (Commit 2), the retry itself 401s / redirects to `/login`; `useStepUp` must NOT re-open the dialog in a loop — detect the logout outcome and stop.
- **Concurrent gated calls** — a single in-flight dialog; queue or reject concurrent step-ups so two dialogs don't stack.

**Components & boundaries:**

| Unit | Purpose | Depends on |
|---|---|---|
| `useStepUp` (rewrite) | Catch `ReauthRequiredError`, open dialog, replay action on success, return result | api-client, dialog, auth context |
| `<ReauthenticationDialog>` | Collect password/TOTP, POST /reauth, report success/cancel | shipped reauth API, `TwoFactorEnabled` |

**Tests:** `useStepUp` unit — replay-on-success returns the action's result; cancel abandons without re-running; MFA-vs-password branch; self-logout outcome stops the loop; concurrent-call guard. Dialog renders the correct field per `TwoFactorEnabled`; drives end-to-end against the existing `/security` re-enroll-TOTP trigger. Frontend checklist (empty/error/375px) at ship.

---

## Commit 2 — Sessions list + revoke + block IP (12.1–12.3, no IP-anchor)

First reauth-gated SPA surface; consumes Commit 1.

**New server surface — sessions controller, `[RequireRecentAuth]` on the listing/mutating actions:**
- `GET /api/sessions` → list the current user's non-revoked `UserSession` rows: `createdAt`, `lastUsedAt`, `ipCreatedAt`, a user-agent summary, and an `isCurrent` flag (compare row id to the request's `sid` claim). Owner-scoped (global query filter + explicit `.Where(s => s.UserId == ...)` per ADR-0065).
- `DELETE /api/sessions/{id}` → revoke one session (`RevokedAt = now`). `204`. `404` if not the caller's session (no cross-user leak — IDOR-covered). Revoking the current session ends the session; the client redirects to `/login`.
- `POST /api/sessions/block-ip` `{ ipAddress }` → insert a `UserBlockedIp` row for the caller + bulk-revoke all the caller's `UserSession` rows whose `IpCreatedAt == ipAddress`. `204` (or `201` for the created block — spec pins one; lean `204`, it's an action not a fetchable resource). `UserBlockedIpMiddleware` already enforces blocks on subsequent requests.

**The page (`/settings/sessions`):**
- New route under Settings. Reauth-gated client-side: the first `GET /api/sessions` returns `401 REAUTH_REQUIRED` when the window is stale → `useStepUp` opens the dialog → list loads.
- Lists sessions; per-row **Revoke** + **Block this IP**; optimistic refresh after each.
- **Self-affecting guards:** revoking the current session or blocking the current IP logs you out → confirm via `<AlertDialog>` (not a bare button) before acting, then handle the logout → `/login` redirect.
- **Empty state:** "No other active sessions" when only the current row exists.
- **Responsive** (`planning-phase3-responsive.md`): cards on mobile AND tablet, table on desktop; revoke + block targets ≥44×44px on mobile.

**Tests:** revoke own session → cookie no longer authenticates; block own IP → subsequent same-IP request rejected; IDOR (A cannot revoke/see B's session) — integration; reauth-gated (anonymous/stale → 401); empty state; current-session-revoke logout path. Page via Vitest. **Run the sessions API tests against the WAF fixture; the IDOR isolation test is the security gate.**

---

## Commit 3 — Email-change SPA pages (12.8)

Pure frontend wiring of the shipped Stage 6.12 API. Reuse `PasswordReset.tsx` (Stage 9) as the token-page template. Clears the `EmailChangeService.cs:208` FIXME.

**Three surfaces:**
1. **Request form** — inside Settings (authenticated). New-email input → `POST /api/auth/email-change/request` (this endpoint is `[RequireRecentAuth]` → triggers the dialog from Commit 1 when the window is stale). Server emails the new address a confirm link + the old address a revoke link.
2. **Confirm page** — `/email-change/confirm`, anonymous, under `AuthLayout` (like `/password-reset`). `readTokenFromHash(location.hash)` → `POST /api/auth/email-change/confirm`.
3. **Revoke page** — `/email-change/revoke`, anonymous, under `AuthLayout`. Same shape → `POST /api/auth/email-change/revoke`.

**Reuse (don't rebuild):** token-from-hash read, `ensureCsrf()` + `X-XSRF-TOKEN`, 204/401/422 envelope parsing — all from `PasswordReset.tsx`. The emitted URLs already match these routes (prefix-free `#token=`); **verify the emitted base in `EmailChangeService` matches the routes added** before claiming done.

**Tests:** the three pages render + submit; confirm/revoke handle valid/invalid/expired (401) + success (204); request form triggers reauth on stale window; **link-click E2E** added to `ProjectCeres.Client/e2e/` (Stage 9.11 scoped this out — no page existed); FIXME at `EmailChangeService.cs:208` removed. Mobile/empty/error states.

---

## Commit 4 — Support page + SupportTicket + attachments + admin-notify email (12.4–12.7)

The only greenfield-backend feature. Two new `IUserOwned` entities → full registry discipline (`feedback_iuserowned_requires_five_registries`).

**Entities:**

`SupportTicket : IUserOwned`
- `Id` (UUID), `UserId`, `Subject`, `Message`, `Status` (Open/InProgress/Resolved/Closed enum), `Priority` (Low/Normal/High/Urgent enum), `CreatedAt`, `UpdatedAt`.

`SupportTicketAttachment : IUserOwned` (mirrors `TransactionAttachment` exactly)
- `Id` (UUID), `SupportTicketId` (FK), `UserId`, `FileName` (original), `StoredPath` (system-generated, separate per CLAUDE.md), `ContentType`, `FileSizeBytes`, `UploadedAt`.

**Registry set (both entities):**
- `DbSet<SupportTicket>` + `DbSet<SupportTicketAttachment>` on `AppDbContext` + relationship config in `OnModelCreating` (FK attachment→ticket, `OnDelete` per the attachment convention).
- **One RLS migration** covering both tables: `ENABLE` + `FORCE ROW LEVEL SECURITY` + `user_isolation` policy with the fail-closed `NULLIF(current_setting('app.current_user_ref', true),'')::uuid` guard — modeled on `20260526054514_EnableRlsOnEmailConfirmationTokens.cs`. The global query filter + `UserOwnedModel.RlsTables` auto-include both (model-derived since 9.5b). **`RlsParityStartupCheck` + `ParityTests` fail the build/boot until the migration lands** — the tripwire.
- DI for `ISupportTicketService` in `Program.cs`.
- **EN/ES resx pair** + **`EmailTemplateKey.SupportTicketReceived`** (admin-notify template).
- **`AuditLogAction.SupportTicketCreated`** added to the enum AND the documented-set test (the test fails until the new value is documented).
- `verify-stage-completeness` audits both entities' registries before close.

**Attachments — reuse the hardened `FileAttachmentService` pattern (do NOT hand-roll):**
- Optional, multi-file, **max 10 per ticket**, **10 MB each** (mirror the existing constants).
- **Magic-byte MIME inspection** via the existing `MimeDetective` inspector; extension derived from the *detected* MIME, never user input. Whitelist: `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `application/pdf` (the existing allow-list — "visual examples").
- **Filesystem storage, never BLOB;** path `uploads/support/{ticketId}/{guid}.{ext}`.
- Implementation: either extend `IFileAttachmentService` with a support method or factor the shared validate-and-store core; the plan picks the cleaner option against the actual service shape (it has per-entity methods today).

**Server surface (api-contract conventions):**
- `POST /api/support/tickets` → create (`Status=Open`), `201 Created` + `{ id }` + `Location`. `422` on missing subject/message.
- `POST /api/support/tickets/{id}/attachments` → multipart, returns `AttachmentDto` (one level deep, per api-contract). `422` on too-large/wrong-type with the friendly "Accepted types: JPEG, PNG, GIF, WebP, PDF" message.
- `GET /api/support/tickets` → own tickets only (query-filtered).
- `GET /api/attachments/support/{attachmentId}` → owner-scoped stream, `File(data, contentType, fileDownloadName)` (Content-Disposition: attachment), MIME re-verified — mirrors `AttachmentsApiController`.
- **No edit/delete on tickets** (immutable history, per roadmap).
- **Admin notification (12.6) = email only:** on create, `IEmailService.SendAsync` to the configured admin address via the EN/ES `SupportTicketReceived` template. **No admin list UI** (deferred).

**The page (`/support` — currently a `PagePlaceholder` stub):**
- Ticket form: subject (required), message (required), priority select, **optional file picker** ("Attach screenshots — optional") showing selected files with size + remove control before submit. On submit → ticket created + admin emailed; form clears; new ticket appears in the list.
- Own-tickets list: subject, status badge, last-update, view link. View shows attachments with download links.
- **Status colors** (roadmap-fixed): Open=sky, InProgress=amber, Resolved=emerald, Closed=zinc — via `<Badge variant>`. Confirm these semantic variants exist in the design system; if a color is missing, add the token to `index.css` + document in `docs/design-system.md` before consuming (CLAUDE.md rule).
- **Responsive:** form full-page on mobile / modal-or-full-page on desktop (form-presentation rule); list cards on mobile, table on desktop; targets ≥44×44px.

**Tests:** create ticket → `201` + `Status=Open` + admin email sent; own-tickets list shows only the caller's; **IDOR** — A cannot view B's ticket OR download B's attachment (RLS + owner check); attachment optional (submits fine with none); oversized/wrong-type rejected with the right error; RLS parity + startup check pass (proves the migration landed); documented-audit-set test passes. Mobile cards.

---

## Deferrals (Section 5 — parked, not dropped)

Each lands in `planning-future.md` (the WHAT + the design questions to resolve) with a receiving roadmap `[ ]`, and is removed from under Stage 12's checklist so Stage 12 closes with zero orphaned `[ ]` (Phase E). Same treatment as the Stage 11.5 relocation.

| Deferred item | Why | Open design questions to record |
|---|---|---|
| Per-session IP-anchor toggle | `UserSession` has no such field; new column + enforcement + self-lockout risk | exact-IP vs subnet anchoring; where enforcement hooks; mobile-roaming lockout guard |
| Admin ticket-list UI | No roles / admin-identity infra exists | how "admin" is identified (role claim? configured email? single-operator?); ADR-0065 Admin/ scope |
| New-session-from-new-IP alert email | False-positive / alert-fatigue risk | comparison granularity (exact/subnet/geo); first-login suppression; opt-out-by-default wiring; `NewSessionAlert` template |

## Out of scope

- Any roles / admin-identity system (blocks the admin ticket-list UI).
- IP-anchor column or enforcement.
- New-session-alert detection or template.
- Ticket edit/delete, admin reply mechanism (users see "We'll respond by email").
- Currency/derived-column changes (none in this stage).

## Cross-references

- Roadmap: `docs/roadmap-phase-three.md` → `## Stage 12`.
- `security-model.md` § Reauthentication (shipped 6c.2), § Email Address Change (shipped 6.12).
- `planning-phase3.md` § Support ticket system, § Sessions.
- `planning-phase3-responsive.md` § Surface Inventory (sessions/support breakpoints, form-presentation rule).
- ADR-0019 (sessions), ADR-0065 (query filters + explicit `.Where`), ADR-0068 (RLS), ADR-0067 (audit logging — no financial amounts).
- Exemplars to reuse: `TransactionAttachment` + `FileAttachmentService` (attachments); `PasswordReset.tsx` (token pages); `20260526054514_EnableRlsOnEmailConfirmationTokens.cs` (RLS migration).
