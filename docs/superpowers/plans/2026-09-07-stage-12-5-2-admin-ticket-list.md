# Admin Ticket-List / Triage Surface — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (inline). Steps use `- [ ]`.

**Goal:** Give operators a read/triage surface for support tickets — a paginated cross-user list + a thread view — so a ticket is visible even if its notification email is dropped.

**Architecture:** Extend the existing `SupportAdminApiController` (already `[RequireAdmin]` + `[RequiresAdminContext]`) with two GET endpoints reading via `AdminDbContext.IgnoreQueryFilters()`; a minimal `features/admin/` SPA gated server-side (no cached admin flag).

**Tech Stack:** ASP.NET Core, EF Core (Npgsql), React 19 + Vite + TS, Vitest, Playwright, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-07-stage-12-5-2-admin-ticket-list-design.md`

## Global Constraints
- `IgnoreQueryFilters()` legal ONLY under `Admin/` (ADR-0065). `[RequireAdmin]` class-level, live check, never `[Authorize(Roles=...)]` (ADR-0080). No cached `isAdmin` client flag (fork 2a). Bare JSON responses, not `{data:...}` envelopes (apiFetch surfaces body as `.data`). 404-not-403 for a single-id miss (IDOR). 422 `VALIDATION_ERROR` envelope for bad input. Owner-stamping N/A (read-only).

## File structure
- **Modify** `ProjectCeres/Admin/SupportAdminApiController.cs` — add 2 GET actions + list/thread DTO records.
- **Modify** `ProjectCeres.Tests/Integration/Admin/SupportAdminApiTests.cs` — list/thread/pagination/filter/403/404 tests.
- **Modify** the RLS parity `Group3_AdminAndBackgroundTests` — list read-path case.
- **Create** `ProjectCeres.Client/src/app/features/admin/admin-support-api.ts`, `AdminTicketListPage.tsx`, `AdminTicketThreadSheet.tsx`, `AdminTicketListPage.test.tsx`.
- **Create** `ProjectCeres.Client/src/app/admin/RequireAdmin.tsx` (client gate, no cached flag).
- **Modify** `ProjectCeres.Client/src/app/App.tsx` — admin routes; `TopBar.tsx` — admin nav (probe-driven).
- **Modify** `ProjectCeres.Client/e2e/…` — admin support E2E.

---

### Task 1: Backend — admin ticket list endpoint

**Files:** Modify `ProjectCeres/Admin/SupportAdminApiController.cs`; Test `ProjectCeres.Tests/Integration/Admin/SupportAdminApiTests.cs`.

**Produces:** `GET /api/admin/support/tickets?page&pageSize&status&priority` → `{ items: AdminTicketListItemDto[], page, pageSize, total }`; `AdminTicketListItemDto(Id, Subject, Status, Priority, PrecedingTicketId, CreatedAt, UpdatedAt, MessageCount, LastMessageAt, OwnerUserId, OwnerEmail)`.

- [ ] Test: seed 2 users' tickets via admin ctx → GET returns both, correct OwnerEmail, ordered UpdatedAt DESC.
- [ ] Test: `pageSize=1` → `items.Length==1`, `total==N`; `page=2` returns the next.
- [ ] Test: `?status=Open` filters; `?priority=…` filters.
- [ ] Test: non-admin → 403.
- [ ] Implement: projected query on `adminDb.SupportTickets.IgnoreQueryFilters()` joined to `adminDb.Users` for email, message rollup subquery for `MessageCount`/`LastMessageAt`, `Skip/Take`, `CountAsync` for total. Clamp `pageSize` 1..100, `page>=1`.
- [ ] Run the 4 tests (filter `SupportAdminApiTests`) → green. Commit.

### Task 2: Backend — admin thread endpoint

**Files:** same controller + tests.

**Produces:** `GET /api/admin/support/tickets/{id:guid}` → `AdminTicketThreadDto(Id, Subject, Status, Priority, OwnerUserId, OwnerEmail, Messages: MessageDto[])`; 404 on miss.

- [ ] Test: seed a ticket+messages → GET returns owner + ordered messages.
- [ ] Test: unknown id → 404. Non-admin → 403.
- [ ] Implement: `IgnoreQueryFilters().FirstOrDefault(t=>t.Id==id)`; if null → NotFound. Load messages ordered by CreatedAt; project to the reused `MessageDto` shape.
- [ ] Run tests → green. Commit.

### Task 3: RLS parity — list read path

**Files:** Modify `ProjectCeres.Tests/Integration/Rls/…Group3_AdminAndBackgroundTests`.

- [ ] Test: seed userA + userB tickets; `ceres_admin` list read sees BOTH; a `ceres_app` context acting as a non-owner sees ZERO (filter-stripped, proving BYPASSRLS carries the list).
- [ ] Run → green. Commit.

### Task 4: Frontend — admin API client + list page (via frontend-orchestrator)

**Files:** Create `admin-support-api.ts`, `AdminTicketListPage.tsx`, `AdminTicketListPage.test.tsx`.

**Consumes:** the Task 1/2 endpoints. **Produces:** `AdminTicketListPage` route element.

- [ ] `admin-support-api.ts`: typed `listAdminTickets({page,pageSize,status,priority})` + `getAdminTicket(id)`, DTO types mirroring the C# records, `ADMIN_TICKETS_URL`.
- [ ] Vitest: renders rows + OwnerEmail, paginates (page controls call with new page), filters, 403 → not-authorized state.
- [ ] Implement page: `Card`+table (`Badge` for status/priority), pagination controls, status/priority filter selects, row → thread. Use existing design-system recipes.
- [ ] Run Vitest → green. (No commit yet — pairs with Task 5/6 for the show-then-E2E gate.)

### Task 5: Frontend — RequireAdmin gate + thread + routes (2a)

**Files:** Create `RequireAdmin.tsx`, `AdminTicketThreadSheet.tsx`; Modify `App.tsx`, `TopBar.tsx`.

- [ ] `RequireAdmin.tsx`: wraps children; on mount does NOT read a cached flag — renders children (the page's own API call 403s for non-admins and shows the not-authorized state). No `MeResponse`/`AuthUser` change.
- [ ] `AdminTicketThreadSheet`: reuse `SupportThreadSheet`/`ReplyComposer` patterns; reply/status → existing `POST .../{id}/messages`.
- [ ] `App.tsx`: `admin/support` + `admin/support/:ticketId` under RequireAuth+RequireAdmin.
- [x] **Admin nav link — DEFERRED to Stage 15.8 (deliberate, 2026-09-07).** The nav is static data in `nav-items.ts` consumed by `Sidebar` + `MobileDrawer`; a *conditional* admin item needs an admin-probe threaded into that shared chrome, which is over-engineering the FIRST admin surface for one link. Admins reach `/app/admin/support` directly (or by a bookmarked link); a non-admin who navigates there gets the page's not-authorized state (2a). Stage 15.8 ("Admin screen") is where a proper admin-nav treatment belongs once there's more than one admin page. Keeps the 2a posture cleanest (no admin-ness signal in the chrome). Recorded in the roadmap so it isn't silently dropped.
- [ ] Vitest for gate + thread. Run → green.

### Task 6: E2E + verify + commit frontend

- [ ] E2E (`e2e/…/admin-support.spec.ts`): grant admin (seed/AdminRoleService), load `/app/admin/support`, see another user's seeded ticket, open thread; a non-admin is bounced/not-authorized.
- [ ] `pnpm build` + `pnpm test` green; run the E2E (3 browsers).
- [ ] Commit backend+frontend together.

### Task 7: Evidence bundle + close-out

- [ ] 3-agent reviewer pipeline on the admin/auth diff → reviewer-pipeline.json (no block).
- [ ] curl-transcript (both GETs), agent-walk quartet, build-matrix, turn-shape.
- [ ] sync-docs: retire §12.5 A2 accepted-risk; reframe Stage 16 dropped-notification alert; `api-contract.md` (offset-pagination convention); `security-model.md` § Access Control (admin BYPASSRLS ticket read). Tick §12.5.2 roadmap items.
