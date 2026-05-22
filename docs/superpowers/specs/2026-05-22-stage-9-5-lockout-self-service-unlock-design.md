# Stage 9.5 — Lockout self-service unlock screen — design

**Status:** Active — 2026-05-22.
**Roadmap anchor:** `docs/roadmap-phase-three.md` → Stage 9 → sub-stage 9.5 (and the four "Lockout / unlock" checklist lines at roadmap §`Lockout / unlock`).
**Backend prerequisite:** Stage 6.10 shipped the entire server side. `POST /api/auth/lockout-unlock` accepts a raw token in the body, clears `AccessFailedCount` + `LockoutEnd` on the matched user, returns 204 on success / 401 `INVALID_LOCKOUT_UNLOCK_TOKEN` on miss. The email is sent at the moment of lockout from `AuthController.Login` (transition-only). All of this is verified by the 18 ship-gate tests in `LockoutUnlockIssuanceTests.cs` + `LockoutUnlockConfirmTests.cs`.

This spec is a small one: 9.5 is purely the SPA `/account/unlock` page. No new backend code, no new endpoints, no new entities, no new migrations.

---

## Why it's a small spec

When `AuthController.Login` returns `401 ACCOUNT_LOCKED_OUT`, the SPA currently navigates to `/account/unlock` (`Login.tsx:89`) — but that route does NOT exist in `App.tsx`. The redirect lands on the SPA's 404 page. 9.5 fixes this gap by:

1. Creating `pages/auth/AccountUnlock.tsx`.
2. Adding the route to `App.tsx`.
3. Adding i18n keys under `auth.accountUnlock.*` (EN + ES).
4. Wiring a sonner toast on `/login?unlocked=1` to acknowledge successful unlock on the post-redirect login surface.
5. Six SPA tests pinning the contract.

The unlock email's link points to `${unlockUrlBase}/app/lockout-unlock#token=${rawToken}` per `LockoutUnlockService.cs` — but the matching SPA route is `/account/unlock` per the roadmap and per `Login.tsx`'s redirect. **Decision D-route (below) reconciles this.**

---

## Decisions (locked during the 2026-05-22 brainstorm)

| # | Decision | Rationale |
|---|---|---|
| D-route | **Route is `/account/unlock` (NOT `/app/lockout-unlock`).** The `LockoutUnlockService.cs:127` URL builder must be updated in the same commit. | The `Login.tsx:89` redirect already points to `/account/unlock`. The roadmap row also names `/account/unlock`. The service-side URL was a Stage 6.10 placeholder that predates the roadmap pinning. Three call sites converge on `/account/unlock`; the one outlier loses. |
| D-token | **Token carried in URL fragment (`#token=...`), NOT query (`?token=...`).** | Matches `/password-reset#token=...` and (post-9.3) `/email-verify#token=...`. Fragments don't appear in server logs or `Referer` headers. `LockoutUnlockService.cs:127` already uses `#token=`. The roadmap line saying `?token=` is wrong and will be updated in the doc-sync pass. |
| D-mount | **Button-press confirmation, NOT auto-confirm on mount.** | Defends against email link-prefetchers (Microsoft Defender Safe Links, Gmail safe-link scanners) that follow the URL before the user clicks. Auto-confirm would let a scanner burn the single-use token. One extra click on a recovery flow is cheaper than a burned token + a user re-requesting an email. Matches the deliberate "click to verify" pattern of Stripe, GitHub, Atlassian. |
| D-success | **On 204: redirect to `/login?unlocked=1` + sonner toast.** | Matches `/password-reset` confirm → `/login?reset=1` pattern. The toast fires from `Login.tsx`'s mount-time `?unlocked=1` check. Consistency with the existing recovery-flow vocabulary. |
| D-invalid | **On 401: render an inline error block + "Request a new link" link to `/login`.** | The user can't unlock without a fresh email; sending them back to `/login` is the recovery path (next failed attempt re-locks the account → new email → new link). No new email-trigger endpoint needed; the issuance is involuntary. |
| D-i18n | **Full EN + ES localization.** Not "EN-only at ship" like the email template was (Stage 6.10 deferred ES to Stage 8). | The SPA already has full ES translations for every auth surface; not localizing `/account/unlock` would create a visible English-on-ES gap. |
| D-no-design-discovery | **Reuse the auth-card recipe verbatim from 9.3's "Design-system constraint" section.** | Same single-card centered layout on `AuthLayout` background, same `<Field>` recipe (when needed), same shadcn `<Button>` + `<Link>` primitives, same `role="alert" / aria-live` pattern. No new visual vocabulary. |

No ADR required — every decision is consistent with prior architecture.

---

## SPA design

### Component: `AccountUnlock.tsx`

`ProjectCeres.Client/src/app/pages/auth/AccountUnlock.tsx`. Structure mirrors `EmailVerify.tsx` (Stage 9.3) — token-in-fragment, three render states (token-present / success-after-submit / error-after-submit).

```tsx
export function AccountUnlock() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const token = useMemo(() => readTokenFromHash(location.hash), [location.hash]);
  const [state, setState] = useState<'idle' | 'submitting' | 'invalid' | 'network'>('idle');

  if (!token) {
    return <InvalidLinkBlock />;  // shared shape with the 401 branch
  }

  const onUnlock = async () => {
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

  if (state === 'invalid') return <InvalidLinkBlock />;
  if (state === 'network') return <NetworkErrorBlock onRetry={onUnlock} />;

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">
        {t('auth.accountUnlock.title')}
      </h1>
      <p className="text-sm text-muted-foreground">
        {t('auth.accountUnlock.description')}
      </p>
      <Button onClick={onUnlock} disabled={state === 'submitting'} className="w-full">
        {state === 'submitting' ? t('auth.accountUnlock.submitting') : t('auth.accountUnlock.submit')}
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

`readTokenFromHash` is the existing helper from `PasswordReset.tsx` — **extract it into `src/app/lib/url-hash-token.ts` in the same commit as 9.5** so 9.5 + 9.3's EmailVerify + the existing PasswordReset all consume one helper (cross-codebase consistency rule per `docs/design-system.md` § Working rules).

### Login.tsx `?unlocked=1` toast

In `Login.tsx`'s existing `useEffect` that reads `useSearchParams()` for `?reset=1`, add a parallel branch:

```ts
useEffect(() => {
  if (searchParams.get('reset') === '1') {
    toast.success(t('auth.passwordReset.toastUnlocked'));
    setSearchParams((p) => { p.delete('reset'); return p; }, { replace: true });
  }
  if (searchParams.get('unlocked') === '1') {
    toast.success(t('auth.accountUnlock.toastSucceeded'));
    setSearchParams((p) => { p.delete('unlocked'); return p; }, { replace: true });
  }
}, [searchParams, setSearchParams, t]);
```

(If the existing `useEffect` shape differs, adapt to match; the principle is one effect that reads both flags.)

### Route registration

`App.tsx` — add inside the `<Route element={<AuthLayout />}>` block:

```tsx
<Route path="account/unlock" element={<AccountUnlock />} />
```

### Server URL alignment

`ProjectCeres/Common/Authentication/LockoutUnlockService.cs:127`:

```csharp
var unlockUrl = $"{unlockUrlBase.TrimEnd('/')}/app/lockout-unlock#token={rawToken}";
```

→ change to:

```csharp
var unlockUrl = $"{unlockUrlBase.TrimEnd('/')}/account/unlock#token={rawToken}";
```

Update the corresponding `LockoutUnlockIssuanceTests.cs` assertion that pins the URL substring.

---

## Tests

`ProjectCeres.Client/src/app/pages/auth/AccountUnlock.test.tsx` — 6 tests:

| # | Test | Asserts |
|---|---|---|
| U1 | `renders unlock button when token in hash` | Component mounts with `#token=abc` → button visible, no auto-submit. |
| U2 | `no token in hash → renders invalid-link block` | Mount with empty hash → "This unlock link is invalid" block + back-to-sign-in link. |
| U3 | `click → POSTs /api/auth/lockout-unlock with token` | MSW handler captures the body; assert `{token: 'abc'}`. |
| U4 | `204 → navigates to /login?unlocked=1` | MSW returns 204; assert navigation. |
| U5 | `401 INVALID_LOCKOUT_UNLOCK_TOKEN → invalid-link block` | MSW returns 401; assert error block + back-to-sign-in link. |
| U6 | `network error → retry-able block + form preserved` | MSW returns 5xx; assert retry button + click re-submits. |

`Login.test.tsx` extension — 1 test:

| # | Test | Asserts |
|---|---|---|
| L-toast | `fires the account-unlocked toast when /login?unlocked=1` | Mount with location.search = `?unlocked=1`; assert `toast.success` called with the unlock i18n key. |

`LockoutUnlockIssuanceTests.cs` extension — adjust 1 existing test:

| # | Test | Asserts |
|---|---|---|
| L1 (updated) | `Login_transitioning_into_lockout_writes_token_row_AND_queues_email` | URL substring assertion changes from `/app/lockout-unlock` to `/account/unlock`. |

---

## i18n keys to add

Under `auth.accountUnlock.*`:

| Key | EN | ES |
|---|---|---|
| `title` | "Unlock your account" | "Desbloquea tu cuenta" |
| `description` | "Click below to unlock your account. This link can be used once and expires in 15 minutes." | "Pulsa el botón para desbloquear tu cuenta. Este enlace solo se puede usar una vez y caduca en 15 minutos." |
| `submit` | "Unlock account" | "Desbloquear cuenta" |
| `submitting` | "Unlocking…" | "Desbloqueando…" |
| `backToSignIn` | "Back to sign in" | "Volver a iniciar sesión" |
| `invalidTitle` | "This unlock link is invalid" | "Este enlace de desbloqueo no es válido" |
| `invalidBody` | "The link may have expired or already been used. Try signing in — if your account is still locked, you'll receive a new link." | "Es posible que el enlace haya caducado o que ya se haya usado. Intenta iniciar sesión — si tu cuenta sigue bloqueada, recibirás un nuevo enlace." |
| `toastSucceeded` | "Your account is unlocked. Sign in to continue." | "Tu cuenta está desbloqueada. Inicia sesión para continuar." |
| `errors.network` | "Couldn't reach the server. Try again." | "No se pudo conectar con el servidor. Vuelve a intentarlo." |
| `errors.retry` | "Try again" | "Volver a intentarlo" |

Note: no trailing ellipsis on `submit` (per `feedback_no_trailing_ellipsis_in_labels`). The `submitting` label uses `…` because it's a status string, not a button affordance.

---

## Verification checklist (post-implementation)

Aligns with `docs/design-system.md` § Working rules and the roadmap's Stage 9 §`Lockout / unlock` block:

- [ ] After 10 failed login attempts, account is locked + email sent with self-service unlock link — **already pinned by Stage 6.10 tests; 9.5 adds the URL assertion update**.
- [ ] `/account/unlock#token=...` accepts the signed token, unlocks the account, redirects to `/login` with success toast — **pinned by U1–U4 + L-toast**.
- [ ] Token expires after a reasonable window (15 min per Stage 6.10) — **already pinned by C4 (`Confirm_with_expired_token_returns_401`) in Stage 6.10**.
- [ ] Lockout email also tells the user "valid TOTP codes are still accepted during lockout" (per `security-model.md` § Login) — **this is a copy-only change to the existing email template; verify the EN template at `LockoutUnlockService.cs:158-180` includes this language OR add a separate roadmap line if the copy isn't yet there**.

Manual browser verification (after implementation):
1. Drive 10 wrong passwords in the dev DB to lock a known test user.
2. Inspect MailDrop (`./mail-out/`) for the lockout email.
3. Click the link → lands on `/account/unlock#token=...`.
4. Click "Unlock account" → redirects to `/login?unlocked=1` with toast.
5. Verify in dev DB: `AccessFailedCount = 0`, `LockoutEnd IS NULL`.
6. Repeat at `375px` mobile viewport.
7. Repeat with browser language set to `es-ES` → entire flow renders in Spanish.

---

## Items folded INTO 9.5 scope (no deferral)

- The `LockoutUnlockService.cs:127` URL substring change.
- The shared `readTokenFromHash` extraction into `src/app/lib/url-hash-token.ts` (consumed by AccountUnlock, EmailVerify, PasswordReset — three callers is the project's threshold for extraction per `docs/design-system.md` § Working rules).
- A copy-only verification (NOT a code change unless missing) that the lockout email mentions TOTP-codes-still-work during lockout.

## Scheduled in a downstream stage (with receiving `[ ]` line)

- ES variant of the lockout **email template** (the body of the email itself, not the unlock page) — already deferred to Stage 8 per `docs/roadmap-phase-three.md:1078–1086`. No new deferral.
- The lockout-token cleanup sweep (`DELETE FROM LockoutUnlockTokens WHERE ConsumedAt IS NOT NULL OR ExpiresAt < now() - interval '1 day'`) — already deferred to Stage 7+ per the Stage 6.10 spec § 8. No new deferral.

## Architecturally out of scope (not a deferral)

- Any change to the lockout policy itself (threshold, duration, AllowedForNewUsers). Stage 6 + 6b.2 settled these.
- Any change to the `LockoutUnlockService` confirm-side logic. Stage 6.10 settled this.
- A `/account/unlock` page for users who arrive without a token (e.g. typed the URL directly). The page renders the invalid-link block in that case (test U2). No separate "where do I get an unlock link?" affordance — the answer is "fail a login attempt again".

---

## Definition of Done

1. `pnpm --dir ProjectCeres.Client test` → all green (U1–U6 + L-toast pass).
2. `pnpm --dir ProjectCeres.Client build` → green (TypeScript + Vite bundle).
3. `dotnet test --filter "FullyQualifiedName~LockoutUnlock"` → green (the existing 18 Stage 6.10 tests + the 1 updated URL assertion).
4. Roadmap §`Lockout / unlock` four `[ ]` items ticked where automated tests cover them.
5. Manual browser verification: 7-step list above completed.

---

## Plan handoff

The implementation plan is the next document; it will break this spec into 3–4 sub-tasks following TDD:

1. Update `LockoutUnlockService.cs` URL + the matching integration test assertion (RED → GREEN).
2. Extract `readTokenFromHash` to `src/app/lib/url-hash-token.ts`; migrate `PasswordReset.tsx` to consume it (no behavior change).
3. Write `AccountUnlock.test.tsx` U1–U6 (RED).
4. Write `AccountUnlock.tsx` + route + i18n + `Login.tsx` toast (GREEN).

Estimated execution time: 60–90 focused minutes.
