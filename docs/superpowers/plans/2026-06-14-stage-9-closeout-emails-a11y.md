# Stage 9 close-out batch — Implementation Plan (security-event emails + a11y/error fixes)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Wire 3 security-event notification emails (TotpEnrolled / TotpDisabled / BackupCodesRegenerated) into MfaController, and fix 5 a11y/error-state gaps on the auth SPA. Closes ~10 of the 13 remaining Stage 9 boxes.

**Architecture:** Emails follow the existing template-registry chain (enum → `EmailComposer.KeysFor` → EN/ES resx → generated `EmailKeys.*` → in-controller send). MfaController is in-session (`[Authorize]`), so the send needs no `PreAuthUserScope`. Frontend fixes are targeted edits to existing primitives (`Field`, `Login`, sonner toast, RHF `setFocus`) — no new design-system tokens.

**Tech Stack:** ASP.NET Core / EF, .NET 10; React 19 + RHF 7.75 + vitest + vitest-axe.

**Spec:** `docs/superpowers/specs/2026-06-14-stage-9-closeout-security-emails-and-a11y-design.md`. **Branch:** `stage-9-closeout-emails-a11y` (spec already committed `cdb482a`).

## Key facts (verified, do not rediscover)
- **MfaController** (`ProjectCeres/Controllers/Api/MfaController.cs`) is `sealed`, class-level `[ApiController][Route("api/auth/mfa")][Authorize]`, each action `[RequireRecentAuth]`. Constructor injects only `UserManager<ApplicationUser>` + `IAuditLogWriter`; `MfaBackupCodeService` comes per-action via `[FromServices]`. **No PreAuthUserScope, do not mark `[PreAuthScope]`.** Cancellation token is always `HttpContext.RequestAborted` (no `ct` param on actions).
- The 3 actions end with: `await _auditLog.RecordAsync(user.Id, AuditLogAction.X, ct: HttpContext.RequestAborted);` then `Response.ApplyNoStore();` then `return ...`. EnrollVerify → `Ok(new { backupCodes = codes })`; RegenerateBackupCodes → same; Disable → `NoContent()`.
- **Send-block precedent** (`EmailConfirmationService.cs:136-148`): `try { recipient = await _recipients.ResolveAsync(userId, ct); culture = await _languages.ResolveForUserAsync(userId, ct); msg = _composer.Compose(KEY, culture, args...) with { To = recipient }; await _email.SendAsync(msg, ct); } catch (Exception ex) { _logger.LogError(ex, "..."); }`.
- **resx `<data>` shape** (`EmailsResource.en.resx`): `<data name="LockoutUnlock.BodyText" xml:space="preserve"><value>...{0}...{1}...</value></data>`. `{0}`=arg0, `{1}`=arg1, bound by index regardless of reading order. HTML escapes `&lt;`/`&gt;`/`&amp;` inside `<value>`. ES mirror has accented UTF-8 — preserve exactly.
- **`EmailComposer.KeysFor`** arm shape (two lines): `EmailTemplateKey.<Name> =>` then 12-space `(EmailKeys.<Name>.Subject, EmailKeys.<Name>.BodyText, EmailKeys.<Name>.BodyHtml),` — insert before the `_ => throw` default. `EmailKeys.<Name>` is **generated** by `EmailKeysGenerator` from the resx — never hand-author it.
- **CER020** (resx EN/ES parity) is `error` severity — every new key in BOTH locales or `dotnet build` fails. **CER004** — timestamps via `_timeProvider.GetUtcNow().UtcDateTime`, never `DateTime.UtcNow`.
- **Two EmailComposerTests break** (`ProjectCeres.Tests/Integration/Email/EmailComposerTests.cs`): `All_resx_keys_present_in_both_cultures` asserts `HaveCount(30, "10 templates × 3 keys each")` → becomes **39 / "13 templates"**; `Renders_all_nine_templates_en_and_es` has 18 `[InlineData]` rows (9 keys × 2 cultures, omits RegistrationConfirmation) → add rows for the new templates.
- **Field.tsx** passes `{children}` through (no `cloneElement`), owns the error `<p>` (no id today). aria-describedby fix is two-touch: Field adds an id to its error `<p>`; each call site adds `aria-describedby` to its `<Input>`.
- **Login network vs 500**: `NetworkError` is *thrown* (caught in try/catch); a 500 is *returned* as `{ code:'HTTP_500', status:500 }` (hits the failure path, currently the catch-all). Two separate branches needed.
- **Dropping `/account/unlock` redirect breaks `Login.test.tsx:172`** — rewrite that test (premise changed), don't skip. `LoginTotp`'s identical redirect is out of scope.
- **i18n**: no shared `errors.*`/`common.*` namespace (both `{}`). `auth.login.errors` has invalidCredentials/accountLocked/emailNotConfirmed/resendFailed/resendTooMany — no network/server. Add `auth.login.errors.network` + `.server` to BOTH `en.json` and `es.json`. `accountLocked` already exists in both (currently dead).
- **a11y test helper**: `expectNoA11yViolations(container)` at `src/app/lib/test-axe.ts` (fails on serious/critical). Mirror `Login.a11y.test.tsx` provider stack + fetch-spy. PasswordReset needs two states (request + confirm-on-`#token=`); use `LoginTotp.a11y.test.tsx`'s two-state + 20_000ms-timeout pattern.

## File map
**Backend:** `ProjectCeres/Common/Email/EmailTemplateKey.cs` · `EmailComposer.cs` · `Resources/EmailsResource.en.resx` + `.es.resx` · `Controllers/Api/MfaController.cs` · tests `ProjectCeres.Tests/Integration/Email/EmailComposerTests.cs` + `ProjectCeres.Tests/Integration/Authentication/Mfa/Mfa*Tests.cs`.
**Frontend:** `ProjectCeres.Client/src/app/components/Field.tsx` · `pages/auth/Login.tsx` (+ Register.tsx, PasswordReset.tsx, LoginTotp.tsx, EmailVerify.tsx, AccountUnlock.tsx for aria-describedby) · `i18n/locales/{en,es}.json` · tests `pages/auth/{Login.test,Login.a11y.test,Register.a11y.test,PasswordReset.a11y.test}.tsx` + a Field test.

---

## Phase 1 — Backend: 3 security-event emails

### Task 1: Add the 3 resx triplets (EN + ES) — the build-gating registry layer first

**Files:** Modify `ProjectCeres/Resources/EmailsResource.en.resx`, `EmailsResource.es.resx`

- [ ] **Step 1: Add 3 EN triplets.** In `EmailsResource.en.resx`, after the `RegistrationConfirmation.BodyHtml` entry, add 9 `<data>` entries (Subject/BodyText/BodyHtml for each of TotpEnrolled, TotpDisabled, BackupCodesRegenerated). Args: `{0}` = formatted timestamp, `{1}` = IP. The password-reset link is **baked into the body literally** (not an arg), pointing at `https://localhost/app/password-reset` — actually use a relative-safe absolute the email client can render; since these emails have no per-request base URL passed, hardcode the production-style path text `/app/password-reset` in prose (the user reaches it by navigating). Example (TotpEnrolled):
```xml
  <data name="TotpEnrolled.Subject" xml:space="preserve"><value>Two-factor sign-in enabled on your Ceres account</value></data>
  <data name="TotpEnrolled.BodyText" xml:space="preserve"><value>Two-factor sign-in was enabled on your Ceres account on {0} (UTC) from IP {1}.

If this wasn't you, your account may be compromised — reset your password immediately from the Ceres sign-in page (Forgot password) and review your account security.</value></data>
  <data name="TotpEnrolled.BodyHtml" xml:space="preserve"><value>&lt;p&gt;Two-factor sign-in was &lt;strong&gt;enabled&lt;/strong&gt; on your Ceres account on {0} (UTC) from IP &lt;strong&gt;{1}&lt;/strong&gt;.&lt;/p&gt;&lt;p&gt;If this wasn't you, your account may be compromised — reset your password immediately from the Ceres sign-in page (Forgot password) and review your account security.&lt;/p&gt;</value></data>
```
TotpDisabled: "Two-factor sign-in was **turned off**…" (Subject "Two-factor sign-in turned off on your Ceres account"). BackupCodesRegenerated: "Your Ceres backup codes were **regenerated**… any previous codes no longer work." (Subject "Your Ceres backup codes were regenerated"). All three carry the same `{0}`/`{1}` + "if this wasn't you, reset your password" advisory.

- [ ] **Step 2: Add the 3 ES triplets** to `EmailsResource.es.resx` — same 9 keys, Spanish copy (mirror LockoutUnlock's tone; preserve accented UTF-8). Subjects e.g. "Verificación en dos pasos activada en tu cuenta de Ceres", etc. Same `{0}`/`{1}` placeholders.

- [ ] **Step 3: Verify resx parity compiles (CER020).**
Run: `dotnet build ProjectCeres`
Expected: Build succeeded. If CER020 fires, a key is missing from one locale — fix before proceeding. (No C# consumes the keys yet; that's fine — resx + generator only.)

- [ ] **Step 4: Commit.**
```bash
git add ProjectCeres/Resources/EmailsResource.en.resx ProjectCeres/Resources/EmailsResource.es.resx
git commit -m "feat(9): 3 security-event email resx triplets (TotpEnrolled/Disabled/BackupCodesRegenerated) EN+ES

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

### Task 2: Enum + composer arms (tests-first on the composer)

**Files:** Modify `EmailTemplateKey.cs`, `EmailComposer.cs`, `EmailComposerTests.cs`

- [ ] **Step 1: Update the two breaking composer tests (red first).** In `ProjectCeres.Tests/Integration/Email/EmailComposerTests.cs`:
  - `All_resx_keys_present_in_both_cultures`: change `HaveCount(30, "10 templates × 3 keys each (RegistrationConfirmation added in Stage 9.3)")` → `HaveCount(39, "13 templates × 3 keys each (3 security-event emails added Stage 9 close-out)")`.
  - `Renders_all_nine_templates_en_and_es`: rename to `Renders_all_templates_en_and_es`; add 6 `[InlineData]` rows (TotpEnrolled/TotpDisabled/BackupCodesRegenerated × "en"/"es"). Also add the 2 missing RegistrationConfirmation rows while here (the test pre-existingly omitted it) so the name is honest.

- [ ] **Step 2: Run — verify FAIL.**
Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~EmailComposerTests"`
Expected: FAIL — the new enum values / InlineData reference templates that don't exist yet (compile error on `EmailTemplateKey.TotpEnrolled`, or HaveCount mismatch).

- [ ] **Step 3: Add enum values.** In `ProjectCeres/Common/Email/EmailTemplateKey.cs`, append before the closing brace (match trailing-comma style):
```csharp
    TotpEnrolled,
    TotpDisabled,
    BackupCodesRegenerated,
```

- [ ] **Step 4: Add composer arms.** In `EmailComposer.cs` `KeysFor`, before the `_ => throw` default, add 3 arms:
```csharp
        EmailTemplateKey.TotpEnrolled =>
            (EmailKeys.TotpEnrolled.Subject, EmailKeys.TotpEnrolled.BodyText, EmailKeys.TotpEnrolled.BodyHtml),
        EmailTemplateKey.TotpDisabled =>
            (EmailKeys.TotpDisabled.Subject, EmailKeys.TotpDisabled.BodyText, EmailKeys.TotpDisabled.BodyHtml),
        EmailTemplateKey.BackupCodesRegenerated =>
            (EmailKeys.BackupCodesRegenerated.Subject, EmailKeys.BackupCodesRegenerated.BodyText, EmailKeys.BackupCodesRegenerated.BodyHtml),
```
(`EmailKeys.TotpEnrolled.*` etc. are generated from the Task-1 resx keys by `EmailKeysGenerator` — they resolve at build.)

- [ ] **Step 5: Run — verify PASS.**
Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~EmailComposerTests"`
Expected: PASS (39 keys, all templates render EN+ES).

- [ ] **Step 6: Add the args-map test** (mirror `LockoutUnlock_args_map_to_correct_slots`): new `[Fact] TotpEnrolled_args_map_to_correct_slots` — `Compose(EmailTemplateKey.TotpEnrolled, new CultureInfo("en"), "2026-06-14 12:00", "203.0.113.5")`, assert the timestamp `{0}` and IP `{1}` both appear in the rendered body in the right positions. Run it green.

- [ ] **Step 7: Commit.**
```bash
git add ProjectCeres/Common/Email/EmailTemplateKey.cs ProjectCeres/Common/Email/EmailComposer.cs ProjectCeres.Tests/Integration/Email/EmailComposerTests.cs
git commit -m "feat(9): wire 3 security-event templates into EmailTemplateKey + composer; update resx-count tests

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

### Task 3: MfaController — inject deps + wire the 3 sends (tests-first)

**Files:** Modify `MfaController.cs`; create/extend `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaSecurityEmailTests.cs`

- [ ] **Step 1: Write the failing send tests.** Create `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaSecurityEmailTests.cs` (mirror the `WithReplacedService<IEmailService>(strictMock)` capture pattern used in `EmailConfirmationUnderRlsTests`/`LockoutUnlockIssuanceTests`). Three captures + one negative:
  - `EnrollVerify_sends_TotpEnrolled_email` — drive enroll → enroll/verify with a valid TOTP; assert exactly one captured email whose subject contains "Two-factor sign-in enabled" (or assert the composed key via a captured `EmailMessage` whose Subject matches the resx EN Subject), addressed to the user.
  - `Disable_sends_TotpDisabled_email`.
  - `RegenerateBackupCodes_sends_BackupCodesRegenerated_email`.
  - `EnrollVerify_email_send_failure_does_not_fail_the_action` — mock `IEmailService.SendAsync` to throw; assert the action still returns 200 with backup codes (the try/catch swallows).

- [ ] **Step 2: Run — verify FAIL.**
Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~MfaSecurityEmailTests"`
Expected: FAIL — no email is sent today (captures empty).

- [ ] **Step 3: Inject the email deps into MfaController.** Add to the constructor (use constructor injection, matching `_userManager`/`_auditLog`; add the `using`s: `Microsoft.Extensions.Logging`, `ProjectCeres.Common.Email`):
```csharp
    private readonly IEmailComposer _composer;
    private readonly IEmailService _email;
    private readonly IEmailRecipientResolver _recipients;
    private readonly ILanguageResolver _languages;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MfaController> _logger;

    public MfaController(
        UserManager<ApplicationUser> userManager, IAuditLogWriter auditLog,
        IEmailComposer composer, IEmailService email, IEmailRecipientResolver recipients,
        ILanguageResolver languages, TimeProvider timeProvider, ILogger<MfaController> logger)
    {
        _userManager = userManager;
        _auditLog = auditLog;
        _composer = composer;
        _email = email;
        _recipients = recipients;
        _languages = languages;
        _timeProvider = timeProvider;
        _logger = logger;
    }
```

- [ ] **Step 4: Add a private send helper + wire the 3 call sites.** Add a helper to keep the 3 sites DRY (one place, in-controller — these are notifications, no separate service needed):
```csharp
    private async Task SendSecurityEventEmailAsync(Guid userId, EmailTemplateKey key)
    {
        try
        {
            var recipient = await _recipients.ResolveAsync(userId, HttpContext.RequestAborted);
            var culture = await _languages.ResolveForUserAsync(userId, HttpContext.RequestAborted);
            var timestamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var msg = _composer.Compose(key, culture, timestamp, ip) with { To = recipient };
            await _email.SendAsync(msg, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {Key} security-event email; the MFA operation already completed.", key);
        }
    }
```
Then call it after the audit-log line in each action, before the return:
  - `EnrollVerify` (after `RecordAsync(... MfaEnrolled ...)`): `await SendSecurityEventEmailAsync(user.Id, EmailTemplateKey.TotpEnrolled);`
  - `RegenerateBackupCodes` (after `RecordAsync(... BackupCodesRegenerated ...)`): `await SendSecurityEventEmailAsync(user.Id, EmailTemplateKey.BackupCodesRegenerated);`
  - `Disable` (after `RecordAsync(... MfaDisabled ...)`): `await SendSecurityEventEmailAsync(user.Id, EmailTemplateKey.TotpDisabled);`

- [ ] **Step 5: Run — verify PASS.**
Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~MfaSecurityEmailTests"`
Expected: PASS (4/4).

- [ ] **Step 6: Run the broader MFA + auth suite — no regression.**
Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~Mfa|FullyQualifiedName~Authentication" && dotnet build ProjectCeres`
Expected: green; build 0 errors (CER004 clean — timestamp via `_timeProvider`).

- [ ] **Step 7: Commit.**
```bash
git add ProjectCeres/Controllers/Api/MfaController.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaSecurityEmailTests.cs
git commit -m "feat(9): send TotpEnrolled/Disabled/BackupCodesRegenerated security-event emails from MfaController

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Phase 2 — Frontend: a11y + error-state fixes

### Task 4: Field aria-describedby (component + call sites)

**Files:** Modify `Field.tsx` + the 6 auth call sites + a Field test.

- [ ] **Step 1: Write the failing Field test.** In a new `ProjectCeres.Client/src/app/components/Field.test.tsx`: render `<Field label="Email" htmlFor="email" error="Bad"><input id="email" /></Field>`, assert the error `<p>` has `id="email-error"`. (Fails — no id today.)

- [ ] **Step 2: Run — verify FAIL.** `pnpm --dir ProjectCeres.Client test -- Field.test`

- [ ] **Step 3: Add the id to Field's error `<p>`.** In `Field.tsx`, change `{error && <p className="text-xs text-destructive">{error}</p>}` to:
```tsx
      {error && (
        <p id={htmlFor ? `${htmlFor}-error` : undefined} className="text-xs text-destructive">
          {error}
        </p>
      )}
```

- [ ] **Step 4: Wire aria-describedby at each auth call site.** For every `<Input>` inside a `<Field htmlFor="X" error={...}>` in `Login.tsx`, `Register.tsx`, `PasswordReset.tsx` (both forms), `LoginTotp.tsx` (backup-code input), `EmailVerify.tsx`, `AccountUnlock.tsx`, add `aria-describedby={errors.X ? 'X-error' : undefined}` (use the page's existing error source — RHF `errors.X?.message` presence, or the local error state the page uses). Match the existing `Register.tsx` password-hint precedent for syntax.

- [ ] **Step 5: Run Field test + full client test — verify PASS, no regression.** `pnpm --dir ProjectCeres.Client test`

- [ ] **Step 6: Commit.** `feat(9): wire aria-describedby from auth field errors to their inputs`

### Task 5: focus-to-first-error on server rejection

**Files:** Modify `Login.tsx`, `Register.tsx`, `PasswordReset.tsx`.

- [ ] **Step 1: Add a focus test (Login).** In `Login.test.tsx`: submit, mock a 401 `INVALID_CREDENTIALS`, assert the password input has focus after the rejection (`expect(passwordInput).toHaveFocus()`).
- [ ] **Step 2: Run — FAIL.**
- [ ] **Step 3: Add `setFocus`.** Destructure `setFocus` from `form` in each page; after each server `setError('field', {type:'server', ...})`, call `setFocus('field')`. (Login's INVALID_CREDENTIALS branch sets `password` → `setFocus('password')`.)
- [ ] **Step 4: Run — PASS + full client test.**
- [ ] **Step 5: Commit.** `feat(9): focus first errored field on server-rejection (auth forms)`

### Task 6: Login locked-account inline + network/500 split (the real bug)

**Files:** Modify `Login.tsx`, `i18n/locales/{en,es}.json`, rewrite `Login.test.tsx` locked-account test + add network/500 tests.

- [ ] **Step 1: Add i18n keys.** Add to `auth.login.errors` in BOTH `en.json` and `es.json`: `"network": "Couldn't reach the server. Try again."` / ES `"No se pudo conectar con el servidor. Inténtalo de nuevo."` and `"server": "Something went wrong. Please try again."` / ES `"Algo salió mal. Inténtalo de nuevo."`.

- [ ] **Step 2: Rewrite the locked-account test + add network/500 tests (red first).** In `Login.test.tsx`:
  - Rewrite `redirects to /account/unlock on ACCOUNT_LOCKED_OUT` (~:172) → `shows inline locked-account message and does not navigate on ACCOUNT_LOCKED_OUT`: mock 401 ACCOUNT_LOCKED_OUT, assert the `auth.login.errors.accountLocked` text renders on the form AND the stubbed `/account/unlock` route text does NOT appear.
  - Add `shows network error toast on NetworkError`: mock `apiFetch` to throw `NetworkError`; assert a sonner toast with the network message (or assert the form-level network message renders — match how the page surfaces it).
  - Add `shows server error message on HTTP_500`: mock a failure result `{ ok:false, code:'HTTP_500', status:500 }`; assert the `server` message renders and NOT invalidCredentials.

- [ ] **Step 3: Run — FAIL.** `pnpm --dir ProjectCeres.Client test -- Login.test`

- [ ] **Step 4: Implement in Login.tsx.**
  - Replace the `if (result.code === 'ACCOUNT_LOCKED_OUT') { navigate('/account/unlock'); return; }` block with setting a form-level server error to `t('auth.login.errors.accountLocked')` (use the page's existing `setServerError`/`serverError` mechanism — the page already has a `serverError` state for `emailNotConfirmed`; add an `accountLocked` kind or render the message via that channel). No navigate.
  - In the failure path, before the generic catch-all, add: `if (result.code === 'HTTP_500' || result.status >= 500) { setServerError({ kind: 'server' }); return; }` rendering `t('auth.login.errors.server')`.
  - In the `catch` block, replace the blanket `setError('password', invalidCredentials)` with: `if (err instanceof NetworkError) { toast(t('auth.login.errors.network')); return; }` then rethrow/handle unknown as the server message. Import `NetworkError` from the api-client and `toast` from `sonner` (already imported in Login).

- [ ] **Step 5: Run — PASS + full client test (no regression).** `pnpm --dir ProjectCeres.Client test`

- [ ] **Step 6: Commit.** `fix(9): Login — inline locked-account message + distinct network/500 handling (no longer mislabeled as wrong password)`

### Task 7: vitest-axe coverage for /register + /password-reset

**Files:** Create `Register.a11y.test.tsx`, `PasswordReset.a11y.test.tsx`.

- [ ] **Step 1: Create `Register.a11y.test.tsx`** mirroring `Login.a11y.test.tsx` exactly (provider stack: ThemeProvider > I18nextProvider(i18n) > AuthProvider > MemoryRouter initialEntries=['/register'] > Routes > Route element={<AuthLayout/>}; `beforeEach` fetch-spy → 401 UNAUTHENTICATED; `afterEach vi.restoreAllMocks()`; `await expectNoA11yViolations(container)` from `../../lib/test-axe`).

- [ ] **Step 2: Create `PasswordReset.a11y.test.tsx`** with TWO tests (mirror `LoginTotp.a11y.test.tsx` two-state pattern + 20_000ms timeout): one for the request form (`initialEntries=['/password-reset']`), one for the confirm form (`initialEntries=['/password-reset#token=<43-char-test-token>']` so the confirm form mounts).

- [ ] **Step 3: Run — verify PASS.** `pnpm --dir ProjectCeres.Client test -- a11y`
Expected: all a11y suites green (Login, LoginTotp, Register, PasswordReset). If axe flags a real violation introduced by Tasks 4-6, fix the source (do not weaken the assertion).

- [ ] **Step 4: Commit.** `test(9): vitest-axe coverage for /register + /password-reset`

---

## Phase 3 — Verify + docs + close-out

### Task 8: Full-suite green + web-design-guidelines audit

- [ ] **Step 1: Full backend + frontend suites.**
Run: `dotnet build && dotnet test && pnpm --dir ProjectCeres.Client build && pnpm --dir ProjectCeres.Client test`
Expected: all exit 0; no test-count regression beyond the added tests; CER020 + CER004 clean.

- [ ] **Step 2: Run `web-design-guidelines`** against the changed auth files (Field, Login, the 6 call sites). Address any a11y conformance finding it raises (it's the Phase-5 harden gate from the orchestrator).

- [ ] **Step 3: UX/UI verification checklist** (`docs/design-system.md` § Working rules) — golden path, error state, empty state, 375px, nav links. No browser in this session: hand the checklist to the user with the specific URLs (`/app/login`, `/app/register`, `/app/password-reset`) and the new states to eyeball (locked-account inline message, network toast, server-error message, focus-on-error).

### Task 9: Roadmap ticks + docs + evidence

- [ ] **Step 1: Tick the closed Stage 9 boxes** in `docs/roadmap-phase-three.md` with back-references: 1047 (locked-account inline), 1048 (network toast), 1049 (server-500), 1053 (aria-describedby), 1054 (focus-to-first-error), 1058 (vitest-axe — now all 4 pages), and the 4 email lines 1084-1087 (TotpEnrolled covers both enrol + re-enrol per the reuse decision; note that in the 1087 tick).
- [ ] **Step 2: sync-docs** — security-model.md § Security Event Notifications (the 3 emails now ship); design-system.md if any a11y pattern is worth documenting (the Field error-id + aria-describedby convention is a reusable pattern — document it). api-contract.md unchanged (no endpoint change).
- [ ] **Step 3: changelog-sync** — Added: 3 security-event emails; Fixed: Login network/500 mislabeling, locked-account UX; a11y: aria-describedby, focus-to-first-error, axe coverage.
- [ ] **Step 4: Evidence bundle** — `build-matrix.sh 9`, regenerate turn-shape, and (the diff touches auth `.cs` + MfaController) dispatch the 3-reviewer pipeline; write `reviewer-pipeline.json` at HEAD, no surviving block.
- [ ] **Step 5: Commit** the roadmap + doc-sync + changelog.

---

## Self-review (against the spec)
- **Spec A1-A4 (emails):** Tasks 1-3 — resx (CER020), enum+composer (+ the 2 breaking tests fixed, count 30→39), MfaController send (in-session, no PreAuthUserScope, TimeProvider/CER004, try/catch-swallow, args timestamp+ip). ✓
- **Spec B1-B6 (a11y):** Task 4 (aria-describedby two-touch), Task 5 (setFocus), Task 6 (locked-account inline + network/500 split + i18n keys + Login.test rewrite), Task 7 (axe ×2). ✓
- **Reuse TotpEnrolled for re-enrol:** EnrollVerify sends TotpEnrolled for both paths (no branching) — Task 3 step 4. ✓
- **Type/name consistency:** `SendSecurityEventEmailAsync` defined once (Task 3) used 3×; `${htmlFor}-error` id format consistent between Field (Task 4 step 3) and call sites (step 4) and the test (step 1). ✓
- **No placeholders:** every step has concrete code or an exact command. The ES copy strings and the exact per-call-site `aria-describedby` source expression are the two spots an implementer fills from context — flagged inline with the precedent to match.
