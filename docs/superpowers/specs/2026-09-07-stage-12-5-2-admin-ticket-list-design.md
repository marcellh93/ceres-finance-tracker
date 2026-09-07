# Stage 12.5.2 — Admin ticket-list / triage surface — Design

**Status:** Design (2026-09-07). Brainstormed with `ceres-researcher` (terrain map) + user fork decisions **1a + 2a**. Verified against codebase (Phase B) before writing.

## Problem

The support system (Stage 12.6) shipped an operator *write* endpoint (`POST /api/admin/support/tickets/{id}/messages` — reply + set status) but **no way for an operator to see that a ticket exists**. Today an admin learns of a ticket only from the notification email; if that email is dropped, the ticket is invisible (the Stage 12.5 accepted-risk A2). This stage adds the **read/triage** side: an admin list of all tickets + a thread view, and a minimal SPA to use them.

Closing this **retires the § 12.5 accepted-risk A2** (dropped notification makes a ticket invisible) and **reframes the Stage 16 dropped-notification log-alert** (currently justified "until § 12.5.2 ships").

## Decisions (locked)

- **Fork 1 = 1a — minimal admin area now.** Build a small `features/admin/` with the triage list page + one route. Stage 15.8's "Admin screen" later grows *this* area. (1b — defer the UI — rejected: it would leave 12.5.2 half-done and A2 open.)
- **Fork 2 = 2a — server-gated, no cached admin flag.** Do NOT add `isAdmin` to `/api/auth/me` or `AuthUser`. The admin route renders a page that calls the admin API; a non-admin is refused server-side (403) and shown a "not authorized" state. Honors ADR-0080's live-role-check model — a cached flag would go stale on revoke. (2b — cache the flag — rejected on that security ground.)

## Backend

Extend the **existing** `ProjectCeres/Admin/SupportAdminApiController` (already `[ApiController]`, `[Route("api/admin/support")]`, `[RequireAdmin]`, `[RequiresAdminContext]`, injecting `AdminDbContext`). Two new GET actions — the same cross-tenant read pattern as the shipped `PostMessage`, minus the write:

### `GET /api/admin/support/tickets` — the triage list

- Reads `adminDb.SupportTickets.IgnoreQueryFilters()` (BYPASSRLS `ceres_admin`; `IgnoreQueryFilters` legal under `Admin/` per ADR-0065). Spans **all** users by design — the IDOR-404 rule applies to single-id fetches, not the list.
- **Pagination (new — no prior support-surface convention).** Offset-based: query params `page` (1-based, default 1) + `pageSize` (default 25, cap 100). Response envelope `{ items: AdminTicketListItemDto[], page, pageSize, total }`. Offset over cursor because a triage list wants jump-to-page + a total count, and ticket volume in beta is small; documented as the choice so a later cursor migration is a conscious change.
- **Ordering:** newest activity first — `ORDER BY UpdatedAt DESC` (a triage operator wants the most-recently-active tickets on top), tiebreak `CreatedAt DESC`.
- **Filters (optional query params):** `status` (SupportTicketStatus), `priority` (SupportTicketPriority). Absent = all. (Age/text search deferred — not needed to retire A2.)
- **DTO — `AdminTicketListItemDto`.** Copies `SupportTicketListItem` (Id, Subject, Status, Priority, PrecedingTicketId, CreatedAt, UpdatedAt, MessageCount, LastMessageAt) **plus owner identity**: `OwnerUserId` (Guid) + `OwnerEmail` (string). Bodies never materialised (MessageCount/LastMessageAt are a SQL rollup, mirroring `ListOwnAsync`).
- **Owner→email join.** `SupportTicket.UserId` → `AspNetUsers.Email`. `AdminDbContext` is an `AppDbContext` subclass and exposes the Identity `Users` set, so the join is expressible in one projected query on `adminDb` — no `UserManager` needed. Project `Email` only (no other Identity fields).

### `GET /api/admin/support/tickets/{id}` — the thread

- Same `IgnoreQueryFilters()` single-row read; **404-not-403** on a miss (matches `PostMessage` + the user surface IDOR rule).
- Returns the owner identity + the full ordered `SupportMessage` thread. Reuse the user surface's `MessageDto`/`AttachmentDto` shapes verbatim; wrap in an `AdminTicketThreadDto` that adds `OwnerUserId`/`OwnerEmail`.
- Row actions (reply / set status) on the SPA call the **existing** `POST .../{id}/messages` — no new write in this stage.

### Backend constraints honored
- No new `IUserOwned` entity (DTOs aren't entities) → five-registry rule N/A.
- `[RequiresAdminContext]` already on the class covers the new `AdminDbContext` reads; no new markers.
- `[RequireAdmin]` stays class-level (never per-action, never `[Authorize(Roles=...)]` — ADR-0080).
- No `IUserOwned` write → no owner-stamping concern (read-only).

## Frontend (1a + 2a)

New `ProjectCeres.Client/src/app/features/admin/` (mirrors `features/support/`):
- `admin-support-api.ts` — typed client for the two GET endpoints + the pagination envelope.
- `AdminTicketListPage.tsx` — a paginated table (Subject, owner email, Status badge, Priority, MessageCount, last activity), status/priority filters, row → thread.
- `AdminTicketThreadSheet.tsx` (or page) — the thread + reply/status actions calling the existing operator endpoint. (Reuse `SupportThreadSheet`/`ReplyComposer` patterns.)
- **Route:** under the protected `RequireAuth` branch in `App.tsx`, e.g. `admin/support` → the list, `admin/support/:ticketId` → the thread.
- **Admin gate (2a):** a `RequireAdmin` **client** wrapper that does NOT read a cached flag. On mount the admin page issues the admin API call; a `403` renders a "You don't have access to this page" state (and no admin nav link is shown to non-admins — the nav link's presence is itself driven by a lightweight check, not a stored boolean). No `MeResponse`/`AuthUser` change. The **server** `[RequireAdmin]` live check is the authority; the client gate is only UX.

## Tests

- **Integration** (`ProjectCeres.Tests/Integration/Admin/SupportAdminApiTests.cs`, extend): list returns tickets across users (seed 2 users' tickets, assert both appear with correct owner email); pagination (page/pageSize/total); status+priority filters; thread fetch by id returns owner + messages; **403 for a non-admin** on both GETs; **404** for an unknown/absent ticket id on the thread GET.
- **RLS parity (new case).** Add to `ProjectCeres.Tests/Integration/Rls/…Group3_AdminAndBackgroundTests` a case pinning the **list** read path: `ceres_admin` sees all users' `SupportTicket` rows, `ceres_app` (a non-owner) sees zero — so the cross-tenant list is proven to rely on BYPASSRLS, not an accident.
- **Vitest:** `AdminTicketListPage` renders rows + owner email, paginates, filters; the 403 → not-authorized state; the thread view.
- **E2E:** an admin user (grant via `AdminRoleService`/seed) loads `/app/admin/support`, sees a seeded ticket from another user, opens the thread. A non-admin hitting the route is bounced.

## Evidence bundle (touches admin/auth SOURCE + endpoints + SPA)
- reviewer-pipeline (Common/Authentication-adjacent admin auth) · curl-transcript (2 new endpoints) · agent-walk quartet (SPA) · build-matrix · turn-shape.

## Close-out (sync-docs)
- Retire the § 12.5 accepted-risk A2 line (dropped notification → invisible ticket) — now false, an operator can read all tickets.
- Reframe the Stage 16 dropped-notification log-alert (drop "until § 12.5.2 ships").
- Update `docs/api-contract.md` (new admin endpoints + the offset-pagination envelope — first paginated list, so it sets the convention) and `docs/security-model.md` § Access Control (admin ticket read via BYPASSRLS, documented).
