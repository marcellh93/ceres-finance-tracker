# Stage 9.6 — TOTP setup flow design

**Status:** Draft — 2026-05-18
**Roadmap anchor:** `docs/roadmap-phase-three.md` → Stage 9 → sub-stage 9.6
**Originating context:** `docs/planning-phase3.md` § 10 Auth screen design; ADR-0069 (MFA opt-in for personal users); `MfaController.cs` (server-side, shipped Stage 6.4)

---

## Goal

Replace `/app/security`'s placeholder page with a real Settings/Security surface that hosts the TOTP enrolment wizard. Three-step inline flow: scan + verify → save backup codes → confirmation. After enrolment, the page surfaces a "Regenerate backup codes" affordance. Entry point is locked to Settings/Security per ADR-0069.

The server side is complete (`MfaController` from Stage 6.4: `enroll`, `enroll/verify`, `backup-codes/regenerate`). This is a pure SPA stage.

---

## Server contract

Three endpoints already shipped from Stage 6.4. **One new endpoint added in 9.6** so the SPA "Turn off" button is real and not a deferral.

| Endpoint | Status | Auth | Body | Success | Failure |
|---|---|---|---|---|---|
| `POST /api/auth/mfa/enroll` | shipped | `[Authorize] + [RequireRecentAuth]` | — | `200 { otpAuthUri, manualEntryKey }` | `409 MFA_ALREADY_ENROLLED`; `401 REAUTH_REQUIRED` if last-reauth > 5 min |
| `POST /api/auth/mfa/enroll/verify` | shipped | `[Authorize] + [RequireRecentAuth]` | `{ code }` (6 digits) | `200 { backupCodes: string[] }` (10 items) | `400 NO_ENROLLMENT_IN_PROGRESS`; `400 INVALID_MFA_CODE`; `422 VALIDATION_ERROR`; `401 REAUTH_REQUIRED` |
| `POST /api/auth/mfa/backup-codes/regenerate` | shipped | `[Authorize] + [RequireRecentAuth]` | — | `200 { backupCodes }` | `409 MFA_NOT_ENABLED`; `401 REAUTH_REQUIRED` |
| `POST /api/auth/mfa/disable` | **NEW in 9.6** | `[Authorize] + [RequireRecentAuth]` | — | `204 NoContent` | `409 MFA_NOT_ENABLED`; `401 REAUTH_REQUIRED` |

**`POST /api/auth/mfa/disable` shape:**

- New action in `MfaController` mirroring the shape of `RegenerateBackupCodes` (same attributes, same auth posture).
- On call: `_userManager.SetTwoFactorEnabledAsync(user, false)`, then `_userManager.ResetAuthenticatorKeyAsync(user)` (regenerates the secret so a fresh enrolment starts clean), then `backupCodes.PurgeAsync(user.Id, ct)` to consume all persisted backup-code rows so old codes can't be used after re-enrolment, then `_auditLog.RecordAsync(user.Id, AuditLogAction.MfaDisabled, ct: ...)`.
- New `AuditLogAction.MfaDisabled` enum value if not already present.
- `MfaBackupCodeService` may need a `PurgeAsync(Guid userId, CancellationToken ct)` method; if it doesn't have one, add it alongside the controller change. The existing `RegenerateAsync` already deletes old rows internally, so the deletion pattern is established.

The `auth/me` response already carries `twoFactorEnabled: boolean`, so the Security page knows which state to render without a separate fetch.

---

## URL + route

Single route at `/app/security`. No nested routes. Wizard mounts inline. Per ADR-0069 + `feedback_ui_work_checklist` (no parallel routes that fragment the entry point), there's no `/security/totp/setup` and no first-login redirect.

---

## Page states

### Disabled state (`twoFactorEnabled = false`, no wizard active)

```
Two-factor sign-in is off
─────────────────────────
Add a second step to every sign-in: a 6-digit code from your
authenticator app. Without it, your account stays protected by
password only.

[ Set up two-factor sign-in ]
```

Clicking the button fires `POST /api/auth/mfa/enroll`. On 200 → enter wizard step 1. On 401 REAUTH_REQUIRED → render an inline alert "Your sign-in is older than 5 minutes. Sign out and sign in again to continue." with a "Sign out" link. (The proper reauth modal is Stage 9.8's owner — see "Out of scope" below.) On 409 MFA_ALREADY_ENROLLED → refresh auth context (the `twoFactorEnabled` flag is stale) and re-render the Enabled state.

### Wizard active

The disabled-state block is replaced with the wizard container. A `<SecurityWizardStepper>` (local 3-step component; the existing Import `WizardStepper` is hard-coded to 4 Import-specific labels) renders pills at the top. Cancel link (`<Button variant="link">`) visible on every step:

- Step 1 cancel: drops back to the disabled state. The auth-key persists on the server but is overwritten on the next `enroll` call.
- Step 2 cancel: opens an `AlertDialog` warning the user the backup codes won't be shown again. Confirming drops to Enabled state with no codes saved client-side. (User can immediately click "Regenerate backup codes" to recover.)
- Step 3 cancel: no-op — step 3 has a "Back to Security" button that does the same thing.

### Enabled state (`twoFactorEnabled = true`, no wizard active)

```
Two-factor sign-in is on
────────────────────────
Every sign-in now requires a 6-digit code from your authenticator
app. If you lose your device, use one of your backup codes.

[ Regenerate backup codes ]   [ Turn off two-factor sign-in ]
```

**Turn off** opens an `AlertDialog`: "Turn off two-factor sign-in? Your account will only be protected by your password until you turn it back on." On confirm, fires `POST /api/auth/mfa/disable`. On 200 → `auth.refresh()` → page re-renders the Disabled state.

**Regenerate** opens a confirmation `AlertDialog`: "This invalidates your existing backup codes. Make sure you can scan a fresh QR or have your phone." Confirming fires `POST /api/auth/mfa/backup-codes/regenerate`; on 200, renders the new 10 codes in the same UI as wizard step 2 (without the "you're enrolled now" framing).

---

## Wizard step 1 — Scan & verify

Two-column on `sm:` and up. Single column stacked below:

- **Left column.** QR code via `qrcode.react`'s `QRCodeSVG` component, 192×192px, level `M`. Below: a `<Disclosure>` (chevron + collapsible) "Can't scan? Enter this key manually" — opens to reveal the space-grouped `manualEntryKey` (e.g. `JBSW Y3DP EHPK 3PXP`) in a monospace block with a "Copy" button.
- **Right column.** Heading "Scan with your authenticator app", a short ordered list (Open Google Authenticator / 1Password / Authy → Scan code → Type the 6-digit code below), then a 6-cell `InputOTP` group (same primitive 9.2 uses). Submit button "Turn on two-factor sign-in".

Submit calls `POST /api/auth/mfa/enroll/verify` with `{ code }`. Responses:

- `200 { backupCodes }` → advance to step 2 with the codes in state.
- `400 INVALID_MFA_CODE` → inline `role="alert"` "That code didn't match. Try the next one your app shows." Clear cells; refocus cell 1.
- `400 NO_ENROLLMENT_IN_PROGRESS` → defensive; should not happen unless the user has two tabs open. Surface "Something went wrong, restart setup" + a "Restart" button that re-calls `enroll`.
- `422` (malformed code shape — server may also reject 5-digit / 7-digit input) → zod prevents this client-side, but we map any server `details[]` to field errors as a defensive measure.
- `401 REAUTH_REQUIRED` → same "sign out / sign in again" inline alert as the disabled-state path.

Auto-submit on 6 digits, mirroring 9.2's pattern. The form is `react-hook-form` + zod with `totpCodeSchema` from `auth/schemas/login-totp.schema.ts` (already exists from 9.2 — reuse, don't duplicate).

---

## Wizard step 2 — Save backup codes

```
Save your backup codes

If you lose access to your authenticator app, use one of these
codes to sign in. Each code works once.

  ┌──────────────────┐  ┌──────────────────┐
  │  ABCD-EFGH-IJKL  │  │  MNOP-QRST-UVWX  │
  └──────────────────┘  └──────────────────┘
  ... 8 more codes in a 2×5 grid ...

  [ Copy all ]  [ Download as .txt ]

⚠ These codes will not be shown again.

[x] I've saved my backup codes somewhere safe

      [ Done ]   (disabled until checkbox is checked)
```

- Codes in a 2×5 grid on `sm:`+, single column on mobile. Each code in a bordered card with monospace text. Tabular numerics rule (`docs/design-system.md`) doesn't apply — these are alphanumeric tokens, not numbers, so `font-mono` is correct.
- **Copy all:** writes a newline-separated list to clipboard via `navigator.clipboard.writeText`. Fires a sonner `toast.success("Backup codes copied")`. Includes a 200ms minimum-duration fallback for the visual confirmation.
- **Download as .txt:** synthesises a Blob, creates an object URL, programmatically clicks an `<a download="ceres-backup-codes.txt">`. The file contains the codes one-per-line plus a header with the user's email + date.
- **Forced checkbox:** controlled `<Checkbox>` from shadcn (base-ui). The Done button is `disabled={!confirmed}`. Confirms the user has acknowledged the codes are visible only once.

Done advances to step 3.

---

## Wizard step 3 — Done

```
Two-factor sign-in is on

From now on, every sign-in needs:
  • Your password
  • A 6-digit code from your authenticator app

If you lose your device:
  • Use one of your backup codes
  • Then regenerate codes from this page

[ Back to Security ]
```

The button calls `auth.refresh()` (so `twoFactorEnabled` flips in context) and renders the Enabled state of the page. The wizard unmounts.

---

## File layout

```
ProjectCeres.Client/src/app/
  pages/Security.tsx                     [REWRITE — currently a placeholder]
  pages/Security.test.tsx                [new]
  features/security/
    TotpEnrollmentWizard.tsx             [new] — orchestrates the 3 steps + state
    SecurityWizardStepper.tsx            [new] — local 3-step stepper (Import's is 4-step)
    TotpEnrollStep1ScanVerify.tsx        [new]
    TotpEnrollStep2BackupCodes.tsx       [new]
    TotpEnrollStep3Done.tsx              [new]
    RegenerateBackupCodesDialog.tsx      [new]
    backup-codes-download.ts             [new] — .txt synthesis util + tests
    backup-codes-download.test.ts        [new]
    TotpEnrollmentWizard.test.tsx        [new]
    Security.a11y.test.tsx               [new] — single axe-zero across all 3 states
public/locales/en/translation.json       [edit — new security.totp.* namespace]
public/locales/es/translation.json       [edit — Spanish mirror]
```

No new dependencies. `qrcode.react@4.2.0` already installed (`package.json:30`) and budgeted into `vendor-qr` chunk per `vite.config.ts:79-80`.

---

## i18n keys

```jsonc
"security": {
  "title": "Security",
  "totp": {
    "disabledHeading": "Two-factor sign-in is off",
    "disabledBody": "Add a second step to every sign-in: a 6-digit code from your authenticator app. Without it, your account stays protected by password only.",
    "enableButton": "Set up two-factor sign-in",
    "enabledHeading": "Two-factor sign-in is on",
    "enabledBody": "Every sign-in now requires a 6-digit code from your authenticator app. If you lose your device, use one of your backup codes.",
    "regenerateButton": "Regenerate backup codes",
    "disableButton": "Turn off two-factor sign-in",
    "reauthRequired": "Your sign-in is older than 5 minutes. Sign out and sign in again to continue.",
    "signOutLink": "Sign out",
    "wizard": {
      "stepper": {
        "scanVerify": "Scan & verify",
        "saveCodes": "Save codes",
        "done": "Done"
      },
      "cancel": "Cancel",
      "step1": {
        "heading": "Scan with your authenticator app",
        "instruction1": "Open Google Authenticator, 1Password, Authy, or any TOTP app.",
        "instruction2": "Scan the QR code on the left.",
        "instruction3": "Type the 6-digit code your app shows below.",
        "manualEntryDisclosure": "Can't scan? Enter this key manually",
        "manualEntryCopy": "Copy",
        "manualEntryCopied": "Manual key copied",
        "codeLabel": "Verification code",
        "submit": "Turn on two-factor sign-in",
        "submitting": "Turning on",
        "errors": {
          "invalidCode": "That code didn't match. Try the next one your app shows.",
          "noEnrollmentInProgress": "Something went wrong. Click Restart to begin again.",
          "restartButton": "Restart",
          "network": "Couldn't reach the server. Check your connection and try again."
        }
      },
      "step2": {
        "heading": "Save your backup codes",
        "body": "If you lose access to your authenticator app, use one of these codes to sign in. Each code works once.",
        "copyAll": "Copy all",
        "copiedToast": "Backup codes copied",
        "download": "Download as .txt",
        "downloadFilename": "ceres-backup-codes.txt",
        "warning": "These codes will not be shown again.",
        "confirmCheckboxLabel": "I've saved my backup codes somewhere safe",
        "done": "Done",
        "cancelAlert": {
          "title": "Leave without saving?",
          "body": "You're already enrolled but haven't saved backup codes. They won't be shown again. You can regenerate them from the Security page.",
          "cancel": "Cancel",
          "confirm": "Leave without saving"
        }
      },
      "step3": {
        "heading": "Two-factor sign-in is on",
        "bodyHeading1": "From now on, every sign-in needs:",
        "bodyItem1a": "Your password",
        "bodyItem1b": "A 6-digit code from your authenticator app",
        "bodyHeading2": "If you lose your device:",
        "bodyItem2a": "Use one of your backup codes",
        "bodyItem2b": "Then regenerate codes from this page",
        "back": "Back to Security"
      }
    },
    "regenerate": {
      "dialogTitle": "Regenerate backup codes?",
      "dialogBody": "This invalidates your existing backup codes. Make sure you can scan a fresh QR or have your phone.",
      "dialogCancel": "Cancel",
      "dialogConfirm": "Regenerate codes",
      "successHeading": "Here are your new backup codes",
      "successBody": "Your old codes are no longer valid. Save these somewhere safe."
    }
  }
}
```

Spanish translations follow the same shape (Spanish strings written out in the implementation, not duplicated here for spec brevity).

---

## Reauth handling (deferral with receiving entry)

`useStepUp` at `src/app/auth/use-step-up.ts:8-12` is a documented forward-compat stub that doesn't open a reauth modal yet. Its own docstring says "Phase 4 wires this to open the modal + retry." Stage 9.8 (`docs/roadmap-phase-three.md:959`, "Reauthentication prompts on sensitive operations") is the receiving stage for that work.

**9.6's reauth handling:** call the MFA endpoints directly via `apiFetch`; if the response is `401 REAUTH_REQUIRED` (`apiFetch` throws `ReauthRequiredError`), catch it and render an inline alert: "Your sign-in is older than 5 minutes. Sign out and sign in again to continue." with a "Sign out" link that hits `auth.logout()` and navigates to `/login`.

Crude but functional. Stage 9.8 replaces the inline alert with a real modal.

---

## Tests

Vitest unit + RTL. All use the same `fetchSpy + clearXsrfTokenCacheForTests + I18nextProvider + AuthProvider + MemoryRouter` pattern as `Login.test.tsx` / `LoginTotp.test.tsx`.

### `Security.test.tsx` — page-level (8 tests)

1. `twoFactorEnabled: false` → renders disabled state with "Set up two-factor sign-in" button.
2. `twoFactorEnabled: true` → renders enabled state with Regenerate + Disable buttons; Disable is `disabled`.
3. Click Enable → fires POST `/api/auth/mfa/enroll`, on 200 advances to wizard step 1, QR is visible.
4. Click Enable → on 401 `REAUTH_REQUIRED` (apiFetch throws `ReauthRequiredError`) renders the reauth-required inline alert.
5. Click Enable → on 409 `MFA_ALREADY_ENROLLED` (race condition) calls `auth.refresh` and re-renders enabled state.
6. Click Regenerate → opens AlertDialog → confirm → fires POST `/api/auth/mfa/backup-codes/regenerate` → on 200 renders the new codes block.
7. Regenerate AlertDialog cancel closes without fetching.
8. Click Disable → opens AlertDialog → confirm → fires POST `/api/auth/mfa/disable` → on 204 calls `auth.refresh` and re-renders disabled state.

### `TotpEnrollmentWizard.test.tsx` — wizard flow (9 tests)

9. Step 1 renders QR + manual-entry disclosure + 6-cell input + submit.
10. Manual-entry disclosure expands on click; Copy button writes to clipboard (mocked) and fires sonner toast.
11. Typing 6 digits auto-submits POST `/api/auth/mfa/enroll/verify`.
12. On 200 `{ backupCodes: [...] }` advances to step 2; codes rendered in 10-cell grid.
13. On 400 `INVALID_MFA_CODE` renders inline error, clears cells.
14. Step 2 "Copy all" writes newline-joined codes to clipboard.
15. Step 2 "Download as .txt" triggers blob creation + click (asserts on `URL.createObjectURL` mock).
16. Step 2 Done is disabled until checkbox checked.
17. Step 2 cancel opens AlertDialog; confirming drops to enabled state without saving codes.
18. Step 3 "Back to Security" calls `auth.refresh` and renders enabled state.

### `backup-codes-download.test.ts` — util (3 tests)

19. `synthesizeBackupCodesTxt(codes, email)` returns a string containing all codes + email + date.
20. `downloadBackupCodes(...)` creates a Blob with `text/plain` MIME type.
21. `downloadBackupCodes(...)` revokes the object URL after the click.

### `Security.a11y.test.tsx` — vitest-axe (3 states, 1 test each)

22-24. axe-zero (`expectNoA11yViolations`) on disabled / step 1 / step 2 states.

Total: 24 tests, plus the existing `LoginTotp` tests stay green.

---

## Out of scope (with receiving entries)

| Item | Receiving stage | Reason |
|---|---|---|
| Proper reauth modal (real `useStepUp` behaviour) | Stage 9.8 — Reauthentication prompts on sensitive operations (`docs/roadmap-phase-three.md:959`) | Already-scheduled. 9.6 uses inline alert as a stopgap. |
| TOTP-enrolled security-event email | Stage 8 carry-forward at `docs/roadmap-phase-three.md:1079` (already a `[ ]` line) | Already-scheduled. |
| Backup-codes recovery flow during sign-in (lost device path) | Stage 9.7 — Backup-codes recovery flow (`docs/roadmap-phase-three.md:958`) | Already-scheduled. |

**Folded INTO 9.6 (not deferred):** The disable-MFA server endpoint + client wiring. First draft of this spec marked it as deferred with a same-commit receiving `[ ]` line — but ran through the no-defer gate, no valid reason held (tooling gap = no, already-scheduled = no), so the disable flow is in-scope for 9.6. ~30 lines of server code + 3-4 integration tests + the SPA button wiring.

---

## Definition of Done

1. `pnpm test` green — 24 new tests + existing suite still 941/941.
2. `pnpm build` green — `vendor-qr` chunk emits because `qrcode.react` is now imported; chunk-size budget already covers it.
3. `pnpm exec tsc --noEmit` green.
4. Manual browser checks (per the prerequisite-audit-bearing checklist handed off at end of session).
5. Roadmap line items for 9.6 ticked where automated tests cover them.

---

## Risks + mitigations

| Risk | Mitigation |
|---|---|
| `qrcode.react` first-import balloons `vendor-qr` chunk past budget | Budget already set in `vite.config.ts`. Production build at end of stage confirms or flags. |
| `navigator.clipboard.writeText` not available in jsdom | Mock via `Object.defineProperty(navigator, 'clipboard', ...)` in test setup (or per-test). Tests assert the call, not the actual clipboard. |
| `URL.createObjectURL` not implemented in jsdom | Same — mock per-test, assert the Blob shape. |
| `qrcode.react` SSR/import-time issues breaking the bundle | qrcode.react ships ESM. `import { QRCodeSVG } from 'qrcode.react'` is the canonical form. Vite handles ESM natively. |
| Auth context's `twoFactorEnabled` field is stale immediately after enrolment | Step 3 calls `auth.refresh()` before unmounting the wizard. Tested in test #18. |
| User opens enrolment wizard, walks away, session ages > 5 min, returns to step 1 submit, gets 401 REAUTH_REQUIRED mid-wizard | Step 1's error handler catches `ReauthRequiredError` the same way the disabled-state Enable button does. Inline alert + sign-out link. |
| Manual-key + QR both displayed at once helps the user but doubles the page's secret-on-screen surface area | Manual key is collapsed behind a disclosure by default. Both vanish from the DOM when the wizard advances to step 2. |
| Backup codes appear on screen and could be read over the user's shoulder | Wizard step 2 makes the warning visible. ADR-0069's threat model accepts this; physical-attacker scenarios are out of Phase 3 scope. |

---

## Open questions

None.
