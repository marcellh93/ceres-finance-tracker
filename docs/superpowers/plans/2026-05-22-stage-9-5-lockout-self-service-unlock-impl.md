# Stage 9.5 — Lockout self-service unlock screen — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the SPA `/account/unlock` page that consumes a lockout-unlock token (Stage 6.10 already shipped the server side), redirects to `/login?unlocked=1` on success with a sonner toast, and renders an invalid-link block on failure.

**Architecture:** Mirror `EmailVerify.tsx` (Stage 9.3) for the token-in-fragment pattern, but use **button-press confirmation** to defend against email link-prefetchers. Reuse the auth-card recipe; no new visual vocabulary. One server-side change: the unlock-URL substring in `LockoutUnlockService.cs` flips from `/app/lockout-unlock` to `/account/unlock`.

**Tech Stack:** React 19 + Vite + Vitest + react-i18next + shadcn (base-nova) + sonner.

**Spec:** `docs/superpowers/specs/2026-05-22-stage-9-5-lockout-self-service-unlock-design.md`.

**Sequencing note:** This plan should land **after** Task 10 of the 9.3 plan, because 9.5 consumes the `readTokenFromHash` helper extracted there. If 9.5 is being executed standalone (without 9.3), insert "Extract readTokenFromHash" as a prefix task.

---

## File structure

**New SPA files:**
- `ProjectCeres.Client/src/app/pages/auth/AccountUnlock.tsx`
- `ProjectCeres.Client/src/app/pages/auth/AccountUnlock.test.tsx`

**Modified SPA files:**
- `ProjectCeres.Client/src/app/App.tsx` — add `/account/unlock` route
- `ProjectCeres.Client/src/app/pages/auth/Login.tsx` — `?unlocked=1` toast effect
- `ProjectCeres.Client/src/i18n/locales/en.json` + `es.json` — add `auth.accountUnlock.*`

**Modified backend files:**
- `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` — URL substring flip
- `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs` — URL-assertion update

---

## Task 1: Server URL substring flip (RED → GREEN)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs`
- Modify: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs`

- [ ] **Step 1: Update the test assertion FIRST (RED)**

Find the existing test that asserts the captured-email URL contains `/app/lockout-unlock`. Change the substring:

```csharp
// Before:
capturedEmail.Body.Should().Contain("/app/lockout-unlock#token=");

// After:
capturedEmail.Body.Should().Contain("/account/unlock#token=");
```

- [ ] **Step 2: Run the test — expect RED**

```bash
dotnet test --filter "FullyQualifiedName~LockoutUnlockIssuanceTests" --no-restore
```

Expected: the URL-assertion test fails; the others still pass.

- [ ] **Step 3: Flip the URL in the service**

In `ProjectCeres/Common/Authentication/LockoutUnlockService.cs`, find:

```csharp
var unlockUrl = $"{unlockUrlBase.TrimEnd('/')}/app/lockout-unlock#token={rawToken}";
```

Change to:

```csharp
var unlockUrl = $"{unlockUrlBase.TrimEnd('/')}/account/unlock#token={rawToken}";
```

- [ ] **Step 4: Run the test — expect GREEN**

```bash
dotnet test --filter "FullyQualifiedName~LockoutUnlockIssuanceTests" --no-restore
```

Expected: all LockoutUnlockIssuanceTests pass.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Common/Authentication/LockoutUnlockService.cs \
        ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs
git commit -m "fix(9.5): lockout-unlock URL substring → /account/unlock (matches SPA route)"
```

---

## Task 2: i18n keys (EN + ES)

**Files:**
- Modify: `ProjectCeres.Client/src/i18n/locales/en.json`
- Modify: `ProjectCeres.Client/src/i18n/locales/es.json`

- [ ] **Step 1: EN keys**

Under the existing `auth` block in `en.json`, add:

```json
"accountUnlock": {
  "title": "Unlock your account",
  "description": "Click below to unlock your account. This link can be used once and expires in 15 minutes.",
  "submit": "Unlock account",
  "submitting": "Unlocking…",
  "backToSignIn": "Back to sign in",
  "invalidTitle": "This unlock link is invalid",
  "invalidBody": "The link may have expired or already been used. Try signing in — if your account is still locked, you'll receive a new link.",
  "toastSucceeded": "Your account is unlocked. Sign in to continue.",
  "errors": {
    "network": "Couldn't reach the server. Try again.",
    "retry": "Try again"
  }
}
```

- [ ] **Step 2: ES keys**

Under the existing `auth` block in `es.json`, add:

```json
"accountUnlock": {
  "title": "Desbloquea tu cuenta",
  "description": "Pulsa el botón para desbloquear tu cuenta. Este enlace solo se puede usar una vez y caduca en 15 minutos.",
  "submit": "Desbloquear cuenta",
  "submitting": "Desbloqueando…",
  "backToSignIn": "Volver a iniciar sesión",
  "invalidTitle": "Este enlace de desbloqueo no es válido",
  "invalidBody": "Es posible que el enlace haya caducado o que ya se haya usado. Intenta iniciar sesión — si tu cuenta sigue bloqueada, recibirás un nuevo enlace.",
  "toastSucceeded": "Tu cuenta está desbloqueada. Inicia sesión para continuar.",
  "errors": {
    "network": "No se pudo conectar con el servidor. Vuelve a intentarlo.",
    "retry": "Volver a intentarlo"
  }
}
```

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/i18n/locales/en.json \
        ProjectCeres.Client/src/i18n/locales/es.json
git commit -m "feat(9.5): i18n keys for /account/unlock (EN+ES)"
```

---

## Task 3: `AccountUnlock.tsx` (TDD)

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/auth/AccountUnlock.test.tsx`
- Create: `ProjectCeres.Client/src/app/pages/auth/AccountUnlock.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx`

- [ ] **Step 1: Write failing tests (RED)**

```tsx
// AccountUnlock.test.tsx
import { describe, test, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { AccountUnlock } from './AccountUnlock';
import { renderWithProviders } from '../../testing/providers';
import { server } from '../../testing/msw-server';

describe('AccountUnlock', () => {
  test('U1: renders unlock button when token in hash; does NOT auto-submit', async () => {
    let called = false;
    server.use(http.post('/api/auth/lockout-unlock', () => {
      called = true;
      return new HttpResponse(null, { status: 204 });
    }));
    renderWithProviders(<AccountUnlock />, { initialEntries: ['/account/unlock#token=abc'] });
    expect(screen.getByRole('button', { name: /unlock account/i })).toBeInTheDocument();
    // Give the component a tick to (incorrectly) auto-submit; assert it didn't.
    await new Promise((r) => setTimeout(r, 50));
    expect(called).toBe(false);
  });

  test('U2: no token in hash → renders invalid-link block', () => {
    renderWithProviders(<AccountUnlock />, { initialEntries: ['/account/unlock'] });
    expect(screen.getByText(/this unlock link is invalid/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to sign in/i })).toBeInTheDocument();
  });

  test('U3: click → POSTs /api/auth/lockout-unlock with token', async () => {
    let captured: string | undefined;
    server.use(http.post('/api/auth/lockout-unlock', async ({ request }) => {
      const body = (await request.json()) as { token: string };
      captured = body.token;
      return new HttpResponse(null, { status: 204 });
    }));
    renderWithProviders(<AccountUnlock />, { initialEntries: ['/account/unlock#token=abc'] });
    await userEvent.click(screen.getByRole('button', { name: /unlock account/i }));
    expect(captured).toBe('abc');
  });

  test('U4: 204 → navigates to /login?unlocked=1', async () => {
    server.use(http.post('/api/auth/lockout-unlock', () => new HttpResponse(null, { status: 204 })));
    const { router } = renderWithProviders(<AccountUnlock />, { initialEntries: ['/account/unlock#token=abc'] });
    await userEvent.click(screen.getByRole('button', { name: /unlock account/i }));
    await new Promise((r) => setTimeout(r, 10));
    expect(router.state.location.pathname + router.state.location.search).toBe('/login?unlocked=1');
  });

  test('U5: 401 → invalid-link block', async () => {
    server.use(http.post('/api/auth/lockout-unlock', () => HttpResponse.json({
      error: { code: 'INVALID_LOCKOUT_UNLOCK_TOKEN' }
    }, { status: 401 })));
    renderWithProviders(<AccountUnlock />, { initialEntries: ['/account/unlock#token=bad'] });
    await userEvent.click(screen.getByRole('button', { name: /unlock account/i }));
    expect(await screen.findByText(/this unlock link is invalid/i)).toBeInTheDocument();
  });

  test('U6: network error → retry block; retry re-submits', async () => {
    let firstAttempt = true;
    server.use(http.post('/api/auth/lockout-unlock', () => {
      if (firstAttempt) { firstAttempt = false; return HttpResponse.error(); }
      return new HttpResponse(null, { status: 204 });
    }));
    const { router } = renderWithProviders(<AccountUnlock />, { initialEntries: ['/account/unlock#token=abc'] });
    await userEvent.click(screen.getByRole('button', { name: /unlock account/i }));
    expect(await screen.findByText(/couldn't reach/i)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /try again/i }));
    await new Promise((r) => setTimeout(r, 10));
    expect(router.state.location.pathname + router.state.location.search).toBe('/login?unlocked=1');
  });
});
```

Note: `renderWithProviders` returns an object including the memory router instance (`router`) so we can read `location` in tests. If the project's existing helper doesn't expose this, extend it — `PasswordReset.test.tsx` already needs the same affordance for the `?reset=1` redirect assertion.

- [ ] **Step 2: Run — expect RED**

```bash
pnpm --dir ProjectCeres.Client test AccountUnlock.test
```

Expected: 6 failures (`AccountUnlock` not exported).

- [ ] **Step 3: Implement `AccountUnlock.tsx`**

```tsx
// ProjectCeres.Client/src/app/pages/auth/AccountUnlock.tsx
import { useMemo, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { readTokenFromHash } from '../../lib/url-hash-token';
import { apiFetch } from '../../lib/api-client';

type State = 'idle' | 'submitting' | 'invalid' | 'network';

export function AccountUnlock() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const token = useMemo(() => readTokenFromHash(location.hash), [location.hash]);
  const [state, setState] = useState<State>('idle');

  const onUnlock = async () => {
    if (!token) return;
    setState('submitting');
    const result = await apiFetch('/api/auth/lockout-unlock', {
      method: 'POST',
      body: { token },
    });
    if (result.ok) {
      navigate('/login?unlocked=1');
      return;
    }
    if (result.status === 401) {
      setState('invalid');
      return;
    }
    setState('network');
  };

  if (!token || state === 'invalid') return <InvalidBlock />;
  if (state === 'network') return <NetworkBlock onRetry={onUnlock} />;

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.accountUnlock.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('auth.accountUnlock.description')}</p>
      <Button onClick={onUnlock} disabled={state === 'submitting'} className="w-full">
        {state === 'submitting'
          ? t('auth.accountUnlock.submitting')
          : t('auth.accountUnlock.submit')}
      </Button>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.accountUnlock.backToSignIn')}
        </Link>
      </div>
    </div>
  );
}

function InvalidBlock() {
  const { t } = useTranslation();
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.accountUnlock.invalidTitle')}</h1>
      <p className="text-sm text-muted-foreground" role="alert">{t('auth.accountUnlock.invalidBody')}</p>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.accountUnlock.backToSignIn')}
        </Link>
      </div>
    </div>
  );
}

function NetworkBlock({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation();
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.accountUnlock.title')}</h1>
      <p className="text-sm text-destructive" role="alert" aria-live="assertive">
        {t('auth.accountUnlock.errors.network')}
      </p>
      <Button onClick={onRetry} className="w-full">
        {t('auth.accountUnlock.errors.retry')}
      </Button>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.accountUnlock.backToSignIn')}
        </Link>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Route in App.tsx**

Inside the existing `<Route element={<AuthLayout />}>` block, add:

```tsx
<Route path="account/unlock" element={<AccountUnlock />} />
```

Import `AccountUnlock` at the top.

- [ ] **Step 5: Run tests — expect GREEN**

```bash
pnpm --dir ProjectCeres.Client test AccountUnlock.test
```

Expected: 6/6 passing.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/AccountUnlock.tsx \
        ProjectCeres.Client/src/app/pages/auth/AccountUnlock.test.tsx \
        ProjectCeres.Client/src/app/App.tsx
git commit -m "feat(9.5): AccountUnlock page (TDD, 6 tests) + /account/unlock route"
```

---

## Task 4: `Login.tsx` `?unlocked=1` toast

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/auth/Login.tsx`
- Modify: `ProjectCeres.Client/src/app/pages/auth/Login.test.tsx`

- [ ] **Step 1: Write the failing test (RED)**

Add to `Login.test.tsx`:

```tsx
test('L-toast: fires the account-unlocked toast when /login?unlocked=1', async () => {
  const toastSpy = vi.spyOn(toast, 'success');
  renderWithProviders(<Login />, { initialEntries: ['/login?unlocked=1'] });
  await new Promise((r) => setTimeout(r, 10));
  expect(toastSpy).toHaveBeenCalledWith(expect.stringMatching(/unlocked/i));
  toastSpy.mockRestore();
});
```

(Adapt to whatever import shape the project uses for sonner's `toast` and whatever `toast.success` mock pattern the existing `Login.test.tsx > fires the password-reset toast` test uses.)

- [ ] **Step 2: Run — expect RED**

```bash
pnpm --dir ProjectCeres.Client test Login.test -t "unlocked"
```

Expected: the new test fails.

- [ ] **Step 3: Extend the existing `?reset=1` useEffect**

In `Login.tsx`, find the `useEffect` that handles `?reset=1`. Add a sibling branch for `?unlocked=1`:

```tsx
useEffect(() => {
  if (searchParams.get('reset') === '1') {
    toast.success(t('auth.passwordReset.toastReset'));  // adapt key to project
    setSearchParams((p) => { p.delete('reset'); return p; }, { replace: true });
  }
  if (searchParams.get('unlocked') === '1') {
    toast.success(t('auth.accountUnlock.toastSucceeded'));
    setSearchParams((p) => { p.delete('unlocked'); return p; }, { replace: true });
  }
}, [searchParams, setSearchParams, t]);
```

- [ ] **Step 4: Run tests — expect GREEN**

```bash
pnpm --dir ProjectCeres.Client test Login.test
```

Expected: all Login tests pass, including the new L-toast.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/Login.tsx \
        ProjectCeres.Client/src/app/pages/auth/Login.test.tsx
git commit -m "feat(9.5): Login fires account-unlocked toast on /login?unlocked=1"
```

---

## Task 5: Full-suite regression + manual verification handoff

- [ ] **Step 1: Run full backend suite (URL change ripples through LockoutUnlock tests)**

```bash
dotnet test --filter "FullyQualifiedName~LockoutUnlock"
dotnet test
```

Expected: all passing.

- [ ] **Step 2: Run full SPA suite**

```bash
pnpm --dir ProjectCeres.Client test
pnpm --dir ProjectCeres.Client build
```

Expected: green.

- [ ] **Step 3: Verify the lockout email mentions TOTP-during-lockout**

Open `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` § `BuildLockoutEmail`. The 6.10 spec § D1 captured the email body. Per the roadmap line "Lockout email also tells the user 'valid TOTP codes are still accepted during lockout'": confirm the email body includes language to that effect. If it doesn't, **open a new `[ ]` line in the roadmap's Stage 9.5 checklist** with the deferral fields (per `feedback_deferral_requires_receiving_stage_checkbox`) — don't silently treat this as "out of scope" (per `feedback_scoping_dodge_is_deferral`). If it does, tick the roadmap line in the close-out pass.

- [ ] **Step 4: Manual browser handoff**

Hand the user this checklist (Phase H prerequisite-audit first):

**Prerequisites audit:**
- `/account/unlock` route mounted ✓ (Task 3 step 4)
- `LockoutUnlockService` URL is `/account/unlock#token=...` ✓ (Task 1 step 3)
- `Login.tsx` redirects to `/account/unlock` on `ACCOUNT_LOCKED_OUT` ✓ (already present pre-9.5 at Login.tsx:89)
- MailDrop receives emails ✓

**Steps:**
1. Drive 10 wrong-password attempts on a known dev user (e.g. `seed@example.com`) → 11th attempt returns 401 `ACCOUNT_LOCKED_OUT`, SPA redirects to `/account/unlock`.
2. On `/account/unlock` without a token in the URL → invalid-link block.
3. Open MailDrop (`./mail-out/`) → email with subject "Your Project Ceres account was locked", body contains `/account/unlock#token=...`.
4. Click the link → page renders "Unlock your account" + Unlock button (NOT auto-confirm).
5. Click "Unlock account" → redirects to `/login?unlocked=1` with sonner toast "Your account is unlocked".
6. Verify in dev DB: `UPDATE "AspNetUsers" WHERE Id=...` shows `AccessFailedCount=0`, `LockoutEnd IS NULL`.
7. Repeat with the same token → 401 → invalid-link block (single-use proven).
8. Repeat at 375px mobile viewport.
9. Toggle ES → entire flow renders in Spanish, no English leaks.

---

## Self-review

**Spec coverage:**
- ✅ URL substring flip → Task 1
- ✅ i18n EN + ES → Task 2
- ✅ AccountUnlock SPA + 6 tests → Task 3
- ✅ Login toast on `?unlocked=1` → Task 4
- ✅ Manual checklist with prerequisites audit → Task 5

**Placeholder scan:** none.

**Type consistency:** `apiFetch` body shape `{ token: string }` matches Task 3 SPA + the existing `LockoutUnlockRequest.cs` DTO (Stage 6.10).

**Dependency note:** Task 3 imports `readTokenFromHash` from `src/app/lib/url-hash-token.ts`. If 9.5 runs **before** 9.3 Task 10, add a prefix task to extract that helper.

Plan saved to `docs/superpowers/plans/2026-05-22-stage-9-5-lockout-self-service-unlock-impl.md`.
