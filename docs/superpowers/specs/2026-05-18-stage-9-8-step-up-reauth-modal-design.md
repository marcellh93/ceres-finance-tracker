# Stage 9.8 — Step-up reauthentication modal design

**Status:** Draft — 2026-05-18
**Roadmap anchor:** `docs/roadmap-phase-three.md` → Stage 9 → sub-stage 9.8 ("Reauthentication prompts on sensitive operations")
**Originating context:** `docs/security-model.md` § Login → Reauthentication; existing server endpoint `POST /api/auth/reauth` shipped Stage 6c.2; current SPA stub at `src/app/auth/use-step-up.ts`

---

## Goal

Replace the inline "sign out and sign in again" alert that 9.6 shipped as a stopgap with a real modal that re-collects credentials inline and retries the original action. Wires the forward-compat `useStepUp` hook to a `StepUpReauthDialog` component. Single-modal flow; no route change, no sign-out, no work lost.

---

## Threat-model rationale (the section that was missing from Stage 6c.2 and caused this spec to be re-litigated 2026-05-18)

Step-up reauth exists because the session cookie alone cannot be the floor for destructive actions. The threats it defends against, in order of how reliably the system can stop each:

| Threat | What the attacker has | What stops them at step-up |
|---|---|---|
| Stolen session cookie via XSS / extension / network interception | Cookie only | Step-up requires a credential the attacker doesn't have (TOTP code on the user's phone, or password if MFA is off). System blocks reliably. |
| Walk-away on unattended laptop, phone NOT co-located | Open browser session | Step-up requires TOTP from a phone the attacker doesn't have. System blocks reliably. |
| Walk-away on unattended laptop with unlocked password manager (1Password, Bitwarden, browser-native), phone NOT co-located | Open session + ability to read stored password | If step-up uses password, attacker reads it from the manager and wins. **If step-up uses TOTP, attacker is blocked** (the password manager doesn't generate TOTP codes for the user's account — those live in the authenticator app on the phone). System blocks reliably. |
| Walk-away on unattended laptop with both password manager AND TOTP-generating app on the same laptop | Open session + password + TOTP | No purely-digital wall stops this. User responsibility (lock screen, screen timeout). |
| Walk-away with phone co-located and unlocked | Open session + phone with TOTP app | No purely-digital wall stops this. User responsibility. |
| Full device compromise (malware, EDR-bypass) | Everything on the device | Out of scope for the web app's authentication layer; device-level defenses are the user's OS/EDR. |

The decision: **TOTP is the step-up floor when the user has enrolled MFA; password is the floor when they have not.** This passes the threat-model analysis above (the password-manager threat is real and frequent; the both-factors-co-located threat requires the user to leave their phone unlocked next to the laptop, which is a behavior we cannot prevent in software). It matches `ReauthController.cs:44` already.

**Citations (authoritative, gathered 2026-05-18):**

- NIST SP 800-63B (2025 draft, `https://pages.nist.gov/800-63-4/sp800-63b.html`): "When the inactivity timeout has occurred but the overall timeout has not yet occurred, the verifier MAY allow the subscriber to reauthenticate using only a successful password or biometric comparison in conjunction with the session secret." NIST does not mandate a specific factor for step-up; it explicitly allows password-only with the session cookie as the implicit second factor. Our policy is stricter than NIST's minimum.
- Auth0 step-up guidance (`https://auth0.com/docs/secure/multi-factor-authentication/step-up-authentication`): "Step-up authentication allows applications that provide access to different types of resources to require users to authenticate with a stronger authentication mechanism to access sensitive resources." The principle is "stronger," not "different." For a user who has TOTP enrolled, TOTP IS the stronger factor (it's the one a password manager cannot leak).
- 1Password's own threat-model documentation (`https://support.1password.com/1password-browser-security/`): "Only use 1Password on trusted computers… it assumes that you trust your web browser and your other browser extensions. It stores your Secret Key in local storage and is not meant to be used on a public computer." 1Password disclaims the unattended-unlocked-machine threat — the system has to defend against it.

The choice between password and TOTP is **not** a UX preference. It is the load-bearing security decision of Stage 6c.2, which shipped without the rationale documented. This spec carries that rationale so a future reader doesn't re-litigate.

---

## Server contract (existing, unchanged)

`POST /api/auth/reauth` — `[Authorize]`, rate-limited by `AuthReauthByUser` policy.

| Branch | Body | Success | Failure |
|---|---|---|---|
| User has `TwoFactorEnabled = true` | `{ totpCode }` (6 digits) | `204 NoContent` (re-stamps `LastReauthAt`) | `401 INVALID_REAUTH` (wrong code); `401 ACCOUNT_LOCKED_OUT` (currently no AccessFailedAsync increment on TOTP miss per `ReauthController.cs:44-64`, but the lockout envelope is still returned if a sibling endpoint locked the user); `422 VALIDATION_ERROR` (missing or malformed totpCode); `429 RATE_LIMITED` with `Retry-After` |
| User has `TwoFactorEnabled = false` | `{ password }` | `204 NoContent` | `401 INVALID_REAUTH` (wrong password — also calls `AccessFailedAsync` which can transition into ACCOUNT_LOCKED_OUT); `401 ACCOUNT_LOCKED_OUT`; `422 VALIDATION_ERROR` (missing password); `429 RATE_LIMITED` |

The `auth/me` response carries `twoFactorEnabled: boolean`. The SPA does not need to negotiate with the server about which factor to ask for — the auth context already knows.

---

## SPA design

### File layout

```
ProjectCeres.Client/src/app/
  auth/
    use-step-up.ts                          [REWRITE — forward-compat stub becomes real]
    use-step-up.test.tsx                    [extend — new tests for the modal-driven retry]
    StepUpReauthDialog.tsx                  [new]
    StepUpReauthDialog.test.tsx             [new]
    schemas/
      reauth.schema.ts                      [new — zod for totpCode + password branches]
  pages/Security.tsx                        [edit — remove inline "sign out" alert; useStepUp handles it]
  features/security/RegenerateBackupCodesDialog.tsx [edit — remove onReauthRequired prop; useStepUp absorbs]
src/app/i18n/locales/en.json                [edit — new auth.reauth.* namespace]
src/app/i18n/locales/es.json                [edit — Spanish mirror]
```

### `useStepUp` — new implementation

The hook becomes stateful (modal open/closed + the pending action). The provider that hosts the modal is `AuthProvider` (so any descendant component can call `useStepUp().requireStepUp(action)` and have a single modal mount serve them all).

```tsx
// Pseudocode for the contract; real types in the implementation.
type UseStepUpResult = {
  requireStepUp: <T>(action: () => Promise<T>) => Promise<T>;
};

function requireStepUp(action) {
  try {
    return await action();
  } catch (err) {
    if (err instanceof ReauthRequiredError) {
      // Open the modal, await user submission, on success retry once.
      const ok = await openModalAndAwaitSuccess();
      if (!ok) throw err; // user cancelled — propagate to caller
      return await action(); // retry; if it fails again, throw
    }
    throw err;
  }
}
```

The modal lives at the AuthProvider level (mounted once in `App.tsx` or in `AuthProvider` itself). State is managed in a React context paired with the existing `AuthContext` — or just a sibling `StepUpContext` to keep concerns separate.

### `StepUpReauthDialog` component

`Dialog` (not `AlertDialog` — the user CAN dismiss without confirming). Header, body, single field branching on `auth.user.twoFactorEnabled`, submit button, cancel link.

**TOTP branch (`twoFactorEnabled = true`):**

```
┌─────────────────────────────────────────────────┐
│ Confirm your identity                      ✕   │
├─────────────────────────────────────────────────┤
│ Enter the 6-digit code from your authenticator  │
│ app to continue.                                 │
│                                                  │
│  [ _ _ _ _ _ _ ]                                 │
│                                                  │
│              [ Cancel ]  [ Confirm ]            │
└─────────────────────────────────────────────────┘
```

- 6-cell `InputOTP` group, auto-focus cell 1, auto-submit on 6 digits (mirrors `/login/totp`).
- "Confirm" button is `disabled` when input length ≠ 6.

**Password branch (`twoFactorEnabled = false`):**

```
┌─────────────────────────────────────────────────┐
│ Confirm your identity                      ✕   │
├─────────────────────────────────────────────────┤
│ Enter your password to continue.                │
│                                                  │
│  Password                                        │
│  [ ............... ]                             │
│                                                  │
│              [ Cancel ]  [ Confirm ]            │
└─────────────────────────────────────────────────┘
```

- Single password input, auto-focus, `autoComplete="current-password"`.
- "Confirm" disabled when length is 0.
- Submit triggered by button click OR Enter key (default form behavior).

**Common to both branches:**

- POST to `/api/auth/reauth` with the appropriate body.
- On `204`: close modal, signal success to `useStepUp` → the hook retries the original action.
- On `401 INVALID_REAUTH`: inline `role="alert"` error ("The code is incorrect." / "The password is incorrect."). TOTP cells clear and refocus cell 1; password input keeps the typed value so the user can fix a typo.
- On `401 ACCOUNT_LOCKED_OUT`: modal replaces its body with a "Your account is locked" message + a "Sign out" link that calls `auth.logout()` + navigates to `/account/unlock`. No retry path.
- On `429`: inline error with `Retry-After` countdown (reuses the pattern from `LoginTotp.tsx`).
- On `422 VALIDATION_ERROR`: map `details[]` to the active field's error.
- On network / 5xx: generic inline error.
- Cancel link: close modal, signal failure to `useStepUp` → the hook propagates the original `ReauthRequiredError` to the caller. The caller (e.g. `RegenerateBackupCodesDialog`) interprets that as "the user gave up" and resets its own state to idle.

### How sites consume it

Sites that previously caught `ReauthRequiredError` and surfaced their own UI now just call `requireStepUp`:

```tsx
// Before (9.6)
try {
  const result = await apiFetch('/api/auth/mfa/backup-codes/regenerate', { method: 'POST', body: {} });
  // ...
} catch (err) {
  if (err instanceof ReauthRequiredError) {
    onReauthRequired(); // parent renders an inline alert
    return;
  }
}

// After (9.8)
const result = await requireStepUp(() =>
  apiFetch('/api/auth/mfa/backup-codes/regenerate', { method: 'POST', body: {} })
);
// requireStepUp handles the modal + retry transparently
```

Sites updated by this spec:
- `RegenerateBackupCodesDialog.tsx` — drop the `onReauthRequired` prop, wrap `apiFetch` in `requireStepUp`.
- `Security.tsx` — drop the `reauthRequired` state + the inline alert + the Sign Out link. The `startEnrollment` call wraps `apiFetch` in `requireStepUp`; the `disable` call does the same.
- Any future sensitive endpoint added later automatically inherits the modal by going through `apiFetch` (which throws `ReauthRequiredError` on 401 REAUTH_REQUIRED) wrapped in `requireStepUp`.

### i18n keys

```jsonc
"auth": {
  "reauth": {
    "title": "Confirm your identity",
    "descriptionTotp": "Enter the 6-digit code from your authenticator app to continue.",
    "descriptionPassword": "Enter your password to continue.",
    "codeLabel": "Verification code",
    "passwordLabel": "Password",
    "submit": "Confirm",
    "submitting": "Confirming",
    "cancel": "Cancel",
    "errors": {
      "invalidTotp": "The code is incorrect.",
      "invalidPassword": "The password is incorrect.",
      "tooManyAttempts": "Too many attempts. Try again in {{seconds}} seconds.",
      "tooManyAttemptsNoCountdown": "Too many attempts. Try again in a moment.",
      "network": "Couldn't reach the server. Check your connection and try again."
    },
    "lockedOut": {
      "title": "Account locked",
      "body": "Your account is locked after too many failed attempts. Check your email for an unlock link.",
      "signOut": "Sign out"
    }
  }
}
```

Spanish mirror follows the same shape (Spanish strings spelled out in implementation, not duplicated here for spec brevity).

---

## Tests

### `StepUpReauthDialog.test.tsx` — modal behavior (8 tests)

1. **TOTP branch renders OTP cells when `twoFactorEnabled = true`** — asserts cells present, password input ABSENT. This is the test from the distinguishing-from-another-patch section above.
2. **Password branch renders password input when `twoFactorEnabled = false`** — asserts password input present, OTP cells ABSENT. The negative half of #1.
3. **TOTP auto-submits on 6 digits** — types 6 digits, asserts POST `/api/auth/reauth` fires with `{ totpCode }`.
4. **Password submits on Enter / button click** — types a password, asserts POST fires with `{ password }`.
5. **On 204 the modal closes and the success signal fires** — asserts the modal's `onSuccess` callback was called.
6. **On 401 INVALID_REAUTH the inline error renders, cells clear (TOTP) / value preserved (password)** — single test covering both branches via parametrization.
7. **On 429 with Retry-After surfaces the countdown** — reuses the pattern test from `LoginTotp.test.tsx`.
8. **On 401 ACCOUNT_LOCKED_OUT replaces the body with the locked-out block + sign-out link** — clicking sign-out calls `auth.logout()` and navigates to `/account/unlock`.

### `use-step-up.test.tsx` — hook behavior (4 tests)

9. **`requireStepUp(action)` returns `action()`'s value when action doesn't throw** — pass-through test.
10. **On `ReauthRequiredError`, opens the modal, waits for success, retries the action once** — asserts action is called twice, modal opened once.
11. **On `ReauthRequiredError`, if the modal is cancelled, throws `ReauthRequiredError` back to the caller** — asserts action is called once, the throw propagates.
12. **On `ReauthRequiredError`, if the retried action throws AGAIN, the second throw propagates without re-opening the modal** — guards against infinite loop.

### `RegenerateBackupCodesDialog.test.tsx` — updated (1 modified test)

The existing "on REAUTH_REQUIRED, dialog closes AND parent is notified" test from 2026-05-18 changes shape: there's no more `onReauthRequired` prop because the modal handles it internally. The test now asserts: on `ReauthRequiredError` from the regenerate endpoint, the step-up modal opens; on TOTP success the regenerate is retried and the backup-codes grid renders.

Total: 12 new tests + 1 modified.

---

## Threat-model regression test (the distinguishing-from-another-patch test)

A vitest test that mounts `StepUpReauthDialog` twice in the same test file with two different auth contexts:

```tsx
it('shows TOTP cells when user has MFA, password field otherwise (threat-model gate)', async () => {
  // MFA enrolled
  const { unmount, getByLabelText, queryByLabelText } =
    mountWithAuth({ twoFactorEnabled: true });
  expect(getByLabelText(/verification code/i)).toBeDefined();
  expect(queryByLabelText(/password/i)).toBeNull();
  unmount();

  // MFA not enrolled
  const second = mountWithAuth({ twoFactorEnabled: false });
  expect(second.getByLabelText(/password/i)).toBeDefined();
  expect(second.queryByLabelText(/verification code/i)).toBeNull();
});
```

If both branches render the same input regardless of the flag, the threat-model analysis was lost and the test fails. This is the test that distinguishes "the spec was implemented correctly" from "the modal exists but the branching collapsed."

---

## Definition of Done

1. `pnpm test` green: 12 new tests + 1 modified + existing suite (955 → 966).
2. `pnpm build` green.
3. `pnpm exec tsc --noEmit` green.
4. Manual: with TOTP enrolled, click "Regenerate backup codes" after the 5-minute window has lapsed → modal appears asking for the TOTP code → enter code → regenerate succeeds and codes render. With TOTP disabled (re-test on a fresh user), same flow but the modal asks for the password.
5. Roadmap 9.8 entries ticked where automated tests cover them.

---

## Risks + mitigations

| Risk | Mitigation |
|---|---|
| Modal mounted at the wrong level so siblings can't share it | Mount once in `AuthProvider` (or a dedicated `StepUpProvider` immediately inside). Tests assert that two sibling consumers in the same tree share the same modal instance. |
| `requireStepUp` called from a callsite outside any provider | The hook throws a clear error ("StepUpProvider missing"); same pattern as `useAuth`. |
| User loses their typed work in the original form because the modal navigation is destructive | The modal does not navigate. The original form stays mounted in the background. The retry on success re-runs the same `apiFetch` call with the same body the caller provided. The form state is the caller's responsibility (it never unmounted). |
| Modal opens but the request didn't actually need step-up (e.g., the 5-min window expired between `requireStepUp` invocation and the actual fetch) | This is the intended path: the action runs, throws `ReauthRequiredError`, the modal opens. Working as designed. |
| Two concurrent calls trigger two modals | The provider state is a single `{ pending, action }` slot; the second call waits in queue until the first resolves OR the second's action sees the freshened `LastReauthAt` and never throws. Test #12 covers this. |
| `apiFetch` doesn't currently throw on 401 REAUTH_REQUIRED for non-JSON responses | Confirmed at `api-client.ts:138` — `apiFetch` checks `code === 'REAUTH_REQUIRED'` and throws `ReauthRequiredError`. Already correct. |
| The retry runs but the user's session was revoked in the interim | Server returns a different 401 (UNAUTHENTICATED) on the retry; `apiFetch` throws an `ApiGenericFailure` (not `ReauthRequiredError`), which propagates to the caller. The caller treats it as a normal failure. |

---

## Out of scope (no deferrals)

- The 5-minute window value itself. Set in `appsettings.json` → `Authentication:Cookie:ReauthWindowMinutes`, default 5. Tuning the value is a separate ops decision.
- Hardware-key (WebAuthn) as a step-up factor. Not in roadmap; would need its own brainstorm.
- "Remember me on this device for 30 days" on the reauth modal. Would require a new server endpoint to mint a device-bound trust token; out of scope and arguably weakens the security principle.

---

## Open questions

None.
