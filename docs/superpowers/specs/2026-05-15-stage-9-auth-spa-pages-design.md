# Stage 9 — Auth SPA Pages — Design

**Status:** Brainstormed 2026-05-15. Ready for user review before plan-writing.
**Roadmap section:** `docs/roadmap-phase-three.md` § Stage 9 (lines 942–1083).

---

## Context

Stage 9 is the first user-visible Phase 3 work. Every authentication screen currently lives in legacy Razor pages or doesn't exist at all; Stage 9 builds the React SPA versions that beta users will land on. Sixteen authentication API endpoints are already wired on the server (Stage 6 + 6c.2 + Stage 8). The SPA has zero auth scaffolding — no `<AuthLayout>`, no internationalisation, no validation library, no auth context, no QR-rendering library, and no API client that participates in the existing CSRF handshake.

Two server endpoints need to ship as part of Stage 9: `POST /api/auth/email/verify` and `POST /api/auth/mfa/disable`. The first closes a serious gap in the current registration flow: `Program.cs:112` enforces `Identity.SignIn.RequireConfirmedEmail = true`, but `AuthController.Register` does NOT set `EmailConfirmed = true` and does NOT send a confirmation email. ASP.NET Identity defaults `EmailConfirmed` to `false`, which means a user who registers today through the API cannot subsequently log in — the product path is broken end-to-end and only the integration test fixture papers over it (`AuthTestFixture.cs:36–37` calls `GenerateEmailConfirmationTokenAsync` + `ConfirmEmailAsync` after every test register; production has no equivalent). The second endpoint is `POST /api/auth/mfa/disable` — the audit-log enum value `MfaDisabled` is reserved but no controller action exists yet. One small server-side prerequisite endpoint also lands first: `GET /api/auth/me`, returning the data the SPA's auth context depends on.

Five email templates carried forward from Stage 8 wire here: `RegistrationConfirmation`, `TotpEnrolled`, `TotpReEnrolled`, `TotpDisabled`, `BackupCodesRegenerated`. One Stage 6 carry-forward — manual DevTools verification of the `__Host-Session` cookie's attributes — runs once a real login UI exists.

### Locked design call

Per the user's explicit decision in this brainstorm, login success **always redirects to `/` (dashboard) regardless of TOTP-enrolment status**. There is no banner, no prompt, and no onboarding push for MFA. The `/security/totp/setup` route is reachable only via the "Enable two-factor" button on `/security`. Roadmap line 977 (which currently asserts a redirect to `/login/totp/setup` on first login) is rewritten in the same commit as the `/login` page lands. This call follows ADR-0069 (MFA opt-in for personal users) — no enrollment grace period exists.

---

## Section 1 — Architecture

### Two top-level branches in the SPA

Today every route in `App.tsx` mounts inside `<AppLayout>` (sidebar + topbar + assumes a logged-in user). Stage 9 splits the route tree into two branches:

1. **Public branch** — wraps `<AuthLayout>` (centered card, brand wordmark, language toggle in footer, no sidebar). Hosts seven public pages: `/login`, `/login/totp`, `/register`, `/email-verify`, `/password-reset`, `/password-reset/confirm`, `/account/unlock`.
2. **Protected branch** — wraps `<RequireAuth>` (a guard component that redirects anonymous visitors to `/login?redirect=...`) around the existing `<AppLayout>`. Everything currently in `App.tsx` moves into this branch unchanged. The new `<ReauthenticationDialog>` is mounted at this level so any sensitive-action page can trigger it. The `/security/totp/setup` route sits here too — it's a logged-in flow, not a login flow, so it uses the app shell rather than the centered card.

### Two new server endpoints

- **`POST /api/auth/email/verify`** — accepts a single-use 256-bit Argon2id-hashed token; flips `EmailConfirmed = true`; redirects the SPA to `/login` on success. Plus a small change to `AuthController.Register`: today it does nothing about email-confirmation, leaving the user stuck on `EmailConfirmed = false` (the Identity default). The new behaviour: register issues the token and sends `RegistrationConfirmation` — only on the genuinely-new-account path; the duplicate-email anti-enumeration path stays silent, since sending an email there would itself be an enumeration leak. `EmailConfirmed` stays `false` until the user clicks the link. Login already refuses unconfirmed accounts (because `RequireConfirmedEmail = true` is already on); Stage 9 wires a new error code `EMAIL_NOT_CONFIRMED` so the SPA can tell the user *why* their otherwise-valid credentials were rejected (see Section 4 for the design rationale on splitting this out from the existing `INVALID_CREDENTIALS` envelope).
- **`POST /api/auth/mfa/disable`** — gated by `[RequireRecentAuth]`; sets `TwoFactorEnabled = false`; deletes the TOTP seed + backup codes; regenerates `SecurityStamp` (which signs out other devices); sends `TotpDisabled`; writes an audit-log row with action `MfaDisabled`.

Both endpoints stay on the existing `/api/auth/...` route prefix. The `/api/v1/` versioning prefix called out in `docs/api-contract.md` (line 263) ships with Stage 11's URL cleanup, not Stage 9.

### Prerequisite endpoint

**`GET /api/auth/me`** — the SPA's auth context calls this once on mount to determine session state. Returns `{ userId, email, twoFactorEnabled, lastReauthAt, backupCodesRemaining, usedBackupCodeAtLastLogin }` for an authenticated request, or `401` for an anonymous request. `Cache-Control: no-store`. Lands as the very first commit of Stage 9.

### Five new email templates

Five new values added to `EmailTemplateKey` enum (`ProjectCeres/Common/Email/EmailTemplateKey.cs`) plus three keys each (`<Key>.Subject`, `<Key>.BodyText`, `<Key>.BodyHtml`) in both `ProjectCeres/Resources/EmailsResource.en.resx` and `EmailsResource.es.resx`. The wirings are detailed in Section 5.

### Four foundation pieces in the SPA

Four pieces have to exist before any auth page can be built:

1. **i18n** — `react-i18next` + `i18next` + `i18next-browser-languagedetector` installed via **pnpm only** (`pnpm --dir ProjectCeres.Client add ...` — npm is forbidden in this codebase, see `feedback_pnpm_only_never_npm`). Plus a `lang` cookie reader/writer, `en.json` + `es.json` files starting with the keys commits 5 and 6 will need, and a server-side `LanguagePreferenceMiddleware` that reads the cookie and sets `CultureInfo.CurrentUICulture` for the request.
2. **AuthLayout + routing split** — described above.
3. **API client + auth context** — a single `apiFetch()` wrapper that does the cross-site-request-forgery handshake (the SPA gets a token from the server via `GET /api/auth/csrf`, then sends it back as a header on every state-changing request — this protects against malicious sites tricking a logged-in user into submitting requests they didn't mean to). The header name is `X-XSRF-TOKEN` (constant: `SessionConstants.CsrfHeaderName`); the cookie name is `__Host-XSRF` (constant: `SessionConstants.CsrfCookieName`). Plus `useAuth()` for session state (calls `/api/auth/me`) and `useStepUp()` for the reauth-dialog trigger.
4. **Form library + OTP input + QR** — `react-hook-form` + `zod` + `@hookform/resolvers` + `qrcode.react` installed via pnpm; the shadcn `input-otp` component added via `pnpm dlx shadcn add input-otp`. Documented in **ADR-0074** (`docs/decisions/ADR-0074-react-hook-form-with-zod-for-spa-forms.md`), written alongside the dependency addition.

---

## Section 2 — Data flow

How a request travels through the SPA, end-to-end. Three flows worth pinning.

### Flow 1 — A logged-out user opens any protected page

1. User hits `https://app.ceres/.../movements` (or any protected route).
2. `<RequireAuth>` reads the auth context; the auth context calls `GET /api/auth/me` once on mount.
3. Server returns 401. Auth context flips to `status: 'anon'`.
4. `<RequireAuth>` redirects to `/login?redirect=/movements`. After successful login, the user lands back on `/movements` instead of the dashboard.

### Flow 2 — A successful login

1. User submits the login form. `react-hook-form` runs the zod-resolved client-side validation first (must look like an email; password must be present). Field errors render inline below each input.
2. Form calls `apiFetch('/api/auth/login', { method: 'POST', body: { email, password, rememberMe } })`. `apiFetch` notices this is a state-changing request; runs the **CSRF handshake** if it hasn't already this session: a single `GET /api/auth/csrf` that sets the `__Host-XSRF` cookie. The handshake is deduped via a module-level promise so concurrent submissions only fire it once. `apiFetch` reads the cookie, attaches it as the `X-XSRF-TOKEN` header, sets `credentials: 'include'` so the session cookie is sent on subsequent requests.
3. Server validates → sets `__Host-Session` cookie → returns either `204 No Content` (no TOTP) or `200 { requiresTotp: true }` (TOTP enrolled). (The exact response field is `requiresTotp`, per `api-contract.md:460`.)
4. `useAuth().login()` handles the branch:
   - `204` → call `refresh()` to re-fetch `/api/auth/me`, then navigate to `/` regardless of TOTP status.
   - `200 + requiresTotp` → navigate to `/login/totp`, where the OTP step continues.

### Flow 3 — A sensitive action that needs reauth

1. User clicks "Disable two-factor" on `/security`.
2. The button handler is wrapped in `useStepUp().requireStepUp(action)`. It optimistically calls the server.
3. Server's `[RequireRecentAuth]` attribute checks the `last_reauth_at` claim baked into the session cookie (constant: `SessionConstants.LastReauthAtClaim`). If older than 5 minutes, it returns `401` with the project envelope `{ error: { code: "REAUTH_REQUIRED", message: "..." } }`.
4. `apiFetch` sees code `REAUTH_REQUIRED`; throws `ReauthRequiredError`.
5. `useStepUp` catches it; opens `<ReauthenticationDialog>` (mounted at the protected-branch root). The dialog renders a password input (if `useAuth().user.twoFactorEnabled === false`) or the OTP input (if `true`).
6. User submits → `POST /api/auth/reauth` → server stamps a fresh `last_reauth_at` claim. Modal closes, the original action retries automatically.

### Error envelope contract — dual-shape 422s

Per `docs/api-contract.md:138–212`, the project's error envelope is **the project's own shape, not ASP.NET `ProblemDetails`**:

```json
{ "error": { "code": "VALIDATION_ERROR", "message": "...", "details": [...] } }
```

422 responses come in **two shapes** the SPA must handle distinctly:

- **Field validation** (`InvalidModelStateResponseFactory` in `Program.cs:37–47`): `details` is an array of `{ field, message }`. SPA: pass each entry to `react-hook-form`'s `setError(field, ...)` so errors render inline next to the corresponding input.
- **Business-rule validation** (controller `return UnprocessableEntity(...)` after `[ApiController]` auto-validation has passed): `details` is an empty array. The user-facing message lives in `error.message`. SPA: render as a page-level alert above the form (not as a toast — toasts are for systemic errors only; see Section 4).

Frontend code distinguishes the two by checking `details.length > 0`.

### Cookies in play

| Cookie | Constant | Attributes | Purpose |
|---|---|---|---|
| `__Host-Session` | `SessionConstants.SessionCookieName` | HttpOnly, Secure, SameSite=Lax, Path=/ | Session token |
| `__Host-Persist` | `SessionConstants.PersistentCookieName` | Same as above; 30-day | Remember-me; only set when the user ticks the box |
| `__Host-XSRF` | `SessionConstants.CsrfCookieName` | NOT HttpOnly, Secure, SameSite=Lax, Path=/ | CSRF token; readable by SPA so it can echo it back as a header |
| `Mfa.RememberMe` | (string literal in `AuthController.cs:177`) | HttpOnly, Secure, SameSite=Lax, Path=/api/auth/login, transient | Carries the remember-me intent through the TOTP step |
| `lang` | (new in Stage 9) | NOT HttpOnly, Secure, SameSite=Lax, Path=/, 1-year | Pre-login language preference; the SPA reads it to bootstrap `react-i18next`; after login, the user's account language preference takes over on the next page load |

---

## Section 3 — Per-page contract

| Route | Branch | Trigger | Visible chrome |
|---|---|---|---|
| `/login` | public | URL or `<RequireAuth>` redirect | `<AuthLayout>` |
| `/login/totp` | public | post-credentials, server returns `requiresTotp:true` | `<AuthLayout>` |
| `/register` | public | URL or "Create account" link | `<AuthLayout>` |
| `/email-verify` | public | URL after register, or token link from email | `<AuthLayout>` |
| `/password-reset` | public | URL or "Forgot password?" link | `<AuthLayout>` |
| `/password-reset/confirm` | public | token link from email | `<AuthLayout>` |
| `/account/unlock` | public | URL or token link from email | `<AuthLayout>` |
| `/security/totp/setup` | **protected** | "Enable two-factor" button on `/security` | `<AppLayout>` (sidebar + topbar) |
| `<ReauthenticationDialog>` | protected (modal) | any `ReauthRequiredError` | `<AppLayout>` overlay |
| `<BackupCodeBanner>` | protected (banner) | `usedBackupCodeAtLastLogin && backupCodesRemaining < 10` | `<AppLayout>` top-of-content |

### Per-page behaviour

| Route | Purpose | Success | Failure |
|---|---|---|---|
| `/login` | Email + password + remember-me; "Forgot password?" + "Create account" links | Without TOTP → `/`; with TOTP → `/login/totp`; with `?redirect=...` → that path | Wrong creds → field error "Email or password is incorrect."; locked → redirect to `/account/unlock` (no-token mode); unverified email → field error "Verify your email first. Check your inbox." with inline "Resend" link |
| `/login/totp` | 6-digit OTP (default) or "Use a backup code" toggle → 8-char alphanumeric | OTP → `__Host-Session` set, `/`; backup code → same plus `BackupCodeBanner` flagged on dashboard | Wrong code → field error "Code didn't work. Try again."; the project does NOT lock out on bad TOTP (Stage 6 design call) — the lockout matrix only triggers on bad password |
| `/login/totp` (backup mode) | 8-char Crockford-format input (with or without dashes; server accepts both per `api-contract.md:461`) | Same as OTP | Same as OTP |
| `/register` | Email + password + password-confirm + terms checkbox; live strength meter; cross-field match check | 202 Accepted → `/email-verify?email=...` interstitial regardless of duplicate (no enumeration) | 422 → field errors; weak/mismatch handled client-side first |
| `/email-verify` (interstitial mode) | Renders "Check your inbox at {email}" + rate-limited "Resend" button | Just renders | — |
| `/email-verify?token=...` | POSTs the token to `/api/auth/email/verify` | Toast "Email verified." + redirect to `/login` | Expired/invalid token → page-level error block + "Request a new link" |
| `/password-reset` | Email field only | Always shows "If an account exists for that email, you'll receive a link." (no enumeration) | Rate-limit hit → same generic message + `Retry-After` honoured silently |
| `/password-reset/confirm?token=...` | New password + confirm; TOTP field renders only if user has TOTP enabled | Token consumed, all sessions revoked, redirect to `/login` with toast "Password updated." | Wrong TOTP → field error, **token NOT consumed** (server enforces this); expired token → "Request a new link" |
| `/account/unlock?token=...` | POSTs the token to `/api/auth/lockout-unlock` | Toast "Account unlocked." + redirect to `/login` | Invalid/expired token → page-level error block |
| `/account/unlock` (no token) | Static info: "Your account is locked. Check your email for an unlock link. Valid TOTP codes are still accepted during lockout." | Just renders | — |
| `/security/totp/setup` | Multi-step: enroll (QR + manual entry) → verify (6-digit OTP) → backup-codes (download / copy + "I've saved them" checkbox) | TOTP enabled, return to `/security` | Wrong verify code → inline error; "I've saved them" checkbox gates the final advance |
| `<ReauthenticationDialog>` | Modal: password (no TOTP) or OTP (TOTP enabled) | Closes; original action retries | Wrong creds → field error inside dialog; cancel aborts the original action |
| `<BackupCodeBanner>` | Persistent (no dismiss); copy: "Re-enroll TOTP soon. You have N backup codes remaining." | Disappears when user re-enrolls (count returns to 10) | — |

### Cross-cutting

- **Tab order on every form**: email → password → submit → "Forgot password?" → "Create account" (per roadmap line 1051).
- **Footer on every auth card**: globe icon dropdown with EN / Español. Reads + writes the `lang` cookie. After login, the user's account language preference takes over on the next page load.
- **All forms are real `<form>` elements with `<button type="submit">`** so Enter submits — see Section 4 Rule 4.

---

## Section 4 — Error handling

Four rules govern the matrix. The rules first because they govern the matrix.

### Rule 1 — Field-bound by default; toasts only for systemic errors

A 422 with `details.length > 0` renders inline next to each named field. A 401 with a known auth code is field-bound when there's an obvious field to attach it to. Toasts are reserved for **systemic** problems: network failure, 5xx, "couldn't reach Ceres". Rationale: `docs/design-system.md`'s forms section. Field errors stay visible while the user fixes them; toasts disappear.

### Rule 2 — No enumeration leaks

Every response that touches "does this email exist" returns the same body and takes the same wall-clock time regardless. The server already enforces this (dummy Argon2id hash on unknown-user login per `AuthController.cs:126`; identical 204s on register-duplicate vs register-fresh per `AuthController.cs:97`; identical 204s on password-reset request). The SPA must not undo it with UI cleverness — never render "this email is already registered" on `/register`; never render "no account found for that email" on `/password-reset`.

The exact strings are pinned in the roadmap and they go straight into `en.json` + `es.json` as-is:

- `/login` wrong creds → "Email or password is incorrect."
- `/login` locked → "Account locked. Check your email for an unlock link."
- `/login` unverified → "Verify your email first. Check your inbox." + inline "Resend" link
- `/login/totp` wrong code → "Code didn't work. Try again."
- `/login/totp` expired window → same message (no leak that "the code was right but the time window passed")
- `/password-reset` always → "If an account exists for that email, you'll receive a link."
- `/register` always → 202 + "Check your inbox to verify your email."

### Rule 3 — Lockout transitions don't lock the user out of recovery

Per `security-model.md` and confirmed by `AuthController.cs`, the lockout counter only increments on bad password — bad TOTP doesn't increment it (`AuthController.cs:265, 281, 315` use `VerifyTwoFactorTokenAsync`, not `TwoFactorAuthenticatorSignInAsync`). Successful TOTP or backup code on a locked account clears the lockout. The SPA inherits this: the no-token unlock page tells users in plain English that "valid TOTP codes are still accepted during lockout."

### Rule 4 — Enter submits, every form, every page

Every form on every Stage 9 page is a `<form onSubmit={handleSubmit(...)}>` with a `<button type="submit">` as its primary action. The shadcn `<Button>` defaults to `type="button"`, so the explicit `type="submit"` is required. A vitest case on every form fires `keyDown(Enter)` from the primary input and asserts the submit handler ran, so the rule is enforced by the test suite, not by promise.

The OTP input on `/login/totp` auto-submits on the 6th digit; Enter from any cell also submits because the surrounding form catches it. Modals (`<ReauthenticationDialog>`) follow the same rule; Escape closes the dialog.

### The matrix

| Trigger | Server response | SPA reaction |
|---|---|---|
| Wrong email/password on `/login` | `401 INVALID_CREDENTIALS` | Field error on password input; password input cleared, focus restored |
| Account locked on `/login` | `401 ACCOUNT_LOCKED_OUT` | Redirect to `/account/unlock` (no-token mode) |
| Email unconfirmed on `/login` | `401 EMAIL_NOT_CONFIRMED` (new in Stage 9) | Field error: "Verify your email first. Check your inbox." with inline "Resend" link |
| Wrong OTP / backup code on `/login/totp` | `401 INVALID_MFA_CODE` | Field error on the OTP input; input cleared, focus restored to first cell |
| Account already locked when reaching `/login/totp` | `401 ACCOUNT_LOCKED_OUT` | Redirect to `/account/unlock` |
| Expired/invalid token on `/email-verify`, `/password-reset/confirm`, `/account/unlock` | `401` with token-specific code | Page-level error block (no field to attach to) + "Request a new link" button |
| Wrong TOTP on `/password-reset/confirm` | `401 INVALID_MFA_CODE` | Field error on OTP input; **token NOT consumed** (server enforces this), so user can retry |
| Reauth required on a sensitive action | `401 REAUTH_REQUIRED` | Caught by `useStepUp`; opens `<ReauthenticationDialog>`; original action retried after success |
| Wrong password/OTP in reauth dialog | `401 INVALID_REAUTH` | Field error inside dialog; dialog stays open; user can cancel to abort the original action |
| Field validation failure (any form) | `422 VALIDATION_ERROR` with `details: [{field, message}]` | `setError(field, ...)` on `react-hook-form`; focus moves to first errored field |
| Business-rule validation failure | `422 VALIDATION_ERROR` with `details: []` | Page-level alert above the form; renders `error.message` |
| Rate-limit hit (any endpoint) | `429` with `Retry-After` header | Toast: "Too many attempts. Try again in N seconds." Submit button disabled and counts down inline; form state preserved |
| Network failure | (no response) | Toast: "Couldn't reach Ceres. Try again in a moment." Form state preserved |
| 5xx | `500` | Toast: "Something went wrong on our end. Try again in a moment." No stack trace in UI. Form state preserved |

### One design call documented here

The `EMAIL_NOT_CONFIRMED` error code on `/login` is **new in Stage 9**. Today the server returns `INVALID_CREDENTIALS` for unverified-email cases, lumping them with wrong-password cases. Splitting them out lets the SPA render the inline "Resend" link only when actually relevant. This is an additive change to the error-code table in `api-contract.md § Canonical error codes`; documenting it here so it doesn't look like an accidental introduction.

### Cross-cutting

- **Focus moves to first error.** `react-hook-form`'s `shouldFocusError: true` (the default) handles this; asserted in tests because the roadmap calls it out as an a11y requirement (line 1049).
- **`aria-live="polite"` on toasts.** `Sonner` handles this internally; verified in axe pass.

---

## Section 5 — Email wirings

Five new emails. Each ships with: enum value, EN + ES resx copy (3 keys × 2 languages = 6 entries per email), call-site wiring, and a `CapturingEmailService` test.

| Key | Trigger | Sent to | Contents | Call site |
|---|---|---|---|---|
| `RegistrationConfirmation` | New account registered | New email address | Welcome line + link to `/email-verify?token=...` (single-use, 30-min expiry) + "If this wasn't you, ignore this email." | `AuthController.Register`, after `tx.CommitAsync`, **only on the genuinely-new-account path** (NOT the duplicate-email anti-enumeration path) |
| `TotpEnrolled` | First-time TOTP enrolment succeeds | User's verified email | "Two-factor authentication enabled at {timestamp} from {IP}. **As part of setup, 10 backup codes were generated and shown to you once — keep them somewhere safe.** If this wasn't you, [revoke + change password]." | `MfaController.EnrollVerify`, on the branch where `wasTwoFactorEnabledBefore == false` |
| `TotpReEnrolled` | TOTP re-enrolment succeeds (already had TOTP, set up a fresh device) | User's verified email | "Two-factor authentication re-enrolled at {timestamp} from {IP}. Your old device no longer works. **A fresh set of 10 backup codes was generated; your previous backup codes are no longer valid.** If this wasn't you, [revoke + change password]." | `MfaController.EnrollVerify`, on the branch where `wasTwoFactorEnabledBefore == true` |
| `TotpDisabled` | User disables TOTP from `/security` | User's verified email | "Two-factor authentication disabled at {timestamp} from {IP}. Your account is now protected by password only. If this wasn't you, [re-enable + revoke sessions]." | New `MfaController.Disable` action |
| `BackupCodesRegenerated` | User regenerates backup codes (without re-enrolling TOTP) | User's verified email | "Backup codes regenerated at {timestamp} from {IP}. Your old backup codes no longer work. If this wasn't you, [change password]." | `MfaController.RegenerateBackupCodes` |

### Three design calls baked into the matrix

1. **`TotpEnrolled` and `TotpReEnrolled` are separate templates, not one parameterised template.** The events convey different things — first enrolment is "you turned on a security feature"; re-enrolment is "your previous TOTP device + backup codes are now invalid." Two templates, two distinct copy treatments. Tradeoff: one extra resx pair (six more lines).
2. **The duplicate-register path sends NO email.** Sending one — even something innocuous like "someone tried to register with your email" — itself leaks enumeration: an attacker types 1000 emails, sees who got a notification, knows which ones are real accounts. The "someone tried" notification is a future-stage feature gated on a per-email rate limiter that makes it safe.
3. **No separate `BackupCodesGenerated` email** for first-time generation. The codes appear on the screen during setup; `TotpEnrolled` copy explicitly mentions them ("10 backup codes were generated"). A separate email would arrive within a second of `TotpEnrolled` — inbox noise. Audit-trail value is preserved by the bolded sentence in `TotpEnrolled` and `TotpReEnrolled` copy.

### Tests

For each of the five, a test in the existing email-test suite asserts:
1. The email fires when the trigger condition is met.
2. The right `EmailTemplateKey` value is used.
3. The args include the data the template needs (timestamp, IP, link URL).
4. The body text contains the security-relevant phrases (the backup-codes-mention assertion lives here for `TotpEnrolled` and `TotpReEnrolled`).
5. **Negative assertion**: the duplicate-register path does NOT fire `RegistrationConfirmation`. This is the kind of test that catches enumeration regressions if a future contributor "helpfully" adds a notification.

---

## Section 6 — Testing

Three layers, each with a clear scope.

### Layer 1 — Server tests (xUnit)

| Surface | New tests | What they assert |
|---|---|---|
| `POST /api/auth/email/verify` | ~12 | Token issued on register and persisted with Argon2id hash; redeems exactly once (replay returns 401); expired returns clear error; unknown returns indistinguishable error; `EmailConfirmed` flips to true on success; rate-limit on `/verify` and on the resend endpoint; `RegistrationConfirmation` fires with correct args; duplicate-register path does NOT fire (negative); login returns the new `EMAIL_NOT_CONFIRMED` code when called against an unverified account (replaces the implicit `INVALID_CREDENTIALS` path) |
| `POST /api/auth/mfa/disable` | ~14 | Happy path disables TOTP, deletes seed + backup codes, regenerates security stamp; without recent reauth returns `401 REAUTH_REQUIRED`; audit log row written with `MfaDisabled`; `TotpDisabled` fires; rate-limit (`AuthMfaByUser`); CSRF token required; idempotency (calling twice while disabled returns clean error not 500); architecture test asserts `[RequireRecentAuth]` attribute present |
| `GET /api/auth/me` | ~6 | Returns expected shape when authenticated; returns 401 when anonymous; correct `twoFactorEnabled` flag; correct `backupCodesRemaining` count; `usedBackupCodeAtLastLogin` flag set by previous login; `Cache-Control: no-store` header |
| 4 existing-endpoint email wirings | ~12 | Each: email fires with correct template key; args include timestamp + IP + link URL; body text contains the security-relevant phrases (backup-codes-mention for the TOTP variants) |

All run against the existing `IntegrationTests` xUnit collection (one shared Postgres database). The lesson from `feedback_filter_test_queries_by_test_data` applies: every new test filters DB queries by a per-test marker (unique email suffix or marker UserId) — never an unfiltered `FirstAsync()` on a shared table.

Total: ~44 new server tests. The auth subset already takes 4–5 minutes by design (Argon2id at OWASP minimums); +44 fast-isolated tests pushes it toward 6 minutes — within budget.

### Layer 2 — Client tests (vitest + RTL)

Each page gets two test files. Behaviour (`*.test.tsx`) and accessibility (`*.a11y.test.tsx` running `expectNoA11yViolations()`).

| Surface | Key assertions |
|---|---|
| `Login.tsx` | Submit on Enter; field error on wrong creds; redirect to `/login/totp` on `requiresTotp`; redirect to `/account/unlock` on lockout; "Resend" link appears on `EMAIL_NOT_CONFIRMED`; tab order email → password → submit → forgot → register |
| `LoginTotp.tsx` | Auto-advance through cells; auto-submit on 6th digit; paste 6 digits works; backup-code toggle switches input mode; field error on wrong code; OTP cells have `font-size: 16px` and `inputMode="numeric"` (iOS no-zoom guardrail) |
| `Register.tsx` | Strength meter updates per keystroke; cross-field password match enforced before submit; submit always navigates to `/email-verify` regardless of duplicate (no enumeration); Enter submits |
| `EmailVerify.tsx` | Interstitial mode renders `?email=...`; token mode calls verify endpoint; expired token shows "Request a new link"; resend respects rate-limit countdown |
| `PasswordReset.tsx` | Always renders generic confirmation; Enter submits |
| `PasswordResetConfirm.tsx` | TOTP field appears only when `requiresTotp`; wrong TOTP keeps token valid; password-confirm match check; Enter submits |
| `AccountUnlock.tsx` | Token-mode calls unlock endpoint; no-token-mode renders the static info copy with the "TOTP still works during lockout" sentence |
| `TotpSetup.tsx` (under `/security/`) | Multi-step wizard advances only after each step is satisfied; QR renders ≥ 240px; backup codes download triggers a Blob anchor; copy-to-clipboard fallback works; "I've saved them" checkbox gates the final step |
| `ReauthenticationDialog.tsx` | Renders password input when `twoFactorEnabled=false`; OTP input when `true`; Enter inside dialog submits; Escape cancels and aborts the original action; wrong creds keeps dialog open with field error |
| `BackupCodeBanner.tsx` | Visible only when `usedBackupCodeAtLastLogin && backupCodesRemaining < 10`; copy reflects count; persistent (no dismiss button) |
| Foundation: `apiFetch` + CSRF helper | CSRF handshake fires once and dedupes concurrent calls; `X-XSRF-TOKEN` header attached to non-GET requests; 422 with `details.length > 0` maps to field errors; 422 with `details: []` maps to page-level alert; 401 `REAUTH_REQUIRED` throws `ReauthRequiredError`; network failure throws typed error |
| Foundation: `useAuth` + `useStepUp` | `useAuth().refresh()` re-fetches `/api/auth/me`; `useStepUp().requireStepUp(action)` opens dialog on `REAUTH_REQUIRED`; modal closes + retries on success; modal closes + aborts on cancel |
| Foundation: i18n parity | Every key in `en.json` exists in `es.json` (catches missing translations at test time, not on a user's screen) |
| Foundation: `<Button type=...>` lint guardrail | Asserts every `<Button>` inside a `<form>` in the auth pages has explicit `type="submit"` or `type="button"` |

Total: ~40–50 new client tests across ~25 files. Vitest is fast; full suite stays well under a minute.

### Layer 3 — Manual UX checklist

Gated to **before stage close-out**, not per commit:

- **Cookie attributes in DevTools** (the Stage 6 carry-forward) — log in; DevTools → Application → Cookies; confirm `__Host-Session` has HttpOnly + Secure + SameSite=Lax + Path=/ + no Domain.
- **iOS Safari OTP no-zoom** — OTP cells must not trigger viewport zoom on focus. Chrome devtools mobile emulation does NOT catch this; needs a real iPhone or iOS Safari.
- **Backup-codes download on Safari iOS** — `Blob` + `URL.createObjectURL` + `<a download>` sometimes opens in a new tab on iOS instead of downloading. The clipboard-fallback button is the safety net; verify it on a real device.
- **Mobile 375px walkthrough** — every page at 375px: no horizontal scroll; touch targets ≥ 44 × 44 px; language toggle reachable without scrolling; QR code visibly large enough to scan with a phone.
- **Language toggle smoke test** — toggle EN ↔ ES on every auth page; verify nothing renders untranslated; refresh the page; verify the cookie persisted the choice.
- **Dark mode walkthrough** — every auth page in dark mode; verify card chrome, error states, OTP cells, and the QR code (which should stay light-on-white inside a white panel because TOTP scanners need contrast) all read correctly.
- **End-to-end happy path tour** — register → verify → login → enable TOTP → log out → log back in with TOTP → disable TOTP → log out → log back in without TOTP → password reset → log in again. Roughly 5–10 minutes; surfaces interaction bugs no individual test would catch.

---

## Section 7 — Sequencing

Eighteen commits in four phases. Per the project's TDD rule (`docs/testing.md` § Rules): tests first on every commit. The `verify` skill runs before each commit closes.

### Phase 1 — Foundations and the vertical slice (6 commits)

The goal: get `/login` in front of the user in a real browser, with the language toggle working, before building anything else.

| # | Commit | What lands | Doc sync in same commit |
|---|---|---|---|
| 1 | server: add `GET /api/auth/me` | New endpoint returning `{userId, email, twoFactorEnabled, lastReauthAt, backupCodesRemaining, usedBackupCodeAtLastLogin}`; xUnit tests | None (server-only) |
| 2 | client: i18n + `lang` cookie | `react-i18next` deps via `pnpm --dir ProjectCeres.Client add ...`; `src/app/i18n/i18n.ts`; `en.json` + `es.json` skeleton; server `LanguagePreferenceMiddleware`; parity test | `planning-phase3-spa-migration.md` § 8 (Razor view retirement timing for the bilingual middleware) |
| 3 | client: `<AuthLayout>` + routing split | `AuthLayout.tsx`; `RequireAuth.tsx`; `App.tsx` reorganised into public + protected branches; AuthLayout a11y test | `planning-phase3-spa-migration.md` § 2 (route map gains `/login`, `/login/totp`, etc.) |
| 4 | client: API client + auth context | `apiFetch()` with CSRF handshake; `<AuthProvider>` + `useAuth()` + `useStepUp()`; `ReauthRequiredError` typed class; api-client + auth-context tests | None |
| 5 | client: form library + OTP + QR + ADR-0074 | `react-hook-form`, `zod`, `@hookform/resolvers`, `qrcode.react` via `pnpm --dir ProjectCeres.Client add`; `pnpm dlx shadcn add input-otp`; ADR-0074 written | `docs/decisions/ADR-0074-react-hook-form-with-zod-for-spa-forms.md` (new); `planning-phase3.md` § 14 implementation order updated |
| 6 | client: `/login` + globe language toggle + roadmap line 977 rewrite | `pages/auth/Login.tsx` with `react-hook-form` + zod; behaviour + a11y tests; `<LanguageToggle>` mounted in AuthLayout footer; the locked design call gets pinned in the roadmap | `roadmap-phase-three.md` line 977 rewritten; checklist items 967–976 ticked; `planning-phase3-spa-migration.md` § 8 updated |

**End of Phase 1: pause for UX review.** User opens `https://localhost:.../login` in a real browser; walks golden + edge paths on desktop + 375px mobile; toggles the language; looks at dark mode. If the design or the form pattern needs revision, rework lands here, not after eight more pages.

### Phase 2 — Email verify and the register flow (3 commits)

| # | Commit | What lands | Doc sync in same commit |
|---|---|---|---|
| 7 | server: `POST /api/auth/email/verify` + register flow change + `RegistrationConfirmation` email | New `EmailVerifyTokens` table + EF migration; new endpoint; register issues the verify token + sends the email on the genuinely-new path only (`EmailConfirmed` stays `false`, the Identity default, until the user clicks the link); login still falls through `RequireConfirmedEmail` and now maps the not-allowed result to a new error code `EMAIL_NOT_CONFIRMED` (split out from `INVALID_CREDENTIALS`); ~12 xUnit tests; `RegistrationConfirmation` enum value + EN + ES resx copy; rate-limits use existing `EmailByUser` + `EmailByIp` policies | `roadmap-phase-three.md` line 1078 ticked; `models.md` updated with new table; `api-contract.md § Canonical error codes` gains `EMAIL_NOT_CONFIRMED` row |
| 8 | client: `/register` page | `pages/auth/Register.tsx` with strength meter + cross-field match; behaviour + a11y tests; updated `Login.tsx` to render inline "Resend" link on `EMAIL_NOT_CONFIRMED` | Lines 992–994 ticked |
| 9 | client: `/email-verify` page | `pages/auth/EmailVerify.tsx` (interstitial + token modes); rate-limited resend; behaviour + a11y tests | Lines 995–996 ticked |

### Phase 3 — Recovery and the rest of the auth pages (3 commits)

| # | Commit | What lands | Doc sync in same commit |
|---|---|---|---|
| 10 | client: `/login/totp` page | OTP via shadcn `input-otp`; backup-code toggle on the same page; auto-advance + auto-submit; behaviour + a11y tests; iOS no-zoom guardrail test | Lines 982–989 ticked |
| 11 | client: `/password-reset` + `/password-reset/confirm` | Both pages; conditional TOTP field on confirm; wrong-TOTP-doesn't-consume-token honoured in tests; behaviour + a11y tests | Lines 998–1005 ticked |
| 12 | client: `/account/unlock` | Token-mode + no-token-mode; behaviour + a11y tests; copy includes the "TOTP still works during lockout" sentence | Lines 1009–1012 ticked |

### Phase 4 — MFA management, reauth, dashboard banner, doc sweep (6 commits)

| # | Commit | What lands | Doc sync in same commit |
|---|---|---|---|
| 13 | server: `POST /api/auth/mfa/disable` + 4 MFA email wirings | New `MfaController.Disable` action with `[RequireRecentAuth]` + `AuthMfaByUser` rate limit; `EnrollVerify` branches first-vs-re-enrol email; `RegenerateBackupCodes` sends notification; four enum values + 24 resx entries (4 keys × 3 fields × 2 languages); ~14 xUnit tests for `Disable` + ~12 for the email wirings; the `// wired when an MFA-disable endpoint ships` dead comment in `Models/AuditLog.cs:31` removed in the same commit | Roadmap lines 1079–1082 ticked; **ADR-0075** (`TotpReEnrolled` as distinct template) — recommended |
| 14 | client: `<ReauthenticationDialog>` | Modal driven by `useStepUp()`; behaviour + a11y tests; wired into existing change-password / change-email Settings flows + the new TOTP-disable button | Lines 1031–1034 ticked |
| 15 | client: `/security/totp/setup` | Multi-step wizard: enroll → verify → backup-codes; QR via `qrcode.react`; download (.txt) + clipboard fallback; "I've saved them" gate; behaviour + a11y tests; "Enable two-factor" button on `/security` routes here | Lines 1014–1022 ticked; roadmap line 1014 references updated from `/login/totp/setup` to `/security/totp/setup` |
| 16 | client: backup-code recovery polish | `<BackupCodeBanner>` mounted in protected layout; appears when `usedBackupCodeAtLastLogin && backupCodesRemaining < 10`; persistent; behaviour test | Lines 1025–1028 ticked |
| 17 | manual: cookie DevTools verify (Stage 6 carry-forward) | No code; user runs the DevTools check and confirms in conversation. Roadmap line 516 ticked in a docs-only commit | `roadmap-phase-three.md` line 516 ticked |
| 18 | doc sync sweep | Every remaining `[ ]` under Stage 9 either ticked or moved to a receiving stage's checklist (per `feedback_finished_stages_have_no_unchecked_items` and `feedback_deferral_requires_receiving_stage_checkbox`); `planning-phase3-spa-migration.md` §§ 2 + 8 fully reflect eight new public routes + one moved protected route; `planning-phase3.md` § 14 updated; Stage 9 marked ✅ Done | Stage 9 close-out |

### What this ordering buys

- **Vertical-slice safety.** Phase 1 ends at a real working `/login` page, not at the end of all foundations. If anything in the foundation is wrong (CSRF handshake misbehaves, `<Field>` doesn't compose with `react-hook-form` cleanly, the AuthLayout renders weirdly on iOS), it surfaces against one page, not nine.
- **No backend-blocking-frontend cliffs.** The two new server endpoints are introduced one phase at a time, each immediately followed by the SPA pages that consume them.
- **Doc sync is per-commit, not big-bang.** Final sweep (commit 18) is small because the work has been spread across the stage.
- **Carry-forwards land in the right places.** Stage 6 manual cookie check (commit 17) lands once a real login UI exists. The five Stage 8 email wirings land alongside the endpoints they trigger from (commit 7 for `RegistrationConfirmation`, commit 13 for the four MFA emails).
- **The "log for later" rule is honoured.** Anything in Stage 9 that genuinely can't ship gets a matching `[ ]` line in the receiving stage's checklist in commit 18 — never left as an unticked box under a Done banner.

---

## Convention conformance (verified pre-spec)

The following project conventions were verified before this spec was written (see the `verify-against-codebase` pre-flight earlier in the brainstorm session):

- HTTP status codes match `docs/api-contract.md § HTTP status codes`. Lockout returns **401**, not 423. Validation returns **422** via `Program.cs`'s `InvalidModelStateResponseFactory`.
- Error envelope is the project's `{ error: { code, message, details? } }` shape — NOT ASP.NET `ProblemDetails`. 422s have a dual-shape `details` contract (field-validation array vs business-rule empty array).
- Cookie names match `SessionConstants.cs` literals: `__Host-Session`, `__Host-Persist`, `__Host-XSRF`, `Mfa.RememberMe`. CSRF header name is `X-XSRF-TOKEN` (`SessionConstants.CsrfHeaderName`).
- Rate-limit policies use existing names from `AuthRateLimitPolicies.cs`: `EmailByUser` + `EmailByIp` for new email-triggering endpoints; `AuthMfaByUser` for the new disable endpoint.
- Auth-error envelope helper is `UnauthorizedEnvelope("CODE", "message")`, not raw `Unauthorized()`.
- shadcn baseline is `style: base-nova`, `cssVariables: true`, alias `@/components/ui` per `components.json`. All needed primitives (Card, Button, Input, Label, Dialog, AlertDialog, Tabs, Switch, Sonner, DropdownMenu) already exist.
- Endpoints stay on `/api/auth/...`; `/api/v1/` versioning prefix migration ships with Stage 11 per `docs/api-contract.md:455`.
- Token-table naming follows `LockoutUnlockTokens` / `EmailChangeTokens` / `PasswordResetTokens` convention → new table is `EmailVerifyTokens` (plural).
- All package management goes through `pnpm`. `npm`, `npx`, `yarn` are forbidden in this codebase per `feedback_pnpm_only_never_npm` (security concerns with npm's supply-chain implementation).
