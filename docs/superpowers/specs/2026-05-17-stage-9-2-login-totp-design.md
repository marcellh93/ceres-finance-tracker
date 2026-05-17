# Stage 9.2 — `/login/totp` page design

**Status:** Draft — 2026-05-17
**Roadmap anchor:** `docs/roadmap-phase-three.md` → Stage 9 → sub-stage 9.2 ("`/login/totp` page (6-digit TOTP step)")
**Originating context:** `docs/planning-phase3.md` § 10 Auth screen design

---

## Goal

Ship the SPA page mounted at `/login/totp` that completes the two-step login flow for users with TOTP enabled (or, via the same form, those falling back to a backup code). The server side is fully wired today (`POST /api/auth/login/totp` in `AuthController.cs:248`); the gap is the SPA page itself.

Today, `Login.tsx:46` already navigates the browser to `/login/totp` when the login endpoint returns `{ requiresTotp: true }`, but that route is not registered in `App.tsx` and resolves to `NotFound` inside the public auth branch. This stage closes that gap.

---

## Scope

In scope:

1. New page component `LoginTotp.tsx` mounted at `/login/totp` inside the existing `AuthLayout` (centered card, no app shell).
2. 6-digit TOTP input as the default state, using the existing shadcn `InputOTP` primitive.
3. Backup-code fallback as a second state of the same page, toggled via "Lost your device? Use a backup code".
4. Submit, error, and success paths wired to `POST /api/auth/login/totp` per the server contract in `AuthController.cs:248-356`.
5. Localisation (EN + ES) under a new `auth.totp.*` namespace.
6. Accessibility: focus management on the OTP cells, `role="alert"` on the error region, `aria-live="polite"` for the rate-limit countdown.
7. Vitest unit tests + a vitest-axe a11y test, matching the pattern in `Login.test.tsx` and `Login.a11y.test.tsx`.
8. Route registration in `App.tsx`.

Out of scope (these belong to later sub-stages or already exist):

- TOTP enrolment flow (Stage 9.6 / `/login/totp/setup`).
- Backup-codes regeneration after consumption (Stage 9.7).
- "You have N backup codes remaining" warning banner on the dashboard (Stage 9.7).
- Server-side TOTP verification logic (already complete in Stage 6b.2).

---

## User-visible behaviour

### Default state — TOTP code

- Header: "Verify your identity" (`auth.totp.title`).
- Body: "Enter the 6-digit code from your authenticator app" (`auth.totp.description`).
- Six-cell `InputOTP` group, single visible field. Auto-focus on cell 1. Auto-advance between cells.
- When the user types the 6th digit, the form auto-submits (no button press needed).
- A disabled "Verify" submit button is also visible below the cells, becoming enabled once 6 digits are present. Both paths (Enter key + button click) trigger the same submit handler.
- Below the submit: a `<Button variant="link">` with copy "Lost your device? Use a backup code" — clicking swaps the page into backup-code state.
- Below that: a plain `<Link to="/login">` "Back to sign in".

### Backup-code state

- Header copy unchanged.
- Body: "Enter one of your backup codes" (`auth.totp.backupCodeDescription`).
- Single plain `<Input>` of type text, `autoComplete="one-time-code"`, no auto-advance, no auto-submit. The user must click "Verify" to send.
- Below the submit: "Use verification code instead" link, swaps back to the TOTP state. The previously-typed TOTP digits are NOT preserved across the swap (this is a user-initiated reset, simplifies state, no real loss).
- "Back to sign in" link unchanged.

### Error UX — all six failure paths

| Server status / code | UI behaviour |
|---|---|
| `401 INVALID_MFA_CODE` | Cells visually flash invalid (`aria-invalid="true"` propagates to `InputOTPGroup`); inline error in `role="alert"` reads "Invalid code"; cells cleared; focus returns to cell 1. |
| `401 ACCOUNT_LOCKED_OUT` | `navigate('/account/unlock')` — matches the Login page's behaviour at `Login.tsx:62`. The unlock page (Stage 9.5) does not exist yet; for now the route resolves to `NotFound`. That is acceptable: the lockout path is a server-side state that the user has to recover from via email, and the Login page already has this same dead-end behaviour. Stage 9.5 closes it. |
| `401 UNAUTHENTICATED` | Half-auth cookie expired or missing. `navigate('/login?expired=1')`. The Login page does not currently read the `expired` query param; that's a one-line addition (`searchParams.get('expired')` → show a toast). I will add it as part of this stage so the UX loop is closed. |
| `429 Too Many Requests` | Read `response.headers.get('Retry-After')`; if present and parseable, display "Too many attempts. Try again in {{seconds}} seconds." with an `aria-live="polite"` countdown that ticks down. If header is missing or unparseable, fall back to "Too many attempts. Try again in a moment." Submit button disabled until the countdown ends. |
| Network / 5xx | "Could not verify the code. Check your connection and try again." Inline error in `role="alert"`. Cells preserved (user shouldn't have to re-type). |
| 200 `{ requiresTotp: true }` (defensive) | Cannot happen on this endpoint per server contract, but treat as `INVALID_MFA_CODE` if it ever does. |

### Success path

- Server returns `204`.
- Page calls `auth.refresh()` to reload `useAuth()` state.
- `navigate('/')` to the dashboard.
- No success toast — the navigation itself is the feedback.

---

## File layout

```
ProjectCeres.Client/src/app/
  pages/auth/
    LoginTotp.tsx                          [new]
    LoginTotp.test.tsx                     [new]
    LoginTotp.a11y.test.tsx                [new]
  auth/schemas/
    login-totp.schema.ts                   [new]   zod for totp + backup modes
  App.tsx                                  [edit]  add <Route path="login/totp" element={<LoginTotp />} />
  pages/auth/Login.tsx                     [edit]  on mount, if searchParams has expired=1, surface a toast (sonner)
src/app/i18n/locales/en.json               [edit]  new auth.totp.* keys
src/app/i18n/locales/es.json               [edit]  new auth.totp.* keys
```

No new dependencies. `react-hook-form`, `zod`, `@hookform/resolvers/zod`, `input-otp`, and the shadcn `InputOTP` primitive are all already installed and wired (see `docs/planning-phase3.md:295` and `src/components/ui/input-otp.tsx`).

---

## i18n keys (added to `auth.totp.*`)

```jsonc
{
  "auth": {
    "totp": {
      "title": "Verify your identity",
      "description": "Enter the 6-digit code from your authenticator app",
      "codeLabel": "Verification code",
      "submit": "Verify",
      "submitting": "Verifying",
      "backupCodePrompt": "Lost your device? Use a backup code",
      "backupCodeDescription": "Enter one of your backup codes",
      "backupCodeLabel": "Backup code",
      "useTotpInstead": "Use verification code instead",
      "backToSignIn": "Back to sign in",
      "errors": {
        "invalid": "Invalid code",
        "tooManyAttempts": "Too many attempts. Try again in {{seconds}} seconds.",
        "tooManyAttemptsNoCountdown": "Too many attempts. Try again in a moment.",
        "expired": "Your sign-in expired. Please sign in again.",
        "network": "Could not verify the code. Check your connection and try again."
      }
    }
  }
}
```

Spanish translations follow the same shape:

```jsonc
"totp": {
  "title": "Verifica tu identidad",
  "description": "Introduce el código de 6 dígitos de tu aplicación de autenticación",
  "codeLabel": "Código de verificación",
  "submit": "Verificar",
  "submitting": "Verificando",
  "backupCodePrompt": "¿Perdiste el dispositivo? Usa un código de respaldo",
  "backupCodeDescription": "Introduce uno de tus códigos de respaldo",
  "backupCodeLabel": "Código de respaldo",
  "useTotpInstead": "Usar código de verificación",
  "backToSignIn": "Volver a iniciar sesión",
  "errors": {
    "invalid": "Código no válido",
    "tooManyAttempts": "Demasiados intentos. Inténtalo en {{seconds}} segundos.",
    "tooManyAttemptsNoCountdown": "Demasiados intentos. Espera un momento.",
    "expired": "Tu sesión caducó. Inicia sesión de nuevo.",
    "network": "No se pudo verificar el código. Comprueba tu conexión e inténtalo de nuevo."
  }
}
```

Also add to Login.tsx's existing namespace (one new key for the expired toast):

```jsonc
"auth.login.toasts.totpExpired"  // "Your sign-in expired. Please sign in again." / "Tu sesión caducó. Inicia sesión de nuevo."
```

No trailing ellipsis on new keys per memory `feedback_no_trailing_ellipsis_in_labels`. (Existing `auth.login.submitting` "Signing in…" has one — pre-existing, not in scope here.)

---

## Server contract recap (no changes)

`POST /api/auth/login/totp` — body `{ "code": "123456" }` (string, 6 digits OR backup-code shape per `MfaConstants.BackupCodeShape`).

| Outcome | Status | Body |
|---|---|---|
| Code accepted, session issued | 204 | (empty) |
| Bad / expired code | 401 | `{ error: { code: "INVALID_MFA_CODE", message: "..." } }` |
| Account is locked | 401 | `{ error: { code: "ACCOUNT_LOCKED_OUT", message: "..." } }` |
| No half-auth cookie | 401 | `{ error: { code: "UNAUTHENTICATED", message: "..." } }` |
| Per-user TOTP rate limiter (10/min) | 429 | (rate-limit default body); `Retry-After` header set |

The server reads the `Mfa.RememberMe` cookie set by `/api/auth/login` to honour the user's earlier "Remember me" choice on this device. The SPA does not need to do anything special for that — the cookie travels automatically since `apiFetch` uses `credentials: 'include'`.

---

## Rate-limit (429) handling — implementation detail

`apiFetch` does not currently expose response headers. For 429 we need `Retry-After`. Two options:

- **A (chosen) — bypass `apiFetch` only on this path.** Use raw `fetch('/api/auth/login/totp', ...)` for this one endpoint so the page can read the response headers itself. The CSRF handshake helper `ensureCsrfToken()` is not exported, but we can call `apiFetch('/api/auth/csrf', { method: 'GET' })` first to prime the cache and then a raw `fetch` for the submission. Trade-off: a small amount of duplication of `apiFetch`'s header logic in the page.
- B — extend `apiFetch` to return headers in the failure shape. Larger change, ripples across every page that consumes `ApiFailure`. Out of proportion for one 429 case.

Going with A. The duplication is ~10 lines; the extension would touch every consumer's type signature.

To keep that duplication contained, we add a tiny page-local helper `submitTotp({ code })` that handles the CSRF prime + raw POST + status/header parsing. It returns a discriminated union mirroring `ApiResult` but with `retryAfterSeconds?: number` on the 429 branch.

---

## Tests

`LoginTotp.test.tsx` — pattern matches `Login.test.tsx` exactly: `fetchSpy = vi.spyOn(global, 'fetch')` + `clearXsrfTokenCacheForTests()` in `beforeEach` + `MemoryRouter` with sibling routes for `/`, `/login`, `/account/unlock`.

Tests (11 total):

1. Renders 6 OTP cells, the submit button (disabled at zero input), the "Lost your device?" link, and the "Back to sign in" link.
2. Auto-submits when 6 digits typed (asserts a `POST /api/auth/login/totp` with the 6-digit body).
3. On 204: navigates to `/`.
4. On 401 INVALID_MFA_CODE: shows the inline "Invalid code" error in `role="alert"`; cells cleared; focus returns to cell 1.
5. On 401 ACCOUNT_LOCKED_OUT: navigates to `/account/unlock`.
6. On 401 UNAUTHENTICATED: navigates to `/login?expired=1`.
7. On 429 with `Retry-After: 30`: surfaces "Too many attempts. Try again in 30 seconds." in `aria-live="polite"`. (We don't test the countdown ticking — implementation can use `setTimeout`; we test the initial render only.)
8. On 429 with no `Retry-After`: surfaces the no-countdown variant.
9. Backup-code link toggles the form: cells disappear, plain text input appears with the backup-code label. Auto-submit does NOT fire (user must press button). Submit posts the typed string.
10. "Use verification code instead" link toggles back. State is cleared (no leftover backup-code value in the new TOTP cells).
11. Submit button shows the "Verifying" label while the request is in flight.

`LoginTotp.a11y.test.tsx`:

12. vitest-axe `await expect(await axe(container)).toHaveNoViolations()` against the default and backup-code states.

`Login.test.tsx` (existing) — one new test:

13. Renders a sonner toast with the i18n `auth.login.toasts.totpExpired` copy when the URL is `/login?expired=1`. Uses `sonner`'s `<Toaster>` placed in the MemoryRouter alongside `<Login>`.

---

## Accessibility

- The 6-cell `InputOTPGroup` has `aria-label` "Verification code" so a single screen-reader read covers all six cells (per `input-otp` library convention).
- The inline error region is rendered as `<div role="alert" aria-live="assertive">` and is the *target* of `aria-describedby` from the group.
- The 429 countdown is `aria-live="polite"` (not assertive — it changes every second; assertive would spam the SR).
- Tab order: OTP cells → Verify button → "Lost your device?" → "Back to sign in".
- The "Lost your device?" link uses `role="button"` because clicking it changes the page's mode rather than navigating.
- Focus moves to the first cell when the page mounts.

---

## Edge cases pinned by tests

- Paste-of-six-digits works (the `input-otp` library handles this internally; we test that auto-submit fires once after a paste).
- Typing fewer than 6 digits keeps the Verify button disabled.
- Pressing Enter from inside a cell submits (default `<form>` behaviour, no special handling needed).
- After an INVALID_MFA_CODE error, typing a new code clears the error region.
- Mode swap (TOTP ↔ backup-code) resets the form state and any error.

---

## What this stage does NOT change

- No new server endpoints.
- No new database columns, migrations, or services.
- No changes to `AuthLayout`, `LanguageToggle`, `ThemeToggle`.
- No changes to `auth-context` (refresh() already exists).

---

## Definition of Done

1. `pnpm test` green in `ProjectCeres.Client/` (all 12 new tests + 1 modified Login test + existing suite).
2. `pnpm build` green (TypeScript + Vite production build).
3. `dotnet test --filter "FullyQualifiedName~ProjectCeres.Tests.Unit"` green (defensive — no server code changed but the Stop hook may run it; smaller scope per `CLAUDE.md` § Tier 1).
4. Manual browser click-through: typing 6 digits navigates to `/`; typing wrong digits shows the inline error; backup-code toggle works; "Back to sign in" returns to `/login`; the `expired=1` toast renders on `/login` after a `UNAUTHENTICATED` response.
5. The roadmap's "9.2 `/login/totp`" verification block (lines 982–989) — every line ticked or explicitly carried over to a later stage with a back-reference. (Today five of those six lines are achievable in this stage; "On success: redirect to `/` (dashboard) or onboarding if first-run" — the onboarding redirect is Stage 15.5 and is NOT in scope here; that bullet stays unchecked with a back-reference to Stage 15.5.)
6. No new untranslated copy when toggling to ES.

---

## Risks + mitigations

| Risk | Mitigation |
|---|---|
| `apiFetch` duplication for 429 path drifts from the main helper over time. | Isolate to a single `submitTotp()` helper in `LoginTotp.tsx`. Comment cites the reason. If a second page needs response-header access, that's the trigger to extend `apiFetch` properly. |
| `Mfa.RememberMe` cookie path is `/api/auth/login` — does it travel on `/api/auth/login/totp`? | Yes. Path `/api/auth/login` matches both `/api/auth/login` and `/api/auth/login/totp` per cookie path-matching rules (prefix match). Confirmed at `AuthController.cs:186`. |
| Test environment doesn't set the `Identity.TwoFactorUserId` cookie — `getTwoFactorAuthenticationUserAsync` will return null in vitest. | Tests don't hit a real server; the `fetch` spy returns whatever response shape we want per test. The half-auth cookie isn't real in vitest land. This is correct test isolation. |
| The OTP cells render but the auto-submit doesn't fire on paste in older Safari. | The `input-otp` library handles paste internally and fires `onChange` once for the full pasted value. Test 2 covers the happy path; if Safari-specific issues arise, they'll be caught in the manual browser verification. |
| Stage 9.5 (`/account/unlock`) doesn't exist yet — `ACCOUNT_LOCKED_OUT` path navigates to a 404. | Documented in the "User-visible behaviour" table above. Acceptable mirror of the Login page's current behaviour; Stage 9.5 closes it. |

---

## Open questions

None.
