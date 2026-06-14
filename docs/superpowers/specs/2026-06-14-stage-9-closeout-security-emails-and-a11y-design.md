# Stage 9 close-out batch — security-event emails + a11y/error-state fixes (design)

**Date:** 2026-06-14
**Status:** Draft — pending user review
**Closes Stage 9 boxes:** security-event emails (roadmap 1084–1087), locked-account (1047), network error (1048), server-500 (1049), aria-describedby (1053), focus-to-first-error (1054), vitest-axe coverage (1058). ~10 of the 13 remaining; after this, only tablet/desktop responsive (manual browser) + date/time-locale (N/A) remain.

**Locked decisions (user, 2026-06-14):** plain-advisory email bodies pointing at `/app/password-reset` (no new endpoint); reuse `TotpEnrolled` for both first-enrol and re-enrol (3 templates, not 4); locked-account = inline message, drop the `/account/unlock` redirect.

**Verify-against-codebase corrections folded in** (workflow `wf_9dce8f5b-5bc`, 2026-06-14) — cited inline.

---

## Half A — 3 security-event email templates (backend)

### A1. The three templates

`TotpEnrolled`, `TotpDisabled`, `BackupCodesRegenerated`. Plain-advisory notifications fired **after** the MFA operation commits. Body shape (EN, ES mirrors): *"Two-factor sign-in was [enabled / turned off / had its backup codes regenerated] on your Ceres account on {0} from IP {1}. If this wasn't you, reset your password immediately: https://localhost/app/password-reset (the link is baked into the resx body, not an arg)."* Subject e.g. "Two-factor sign-in enabled on your Ceres account".

`TotpEnrolled` is sent from `EnrollVerify` for **both** first-time and re-enrolment (the endpoint doesn't distinguish, and the user-facing event "MFA was set up" is the same).

### A2. Registry chain — every link, in one change (the multi-registry discipline)

1. **`EmailTemplateKey` enum** (`ProjectCeres/Common/Email/EmailTemplateKey.cs`) — add `TotpEnrolled`, `TotpDisabled`, `BackupCodesRegenerated`.
2. **`EmailComposer.KeysFor` switch** (`EmailComposer.cs:54-77`) — exhaustive switch with a throwing default; add 3 arms referencing `EmailKeys.TotpEnrolled.*` etc.
3. **EN + ES resx** (`ProjectCeres/Resources/EmailsResource.en.resx` + `.es.resx`) — `Subject`/`BodyText`/`BodyHtml` triplet × 3 templates × 2 locales = **18 entries**. **CER020 (resx EN/ES parity) is at `error` severity** — any key missing from one locale fails `dotnet build`. Both locales must land together.
4. **`EmailKeys.*` constants** — regenerated automatically by `EmailKeysGenerator` from the resx; **no manual edit** (a rename would be a `CS0117` compile error, which is the point).
5. **`MfaController`** (`ProjectCeres/Controllers/Api/MfaController.cs`) — currently injects only `UserManager` + `IAuditLogWriter`. Add `IEmailComposer`, `IEmailService`, `IEmailRecipientResolver`, `ILanguageResolver`, and **`TimeProvider`** (not currently in its ctor) to the constructor.

### A3. Send wiring — mirror the *block*, not the service

**Mirror only the send block** from `EmailConfirmationService.IssueAsync` (`EmailConfirmationService.cs:136-148`):
```
try {
  var recipient = await _recipients.ResolveAsync(userId, ct);
  var culture   = await _languages.ResolveForUserAsync(userId, ct);
  var msg = _composer.Compose(EmailTemplateKey.TotpEnrolled, culture, timestamp, ip) with { To = recipient };
  await _email.SendAsync(msg, ct);
} catch (Exception ex) {
  _logger.LogError(ex, "Failed to send TOTP-enrolled notification; MFA enrolment already committed.");
}
```
Do **NOT** mirror `EmailConfirmationService`'s enclosing machinery — it is `[PreAuthScope]` + `[RequiresAdminContext]` with token-issue writes under a `PreAuthUserScope`, per-user semaphores, and `[RlsBypassJustified]`. **None of that applies here**: `MfaController` is class-level `[Authorize]` + per-action `[RequireRecentAuth]` (`MfaController.cs:13-16,28,52,87,112`) — fully in-session, the RLS GUC is already set for the request, so **no `PreAuthUserScope`, do not mark the class `[PreAuthScope]`** (doing so would trip CER001/CER006).

**Placement:** the send fires after the action's existing success work + audit-log row (`MfaController.cs:79,99,127` already write `_auditLog.RecordAsync` — do **not** duplicate it in the email path). Wire into `EnrollVerify` (→ `TotpEnrolled`), `Disable` (→ `TotpDisabled`), `RegenerateBackupCodes` (→ `BackupCodesRegenerated`).

**Args:** `Compose(key, culture, arg0, arg1)` maps positionally to resx `{0}`/`{1}` (confirmed via `LockoutUnlock_args_map_to_correct_slots`). Pass `{0}` = a **pre-formatted** timestamp string (`Compose` does not format a `DateTime` — caller formats from `_timeProvider.GetUtcNow().UtcDateTime` per CER004), `{1}` = `HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""` (AuthController.cs:430 pattern). HTML body HTML-encodes args; subject/text strip CR/LF — both fine for a timestamp + IP.

### A4. Backend tests (ship-gate)

- **`EmailComposerTests.cs` — two existing tests MUST be updated same-change** (they break otherwise):
  - `All_resx_keys_present_in_both_cultures` (`:104-126`): `HaveCount(30)` → **`HaveCount(39)`**, comment "10 templates" → "13".
  - `Renders_all_nine_templates_en_and_es` (`:36`): add `[InlineData]` rows (EN+ES) for the 3 new templates. **Also fix the pre-existing gap**: this test already omits `RegistrationConfirmation` — add its row too while here, so the test name's "templates" claim is honest (rename to match the new count).
- **New composer test** `TotpEnrolled_args_map_to_correct_slots` (mirror `LockoutUnlock_args_map_to_correct_slots`): `{0}`=timestamp, `{1}`=IP land in the right sentence positions, EN + ES.
- **MfaController send tests** (integration, mock `IEmailService` captures): `EnrollVerify` sends `TotpEnrolled`; `Disable` sends `TotpDisabled`; `RegenerateBackupCodes` sends `BackupCodesRegenerated`; each to the acting user's address. Plus one negative: a thrown `IEmailService.SendAsync` does **not** fail the MFA action (the try/catch swallows) — assert the action still returns its success status.
- **Untouched:** `IEmailService_impls_are_LogOnly_Noop_or_Resend` (checks impls, not templates). No `EmailTemplateKey` documented-set test exists.

---

## Half B — a11y + error-state fixes (frontend)

Routed through `frontend-orchestrator` → **Phase 5 (harden)**: primary tool `web-design-guidelines` for a11y conformance, no design-system extension (all items reuse existing primitives: `Field`, sonner `toast`, i18n keys). Verification gate = `web-design-guidelines` + the vitest-axe suites + the UX/UI checklist (handed to the user with URLs, since no browser here).

### B1. `aria-describedby` for field errors — inherently two-touch

`Field` (`src/app/components/Field.tsx`) renders the control via `{children}` passthrough (line 34, no `cloneElement`) and owns the error `<p>` (line 35, **no id today**). So a complete fix is two parts:
- **Field (one file):** give the error `<p>` an `id` derived from the field — `id={`${htmlFor}-error`}` — and render it only when `error` is present.
- **Each auth call site:** add `aria-describedby={`${id}-error`}` to its `<Input>` (conditionally, when an error is present — matches the existing per-call-site precedent at `Register.tsx:109,112` for `password-hint`). Call sites: `Login`, `Register`, `PasswordReset` (both forms), `LoginTotp` (backup-code input), `EmailVerify`, `AccountUnlock`.

### B2. Focus-to-first-error on server rejection

RHF's auto-focus only fires for client (zod) validation. After a server `form.setError(field, {type:'server'})`, call `form.setFocus(field)` (RHF 7.75 exposes it; used nowhere yet — new correct addition). Apply on the server-rejection paths in `Login`, `Register`, `PasswordReset` where `setError` is called with a field target.

### B3. Locked-account inline message — drop the redirect

`Login.tsx:93-96` currently `navigate('/account/unlock')` on `ACCOUNT_LOCKED_OUT`. Replace with an inline form-level message using the **already-authored but dead** key `auth.login.errors.accountLocked` ("Account locked. Check your email for an unlock link." / ES mirror). The unlock link still arrives by email; the user stays oriented on the login form.
- **`Login.test.tsx:172`** (`redirects to /account/unlock on ACCOUNT_LOCKED_OUT`) **WILL break** — this is a legitimate test rewrite (premise changed), not a skip: rewrite it to assert the inline `accountLocked` message renders and that navigation does **not** occur.
- **Out of scope, leave alone:** `LoginTotp.tsx:99` + `LoginTotp.test.tsx:117` do the same redirect on the TOTP path — that's a separate surface, not this change.

### B4. Login network/500 — two distinct branches (the real bug)

Today `Login.tsx` surfaces "Email or password is incorrect" for both a network blip and a 500. The two arrive on **different paths** (verify finding Q2):
- **Network:** `NetworkError` is *thrown* from `apiFetch` (`api-client.ts:56-63,163-165`) → caught in Login's `try/catch` (line 115-119). Branch: `if (err instanceof NetworkError)` → retry-able **sonner `toast`** (`auth.login.errors.network`), form values preserved (RHF keeps them).
- **5xx:** a 500 is *returned* (not thrown) as `{ ok:false, status:500, code:'HTTP_500', message }` (`api-client.ts:200,227`) → flows through the **failure path** and currently hits the catch-all `setError('password', invalidCredentials)` at line 114. Branch: `if (result.code === 'HTTP_500' || result.status >= 500)` → generic form-level message (`auth.login.errors.server`, "Something went wrong. Please try again."), **not** the password field.
- **New i18n keys** (no shared `errors.*`/`common.*` namespace exists — both are `{}`): add `auth.login.errors.network` + `auth.login.errors.server` to **both** `en.json` and `es.json` (i18n key-parity discipline). Copy mirrors the existing per-page `network` keys (e.g. "Couldn't reach the server. Try again.").

### B5. vitest-axe coverage

Add `Register.a11y.test.tsx` + `PasswordReset.a11y.test.tsx`, mirroring `Login.a11y.test.tsx` exactly: provider stack (`ThemeProvider > I18nextProvider > AuthProvider > MemoryRouter > Routes > Route element={<AuthLayout/>}`), `beforeEach` fetch-spy → 401 UNAUTHENTICATED (so `AuthProvider`'s `/api/auth/me` probe resolves anon), `afterEach vi.restoreAllMocks()`, `await expectNoA11yViolations(container)` (helper at `src/app/lib/test-axe.ts`, fails on serious/critical). `PasswordReset` has **two** form states (request + confirm) — confirm mounts on a `#token=` hash, so add a second test with `initialEntries` carrying a token (mirrors `LoginTotp.a11y.test.tsx`'s two-state pattern + its 20_000ms per-test timeout).

### B6. Frontend tests
- `Field` aria-describedby association test (error `<p>` has the derived id; input points at it when error present).
- Login network test (`NetworkError` → toast, not password error); Login 500 test (`HTTP_500` → server message, not invalidCredentials).
- Login locked-account inline test (rewrite of `Login.test.tsx:172`).
- focus-to-first-error test (server `setError` → that field receives focus).
- The two new a11y suites.

---

## Out of scope / not touched
- Reauth dialog (9.8) — already moved to Stage 12.9.
- The `/account/unlock` **page** and its tests, and the LoginTotp redirect — unchanged.
- No new endpoint, no new `IEmailService` impl, no schema/migration, no new design-system token or primitive.

## Ship gates
- `dotnet build` (CER020 resx-parity + CER004 all green), `dotnet test` (the updated + new composer/MfaController tests), `pnpm build`, `pnpm test` (new a11y suites + Field/Login tests) — all exit 0.
- `web-design-guidelines` audit on the changed auth files clean.
- UX/UI verification checklist handed to the user with URLs (no browser in this session).
- Roadmap boxes 1047/1048/1049/1053/1054/1058/1084–1087 ticked at close-out (Phase E), with `sync-docs` + `changelog-sync` fired.
