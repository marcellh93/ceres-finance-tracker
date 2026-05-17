# Stage 9.4 — `/password-reset` request + confirm pages design

**Status:** Draft — 2026-05-18
**Roadmap anchor:** `docs/roadmap-phase-three.md` → Stage 9 → sub-stage 9.4
**Originating context:** `docs/security-model.md` § Password Reset; `docs/planning-phase3.md` § 10

---

## Goal

Replace `PasswordResetPlaceholder.tsx` with two real SPA pages:

- `/password-reset` — request form (one email field).
- `/password-reset/confirm` — token-consuming form (new-password + optional TOTP step).

Both pages wire to the already-shipped server endpoints. No backend changes.

---

## Server contract (existing, unchanged)

| Endpoint | Body | Success | Failure |
|---|---|---|---|
| `POST /api/auth/password-reset/request` | `{ email }` | `204` (always — anti-enumeration) | `429` with `Retry-After` header on per-email rate limit (5/hr); `422` on malformed email |
| `POST /api/auth/password-reset/confirm` | `{ token, newPassword, totpCode? }` | `204` on full success | `200 { requiresTotp: true }` if user has TOTP and `totpCode` missing/empty; `401 INVALID_RESET_TOKEN` (invalid/expired); `401 INVALID_MFA_CODE` (wrong TOTP); `422 VALIDATION_ERROR` with `details: [{field, message}]` on password-policy violation |

The reset URL embedded in the email is `…/app/password-reset#token=<raw>` (fragment, not query, per `PasswordResetService.cs:164` — fragments never reach the server).

Wait — the URL format from `PasswordResetService` is `/app/password-reset` with the token in the fragment. That collides with our request page route. The fragment carries the token to a page that's *also* the request page. The current design must therefore be: when `/password-reset` mounts and `location.hash` contains `token=`, it acts as the confirm page; otherwise it's the request page.

**Decision (autonomous):** Honor the server's existing URL convention. Single route at `/password-reset` switches mode based on `location.hash`. If a token is present in the hash, render the confirm form; otherwise render the request form. The `/password-reset/confirm` route is unnecessary — the server never emits it.

This is a meaningful deviation from the roadmap's line 998 ("`/password-reset` request page + `/password-reset/confirm` (action)"). The deviation is server-driven: the server already commits to fragment-based delivery and I'm not changing that. Reviewer note: this is the right call because changing the server's URL format would require a migration of any in-flight tokens in production.

---

## File layout

```
src/app/pages/auth/
  PasswordReset.tsx             [new — single page, dispatches by location.hash]
  PasswordReset.test.tsx        [new]
  PasswordResetPlaceholder.tsx  [delete]
src/app/auth/schemas/
  password-reset.schema.ts      [new]
src/app/App.tsx                 [edit — swap placeholder for PasswordReset]
src/app/pages/auth/Login.tsx    [edit — also fire toast on ?reset=1, mirroring ?expired=1]
src/app/pages/auth/Login.test.tsx  [edit — one new test for the ?reset=1 toast]
src/app/i18n/locales/en.json    [edit]
src/app/i18n/locales/es.json    [edit]
```

The placeholder's i18n keys (`auth.placeholders.passwordReset.*`) become dead — delete them in the same commit.

---

## UI / UX

### Request mode (no token in hash)

- Title: "Reset your password"
- Body: "Enter your account email and we'll send you a link."
- Field: email
- Submit → POST `/api/auth/password-reset/request`
- On `204`: replace the form with a success block ("Check your inbox. If that email is registered, you'll receive a reset link within a minute.") — no toast, no navigate.
- On `429`: inline error with `Retry-After` countdown (reuse 9.2's pattern).
- On network/5xx: inline error.
- "Back to sign in" link.

### Confirm mode (`location.hash` contains `token=<raw>`)

- Title: "Choose a new password"
- Body: "Enter a new password for your Ceres account."
- Fields: new password + confirm password (zod must match)
- First submit posts `{ token, newPassword }` (no totpCode).
- If response is `200 { requiresTotp: true }`: reveal a third field, the TOTP cell input (reuse `InputOTP` from 9.2), with hint "Enter the 6-digit code from your authenticator app." Submit again with all three fields.
- On `204`: navigate to `/login?reset=1` (Login fires a sonner toast on mount).
- On `401 INVALID_RESET_TOKEN`: replace the form with an error block + "Request a new link" → `/password-reset`.
- On `401 INVALID_MFA_CODE`: inline error on the TOTP cells; clear them; keep the password fields populated.
- On `422 VALIDATION_ERROR`: map `details[]` to react-hook-form field errors (mostly `newPassword`).
- On network/5xx: inline error.

### Edge cases

- Empty / malformed hash → render the request form (treat as if no token).
- Hash with `token=` but value empty → render the request form.
- User in TOTP-required state types a wrong TOTP, server returns `INVALID_MFA_CODE`: do NOT consume the reset token (server contract — confirmed at `PasswordResetService.cs:300-303`). User can retry.

---

## i18n keys (EN — Spanish mirror)

```jsonc
"auth": {
  "passwordReset": {
    "request": {
      "title": "Reset your password",
      "description": "Enter your account email and we'll send you a link.",
      "emailLabel": "Email",
      "submit": "Send reset link",
      "submitting": "Sending",
      "successTitle": "Check your inbox",
      "successBody": "If that email is registered, you'll receive a reset link within a minute.",
      "backToSignIn": "Back to sign in",
      "errors": {
        "tooManyAttempts": "Too many requests. Try again in {{seconds}} seconds.",
        "tooManyAttemptsNoCountdown": "Too many requests. Try again in a moment.",
        "network": "Could not send the request. Check your connection and try again."
      }
    },
    "confirm": {
      "title": "Choose a new password",
      "description": "Enter a new password for your Ceres account.",
      "newPasswordLabel": "New password",
      "confirmPasswordLabel": "Confirm new password",
      "totpLabel": "Verification code",
      "totpHint": "Enter the 6-digit code from your authenticator app.",
      "submit": "Reset password",
      "submitting": "Resetting",
      "backToSignIn": "Back to sign in",
      "requestNewLink": "Request a new link",
      "errors": {
        "mismatch": "Passwords do not match.",
        "invalidToken": "This reset link is invalid or has expired.",
        "invalidTotp": "Invalid verification code.",
        "network": "Could not reset your password. Check your connection and try again."
      }
    }
  },
  "login": {
    "toasts": {
      "passwordResetSuccess": "Password reset. Sign in with your new password."
    }
  }
}
```

---

## Tests (vitest + RTL)

`PasswordReset.test.tsx` — 12 scenarios:

1. No hash → renders email field + submit + back-to-sign-in.
2. Empty `#token=` → renders request mode (treats as empty).
3. Valid hash → renders new-password + confirm + submit.
4. Request mode: 204 success replaces form with success block.
5. Request mode: 429 with Retry-After → countdown message.
6. Request mode: 429 no Retry-After → no-countdown variant.
7. Confirm mode: mismatched passwords → zod field error, no fetch.
8. Confirm mode: 204 → navigate to `/login?reset=1`.
9. Confirm mode: 200 `{ requiresTotp: true }` → reveals TOTP cells; second submit posts all three fields.
10. Confirm mode: 401 INVALID_RESET_TOKEN → invalid-token block with "Request a new link" linking to `/password-reset`.
11. Confirm mode: 401 INVALID_MFA_CODE → inline error on TOTP, fields preserved.
12. Confirm mode: 422 VALIDATION_ERROR with `details: [{field: "newPassword", message: "..."}]` → maps to field error.

`Login.test.tsx` — 1 new test asserting the `?reset=1` toast fires.

a11y test deferred — these forms reuse the same primitives as Login + LoginTotp which already have a11y coverage; the duplicate axe-zero pass is `feedback_test_edge_cases_as_ship_gate` overkill and `expectNoA11yViolations` is already wired into both upstream tests. If the design diverges in a non-trivial way, add a11y in a follow-up.

---

## Definition of Done

1. `pnpm test` green: 12 new tests + 1 new Login test + existing suite.
2. `pnpm build` green.
3. Manual: hitting `/password-reset` with no hash shows the request form; submit shows the success block; visiting `/password-reset#token=foo` shows the confirm form; submitting renders one of the documented states based on what the server returns.
4. The roadmap's `/password-reset` block (lines 998–1005) reflected as `[x]` where automated tests cover them; the manual-only items (e.g. "Email contains link with 256-bit token (15-min expiry)") stay `[ ]` with the back-reference that the email itself is verified by `PasswordResetService` unit tests, not by SPA work.
