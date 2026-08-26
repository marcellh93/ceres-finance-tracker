# Stage 12 — Sessions + Support SPA pages (core) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Stage 12 core — a shared reauthentication dialog, a sessions list (revoke + block-IP), the email-change SPA pages, and a support page with optional screenshot attachments backed by a new `SupportTicket` entity.

**Architecture:** Four dependency-ordered commits. The reauth dialog lands first because the sessions list and email-change request form are both `[RequireRecentAuth]`-gated and consume it. Email-change pages and the reauth flow wire already-shipped APIs (Stage 6.12 / 6c.2); only `SupportTicket` + its attachment are new backend. All SPA work routes through `frontend-orchestrator`.

**Tech Stack:** ASP.NET Core 10 Web API, EF Core + Npgsql + Postgres RLS, React 19 + Vite + TypeScript, shadcn/`@base-ui/react`, Vitest + Playwright, xUnit + FluentAssertions.

## Global Constraints

- **Status codes (api-contract.md):** create → `201 Created` + `{ id }` + `Location`; delete/action → `204`; validation → `422` `{error:{code,message,details[]}}`; reauth-needed / invalid-token → `401`; rate-limited → `429` + `Retry-After`. NEVER 400 for validation.
- **Reauth API (shipped):** `POST /api/auth/reauth` body `{password}` (non-MFA) or `{totpCode}` (MFA); `204` ok; `422` missing-required-field; `401 INVALID_REAUTH` / `401 ACCOUNT_LOCKED_OUT`.
- **IUserOwned discipline (`feedback_iuserowned_requires_five_registries`):** every new `: IUserOwned` entity lands in DbSet + OnModelCreating + an RLS migration (ENABLE+FORCE+`user_isolation`) + DI (if serviced) + EN/ES resx + `EmailTemplateKey` + `AuditLogAction` documented-set where it emits one. The global query filter + `UserOwnedModel.RlsTables` auto-include it; `RlsParityStartupCheck` + `ParityTests` fail the build/boot until the RLS migration lands.
- **File attachments (CLAUDE.md):** filesystem, never BLOB; `FileName` (original) separate from `StoredPath` (system-generated); reuse `FileAttachmentService` (magic-byte MIME via MimeDetective, whitelist JPEG/PNG/GIF/WebP/PDF, 10 MB cap, max 10 per parent, extension derived from detected MIME never user input).
- **Frontend:** `pnpm --dir ProjectCeres.Client …` only; design-system tokens/recipes, no hard-coded values; `<Badge variant>` for status, never hand-rolled spans; base-ui idioms (`render={...}` not `asChild`); show rendered result + wait for approval before commit; UX checklist (golden path / empty / error / 375px / nav) before done.
- **Test-DB rule:** never run `dotnet test` in background or concurrently (shared `project_ceres_test` collision). Run ONE scoped filter at a time, foreground. The Stop hook runs the full suite on turn-end.
- **No `Co-Authored-By` trailer** — no attribution trailer of any kind, in any commit. This plan originally mandated one here and embedded it in four copy-paste commit blocks; corrected 2026-08-27. See CLAUDE.md § What NOT to Do (violated 2026-08-09 across 14 commits, required a full-history rewrite to undo).

---

## COMMIT 1 — Reauthentication dialog (12.9, shared dependency)

### Task 1: `<ReauthenticationDialog>` component

**Files:**
- Create: `ProjectCeres.Client/src/app/auth/ReauthenticationDialog.tsx`
- Create: `ProjectCeres.Client/src/app/auth/ReauthenticationDialog.test.tsx`

**Interfaces:**
- Produces: `<ReauthenticationDialog open onSuccess onCancel />` where `open: boolean`, `onSuccess: () => void`, `onCancel: () => void`. Reads `useAuth().user.twoFactorEnabled` to pick the field. Submits `POST /api/auth/reauth`.

- [ ] **Step 1: Write failing tests**

```tsx
// ReauthenticationDialog.test.tsx — render with a mocked auth context + mocked fetch.
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ReauthenticationDialog } from './ReauthenticationDialog';

// Mock the auth context hook the dialog reads.
vi.mock('./auth-context', () => ({ useAuth: vi.fn() }));
import { useAuth } from './auth-context';

beforeEach(() => {
  vi.restoreAllMocks();
  global.fetch = vi.fn(async (url: string) =>
    url.includes('/api/auth/csrf')
      ? ({ headers: new Headers({ 'X-XSRF-TOKEN': 't' }) } as unknown as Response)
      : ({ status: 204 } as Response));
});

it('renders the password field for a non-MFA user', () => {
  (useAuth as unknown as vi.Mock).mockReturnValue({ user: { twoFactorEnabled: false } });
  render(<ReauthenticationDialog open onSuccess={() => {}} onCancel={() => {}} />);
  expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
  expect(screen.queryByLabelText(/code/i)).not.toBeInTheDocument();
});

it('renders the TOTP field for an MFA user', () => {
  (useAuth as unknown as vi.Mock).mockReturnValue({ user: { twoFactorEnabled: true } });
  render(<ReauthenticationDialog open onSuccess={() => {}} onCancel={() => {}} />);
  expect(screen.getByLabelText(/code/i)).toBeInTheDocument();
});

it('calls onSuccess after a 204 reauth', async () => {
  (useAuth as unknown as vi.Mock).mockReturnValue({ user: { twoFactorEnabled: false } });
  const onSuccess = vi.fn();
  render(<ReauthenticationDialog open onSuccess={onSuccess} onCancel={() => {}} />);
  await userEvent.type(screen.getByLabelText(/password/i), 'pw');
  await userEvent.click(screen.getByRole('button', { name: /confirm/i }));
  await waitFor(() => expect(onSuccess).toHaveBeenCalled());
});

it('shows the error envelope message on 401 and does not call onSuccess', async () => {
  (useAuth as unknown as vi.Mock).mockReturnValue({ user: { twoFactorEnabled: false } });
  (global.fetch as vi.Mock).mockImplementation(async (url: string) =>
    url.includes('/api/auth/csrf')
      ? ({ headers: new Headers({ 'X-XSRF-TOKEN': 't' }) } as unknown as Response)
      : ({ status: 401, json: async () => ({ error: { code: 'INVALID_REAUTH', message: 'Password is incorrect.' } }) } as Response));
  const onSuccess = vi.fn();
  render(<ReauthenticationDialog open onSuccess={onSuccess} onCancel={() => {}} />);
  await userEvent.type(screen.getByLabelText(/password/i), 'wrong');
  await userEvent.click(screen.getByRole('button', { name: /confirm/i }));
  expect(await screen.findByText(/password is incorrect/i)).toBeInTheDocument();
  expect(onSuccess).not.toHaveBeenCalled();
});
```

- [ ] **Step 2: Run, verify fail** — `pnpm --dir ProjectCeres.Client test --run src/app/auth/ReauthenticationDialog.test.tsx` → FAIL (module not found).

- [ ] **Step 3: Implement the dialog.** Use the shadcn `Dialog` primitive (`@/components/ui/dialog`). Read `useAuth().user.twoFactorEnabled`. Render a password `<Input type="password">` or a TOTP `<Input inputMode="numeric">` accordingly. On submit: `ensureCsrf()` (copy the helper from `PasswordReset.tsx` — `GET /api/auth/csrf`, read `X-XSRF-TOKEN`), then `POST /api/auth/reauth` with `{ password }` or `{ totpCode }` + the `X-XSRF-TOKEN` header + `credentials: 'include'`. On `204` → `onSuccess()`. On `401`/`422` → parse `error.message` (or the 422 `details`) and show it inline, leave the dialog open for retry. On `429` → show a rate-limit message. A Cancel button calls `onCancel()`. Use design-system tokens; `<Badge>`/`<Button>` from `@/components/ui`. No hard-coded colors.

- [ ] **Step 4: Run, verify pass** — same command → PASS (4 tests).

- [ ] **Step 5: Commit** (defer to Task 3 — Commit 1 lands together).

### Task 2: Wire `useStepUp` to drive the dialog (auto-replay)

**Files:**
- Modify: `ProjectCeres.Client/src/app/auth/use-step-up.ts`
- Create: `ProjectCeres.Client/src/app/auth/use-step-up.test.tsx`
- Create: `ProjectCeres.Client/src/app/auth/StepUpProvider.tsx` (holds dialog state so any caller can trigger it)
- Modify: `ProjectCeres.Client/src/app/layout/AppLayout.tsx` (mount `<StepUpProvider>` once, inside the auth context)

**Interfaces:**
- Consumes: `ReauthRequiredError` (api-client), `<ReauthenticationDialog>` (Task 1).
- Produces: `useStepUp().requireStepUp(action)` — unchanged signature `<T>(action: () => Promise<T>) => Promise<T>` — now opens the dialog on `ReauthRequiredError`, awaits success, re-runs `action`, returns its result. Cancel rejects with a `ReauthCancelledError`.

- [ ] **Step 1: Write failing tests**

```tsx
// use-step-up.test.tsx — render a component using requireStepUp inside StepUpProvider.
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';
import { StepUpProvider } from './StepUpProvider';
import { useStepUp } from './use-step-up';
import { ReauthRequiredError } from '../lib/api-client';

vi.mock('./auth-context', () => ({ useAuth: () => ({ user: { twoFactorEnabled: false } }) }));

function Probe({ action }: { action: () => Promise<string> }) {
  const { requireStepUp } = useStepUp();
  return <button onClick={async () => { (window as any).result = await requireStepUp(action); }}>go</button>;
}

it('replays the action after a successful reauth and returns its result', async () => {
  // First call throws REAUTH_REQUIRED; second call (the replay) succeeds.
  const action = vi.fn()
    .mockRejectedValueOnce(new ReauthRequiredError('reauth'))
    .mockResolvedValueOnce('done');
  // Mock the dialog's reauth POST to 204 so onSuccess fires.
  global.fetch = vi.fn(async (url: string) =>
    url.includes('/api/auth/csrf') ? ({ headers: new Headers({ 'X-XSRF-TOKEN': 't' }) } as unknown as Response)
                                   : ({ status: 204 } as Response));
  render(<StepUpProvider><Probe action={action} /></StepUpProvider>);
  await userEvent.click(screen.getByText('go'));
  // dialog opens; submit it
  await userEvent.type(await screen.findByLabelText(/password/i), 'pw');
  await userEvent.click(screen.getByRole('button', { name: /confirm/i }));
  await waitFor(() => expect((window as any).result).toBe('done'));
  expect(action).toHaveBeenCalledTimes(2);
});

it('does not replay if the action did not throw ReauthRequiredError', async () => {
  const action = vi.fn().mockResolvedValue('immediate');
  render(<StepUpProvider><Probe action={action} /></StepUpProvider>);
  await userEvent.click(screen.getByText('go'));
  await waitFor(() => expect((window as any).result).toBe('immediate'));
  expect(action).toHaveBeenCalledTimes(1);
});
```

- [ ] **Step 2: Run, verify fail** — `pnpm --dir ProjectCeres.Client test --run src/app/auth/use-step-up.test.tsx` → FAIL.

- [ ] **Step 3: Implement.** `StepUpProvider` holds `{ open, resolve, reject }` state + renders `<ReauthenticationDialog open={open} onSuccess={...} onCancel={...}>`. Expose a context method `openStepUp(): Promise<void>` that sets `open=true` and resolves/rejects when the dialog reports success/cancel. Rewrite `requireStepUp`:

```ts
const requireStepUp = useCallback(async <T,>(action: () => Promise<T>): Promise<T> => {
  try {
    return await action();
  } catch (err) {
    if (err instanceof ReauthRequiredError) {
      await openStepUp();          // opens dialog, resolves on 204, rejects (ReauthCancelledError) on cancel
      return await action();       // auto-replay; a second ReauthRequiredError propagates (no loop)
    }
    throw err;
  }
}, [openStepUp]);
```

Add `export class ReauthCancelledError extends Error` (in `use-step-up.ts` or api-client). The self-logout case: if the replayed `action()` itself triggers logout (its own 401 → `setOnUnauthenticated` redirect), no dialog re-opens because the replay's error is a fresh `ReauthRequiredError` only if still gated — a logout redirect surfaces as the app's unauthenticated handler, not a step-up loop. Mount `<StepUpProvider>` in `AppLayout` inside the auth provider.

- [ ] **Step 4: Run, verify pass** — same command → PASS.

- [ ] **Step 5: Commit** (defer to Task 3).

### Task 3: Verify dialog end-to-end against `/security` + commit Commit 1

**Files:** none new (verification + commit).

- [ ] **Step 1:** `pnpm --dir ProjectCeres.Client test --run src/app/auth/` → all green.
- [ ] **Step 2:** `pnpm --dir ProjectCeres.Client build` → within budgets (the dialog adds a small chunk; confirm no budget breach).
- [ ] **Step 3:** Manual/golden-path note for the close-out checklist: the re-enroll-TOTP action at `/security` (exists today) is the live trigger — record it for the stage browser walk.
- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/auth/ ProjectCeres.Client/src/app/layout/AppLayout.tsx
git commit -m "feat(12.9): reauthentication dialog + useStepUp auto-replay

<ReauthenticationDialog> (password or TOTP per twoFactorEnabled) + StepUpProvider;
useStepUp captures the gated action, opens the dialog on 401 REAUTH_REQUIRED,
and replays the action on success. Shared dependency for the sessions list (12.1)
and email-change request form (12.8)."
```

---

## COMMIT 2 — Sessions list + revoke + block IP (12.1–12.3)

### Task 4: Sessions API controller (list / revoke / block-IP)

**Files:**
- Create: `ProjectCeres/Controllers/Api/SessionsApiController.cs`
- Create: `ProjectCeres/ViewModels/Sessions/SessionDto.cs`, `BlockIpRequest.cs`
- Test: `ProjectCeres.Tests/Integration/Api/SessionsApiTests.cs`

**Interfaces:**
- Produces: `GET /api/sessions` → `SessionDto[]` `{ id: Guid, createdAt, lastUsedAt, ipCreatedAt: string, userAgent: string, isCurrent: bool }`; `DELETE /api/sessions/{id}` → 204; `POST /api/sessions/block-ip` `{ ipAddress: string }` → 204.

- [ ] **Step 1: Write failing integration tests** (use the existing auth WAF fixture pattern; model after `ProjectCeres.Tests/Integration/Authentication/SessionRevocationTests.cs`):

```csharp
// SessionsApiTests.cs — key cases:
// 1. GET /api/sessions lists the caller's active sessions, marks the current one isCurrent=true.
// 2. DELETE /api/sessions/{otherSessionId} revokes it; a request carrying that cookie no longer authenticates.
// 3. DELETE /api/sessions/{idOwnedByAnotherUser} returns 404 (IDOR — no cross-user revoke).
// 4. POST /api/sessions/block-ip { ipAddress } inserts a UserBlockedIp row AND revokes the caller's sessions with that IpCreatedAt.
// 5. GET /api/sessions without a recent-auth claim returns 401 REAUTH_REQUIRED (the [RequireRecentAuth] gate).
```

Write all five with explicit asserts (`resp.StatusCode.Should().Be(...)`, body shape via `ReadFromJsonAsync`). For #5, build a client whose cookie lacks a fresh `LastReauthAt` (mirror how `ReauthGateTests` sets up a stale-window client).

- [ ] **Step 2: Run, verify fail** — `dotnet test --filter "FullyQualifiedName~SessionsApiTests"` → FAIL (controller missing). Run ALONE.

- [ ] **Step 3: Implement the controller.** `[ApiController] [Route("api/sessions")] [Authorize] [RequireRecentAuth]`. Inject `AppDbContext` + `ICurrentUserAccessor`.
  - `GET` → `_db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null)` (explicit `.Where` per ADR-0065, even with the global filter), project to `SessionDto`; `isCurrent = s.Id == currentSid` (read `sid` from `User.FindFirstValue(SessionConstants.SessionIdClaim)`).
  - `DELETE {id}` → load the row scoped to the user; `null` → `NotFound()`; else set `RevokedAt = DateTimeOffset.UtcNow`, save, `NoContent()`.
  - `POST block-ip` → insert `UserBlockedIp { UserId, IpAddress, ... }`; bulk `ExecuteUpdateAsync` set `RevokedAt` on the caller's sessions where `IpCreatedAt == request.IpAddress`; `NoContent()`. Validate `ipAddress` non-empty → else `UnprocessableEntity` envelope.
  - Read `UserSession`/`UserBlockedIp` actual field names first; match them.

- [ ] **Step 4: Run, verify pass** — `dotnet test --filter "FullyQualifiedName~SessionsApiTests"` → PASS (5). ALONE.

- [ ] **Step 5: Commit** (defer to Task 6).

### Task 5: `/settings/sessions` SPA page

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/Sessions.tsx` (or `features/sessions/`)
- Create: `ProjectCeres.Client/src/app/features/sessions/sessions-api.ts`
- Create: `ProjectCeres.Client/src/app/pages/Sessions.test.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx` (add `/settings/sessions` route under the authed layout)
- Modify: settings nav (link to the new page)

**Interfaces:**
- Consumes: `useStepUp` (Task 2), the sessions API (Task 4).
- Produces: the page; `sessions-api.ts` exports `listSessions()`, `revokeSession(id)`, `blockIp(ip)` — each wrapped so a `401 REAUTH_REQUIRED` throws `ReauthRequiredError` (the api-client already does this) and is run through `requireStepUp`.

- [ ] **Step 1: Write failing tests** — render with mocked `sessions-api`: lists rows; clicking Revoke on a non-current row calls `revokeSession` + optimistically removes it; clicking Revoke on the current row (or Block-IP on current IP) opens an `<AlertDialog>` confirm first; empty state ("No other active sessions") when only the current row. (4 tests.)

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement** via `frontend-orchestrator` routing. Load via `requireStepUp(() => listSessions())` so a stale window opens the dialog. Render: responsive — `<Table>` desktop, cards mobile/tablet (per `planning-phase3-responsive.md`); each row shows created/last-used/IP/UA + "this session" marker; per-row Revoke + Block-IP (≥44×44px). Self-affecting actions (revoke current / block current IP) → `<AlertDialog>` confirm → on confirm, run the action, then handle the logout→`/login` redirect (the app's `setOnUnauthenticated` handles it). Optimistic refresh. Use `<Badge>`/design tokens.

- [ ] **Step 4: Run, verify pass.** Then `pnpm --dir ProjectCeres.Client test --run` + `pnpm --dir ProjectCeres.Client build`.

- [ ] **Step 5: Commit** (defer to Task 6).

### Task 6: Verify + commit Commit 2

- [ ] **Step 1:** `dotnet test --filter "FullyQualifiedName~SessionsApiTests"` ALONE → green; `pnpm --dir ProjectCeres.Client test --run` → green; `pnpm build` → budgets clean.
- [ ] **Step 2: Commit**

```bash
git add ProjectCeres/Controllers/Api/SessionsApiController.cs ProjectCeres/ViewModels/Sessions/ ProjectCeres.Client/src/app/pages/Sessions.tsx ProjectCeres.Client/src/app/features/sessions/ ProjectCeres.Client/src/app/App.tsx ProjectCeres.Tests/Integration/Api/SessionsApiTests.cs
git commit -m "feat(12.1-12.3): sessions list + revoke + block-IP (reauth-gated)"
```

---

## COMMIT 3 — Email-change SPA pages (12.8)

### Task 7: Confirm + revoke token pages (anonymous, AuthLayout)

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/auth/EmailChangeConfirm.tsx`, `EmailChangeRevoke.tsx`
- Create: their `.test.tsx`
- Modify: `App.tsx` (routes `/email-change/confirm`, `/email-change/revoke` under `AuthLayout`)

**Interfaces:** mirror `PasswordReset.tsx` — `readTokenFromHash(location.hash)`, `ensureCsrf()`, POST to the live endpoint.

- [ ] **Step 1: Write failing tests** — token-from-hash read; valid token → 204 → success state; invalid/expired → 401 → error state; no token → "invalid link" state. (Model after `PasswordReset.test.tsx`.)
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** by adapting `PasswordReset.tsx`: confirm page POSTs `/api/auth/email-change/confirm` `{ token }`; revoke page POSTs `/api/auth/email-change/revoke` `{ token }`. Reuse `readTokenFromHash`, `ensureCsrf`, status-code branching (204/401/422/429). Under `AuthLayout` (anonymous). **Verify the emitted URL base** in `EmailChangeService.cs` matches `/email-change/confirm` + `/email-change/revoke` exactly.
- [ ] **Step 4: Run, verify pass.**
- [ ] **Step 5: Commit** (defer to Task 9).

### Task 8: Email-change request form (in Settings, reauth-gated) + clear FIXME

**Files:**
- Create: `ProjectCeres.Client/src/app/features/settings/EmailChangeForm.tsx` + test
- Modify: the Settings page to mount it
- Modify: `ProjectCeres/Common/Authentication/EmailChangeService.cs` (remove the `// FIXME: re-surface in Stage 12` at line ~208)
- Create: `ProjectCeres.Client/e2e/auth/email-change.spec.ts` (link-click E2E)

**Interfaces:** consumes `useStepUp` (the request endpoint is `[RequireRecentAuth]`).

- [ ] **Step 1: Write failing test** — submitting a new email calls `POST /api/auth/email-change/request` wrapped in `requireStepUp`; a `401 REAUTH_REQUIRED` opens the dialog; success shows "check your email" confirmation.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** the form + wire through `requireStepUp(() => requestEmailChange(newEmail))`. Remove the FIXME comment in `EmailChangeService.cs` (the page now exists). Add the Playwright spec driving the request → (stub the email) → visiting the confirm URL → success.
- [ ] **Step 4: Run, verify pass** — Vitest green; `pnpm build`.
- [ ] **Step 5: Commit** (defer to Task 9).

### Task 9: Verify + commit Commit 3

- [ ] **Step 1:** `pnpm --dir ProjectCeres.Client test --run` green; `pnpm build` budgets clean; confirm `grep -n "FIXME.*Stage 12" ProjectCeres/Common/Authentication/EmailChangeService.cs` returns nothing.
- [ ] **Step 2: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/EmailChange*.tsx ProjectCeres.Client/src/app/features/settings/EmailChangeForm.tsx ProjectCeres.Client/src/app/App.tsx ProjectCeres.Client/e2e/auth/email-change.spec.ts ProjectCeres/Common/Authentication/EmailChangeService.cs ProjectCeres.Client/src/app/**/EmailChange*.test.tsx
git commit -m "feat(12.8): email-change SPA pages (request/confirm/revoke); clear FIXME"
```

---

## COMMIT 4 — Support page + SupportTicket + attachments + admin email (12.4–12.7)

### Task 10: `SupportTicket` + `SupportTicketAttachment` entities + DbSet + OnModelCreating + RLS migration

**Files:**
- Create: `ProjectCeres/Models/SupportTicket.cs`, `SupportTicketAttachment.cs`, `SupportTicketStatus.cs`, `SupportTicketPriority.cs`
- Modify: `ProjectCeres/Data/AppDbContext.cs` (DbSets + relationship config)
- Create: migration `AddSupportTickets` (schema) + the RLS migration (ENABLE/FORCE/policy) — may be one migration with both schema + `migrationBuilder.Sql(...)`
- Test: `ProjectCeres.Tests/Integration/Rls/` parity is auto-covered; add `ProjectCeres.Tests/Unit/UserOwnedModelTests` is auto; rely on `ParityTests` + `RlsParityStartupCheck`.

**Interfaces:**
- Produces: `SupportTicket : IUserOwned { Id, UserId, Subject, Message, Status (enum), Priority (enum), CreatedAt, UpdatedAt }`; `SupportTicketAttachment : IUserOwned { Id, SupportTicketId, UserId, FileName, StoredPath, ContentType, FileSizeBytes, UploadedAt }` + `SupportTicket.Attachments` nav.

- [ ] **Step 1: Write the entities + enums** (no test-first here — schema; the parity test IS the gate). `SupportTicketStatus { Open, InProgress, Resolved, Closed }`, `SupportTicketPriority { Low, Normal, High, Urgent }`.
- [ ] **Step 2: Add DbSets + OnModelCreating.** `DbSet<SupportTicket> SupportTickets`, `DbSet<SupportTicketAttachment> SupportTicketAttachments`. Configure the attachment FK → ticket (`HasOne(a => a.SupportTicket).WithMany(t => t.Attachments).HasForeignKey(a => new { a.SupportTicketId, a.UserId })
                .HasPrincipalKey(t => new { t.Id, t.UserId }).OnDelete(DeleteBehavior.Cascade)`) + index on `SupportTicketId`. Both are `IUserOwned` → the model-derived query filter auto-applies (no manual `HasQueryFilter` needed; `RegisterUserOwnedFilter` loop picks them up). **Update the now-stale comment at `AppDbContext.cs:309`** that says attachments have no UserId/filter (no longer true after the Stage-11 attachment-filter change + this entity).
- [ ] **Step 3: Generate migrations** — `dotnet ef migrations add AddSupportTickets`. Then add a second migration `EnableRlsOnSupportTickets` (or append `migrationBuilder.Sql` to the same) mirroring `20260526054514_EnableRlsOnEmailConfirmationTokens.cs` for BOTH tables:

```csharp
migrationBuilder.Sql(@"
  ALTER TABLE ""SupportTickets"" ENABLE ROW LEVEL SECURITY;
  ALTER TABLE ""SupportTickets"" FORCE ROW LEVEL SECURITY;
  DROP POLICY IF EXISTS user_isolation ON ""SupportTickets"";
  CREATE POLICY user_isolation ON ""SupportTickets""
    USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
    WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);

  ALTER TABLE ""SupportTicketAttachments"" ENABLE ROW LEVEL SECURITY;
  ALTER TABLE ""SupportTicketAttachments"" FORCE ROW LEVEL SECURITY;
  DROP POLICY IF EXISTS user_isolation ON ""SupportTicketAttachments"";
  CREATE POLICY user_isolation ON ""SupportTicketAttachments""
    USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
    WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
");
```

- [ ] **Step 4: Apply + verify parity** — `dotnet ef database update` (dev + test DB per the migrate script). Run `dotnet test --filter "FullyQualifiedName~ParityTests"` ALONE → PASS (proves both tables have forced RLS + policy). Boot once (`RlsParityStartupCheck` must not refuse). `dotnet build` → 0 errors.
- [ ] **Step 5: Commit** (defer to Task 13).

### Task 11: `SupportTicketService` + attachment storage (reuse FileAttachmentService)

**Files:**
- Create: `ProjectCeres/Services/ISupportTicketService.cs`, `SupportTicketService.cs`
- Modify: `ProjectCeres/Services/IFileAttachmentService.cs` + `FileAttachmentService.cs` (add `UploadForSupportTicketAsync` + `GetSupportTicketAttachmentAsync`, reusing the private `ValidateAsync` + store core)
- Modify: `Program.cs` (DI: `ISupportTicketService`)
- Test: `ProjectCeres.Tests/Integration/SupportTicketServiceTests.cs`

**Interfaces:**
- Produces: `ISupportTicketService { Task<SupportTicket> CreateAsync(string subject, string message, SupportTicketPriority priority); Task<IReadOnlyList<SupportTicket>> ListOwnAsync(); }`; `IFileAttachmentService.UploadForSupportTicketAsync(Guid ticketId, IFormFile) → SupportTicketAttachment` + `GetSupportTicketAttachmentAsync(Guid) → (byte[],string,string)`.

- [ ] **Step 1: Write failing tests** — create ticket (Status=Open, stamped UserId); list returns only own; upload an attachment stores it on disk + row created; oversized/wrong-type file throws (reuse the existing `ValidateAsync` rejections); attachment optional (create with none works).
- [ ] **Step 2: Run, verify fail** — `dotnet test --filter "FullyQualifiedName~SupportTicketServiceTests"` ALONE.
- [ ] **Step 3: Implement.** `SupportTicketService.CreateAsync` inserts with `Status=Open, CreatedAt=UpdatedAt=now`. Attachment methods mirror `UploadForTransferAsync`/`GetTransferAttachmentAsync` — same `ValidateAsync`, same magic-byte path, path `uploads/support/{ticketId}/{guid}{ext}`, `MaxFilesPerTransaction`-equivalent cap. Register DI in `Program.cs`.
- [ ] **Step 4: Run, verify pass** — ALONE.
- [ ] **Step 5: Commit** (defer to Task 13).

> **Corrected 2026-08-23.** The attachment FK below was originally specified as a
> single column. That shape allows a cross-tenant destructive write — Postgres runs FK
> checks and `ON DELETE CASCADE` through a referential-integrity trigger that RLS does
> not apply to, so user B can attach to user A's ticket and A deleting it destroys B's
> row. Reproduced against the real database. It is composite now; do not revert it.

### Task 12: Support API controller + admin-notify email + EmailTemplateKey/AuditLogAction/resx

**Files:**
- Create: `ProjectCeres/Controllers/Api/SupportApiController.cs` + `AttachmentsApiController` support method (or a new route under the existing controller)
- Modify: `ProjectCeres/Common/Email/EmailTemplateKey.cs` (+`SupportTicketReceived`), the EN/ES resx pair, `ProjectCeres/Models/AuditLog.cs` (+`SupportTicketCreated`) + the documented-set test
- Create: `ProjectCeres.Tests/Integration/Api/SupportApiTests.cs`

**Interfaces:** `POST /api/support/tickets` → 201 `{id}`; `POST /api/support/tickets/{id}/attachments` → `AttachmentDto`; `GET /api/support/tickets` → own list; `GET /api/attachments/support/{id}` → owner-scoped stream.

- [ ] **Step 1: Write failing integration tests** — create → 201 + Status=Open + admin email captured (use the `CapturingEmailService` fixture); GET lists own only; IDOR: user A `GET /api/attachments/support/{B's attachmentId}` → 404; upload too-large → 422 with the friendly message; create with no attachment → 201.
- [ ] **Step 2: Run, verify fail** — ALONE.
- [ ] **Step 3: Implement.** Controller `[Authorize]`. Create action → service + send admin email via `IEmailService` using `EmailTemplateKey.SupportTicketReceived` (add EN/ES resx keys `SupportTicketReceived.Subject/BodyText/BodyHtml`); write an `AuditLog` with `AuditLogAction.SupportTicketCreated` (add the enum value + the documented-set test entry — find the test that enumerates documented actions and add the row). Attachment upload mirrors `AttachmentsApiController`; serve via `GET /api/attachments/support/{id}` with `File(data, contentType, fileDownloadName)`.
- [ ] **Step 4: Run, verify pass** — `dotnet test --filter "FullyQualifiedName~SupportApiTests"` + the audit documented-set test, ALONE.
- [ ] **Step 5: Commit** (defer to Task 13).

### Task 13: `/support` SPA page + verify + commit Commit 4

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Support.tsx` (currently `PagePlaceholder`)
- Create: `ProjectCeres.Client/src/app/features/support/support-api.ts` + `Support.test.tsx`
- Possibly add `<Badge>` semantic variants for the status colors if missing (sky/amber/emerald/zinc) → `index.css` + `docs/design-system.md` first.

- [ ] **Step 1: Write failing tests** — form (subject/message/priority required) submits → ticket appears in list; optional file picker shows selected files + remove; status badges render correct colors; wrong-type file shows the rejection message; empty list state.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** via `frontend-orchestrator`. Form + optional multi-file picker ("Attach screenshots — optional"); on submit create ticket (+ upload attachments); own-tickets list with `<Badge variant>` status (Open=sky/InProgress=amber/Resolved=emerald/Closed=zinc — confirm/add variants); responsive cards/table; ≥44×44px targets; download links on the view.
- [ ] **Step 4: Run, verify pass** — `pnpm test` + `pnpm build`; `dotnet build` 0 errors.
- [ ] **Step 5: Commit Commit 4** (let the Stop hook run the full `dotnet test`):

```bash
git add ProjectCeres/Models/SupportTicket*.cs ProjectCeres/Data/AppDbContext.cs ProjectCeres/Migrations/ ProjectCeres/Services/*SupportTicket* ProjectCeres/Services/*FileAttachment* ProjectCeres/Controllers/Api/SupportApiController.cs ProjectCeres/Controllers/Api/AttachmentsApiController.cs ProjectCeres/Common/Email/EmailTemplateKey.cs ProjectCeres/Resources/ ProjectCeres/Models/AuditLog.cs ProjectCeres/Program.cs ProjectCeres.Client/src/app/pages/Support.tsx ProjectCeres.Client/src/app/features/support/ ProjectCeres.Tests/
git commit -m "feat(12.4-12.7): support tickets + optional attachments + admin-notify email

SupportTicket + SupportTicketAttachment (IUserOwned, forced RLS), reuse
FileAttachmentService for hardened uploads, admin notification email, /support
page. Admin ticket-LIST UI deferred to Stage 12.5."
```

---

## COMMIT 5 — Stage close-out

### Task 14: Docs sync + roadmap tick + close

- [ ] **Step 1:** `sync-docs` against the full Stage 12 diff — update `api-contract.md` (sessions + support endpoints), `models.md` (SupportTicket + SupportTicketAttachment), `security-model.md` if the reauth-UI surface note needs it, `planning-phase3.md` § Sessions/Support status.
- [ ] **Step 2:** Tick the Stage 12 verification checklist in `roadmap-phase-three.md`; confirm zero unchecked `[ ]` remain under the Stage 12 heading (the 3 deferred items already live in Stage 12.5). Flip Stage 12 → ✅ Done.
- [ ] **Step 3:** Full ship-gate: `dotnet build`, `dotnet test` (Stop hook, full), `pnpm build`, `pnpm test`; frontend checklist (golden path / empty / error / 375px / nav) across the 4 surfaces — agent-walk evidence bundle if the gate requires it (the diff touches user-facing Client + Controllers).
- [ ] **Step 4: Commit** the close-out doc changes.

---

## Self-Review

**Spec coverage:** 12.9 dialog → Tasks 1–3; 12.1–12.3 sessions → Tasks 4–6; 12.8 email-change → Tasks 7–9; 12.4–12.7 support + attachments + admin-email → Tasks 10–13; deferrals already locked (separate commit `60b1c9e`); close-out → Task 14. Attachments (user addition) → Tasks 10–13. Every spec section maps to a task.

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to". Two deliberately-flagged implementation choices (block-ip 204-vs-201; attachment-method extension vs shared-core) carry the decision criteria, not vague gaps. Test code is shown for the non-obvious tasks; schema Task 10 relies on the parity test as its gate (stated).

**Type consistency:** `requireStepUp<T>` signature unchanged across Tasks 2/5/8; `SupportTicket`/`SupportTicketAttachment` field lists identical in Tasks 10/11/12; `SessionDto` shape consistent Tasks 4/5; `EmailTemplateKey.SupportTicketReceived` + `AuditLogAction.SupportTicketCreated` named identically in Tasks 10/12.

**Open items resolved during planning:** reauth contract (204/422/401) read verbatim from `ReauthController`; `useStepUp` stub confirmed forward-compatible; `FileAttachmentService` per-entity-method pattern confirmed (add support methods, reuse `ValidateAsync`); `PasswordReset.tsx` confirmed as the token-page template; RLS migration SQL templated from `EnableRlsOnEmailConfirmationTokens`. One residual flagged for execution: the stale `AppDbContext.cs:309` attachment comment needs updating (Task 10 Step 2).
