# Changelog

## [Unreleased]

### Phase 3

#### Added

**Support (Stage 12.6 — conversation model, 2026-08-28)**
- The support surface is now a two-way conversation: a ticket holds an ordered thread of messages between the user and the operator, replacing the single-message ticket. Attachments now hang off a message (upload on the first message or any reply; download links in the thread).
- New `/support` page — a list of your tickets, each opening its conversation in a slide-in panel whose address is in the URL (so a link opens straight to that thread). Compose a new ticket, reply, attach files, and close your own ticket; a closed ticket offers a follow-up.
- Five ticket statuses (Open, Pending, On-hold, Solved, Closed). A reply to a Solved ticket reopens it; Closed is final and continues via a follow-up ticket.
- Operators can reply and set a status through an admin-only endpoint (no operator screen yet); their replies appear in your thread and are emailed to you.
- Two new user-facing emails: an operator reply (including the reply text) and a "ticket solved" notice.

**Tooling (SPA staging, 2026-08-28)**
- New `tools/stage-spa.sh` — builds the React client and stages it into the served bundle in one step, for verifying a change against a non-Vite build.
- New advisory Stop hook (`spa-dist-freshness`) that warns when a turn edits SPA source but leaves the served bundle stale.

**Authentication (Stage 9 close-out — security-event emails, 2026-06-14)**
- Three security-event notification emails — sent when two-factor sign-in is enabled, turned off, or backup codes are regenerated. Each names the time and IP and advises resetting your password if it wasn't you.

**Authentication (Stage 9 close-out — auth-form accessibility, 2026-06-14)**
- Inline form errors are now announced to screen readers (each field is linked to its error via `aria-describedby`) and focus moves to the first errored field when the server rejects a submission.
- Added accessibility (vitest-axe) test coverage for the Register and Password-reset pages.

**Subagents (Stage 9.5e — 3-agent reviewer pipeline, 2026-06-08)**
- New review-pipeline roles `reviewer-writer` / `reviewer-security` / `reviewer-playwright-test-audit` (`.claude/agents/`) that audit a finished diff touching pre-auth auth code, EF migrations, or user-owned models — dispatched by the main session, each emitting a `VERDICT: pass|block` under the 9.5k read-first contract; documented in `docs/agents.md`
- New turn-end enforcement: a `reviewer-pipeline.json` evidence slot in the existing evidence-bundle hook requires those three verdicts (three roles present, diff SHA matches HEAD, no surviving `block`) before a diff touching the watched paths can end its turn. Hooks are read-only and can't run agents, so the hook checks the proof the dispatch produces; no new Stop hook (Trip-wire C stays green). Bypass: `CERES_SKIP_REVIEWER_PIPELINE=1`
- New "Trip-wire A" escalation counter (`.claude/hooks/lib/reviewer-escalation.js`): when the third reviewer catches a mechanical miss the first two passed, twice consecutively for the same rule-class, it writes a `graduate-to-analyzer` marker — the work-order to promote that rule to a Roslyn analyzer / pre-commit hook
- Spec: `docs/superpowers/specs/2026-06-08-stage-9-5e-reviewer-pipeline-design.md`; plan: `docs/superpowers/plans/2026-06-08-stage-9-5e-reviewer-pipeline-impl.md`

**Subagents (ceres-researcher — pre-design fact-finder, 2026-06-15)**
- New 6th `ceres-*` strategy agent `ceres-researcher` (`.claude/agents/`) — a read-first pre-design fact-finder dispatched at stage-start, as brainstorming's first step, to map the subsystems / prior art / conventions / unknowns an activity touches before a design exists. Deny-list (`Write, Edit, NotebookEdit`) keeps read-only `Bash` like `ceres-tech-lead`; its body opens with the shared `## What I read` first line then a four-section brief (`Subsystems & files touched` / `Prior art & reusable primitives` / `Conventions & constraints` / `Open unknowns & risks`)
- Generalized the dispatcher-gate rule (`CLAUDE.md` + `docs/agents.md`) to accept a role's designated second section — `## Conflicts found` for the review-lens roles, `## Subsystems & files touched` for `ceres-researcher` — so the new agent isn't rejected into an infinite re-dispatch loop. Wired into Phase A advisory-only (constitution note + `stage-start-detect.js` line); no hard gate, no auto-dispatch
- Spec: `docs/superpowers/specs/2026-06-15-ceres-researcher-agent-phase-a-design.md`; plan: `docs/superpowers/plans/2026-06-15-ceres-researcher-agent-phase-a.md`

**Analyzers (Stage 9.5f — CER005 TokenLookup discipline, 2026-06-09)**
- New `CER005` Roslyn analyzer (`TokenLookupDisciplineAnalyzer`) — fires when a class named `*Token` under `ProjectCeres.Models` implementing `IUserOwned` lacks a `byte[] TokenLookup` property (the HMAC fingerprint that lets a token confirm in O(1) instead of running Argon2id over every candidate row). Guards the 9.1.5.a regression class (a token entity shipping without its lookup column). Property-presence only — the index/migration coupling is owned by the E3 `MigrationDriftTests` (9.5e), not this analyzer
- Shipped at `warning` severity with 0 violations against `main` (the four existing token tables all carry `byte[] TokenLookup`); the `warning`→`error` flip is deferred ≥48h per the C-2 soak. 7-case test matrix (2 fires + 5 exclusions); CER005 row added to `AnalyzerReleases.Unshipped.md` (RS2008); ADR-0077 rule table extended
- Spec: `docs/superpowers/specs/2026-06-09-stage-9-5f-cer005-tokenlookup-analyzer-design.md`; plan: `docs/superpowers/plans/2026-06-09-stage-9-5f-cer005-tokenlookup-analyzer-impl.md`

**Analyzers (Stage 9.5g — CER006 reverse-direction [PreAuthScope] marker, 2026-06-09)**
- New `CER006` Roslyn analyzer (`PreAuthScopeMarkerAnalyzer`) — the reverse of CER001: it fires when a class calls `BeginPreAuthUserScopeAsync` (the helper that opens a pre-login user-scoped transaction) but its enclosing class is not marked `[PreAuthScope]`. CER001 + CER006 together pin the marker contract from both sides — you can't carry the marker without using the helper (CER001), and you can't use the helper without the marker (CER006). Mirrors CER001's syntax-tree walk, so it does NOT inherit CER002's `GetEnclosingSymbol` blind spot; needs zero exclusion logic
- Shipped at `warning` severity with 0 violations against `main` (all 8 pre-auth callers already carry `[PreAuthScope]`); the `warning`→`error` flip is deferred ≥48h per the C-2 soak. 4-case test matrix (1 fires + 3 exclusions); CER006 row added to `AnalyzerReleases.Unshipped.md` (RS2008); ADR-0077 rule table extended
- Spec: `docs/superpowers/specs/2026-06-09-stage-9-5g-cer006-preauthscope-marker-analyzer-design.md`; plan: `docs/superpowers/plans/2026-06-09-stage-9-5g-cer006-preauthscope-marker-analyzer-impl.md`

**Analyzers (Stage 9.5i — typed email-key source generator, 2026-06-09)**
- New `EmailKeysGenerator` source generator — reads the email translation file and emits a typed `EmailKeys.*` constant for every key, so `EmailComposer` references the keys by name instead of building them from strings. Renaming a key in the resx now breaks the build (a `CS0117` compile error) instead of silently falling back at runtime. The generator family's first code-emitting member (CER020 only emits a parity-check stub); additive to CER020, which is unchanged
- Re-scoped from the roadmap's original "migrate string-key consumers" plan: an audit found no string-key consumers — the one consumer (`EmailComposer`) already derived its keys from the `EmailTemplateKey` enum, which matches the resx 1:1. So 9.5i closes the resx↔code rename gap rather than migrating string literals. Emits no diagnostic → no soak → shipped and closed in one commit. Behavior unchanged (40/40 analyzer + 131/131 email tests green)
- Spec: `docs/superpowers/specs/2026-06-09-stage-9-5i-typed-localizer-generator-design.md`; plan: `docs/superpowers/plans/2026-06-09-stage-9-5i-typed-localizer-generator-impl.md`

**Analyzers (Stage 9.5j — code-fix providers for CER004 + CER001, 2026-06-10)**
- New IDE quick-fix (lightbulb) actions for two existing analyzer warnings. CER004 offers a one-click rewrite of `DateTime.UtcNow`/`.Now` to `_timeProvider.GetUtcNow().UtcDateTime` — but only when the class already has a `_timeProvider` field, so the offered fix always compiles. CER001 offers a placeholder rewrite of the wrong transaction call to `BeginPreAuthUserScopeAsync(userId, ct)` that deliberately leaves the build red (the undeclared `userId`/`ct` and the unchanged receiver are the developer's to-do list — a fully-compiling fix would risk shipping an empty user scope)
- CER010 ships no fix (re-scoped from the roadmap's "ticket-format completion" premise): a format-valid invented ticket would pass the analyzer while pointing the bypass-justified audit trail at a non-existent ticket — strictly worse than the warning it removes. So 9.5j shipped two providers, not three. Code-fixes emit no diagnostic → no soak → shipped and closed in one session (analyzer suite 44/44 green)
- Spec: `docs/superpowers/specs/2026-06-10-stage-9-5j-code-fix-providers-design.md`; plan: `docs/superpowers/plans/2026-06-10-stage-9-5j-code-fix-providers-impl.md`

**Tests (Stage 9.5e — migration-drift ship-gate, 2026-06-08)**
- New `MigrationDriftTests.Model_has_no_pending_migration_changes` (Condition E3) — asserts EF Core's `HasPendingModelChanges()` is false, so an entity-shape change must ship its migration in the same commit. A Unit test (model + snapshot built from the assembly, no DB connection); fails the build on drift regardless of how the change was authored

**Authentication (Stage 9.3 — `/register` + email-verify, 2026-05-22)**
- New `EmailConfirmationToken` entity + EF migration `AddEmailConfirmationTokens` — mirrors `PasswordResetToken` shape minus `MfaVerifiedAt`. Uses the TokenLookup HMAC-SHA256 indexed-lookup pattern (Stage 6.15 / 9.1.5.a) for O(1) verify, 30-min expiry, single-use
- New `EmailConfirmationService` with `IssueAsync` / `RequestResendAsync` / `ConfirmAsync` mirroring `PasswordResetService` line-for-line — per-user `SemaphoreSlim`, per-email `MemoryCache` rate gate (5/hour), `PreAuthUserScope` wrapping for RLS-correct writes, Argon2id constant-time mirroring on every anti-enum branch
- New `EmailVerificationController` exposing `POST /api/auth/email/verify` (consume) and `POST /api/auth/email/verify/resend` (anti-enumerating: 204 whether the email is unknown, known-confirmed, or known-unconfirmed)
- `AuthController.Register` rewired with three-branch anti-enumeration shape: fresh-create issues a verification token (AFTER `tx.CommitAsync` so send-failures don't roll back user creation); duplicate-unconfirmed re-issues for the existing user (legitimate owner gets a fresh link); duplicate-confirmed runs `_argon.RunDummyHash()` for timing parity (no token, no email)
- `AuthController.Login` gains an `IsNotAllowed` branch returning `401 EMAIL_NOT_CONFIRMED` — previously this case fell through to `INVALID_CREDENTIALS`, leaving `Login.tsx:92`'s `emailNotConfirmed` state path as dead code. The legitimate user now gets a clear "verify your email" message + a wired Resend Verification button
- New `EmailTemplateKey.RegistrationConfirmation` + EN/ES resx entries (`Subject`, `BodyText`, `BodyHtml`)
- New `AuditLogAction` values `EmailVerificationRequested` (on `IssueAsync`) + `EmailVerified` (on successful confirm)
- New `FailedLoginReason.EmailNotConfirmed` enum value; records the surface for failed-login dashboard
- New SPA route `/register` replacing the placeholder — react-hook-form + zod + `<Field>` recipe + `<Button>` shadcn primitive; on 204 replaces the form with a "Check your inbox" success block referencing the typed email; 422 maps server validation details to react-hook-form field errors; deletes `RegisterPlaceholder.tsx`
- New SPA route `/email-verify` consuming `#token=<raw>` from URL fragment — three render states (verifying / success / invalid); invalid state offers an inline resend form (email input + submit) that posts `{email}` to `/api/auth/email/verify/resend` and renders an anti-enum acknowledgement on 204
- `readTokenFromHash` extracted from `PasswordReset.tsx` into `src/app/lib/url-hash-token.ts` — third caller (AccountUnlock + EmailVerify + PasswordReset) crosses the extraction threshold
- `Login.tsx`'s `onResendVerification` placeholder wired for real: reads email via `form.getValues('email')`, POSTs `{email}` to `/api/auth/email/verify/resend`, surfaces 429 / network / success states with distinct i18n keys
- 14 integration tests pin the contract (`EmailConfirmationTests.cs`); 5 vitest tests pin `Register.test.tsx`; 5 vitest tests pin `EmailVerify.test.tsx`
- Spec: `docs/superpowers/specs/2026-05-18-stage-9-3-register-and-email-verify-design.md` (revised 2026-05-22); plan: `docs/superpowers/plans/2026-05-22-stage-9-3-register-and-email-verify-impl.md`

**Authentication (Stage 9.5 — `/account/unlock` SPA page, 2026-05-22)**
- New SPA route `/account/unlock` consuming `#token=<raw>` from URL fragment — button-press confirmation (NOT auto-confirm on mount) defends against email link-prefetchers (Microsoft Defender Safe Links, Gmail safe-link scanners). One extra click is cheaper than a burned single-use token + a user re-requesting an email
- Three render states: idle (Unlock button), invalid (error block + back-to-sign-in), network (retry-able error). On 204 navigates to `/login?unlocked=1` + sonner toast via `Login.tsx`'s `useEffect`
- `LockoutUnlockService.cs:117` URL substring flipped from `/app/lockout-unlock` to `/account/unlock` so the email link lands on the actual SPA route (the Login.tsx redirect from `ACCOUNT_LOCKED_OUT` and the roadmap line both already pointed at `/account/unlock`)
- New i18n namespace `auth.accountUnlock.{title, description, submit, submitting, backToSignIn, invalidTitle, invalidBody, toastSucceeded, errors.{network, retry}}` in EN + ES
- 6 vitest tests pin `AccountUnlock.test.tsx`; 1 added vitest test pins the `?unlocked=1` toast in `Login.test.tsx`; 1 updated integration test in `LockoutUnlockIssuanceTests.cs` (URL substring assertion)
- Spec: `docs/superpowers/specs/2026-05-22-stage-9-5-lockout-self-service-unlock-design.md`; plan: `docs/superpowers/plans/2026-05-22-stage-9-5-lockout-self-service-unlock-impl.md`

**Authentication (Stage 9.7 — backup-code dashboard banner, 2026-05-21)**
- New `BackupCodeLoginBanner` component rendered as the first child of the dashboard surface when the signed-in user has MFA enabled and `backupCodesRemaining ≤ 7`. CTA shifts copy based on how the user last authenticated: "Re-enrol authenticator" if the most recent login consumed a backup code; "Regenerate backup codes" if the user has since logged in normally but is still low on codes. Per-pageview dismiss via a small `X` button (React-local state, no persistence) — the banner reappears on reload while the conditions still apply
- `UserSession.UsedBackupCodeAtLogin` (nullable bool, defaults false) records which second-factor branch authenticated each active session. Written in `AuthController.IssueSessionAndCookiesAsync` (now takes an explicit `usedBackupCode` parameter; password-only and TOTP-app callers pass `false`, backup-code branch passes `true`). `AuthController.Me` looks the value up by the current request's `sid` claim and exposes it on `MeResponse.UsedBackupCodeAtLastLogin` — multi-device-correct, because each session row is independent
- Deletes the stale "Phase 4 wires this properly" comment at `AuthController.cs:483`; that deferral was the placeholder this stage was created to retire
- Reuses the documented `border-warning/30 bg-warning/10 text-warning` recipe from `TotpEnrollStep2BackupCodes` and `BudgetCreate` — third caller. The recipe is now documented in `docs/design-system.md` § Inline warning strip with a fourth-caller promote-to-primitive threshold
- `docs/models.md` UserSession table grows a row for the new column with the multi-device rationale captured inline
- New i18n namespace `dashboard.backupCodeBanner.{dismiss, reenrol.{body_one,body_other,cta}, regenerate.{body_one,body_other,cta}}` in EN + ES, using i18next's `_one` / `_other` plural-form convention
- 3 xUnit integration tests pin the server contract (`BackupCodeLoginSessionFlagTests.cs`): backup-code login flips the flag true on the new session row; TOTP login leaves it false; `Me` returns the flag scoped to the current session cookie (multi-device proof — two parallel `HttpClient` instances, one signed in via backup code, the other via TOTP, get distinct `usedBackupCodeAtLastLogin` from `Me`)
- 7 vitest unit tests pin the SPA contract (`BackupCodeLoginBanner.test.tsx`): re-enrol CTA when `usedBackupCodeAtLastLogin = true`, regenerate CTA when `false` and codes ≤ 7, absent when codes > 7, absent when MFA off, absent when anonymous, dismiss hides current render but a fresh mount restores it, singular vs plural body copy
- EF migration `AddUsedBackupCodeAtLoginToUserSession` — single AddColumn, server-side default false; applied to `project_ceres` and `project_ceres_test`
- Spec: `docs/superpowers/specs/2026-05-21-stage-9-7-backup-code-banner-design.md`; plan: `docs/superpowers/plans/2026-05-21-stage-9-7-backup-code-banner-impl.md`. Stage 9.7 "Backup-codes recovery flow" closes with all four items checked (toggle, single-use, dashboard banner, re-enrolment-invalidation)

**Authentication (Stage 9.4 — `/password-reset` SPA pages, 2026-05-18)**
- Single SPA page mounted at `/password-reset` that dispatches by `location.hash`: empty/missing hash renders the request form (one email field + "Send reset link" submit + back-to-sign-in); `#token=<raw>` renders the confirm form (new + confirm password, optional TOTP cells revealed if the server responds `200 { requiresTotp: true }`)
- Honors the server's pre-existing URL convention from `PasswordResetService.cs:164` — reset link embeds the raw token in the URL fragment, not query, so the token never reaches the server logs or Referer headers
- Failure paths wired end-to-end: `401 INVALID_RESET_TOKEN` replaces the form with an invalid-token block + "Request a new link" link to `/password-reset`; `401 INVALID_MFA_CODE` shows an inline error on the TOTP cells without losing the password fields; `422 VALIDATION_ERROR` maps `details[]` to react-hook-form field errors (PwnedPasswords / policy violations surface inline)
- On `204` success: navigate to `/login?reset=1`; Login's existing `useEffect` toast handler now also fires `auth.login.toasts.passwordResetSuccess` ("Password reset. Sign in with your new password.")
- 11 vitest unit tests pin the contract: hash parsing (no token / empty token / valid token), request 204 success block, request 429 no-countdown variant, confirm zod mismatch (no fetch), confirm 204 navigate, requiresTotp two-step flow, INVALID_RESET_TOKEN block, INVALID_MFA_CODE preserves passwords, VALIDATION_ERROR maps to field errors
- `PasswordResetPlaceholder.tsx` deleted; its dead i18n keys at `auth.placeholders.passwordReset.*` removed in the same commit
- Spec: `docs/superpowers/specs/2026-05-18-stage-9-4-password-reset-design.md`

**Authentication (Stage 9.2 — `/login/totp` SPA page, 2026-05-17)**
- New `/login/totp` route mounted under the public `AuthLayout` centered-card branch; six-cell `InputOTP` shadcn primitive as the default state with auto-submit when 6 digits are typed or pasted; manual "Verify" button for backup-code mode
- Backup-code state reached via "Lost your device?" link toggle; uses a plain text input (alphanumeric); submit posts to the same `/api/auth/login/totp` endpoint per the server's existing contract
- Failure paths: `401 INVALID_MFA_CODE` renders the inline `auth.totp.errors.invalid` error and clears the cells; `401 ACCOUNT_LOCKED_OUT` navigates to `/account/unlock` (matches Login page parity); `401 UNAUTHENTICATED` navigates to `/login?expired=1` with a new sonner toast (`auth.login.toasts.totpExpired`); `429` reads `Retry-After` header and surfaces the countdown via `aria-live="polite"`, falling back to the no-countdown variant if the header is missing
- `submitTotp` page-local helper bypasses `apiFetch` only for this endpoint so the SPA can read response headers (CSRF prime + raw `fetch`); isolated to 9 lines, called out in the spec as the trade-off
- 10 vitest unit tests + 2 vitest-axe a11y tests (serious/critical filter via `expectNoA11yViolations`); pre-existing `Login.test.tsx` gains a sonner-mocked test asserting the `?expired=1` toast fires
- Test-infrastructure: `document.elementFromPoint` no-op polyfill added to `src/test-setup.ts` so input-otp's background timer no longer throws an Uncaught Exception under jsdom (was poisoning unrelated test files via cross-file pollution)
- Spec: `docs/superpowers/specs/2026-05-17-stage-9-2-login-totp-design.md`; plan: `docs/superpowers/plans/2026-05-17-stage-9-2-login-totp-impl.md`

**Authentication (Stage 9.3 — spec only, implementation in follow-up session, 2026-05-18)**
- `docs/superpowers/specs/2026-05-18-stage-9-3-register-and-email-verify-design.md` — comprehensive design for the `/register` SPA page + `/email-verify` token-consumption page + the server-side `EmailConfirmationToken` pipeline (entity + EF migration with TokenLookup column matching the Stage 6.15 / 9.1.5.a indexed-lookup pattern + service mirroring `PasswordResetService` line-for-line + two new HTTP endpoints + `RegistrationConfirmation` resx EN+ES + `EmailTemplateKey` enum addition + register-flow wiring + DI + ~10 integration tests + ~10 SPA tests)
- Splits the duplicate-username anti-enumeration notice INTO 9.3 scope (rather than deferring it to a still-undefined "Stage 6c follow-up" with no roadmap `[ ]` line) — the verification email itself IS the notice when sent to the existing account holder; per `feedback_deferral_requires_receiving_stage_checkbox`, deferring without a receiving entry isn't allowed
- Receiving entry / tripwire: the existing `[ ]` line at `docs/roadmap-phase-three.md:954` (Stage 9 sub-stage 9.3) — Stage 9 close-out cannot complete with 9.3 unticked

**Authentication (Stage 6 close-out — flow diagrams, 2026-05-11)**
- `docs/security-model.md § Authentication Flow Diagrams (Stage 6 close-out)` — single end-of-stage Mermaid set: request pipeline; registration; login (no-MFA / MFA TOTP / backup-code branches); password reset (request + confirm); reauth step-up + `[RequireRecentAuth]` gate; email-address change (request + confirm + revoke); lockout self-service unlock; audit-log writes overlay; cross-flow authentication state machine
- Each diagram paired with explicit audit prompts pointing at the integration tests that pin the behaviour
- With this in place, Stage 6 (Identity infrastructure, Batch 3b) is ✅ Done — all 12 sub-stages + 2 follow-ups shipped; 303/303 Authentication integration tests green; the Stage 6 verification checklist in `roadmap-phase-three.md` carries no `[ ]` items

**Authentication (Stage 6.12 — Email-address-change flow, 2026-05-10)**
- `POST /api/auth/email-change/request` (authenticated, `[RequireRecentAuth]`) — issues a 30-min VerifyNew token to the new address and a 7-day RevokeOld token to the old address; supersedes any prior pending pair
- `POST /api/auth/email-change/confirm` (anonymous, token-gated) — rewrites `Email` + `NormalizedEmail` + `UserName` + `NormalizedUserName`, sets `EmailConfirmed = true`, consumes both sibling rows atomically, revokes all `UserSession` rows, regenerates `SecurityStamp`, sends notifications to both new and old addresses; does NOT clear lockout (explicit divergence from password-reset)
- `POST /api/auth/email-change/revoke` (anonymous, token-gated) — consumes both sibling rows, leaves `user.Email` UNCHANGED, notifies old address only; sessions and `SecurityStamp` untouched
- `EmailChangeToken` entity with `Purpose` discriminator + `AddEmailChangeTokens` migration (single table, index parity with `PasswordResetTokens`)
- `EmailChangeService` with per-user `SemaphoreSlim` concurrency, per-new-email `MemoryCache` rate gate (5/hour)
- Cross-feature: a successful `/api/auth/password-reset/confirm` now atomically cancels any pending email-change for the same user and notifies the old address — closes the window where an attacker-initiated change with a still-live verify token could survive a victim's password-reset
- Canonical error codes: `INVALID_EMAIL_CHANGE_TOKEN` (401), `EMAIL_ALREADY_IN_USE` (422), `EMAIL_UNCHANGED` (422)
- 37-test integration ship-gate under `ProjectCeres.Tests/Integration/Authentication/EmailChange*`

**Tests (Stage 9.5d — AppRole RLS-active auth suite, 2026-06-07)**
- New `AppRoleTests` suite runs the auth write-flows (register, login, logout, MFA enroll, password-reset, email-confirmation, email-change, lockout-unlock) under the restricted `ceres_app` database role with Row-Level Security active — proving each user-owned write is visible only to its owner. This is coverage the existing admin-context suite couldn't give (it runs with RLS bypassed), and it's the test class that would have caught the Stage 9.3 cross-user gap
- A fail-fast fixture refuses to run the whole suite if `ceres_app` is misconfigured with BYPASSRLS, so the tests can't pass falsely

**Design System**
- OKLCH color palette (light + dark) covering background, foreground, card, popover, primary (deep teal), secondary, muted, accent (pale teal), destructive (rose), success (emerald), warning (amber), info (sky), border, input, ring; tokens defined in `src/index.css` and aliased via `@theme inline`
- Inter Variable + IBM Plex Mono fonts self-hosted via `@fontsource-variable/inter` and `@fontsource/ibm-plex-mono`
- 8-color chart palette (`--chart-1` through `--chart-8`), all WCAG AA against `--background` in both modes
- Motion tokens: `--motion-duration-fast/base/slow`, `--motion-easing-standard/emphasized`
- Shadow tokens (`--shadow-sm` through `--shadow-lg`) tuned for both light and dark surfaces
- `<Numeric>` component for tabular numerics (mono + tabular-nums); enforcement mechanism — never apply `font-mono` directly
- `/design-system.html` showcase route with live token rendering, dark/light toggle, and pages for Overview, Colors, Typography, Spacing, Motion, Charts, Components
- Layout primitives: `<StatTile>` (vertical), `<StatRow>` (inline justify-between), `<EquationRow>` (compact muted caption) — codified in `src/components/`

**App Shell**
- React SPA mounted at `/app/` via ASP.NET Core catch-all route + Vite middleware; shell components: `AppLayout`, `Sidebar` (with collapse toggle + persisted state), `TopBar` (brand mark, global search, quick-add `+`, notifications, avatar menu), `MobileDrawer` for narrow viewports
- React Router v7 (BrowserRouter, basename `/app`) with route map for Dashboard, Movements, Transactions, Transfers, Accounts, Categories, Budgets, Recurring, Reports, Import, Settings, Support, Profile, Security
- Cmd+K / Ctrl+K keyboard shortcut to open global search modal; `useKeyboardShortcut` hook
- shadcn/ui (`base-nova` style) primitives installed: Button, Card, Dialog, Dropdown menu, Input, Label, Popover, Sheet, Skeleton, Switch, Tabs, Tooltip, Avatar, Badge, Kbd, Separator, Progress, Navbar
- Vitest + React Testing Library scaffold

**Dashboard**
- React Dashboard at `/app/` replacing Razor view; cards for Financial Health, KPI strip (Net Worth + Month-to-Date + Reminders), Category Budgets, Goal Budgets
- 4-panel asymmetric Financial Health card with vertical dividers; equation rows for Spendable Balance breakdown (Liquid, Bills due, Available today, Budget reserved, Safe to spend)
- Health card panel captions beneath headlines (Burn Rate "€143 / €350 spent", Runway "at €1000/mo", Income vs Avg "€3000 vs €2700 avg")
- MTD card rendered as 3 KPI tiles (Income / Expenses / Savings Rate) with `bg-muted/40` surfaces
- `useApi<T>` hook with `AbortController` cancellation on unmount and URL change
- Loading skeletons, error states with retry, and empty states on every card

**Backend**
- New `AppController` + `Views/App/Index.cshtml` host the SPA; catch-all route `app/{*path}` for client routing
- Typed `HealthSnapshotData` record (15 fields including `AvailableToday`, `SafeToSpend`, `RunwayMonths`, `AvgMonthlyExpense`, `CurrentMonthIncome`, `RollingAverageIncome`, `IncomeDeltaPercent`, `BudgetBurnRate`, `BudgetSpentMtd`, `BudgetTotalLimit`)
- Typed `DashboardSummaryDto` and `MtdSummary` records
- New `/api/dashboard/health` and `/api/dashboard/summary` endpoints in `DashboardApiController`
- `DashboardService.GetRunwayAsync` now returns `(months, avgMonthlyExpense)` tuple; `GetBudgetBurnRateAsync` returns `(burnRate, spent, totalLimit)` tuple

**Components**
- `<CardError section onRetry>` cross-feature primitive for "couldn't load X" + retry UI

**Movements**
- React Movements page at `/app/movements` with text search (debounced 300ms), account filter, date range filter, and numbered pagination (50/page); URL-encoded filter state (`?q=&accountId=&from=&to=&page=`); `useApi` `AbortController` handles request concurrency
- `MovementsTable`, `MovementsFilterBar`, `MovementsPagination`, `MovementClearedToggle` components under `src/app/features/movements/`
- `IMovementService.GetRecentAsync` and `CountAsync` accept `string? q` parameter; matches description OR category name for transactions, description-only for transfers and liability payments (PostgreSQL `EF.Functions.ILike`)
- `GET /api/movements` returning paged `MovementsPageDto`
- `MovementsApiController.PatchCleared` switch extended to handle `liabilitypayment` type (`ILiabilityPaymentService.MarkClearedAsync` added)
- Full Movements CRUD on the SPA at `/app/movements*`: routed Create page (`/movements/new` with type picker), routed Edit page (`/movements/:id/edit`) with danger-zone delete, row-level ⋯ menu (Edit/Delete), type filter dropdown, attachment upload (two-phase save-first), bulk mark-cleared, CSV export
- 22 typed API endpoints under `/api/transactions`, `/api/transfers`, `/api/liability-payments`, and `/api/movements` (see `docs/api-contract.md`)
- View-transition CSS hooks on movement rows and the form (animation upgrade is a follow-up)

**Quick-Add**
- `<QuickAddModal>` with three tabs (Transaction / Transfer / Liability Payment) wired to TopBar `+` button and Movements page header "+ New" button
- Currency symbol auto-derived from selected account, displayed as amount input prefix
- Inline 422 validation errors mapped from `ValidationProblemDetails` (PascalCase → camelCase key conversion)
- `AccountCombobox` and `CategoryCombobox` searchable selectors using shadcn Command + Popover
- `useDebounced<T>(value, delayMs)` hook for search-input debouncing

**Toasts**
- Sonner adopted as SPA-wide toast system; `<Toaster />` mounted in `AppLayout`; conventions documented in `docs/design-system.md`
- Toaster configured with `position="top-right"`, `closeButton`, `duration={5000}`, neutral popover background with semantic-colored icon (avoids dark-mode contrast issues from `richColors`)

**Backend**
- `POST /api/transactions`, `POST /api/transfers`, `POST /api/liability-payments` create endpoints
- `GET /api/accounts/active` and `GET /api/categories/active` combobox helper endpoints (return minimal `AccountOptionDto` / `CategoryOptionDto`)

**Charts**
- 5 dashboard chart components migrated from Razor islands to SPA (NetWorth Over Time, Income vs Expense, Spending by Category, Account Balances, Cash Flow); moved from `src/components/` to `src/app/features/dashboard/`
- `chartColors` util — semantic tokens (`income`, `expense`, `netWorth`, `assets`, `liabilities`) + `slot(n)` for chart palette; replaces hex literals in chart components
- `formatMonth(yyyyMm)` helper for chart axis labels — uses `Intl.DateTimeFormat` with `{ month: 'short', year: 'numeric' }` (e.g. "Apr 2026")
- Dashboard layout: NetWorth chart full-width as headline trend, then 2x2 grid for IncomeExpense / SpendingByCategory / AccountBalances / CashFlow
- All 5 chart endpoints converted to typed wrapper DTOs with `currencyCode` and `currencySymbol`: `NetWorthTrendDto`, `IncomeExpenseDto`, `SpendingByCategoryDto` (adds `total` for % calculations), `AccountBalancesDto`, `CashFlowDto`
- Trend endpoints (`net-worth-trend`, `income-expense`, `cash-flow`) extended from 6-month to 12-month window

**Design System (v1.2)**
- shadcn `<Badge>` extended with `success`, `warning`, `info` semantic variants matching the existing soft-tinted `destructive` recipe
- `<Tile>` KPI surface wrapper extracted from `MtdCard` into `src/components/Tile.tsx`
- Showcase `Toasts` page (Sonner variants + 5-stack queueing demo)
- Showcase `Patterns` page covering layout primitives (StatTile, StatRow, EquationRow, Tile) and CardError; existing Stat components moved out of Components page

**Docs**
- `docs/design-system.md` — Status badges section, Layout primitives section, CardError section, Skeleton convention paragraph, Known browser console messages section (SES + WebSocket + Recharts width warnings)

**Budgets**
- Full Budgets CRUD on the SPA at `/app/budgets`: unified tabbed list (Category / Goal), `+New` dropdown (Category Budget / Spending Goal / Savings Goal), routed Create + Edit pages with discriminator-based routing, archive lifecycle with one-click reactivate, "Show archived" toggle, conflict-aware Create flow that offers to reactivate existing archived budgets
- `Settings.BudgetPeriodStartDay` (1–31, default 1) — all category-budget actual-spend math respects the configured cycle. Configurable via the existing Razor Settings page (the SPA Settings page migration is a follow-up)
- 16 typed API endpoints under `/api/category-budgets`, `/api/goal-budgets`, `/api/budgets`, and `/api/currencies` (see `docs/api-contract.md`)
- Movement form gains a conditional Spending-Goal picker so transactions can be tagged toward Spending goals — visible only when ≥1 active matching goal exists in the transaction's currency

**Data layer / RLS (Stage 9.5b — model-derived user-owned set, 2026-06-02)**
- `RlsParityStartupCheck` — the app now refuses to start if any user-owned table is missing forced Row-Level Security (`relrowsecurity` + `relforcerowsecurity`), checked against the live database at boot beside the existing privilege-leak check. Tables whose creating migration hasn't been applied yet are skipped, so a rolling deploy (new binary up before the migration runs) doesn't crash. This closes the class of bug where a new user-owned table could ship without its RLS policy
- `UserOwnedModel.RlsTables(model)` / `FinanceTables(model)` — the set of user-owned tables is now derived from the EF model (any concrete entity implementing `IUserOwned` that maps to a table), replacing the hand-typed list that was the original source of the drift
- `DualContextWebApplicationFactory` — a test fixture that boots the app as the restricted `ceres_app` database role (RLS active) so tests can observe a missing RLS policy, exposing an RLS-enforced context bound to a specific acting user plus an admin context for cross-user seeding
- `AdminContextDisciplineTests` — architecture test requiring every consumer of the BYPASSRLS admin context to carry `[RequiresAdminContext]`, so all RLS bypasses are findable by one marker; covers constructor/field, method-parameter, and service-locator injection
- `RlsParityMetaTests` — proves the boot check actually throws when an applied user-owned table loses forced RLS, plus a cross-user isolation test through the production-booted app role

**Tests / E2E (Stage 9.11 — Playwright foundations, 2026-06-12)**
- Five browser-driven auth golden-path E2E suites under `ProjectCeres.Client/e2e/auth/` — register→verify→login, password-reset, TOTP-enrol→first-login, lockout→self-service-unlock, and backup-code recovery (single-use edge) — green across Chromium, Firefox, and WebKit
- New `ASPNETCORE_ENVIRONMENT=E2E` that serves the production-built SPA bundle (staged into `wwwroot/dist`, served via the Vite manifest) against a dedicated `project_ceres_e2e` database. `tools/e2e/run-server.sh` is the Playwright `webServer`: it creates + migrates + wipes the database, builds + stages the bundle, asserts the manifest has the keys the Razor host looks up, then boots over HTTPS
- `FileSinkEmailService` — an E2E-only `IEmailService` that writes each email as JSON so the suites can read verify/reset/unlock links (tokens are hashed in the DB, so the email is the only way in). Registered exclusively under the E2E environment, pinned by an architecture test + DI test so it can never resolve under Production/Development
- `E2eDatabaseGuardStartupCheck` — the E2E environment refuses to boot unless the application connection points at `project_ceres_e2e`, collapsing the blast radius of a stray `ASPNETCORE_ENVIRONMENT=E2E` on a real host
- Config-bound rate limits (`RateLimitOptions`) — login/CSRF/email-per-IP permit counts now bind from config with defaults equal to the prior hardcoded literals; `appsettings.E2E.json` raises them so the single-loopback E2E suite doesn't trip the limiter (a defaults-equal-literals test prevents silent production weakening)
- E2E test tooling: `playwright.golden.config.ts` (webServer + Chromium/Firefox/WebKit matrix, `workers: 1`, `retries: 0`), `tsconfig.e2e.json` (type-checks `e2e/` via `tsc -b`), `otpauth` dev dep for TOTP code generation, and `pnpm e2e` / `e2e:ui` / `e2e:walk` scripts. The pre-existing agent-walk smoke harness keeps its own config
- Spec: `docs/superpowers/specs/2026-06-11-stage-9-11-playwright-e2e-foundations-design.md`; plan: `docs/superpowers/plans/2026-06-12-stage-9-11-playwright-e2e-foundations.md`

#### Removed

**Data layer / RLS (Stage 9.5b, 2026-06-02)**
- `ProjectCeres/Common/UserOwnedTables.cs` (the hand-typed `UserOwnedTables.All` list) — superseded by the model-derived `UserOwnedModel`

#### Fixed

**Sessions (Stage 12.10 — session lifecycle, 2026-08-29)**
- The active-sessions list showed sessions whose login had long since expired, offering Revoke / Block-IP buttons on sessions that were already dead. The list now hides a session once its cookie has expired (30 minutes of inactivity for a normal session, 30 days for a "remember me" one).

**Frontend / SPA host (Stage 9 close-out — production stylesheet, 2026-06-29)**
- The single-page app shipped completely unstyled in production (manifest mode): the SPA host page emitted the JavaScript bundle but never emitted a stylesheet `<link>`, so no CSS loaded on any `/app/*` screen. This was invisible during local development, where the Vite dev server injects CSS through JavaScript. Fixed by adding the missing `<link vite-href="~/src/app/main.tsx" rel="stylesheet">` tag to `Views/App/Index.cshtml`. Found during the Stage 9 close-out responsive verification — the first check to render the production build rather than the dev server.
- New E2E regression guard `ProjectCeres.Client/e2e/auth/spa-stylesheet.spec.ts` — fails if the host stops emitting the stylesheet or if the global styles stop applying on the auth surface.

**Authentication (Stage 9 close-out — Login error handling, 2026-06-14)**
- Login no longer reports network failures or server (500) errors as "Email or password is incorrect" — a network error now shows a retry toast (with the form preserved) and a server error shows a distinct message.
- A locked account now shows an inline "Account locked — check your email for an unlock link" message instead of an abrupt redirect (the previously-unused message is now wired up).

**Tests (Stage 9 close-out — MovementForm flake, 2026-06-14)**
- Fixed a flaky MovementForm budget-picker test that timed out reading a portal-rendered popover under full-suite CPU load (explicit 3000ms wait, matching the App.test.tsx fix).

**Tests (Stage 9.5e close-out — rate-limit flake, 2026-06-08)**
- `Login_LimiterResetsAfterWindow` stabilized under full-suite load — its saturation step fires up to 25 sequential requests, which under CPU contention could take longer than the test's 1-second rate-limit window, so the earliest requests slid off before the bucket tripped and the saturation assertion failed. Switched to the 5-second `WithMediumLoginWindow` (the window the project already sized for multi-request bursts, used by `SlidingWindow_BoundaryAttack_StillBlocked`) + a matching `MediumLoginWindowClearDelay`. No assertion weakened; the test still saturates, waits the real window out, and confirms the rollover

**Authentication (Stage 9.5d — pre-auth-confirm RLS gaps, 2026-06-07)**
- Email-address-change confirm and revoke would have returned "invalid token" (401) for every user once the app runs under the restricted database role — the token lookup ran before the user was known, so Row-Level Security hid the row. Fixed to look the token up via the admin context, then establish the user's context for the writes (the pattern password-reset and email-confirmation already use). No impact on current behavior; the bug would only have surfaced after the database-role cutover
- Account-unlock confirm had the identical gap (401 for every locked-out user under the restricted role) and the identical fix. A completeness audit confirmed every other token-confirm path was already correct

**Subagents (Stage 9.5k — registry comment, 2026-05-30)**
- Corrected a stale section-comment count in `UserOwnedTables.cs` (`Auth-internal (8)` → `(9)`) surfaced by the smoke-test; the registry already held all 9 entries (verified 1:1 against 25 live-DB RLS-forced tables), so no RLS gap — documentation only

**Authentication (Stage 9.3 — Register duplicate-unconfirmed RLS-nested-tx, 2026-05-22)**
- `AuthController.Register`'s duplicate-unconfirmed branch was calling `EmailConfirmationService.IssueAsync` inside the outer plain transaction (which had no `app.current_user_ref` GUC set). `IssueAsync`'s `BeginPreAuthUserScopeAsync` then tripped `PreAuthRlsScope`'s nested-tx mismatch guard and threw `InvalidOperationException` → 500. Fix: capture existing-user state inside the outer tx, commit the outer tx first, then call `IssueAsync` outside it (matches the fresh-create branch's ordering). Caught by `EmailConfirmationTests.Re_register_same_unconfirmed_email_after_expiry_issues_new_token_for_existing_user`

**Authentication (Stage 9.5 — Lockout email copy, 2026-05-22)**
- `LockoutUnlock.BodyText` / `BodyHtml` (EN + ES) updated to tell the user "valid authenticator codes are still accepted during lockout" per `security-model.md § Login`. Previously silent — MFA-enabled users believed they had to wait the full 15 minutes
- Same copy update corrects a longstanding bug: the body said "within 1 hour" / "próxima hora" but the actual token lifetime is 15 minutes (Stage 6.10 D2). Both EN + ES now name the 15-min expiry

**Authentication (Stage 9.11 — emailed-URL prefix + register status, 2026-06-12)**
- Verify-email and account-unlock emails built links to root paths (`/email-verify#token=`, `/account/unlock#token=`) that have no server route — a real browser following them got a 404. Both now use the `/app`-prefixed SPA route (`EmailConfirmationService`, `LockoutUnlockService`); the unlock case was a regression from the route rename in commit `98eef2d`. Caught by the register-login and lockout-self-service E2E suites clicking the real links
- `docs/api-contract.md` + the `AuthController.Register` code comment claimed Identity password-policy failures (too-short, breached) return `422` — they return `400` (the controller calls `ValidationProblem(ModelState)`, which bypasses the 422 factory). Documentation corrected to match the pinned behavior (`RegisterEndpointTests`); model-binding/DataAnnotations failures still return the `422` envelope

#### Changed

**Sessions (Stage 12.10 — session lifecycle, 2026-08-29)**
- Signing in again from the same browser no longer stacks up a new session row each time — the previous session from that same device is superseded, so the active-sessions list shows one entry per device. Signing in from a different browser or network still appears as its own session.
- Added a daily retention sweep (`--sweep-sessions`) that removes session records older than 90 days, so the sessions table no longer grows without bound. (The cron schedule is registered at deploy — Stage 16.)

**Support (Stage 12.6, 2026-08-28)**
- `SupportTicket` now owns a `SupportMessage` conversation (the old `Message` column moved to the first message) and gains `ExternalRef`; the attachment foreign key re-pointed from the ticket to the message. Ticket creation returns the first message's id so the client can attach files to it.
- The operator reply endpoint is rate-limited like the user endpoints (it emails the user) — it had shipped without a limit.

**Code quality (Stage 9.1.6 — code-shape cleanup, 2026-06-15)**
- Replaced the remaining production raw SQL with safer forms: `PreAuthRlsScope` uses `SqlQuery` (interpolated constant) instead of `SqlQueryRaw`; the dev-seed table remap (`SeedDevUser`) uses `ExecuteSqlInterpolatedAsync` with the user/sentinel IDs as real parameters, behind a throwing allow-list guard that validates every table name against the EF-model-derived `UserOwnedModel.FinanceTables` set
- Swept 163 redundant fully-qualified namespace references (IDE0001) solution-wide via `dotnet format`; promoted IDE0001 to `warning` with `dotnet format style --verify-no-changes --diagnostics IDE0001` as the regression check (it is a format/IDE-only analyzer and does not surface at `dotnet build`). `AuthController`'s `SignInResult` stays fully qualified (Identity vs MVC name clash)
- Simplified `SettingsService.ClampStartDay` to `Math.Clamp` and `Categories.Defaults` to a collection expression; the test-project + React simplification sweeps were split to Stage 9.1.7

**Code quality (Stage 9.1.7 — deferred simplification sweeps, 2026-06-15)**
- Completed the test-project + React simplification sweeps deferred from 9.1.6.g. Both trees were already idiomatic; net 13 safe-mechanical simplifications with no behavior change — collection expressions / target-typed `new()` / one expression body in the test project (count unchanged at 1180 tests), and two redundant primitive `useMemo` wrappers removed in the auth pages (all other React memoization kept as load-bearing)

**Subagents (Stage 9.5e — dangling-reference cleanup, 2026-06-08)**
- Retired the undefined "Trip-wire B" and "autonomy level" references in the roadmap's 9.5h container (only Trip-wire C was ever defined; Trip-wire A is now the concrete escalation counter); corrected a false claim in the 9.5k plan and `ceres-cto.md` that the playbook constitution holds the trip-wire definitions
- Removed a dead `UserOwnedTables.cs` predicate from the evidence-bundle hook (the file was deleted in 9.5b); corrected stale `UserOwnedTables.All` references in the `verify-stage-completeness` skill docs to the model-derived `UserOwnedModel.RlsTables`

**Tests (Stage 9.5d, 2026-06-07)**
- The `AppRoleTests` collection runs serialized (`DisableParallelization`) — its concurrent load was destabilizing timing-sensitive rate-limit tests under full-suite runs; matches the existing rate-limit collections' setup

**Subagents (Stage 9.5k — read-first contract, 2026-05-30)**
- The 5 `ceres-*` strategy subagents now restrict tools via a `disallowedTools` deny-list instead of a `tools` allow-list. The deny-list states the real invariant (never mutate code), survives future read-tool additions, and avoids the allow-list footgun where a typo'd tool name silently grants all tools. Smoke-test confirmed all 5 roles pass the read-first contract with no mutating-tool use
- Tightened the response contract: a `ceres-*` reply's first line must now be the `## What I read` heading with no preamble or thinking-aloud, enforced by the dispatcher gate in `CLAUDE.md` § Using subagents — after the smoke-test found 2 of 5 roles opening with prose before the heading

**Data layer / RLS (Stage 9.5b, 2026-06-02)**
- The user-owned table set used by the EF query filters, the dev-seed tool (`SeedDevUser`), the RLS-parity tests, and the exception translator is now derived from the EF model rather than a hand-maintained list — adding a new user-owned entity needs no list edit
- `EmailConfirmationToken` added to `IUserOwnedConformanceTests` (it was missing — a pre-existing test-coverage gap); five docs (`multi-tenancy-strategy.md`, `security-model.md`, the PostgreSQL RLS guide, `planning-phase3.md`, `roadmap-phase-three.md`) de-staled to reflect that attachment + auth tables carry `UserId` + RLS and the set is model-derived

**Settings / Dashboard cycle math**
- Renamed `Settings.BudgetPeriodStartDay` to `Settings.PeriodStartDay` to reflect that it now drives every monthly view (Cycle to Date, Spending by Category, Income vs. Avg, Budget periods, etc.), not just budgets. UI label is now "Monthly cycle start day."
- "Month to Date" dashboard card renamed to "Cycle to Date" — its window now follows the user's configured monthly cycle, not strict calendar months.
- "Spending by Category" dashboard card subtitle is now "This period" (was "This month") and respects the configured cycle.

**Dashboard**
- `CategoryBudgetBars` and `GoalBudgetBars` refactored to render bare body without owning Card chrome (parent owns chrome)
- `CardTitle` accessibility upgrade: `<div>` → `<h3>` with `font-semibold`

**Movements**
- `MovementsTable` Type column migrated to `<Badge>` — Transaction = info, Transfer = chart-6 violet, Liability Payment = chart-7 orange (chart palette opt-out via className for non-status colors)
- `MovementClearedToggle` migrated to `<Badge variant="success|warning">`

**Components**
- `<CardError>` moved from `src/app/features/dashboard/` to `src/app/components/` (cross-feature primitive); 10 import paths updated via `git mv`
- shadcn `<Button>` primitive: `cursor-pointer` added to base classes (every interactive button across the SPA)

**Frontend**
- `AppLayout` grid bounded with `h-screen` (was `min-h-screen`) so only `<main>` scrolls and sidebar footer (Settings/Support/Collapse) stays pinned
- `TopBar` Cmd+K hint shows `Ctrl+K` on non-Mac platforms via `isMac()` runtime check
- All 5 chart `<ResponsiveContainer>` instances given `minWidth={0}` to silence Recharts "width(-1)" warnings
- tsconfig: deprecated `baseUrl` removed (TypeScript bundler resolution handles `paths` without it)

**Razor**
- `/Dashboard` now 302-redirects to `/app/`; Razor dashboard view (`Index.cshtml`), partial (`_HealthSnapshot.cshtml`), and MVC `DashboardController` deleted
- `data-react` mounting blocks for the 5 chart selectors removed from the legacy Razor `main.tsx`
- `/Movements` now 302-redirects to `/app/movements`; Razor `Views/Movements/Index.cshtml` deleted; MVC `MovementsController` reduced to redirect-only stub
- Razor `TransactionsController` and `TransfersController` page actions (Index/Create/Edit/Delete) now 302-redirect to the SPA at `/app/movements*`
- TopBar quick-add button and keyboard shortcut suppressed on `/app/movements*` routes (the routed Create page replaces the modal there)
- Razor `BudgetsController` page actions (Index, Goals, Create, CreateGoal, Edit, EditGoal, Deactivate, DeactivateGoal) now 302-redirect to the SPA at `/app/budgets/*`

**Budgets**
- `ICategoryBudgetService.GetActualSpendAsync(id, year, month)` semantics shift: `(year, month)` now identifies the period whose end falls in that calendar month, computed via the `BudgetPeriod` helper. Behavior unchanged for the default `BudgetPeriodStartDay = 1`

#### Fixed

**Authentication (Stage 6c.2 follow-up — `AuthMfaByUser` rate-limit partition fix, 2026-05-11)**
- `AuthMfaByUser` rate-limit policy now correctly partitions per user. The prior implementation read `httpContext.User?.FindFirst(NameIdentifier)` before `UseAuthentication` ran, so every authenticated MFA request fell into the `"anonymous-mfa"` shared bucket and one hostile user could exhaust the budget for everyone. Fix mirrors `AuthReauthByUser` (shipped in 6c.2): explicit `httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme).Wait()` before reading the claim, applied to both `Program.cs` and the `RateLimitedAuthTestWebApplicationFactory` override.
- New `MfaRegenerate_rate_limit_is_partitioned_by_user` test pins per-user isolation: drains user A's budget then asserts user B's first call is NOT 429.

**Authentication (Stage 6.15 — Argon2id-amplification DoS on token verify, 2026-05-11)**
- Argon2id-amplification DoS vector on `/password-reset/confirm`, `/email-change/confirm`, and `/email-change/revoke` closed via indexed `TokenLookup` column. Verify path is now O(1) regardless of token table size; pre-6.15 each call ran one Argon2id verify per unconsumed unexpired row (~20s at N=200 under OWASP-minimum params).
- New `TokenLookupHasher` singleton computes `HMAC-SHA256(Authentication:TokenLookupSecret, rawToken)`; new column added to `PasswordResetToken` and `EmailChangeToken` with a unique index per table; existing rows backfilled and stamped `ConsumedAt = NOW()` so legacy tokens cannot match real verifies.
- Test-infrastructure crutch removed: `AuthTestTokenCleanup.DeleteAllTestTokensAsync` + 12 `IAsyncLifetime.DisposeAsync` hooks deleted. The full Authentication integration suite (298 tests) now stays green under accumulated token load, proving the production fix is real rather than masked by between-test cleanup.

**Quick-Add**
- Quick-add modal: per-tab fields (account, category, source/dest, asset/liability) reset on tab switch so a stale selection from another tab can't leak in (shared fields like date, amount, description still persist)
- Combobox labels rendered as `<div>` instead of `<label>` since there's no input element to associate (fixes a11y "label without for" warning)

#### Removed

**Razor**
- `ProjectCeres/Helpers/DashboardViewHelper.cs` (server-side runway color helper) — only consumer was the deleted `_HealthSnapshot.cshtml` partial
- `ProjectCeres.Tests/DashboardViewHelperTests.cs`
- Razor views for Transactions and Transfers (Index, Create, Edit, Delete) and `Views/Shared/_AttachmentWidget.cshtml` deleted
- Razor views for Budgets (8 files under `Views/Budgets/`) deleted
- `BudgetsController` POST overloads (Create, CreateGoal, Edit, EditGoal, Deactivate, DeactivateGoal) removed entirely — the SPA POSTs JSON to the new `/api/category-budgets` and `/api/goal-budgets`

---

## [0.3.0] — 2026-04-26

### Phase 2

#### Added

**Transactions**
- Goal Budget field on Transaction Create and Edit forms is now hidden when no active Spending-type goal budgets exist — avoids showing an empty, non-functional dropdown
- Goal Budget field on Transaction Edit hidden when no Spending budgets match the transaction's account currency — prevents tagging a transaction to a mismatched budget
- `NeedsReview bool` column on `Transaction` model — set to `true` by `ImportService` when an imported row is flagged as a duplicate candidate; defaults to `false` for all manually created transactions
- `ITransactionService.MarkNeedsReviewAsync` — sets or clears `NeedsReview` on a transaction by ID
- `TransactionService.MarkNeedsReviewAsync` — implementation of the above
- `NeedsReview` field added to `TransactionListItemViewModel` and `TransactionEditViewModel`; mapped through `GetRecentAsync` and `UpdateAsync`

**Typography**
- Inter variable font self-hosted under `wwwroot/fonts/inter/` — two files cover all weights and italic variants
- IBM Plex Mono self-hosted under `wwwroot/fonts/ibm-plex-mono/` — Regular, Italic, Medium, MediumItalic, SemiBold, SemiBoldItalic, Bold, BoldItalic weights
- Inter set as the global body font for all UI text (labels, headings, buttons, body copy)
- IBM Plex Mono applied to `.amount-income`, `.amount-expense`, and `.amount-neutral` CSS classes — numeric columns in transaction and movements tables now render in monospace for clean vertical digit alignment

**Migrations**
- `AddTransactionNeedsReview` — adds `NeedsReview boolean NOT NULL DEFAULT FALSE` to the `Transactions` table

**Tests**
- `ClearedBadge.test.tsx` — 2 new tests: `needsReview = true` renders "Needs review" badge; `isCleared = true` with `needsReview = true` still renders "Cleared" (cleared state takes priority)

#### Changed

**Transactions**
- Goal Budget label updated to "Goal Budget (must match account currency)" on both Create and Edit forms
- `PopulateViewBagAsync` refactored to accept an optional `currencyFilterAccountId` parameter; on Edit, filters Spending budgets to those matching the selected account's currency; sets `ViewBag.Budgets = null` (hiding the field) when no qualifying budgets exist

**CSV Import**
- `ImportService.ImportAsync` — flagged duplicate rows now call `MarkNeedsReviewAsync(true)` in addition to leaving `IsCleared = false`; the `NeedsReview` flag drives the badge in the Transactions Index

**Movements**
- `ClearedBadge` component updated with a third render state: when `isCleared = false` and `needsReview = true`, renders an amber `AlertTriangle` badge labelled "Needs review" instead of the `Clock` "Pending" badge
- `ClearedBadge` now accepts optional `needsReview` prop (defaults to `false`); existing callers (Movements, Transfers Index) are unaffected
- `main.tsx` — `ClearedBadge` mount now reads `data-needs-review` dataset attribute and passes it as the `needsReview` prop
- `Transactions/Index.cshtml` — cleared badge mount point now emits `data-needs-review` from `item.NeedsReview`

**CsvImportProfiles**
- `CsvImportProfiles/Index.cshtml` — deleted profile countdown wording corrected to "Recoverable for X more day(s)"; Recover button SVG updated to the correct Lucide `rotate-ccw` path

#### Fixed

**Tests**
- `TransactionServiceTests` — 4 new integration tests: budget currency mismatch on Create throws, budget currency match on Create succeeds, same two cases for Update
- `BudgetServiceTests` and `GoalBudgetServiceTests` — constructor call updated to pass `IAccountService` as the second argument; pre-existing compilation error surfaced when the test project compiled after the constructor signature change
- `GoalBudgetServiceTests.GetProgressAsync_SavingsGoal_BalanceGrowsWithTransactions` — test was seeding an Expense transaction to grow a Savings account balance; corrected to use the Salary (Income) category so the transaction correctly increases the account balance

---

#### Added

**Budgets**
- `CategoryBudgetBars` component upgraded to use shadcn `Card`, `CardHeader`, `CardTitle`, `CardContent`, `Progress`, and `Badge` — progress bars now colour-coded green/amber/red by percent used
- `GoalBudgetBars` component upgraded to use shadcn `Card`, `CardHeader`, `CardTitle`, `CardContent`, and `Progress` — goal type shown as a blue pill badge matching the Goals index table
- Dashboard layout updated with two side-by-side mount points (`data-react="category-budget-bars"` and `data-react="goal-budget-bars"`) wired in `main.tsx`

**Movements**
- Type column in Movements table now renders colour-coded pill badges — blue for Transaction, purple for Transfer, orange for Liability Payment
- `CategoryTypeName` field added to `MovementListItemViewModel`; projected from `t.Category.CategoryType.Name` in `MovementService.QueryTransactions` via `ThenInclude`
- Amount column in Movements table now colour-coded — green for Income, red for Expense (matching the Transactions table), neutral for Transfers and Liability Payments

**Reports**
- `BudgetVsActualReportGenerator` — compares active category budget limits against actual spend for a given currency and date range; returns per-category rows with limit, actual, variance, and percent used
- `LargestExpensesReportGenerator` — returns top N expense transactions ordered by amount descending for a given currency and date range; respects `Limit` parameter (default 50)
- `MonthlyCashFlowReportGenerator` — returns income, expenses, and net grouped by calendar month for a given currency and date range; defaults to last 6 months when no range supplied
- `NetWorthOverTimeReportGenerator` — returns cumulative asset, liability, and net worth snapshots at the end of each month for a given currency and date range; defaults to last 12 months
- `ReportsController` actions: `BudgetVsActual`, `LargestExpenses`, `MonthlyCashFlow`, `NetWorthOverTime` — each reads from the corresponding generator with currency/date defaults from Settings
- `Views/Reports/BudgetVsActual.cshtml` — filter bar + table with limit/actual/variance/% used columns; over-budget rows highlighted in red
- `Views/Reports/LargestExpenses.cshtml` — filter bar with Top N selector (10/25/50/100) + table with date, description, category, account, amount
- `Views/Reports/MonthlyCashFlow.cshtml` — filter bar + month-by-month table with income, expenses, net columns; period totals in tfoot
- `Views/Reports/NetWorthOverTime.cshtml` — filter bar + monthly snapshot table with assets, liabilities, net worth columns
- Reports Index updated with links to all four new reports
- `ReportTypeKey` enum extended with `BudgetVsActual = 5`, `LargestExpenses = 6`, `MonthlyCashFlow = 7`, `NetWorthOverTime = 8`
- `_ViewImports.cshtml` — `@using ProjectCeres.Services.Reports` added so report row record types are available in all views

**Migrations**
- `Stage7ReportTypeSeed` migration — inserts `ReportType` rows for the four new report types (IDs 5–8); `Up()` contains only `InsertData` operations, no schema changes

**Docs**
- Developer guide updated: `GroupBy` with anonymous object key and `GroupBy` + `Select` summary pattern in LINQ file; cumulative snapshot pattern (one-query-then-filter-in-memory) in EF Core querying file; extending the factory checklist and enum–DB alignment rule in factory pattern file; seeding lookup table rows pattern with four-step workflow in migrations file

**Tests**
- 16 integration tests for the four Stage 7 generators: `BudgetVsActual` (returns correct plan vs. actual, excludes out-of-range transactions, excludes inactive budgets, shows zero actual when no spend), `LargestExpenses` (orders by amount desc, respects limit, excludes income, excludes out-of-range), `MonthlyCashFlow` (groups by month, excludes system transactions, filters by currency, omits months with no activity), `NetWorthOverTime` (monthly snapshots, includes liabilities, filters by currency, snapshots are cumulative)
- 4 factory dispatch unit tests added to `ReportGeneratorFactoryTests` — one per new generator

#### Changed

**Reports**
- `ReportGeneratorFactory` constructor extended with four new generator parameters; switch extended with four new cases
- `ReportsController` — injects the four new generators directly as constructor parameters (not via factory) since each has a dedicated action
- `AppDbContext.HasData` — `ReportType` seed extended from 4 rows to 8 rows
- `Program.cs` — four new `AddScoped` registrations for Stage 7 generators

**Budgets**
- `BudgetService` constructor updated to accept `IAccountService`; `GetAccountBalanceAsync` now delegates to `AccountService.GetBalanceAsync` — fixes Savings goal progress showing incorrect balance (transfers were excluded)
- `Budgets/Index.cshtml` — Card + Table layout, status as coloured pill badge, Edit button has pencil icon, Deactivate button has power-off icon, New button has plus icon
- `Budgets/Goals.cshtml` — same Card + Table upgrade; GoalType shown as blue pill badge; Edit/Deactivate icons
- `Budgets/Create.cshtml` and `Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, Save/Cancel buttons have check/x icons
- `Budgets/CreateGoal.cshtml` and `EditGoal.cshtml` — same form styling upgrade; conditional Linked Account field preserved
- `Budgets/Deactivate.cshtml` and `DeactivateGoal.cshtml` — descriptive confirmation card with power-off icon on the confirm button

**Transactions**
- `Transactions/Index.cshtml` — Edit/Delete row buttons upgraded to `btn-sm` with pencil/trash-2 icons; New Transaction button has plus icon
- `Transactions/Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, attachment list has download/trash icons, Save/Cancel buttons have check/x icons

**Transfers**
- `Transfers/Index.cshtml` — Edit/Delete row buttons upgraded with pencil/trash-2 icons; New Transfer button has plus icon
- `Transfers/Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, attachment list has download/trash icons, Save/Cancel buttons have check/x icons

**Movements**
- `Movements/Index.cshtml` — Edit/Delete row action buttons upgraded with pencil/trash-2 icons; static cleared/pending spans for Liability Payments converted to pill badge style

**Frontend**
- File input (`input[type="file"].form-control`) globally styled in `app.css` using Tailwind `file:` pseudo-element utilities — picker button now shows as a styled pill with a right-border divider, matching the rest of the form controls

**Docs**
- Developer guide updated: `file:` pseudo-element utilities for styling native file inputs (Tailwind guide); `Progress` + `Card` data-display panel pattern (shadcn/ui guide); `ThenInclude` formal definition for multi-level eager loading (EF Core querying guide)

---

#### Added

**Recurring Reminders**
- `ReminderBehaviour` dispatch in `RecurringTransactionService` — `ConfirmAsync` and `DismissAsync` now route date advancement through three strategies: `SnapToCalendarDay` (advances to `DayOfPeriod` in the next calendar month, skips an extra month if confirmed on or after that day), `RelativeToLastConfirmation` (advances from the actual confirm date rather than the scheduled due date), `ManualDate` (throws `InvalidOperationException` unless a `nextDueDate` is supplied)
- `IRecurringTransactionService.GetUpcomingAsync(int withinDays)` — returns active reminders with `NextDueDate` between today and today + N days, ordered by due date
- `RecurringTransactionsController.Upcoming` action — serves the Upcoming Payments view at `/RecurringTransactions/Upcoming`
- `Views/RecurringTransactions/Upcoming.cshtml` — table of reminders due within 30 days; rows due today highlighted with an amber "Due today" badge
- Navbar upcoming-payments badge — server-rendered count of reminders due within 30 days passed to the React `Navbar` component via a `data-upcoming-count` attribute on `#navbar-root`

**Docs**
- Developer guide updated: string-based switch expression dispatch, `@inject` in `_Layout.cshtml` for layout-level service calls, `data-*` attribute bridge for passing server values to React components, `DateOnly` range filter pattern in EF Core queries

**Tests**
- 5 integration tests for `ReminderBehaviour` advancement: `SnapToCalendarDay` on-time → correct next month snap, `SnapToCalendarDay` confirmed late → skips forward an extra month, `RelativeToLastConfirmation` monthly → advances from confirm date, `ManualDate` without `nextDueDate` → throws, `ManualDate` with `nextDueDate` → sets exact date
- 1 integration test for `GetUpcomingAsync` — reminders due today and in 5 days included; reminder due in 35 days excluded

#### Changed

**Recurring Reminders**
- `IRecurringTransactionService.ConfirmAsync` — signature extended with optional `DateOnly? nextDueDate` parameter (backward-compatible default `null`)
- `RecurringTransactionCreateViewModel` / `RecurringTransactionEditViewModel` — `ReminderBehaviour` field added (defaults to `"SnapToCalendarDay"`)
- `RecurringTransactionService.CreateAsync` / `UpdateAsync` — `ReminderBehaviour` now persisted from ViewModel
- `Views/RecurringTransactions/Create.cshtml` and `Edit.cshtml` — `ReminderBehaviour` selector added; inline JavaScript hides `DayOfPeriod` field when `ManualDate` is selected
- `Views/RecurringTransactions/Confirm.cshtml` — `nextDueDate` date picker rendered when reminder's behaviour is `ManualDate`
- `RecurringTransactionsController.Confirm` POST — accepts optional `nextDueDate` parameter and forwards it to `ConfirmAsync`
- `_Layout.cshtml` — injects `IRecurringTransactionService` to compute upcoming count server-side; count embedded as `data-upcoming-count` on `#navbar-root`
- `main.tsx` — reads `data-upcoming-count` from `#navbar-root` and passes it as `upcomingPaymentsCount` prop to `<Navbar>`

---

**Accounts**
- `ILiabilityProjectionService` / `LiabilityProjectionService` — pure calculation service for amortising loan payoff projection; computes months to payoff, payoff date, total interest, and total paid given balance, annual rate, and monthly payment; throws when payment does not cover first month's interest
- Payoff projection panel on the Account Ledger view for Amortising accounts — form accepts a monthly payment amount and returns projection stats (payoff date, months, total interest, total paid); only rendered when account is Amortising with a non-zero balance
- `LiabilityProjectionViewModel` — read model carrying `MonthsToPayoff`, `PayoffDate`, `TotalInterest`, `TotalPaid`, `MonthlyPayment`

**Docs**
- Developer guide updated: pure calculation service pattern (no `DbContext` dependency, directly unit-testable), service-layer business rule validation via `InvalidOperationException`, conditional field visibility via inline JavaScript in Razor views

**Tests**
- 5 integration tests for `AccountService` — Amortising with null interest rate rejected, FullMonthly with interest rate rejected, Amortising with valid rate succeeds, `UpdateAsync` variants for both failure cases
- 5 unit tests for `LiabilityProjectionService` — known inputs verify payoff and interest range, extra payment yields earlier payoff and less interest, zero interest rate pays off in balance ÷ payment months, very small balance pays off in 1 month, payment too small to cover interest throws

#### Changed

**Accounts**
- `AccountCreateViewModel` — added `LiabilityRepaymentType` and `InterestRate` fields
- `AccountEditViewModel` — added `LiabilityRepaymentType` and `InterestRate` fields
- `AccountService.CreateAsync` — validates repayment type / interest rate rules for Liability accounts before saving; maps `LiabilityRepaymentType` and `InterestRate` onto the new entity
- `AccountService.UpdateAsync` — same validation and mapping on edit; loads `AccountType` via `Include` to access the type name
- `AccountsController` — injects `ILiabilityProjectionService`; `Edit` GET passes `ViewBag.IsLiability`; `Ledger` GET and POST compute and pass projection via ViewBag when account is Amortising with a positive balance
- `Views/Accounts/Create.cshtml` — liability repayment type selector and interest rate field added; JavaScript show/hide driven by account type and repayment type selectors
- `Views/Accounts/Edit.cshtml` — liability repayment type selector and interest rate field added (server-side conditional on `ViewBag.IsLiability`); JavaScript toggles interest rate field based on repayment type
- `Views/Accounts/Ledger.cshtml` — payoff projection panel added; rendered only for Amortising accounts with a positive balance
- `Program.cs` — `ILiabilityProjectionService` registered as scoped

---

**CSV Import**
- `ICsvImportProfileService` / `CsvImportProfileService` — full CRUD for import profiles with soft-delete and 90-day recovery window; column mappings stored as `jsonb` and deserialized to `CsvColumnMappings`
- `CsvImportProfilesController` — Index, Create, Edit, Delete (soft-delete), Recover; Razor views with Lucide icons
- `IImportService` / `ImportService` — CSV parsing via CsvHelper with user-configured column mappings, optional debit sign flip, and SHA-256 fingerprinting for duplicate detection
- `ImportApiController` at `POST /api/import` — accepts multipart form with CSV file and column mapping parameters; returns `ImportResult` JSON
- `ImportController` — Razor upload form with profile selector and manual column mapping fields; Summary page showing imported/flagged/failed counts
- `Views/Import/Index.cshtml` and `Summary.cshtml` — upload form and per-category result summary
- `CsvColumnMappings` ViewModel — carries user-configured column names and flip-debit-sign flag
- `ImportResult` ViewModel — carries `RowsImported`, `RowsFlagged`, `RowsFailed`, and a list of row-level error messages
- `ParsedImportRow` ViewModel — intermediate row produced by `ParseAsync` before persistence
- `ImportRequestViewModel` — model-bound from the multipart form for `ImportApiController`
- `ImportUploadViewModel` / `ImportSummaryViewModel` — ViewModels for the Razor upload and summary pages
- Fixture files: `valid_import.csv`, `duplicate_candidates.csv`, `invalid_rows.csv`, `xlsx_attempt.xlsx` — used by unit and integration tests; registered with `CopyToOutputDirectory: PreserveNewest`

**Docs**
- Developer guide updated: CSV parsing with CsvHelper, SHA-256 fingerprinting, soft-delete with time-bounded recovery, `jsonb` column mapping in EF Core, `Mock<IFormFile>` with `CopyToAsync` setup, fixture files via `CopyToOutputDirectory`, optional constructor parameters for partial unit testability

**Tests**
- 5 integration tests for `CsvImportProfileService` — create/retrieve, soft-delete exclusion from active list, 90-day purge window, update mappings, recover within window
- 6 unit tests for `ImportService` — `ParseAsync` with valid CSV, debit sign flip, custom column mapping, XLSX rejection, `GenerateFingerprint` determinism, fingerprint sensitivity to amount change
- 3 integration tests for `ImportService.ImportAsync` — 10 rows all cleared, duplicate flagged as `IsCleared = false`, result count correctness
- 3 `WebApplicationFactory` tests for `ImportApiController` — shape test, missing `accountId` → 422 with `VALIDATION_ERROR`, `.xlsx` file → 400 with message

**Movements**
- `MovementsApiController` at `PATCH /api/movements/{id}/cleared` — routes to `ITransactionService` or `ITransferService` based on `type` field in request body; returns 404 for unknown id, 400 for unknown type
- React `ClearedBadge` component — clickable badge that calls `PATCH /api/movements/{id}/cleared`, flips state optimistically on click, and reverts to original state on API error or network failure
- `MovementsApiTests` — 5 `WebApplicationFactory` integration tests covering transaction clear, toggle back to false, transfer clear, unknown id → 404, and unknown type → 400
- `ClearedBadge.test.tsx` — 5 Vitest tests covering static rendering (Cleared/Pending), PATCH call correctness, optimistic flip, revert on HTTP error, and revert on network error
- `ClearedBadge` mount point in `main.tsx` — mounts from `[data-react="cleared-badge"]` elements; reads `data-id`, `data-movement-type`, and `data-cleared` dataset attributes
- `MovementsController` with `Index` action — unified ledger showing all three movement types (Transaction, Transfer, LiabilityPayment) interleaved, with account/date filters and pagination
- `Views/Movements/Index.cshtml` — unified table rendering all three row types with type-specific column display, cleared badge, and Edit/Delete links that pass `returnUrl=/Movements`
- `MovementsControllerTests` — 10 `WebApplicationFactory` tests covering: `GET /Movements` returns 200, Transaction/Transfer Edit and Delete redirect to `returnUrl` when present and local, fall back to own Index when absent, and open redirect safety (external URL rejected)
- `WafCollection.cs` — `[CollectionDefinition("IntegrationTests")]` grouping all 19 integration test classes into one xUnit collection to prevent parallel races on `project_ceres_test`

**Security**
- Open redirect rule added to `docs/security-model.md` under Input Validation Rules and the phase table — `Url.IsLocalUrl(returnUrl)` required on every action that accepts a `returnUrl` parameter

**Budgets**
- `ICategoryBudgetService` / `CategoryBudgetService` — monthly spend caps for expense categories; guards against non-expense categories and duplicate active budgets
- `CategoryBudgetService.GetActualSpendAsync(id, year, month)` — caller-specified period for current dashboard use and future Budget vs. Actual reports
- `BudgetsController` — single controller covering Category Budgets (Index, Create, Edit, Deactivate) and Goal Budgets (Goals, CreateGoal, EditGoal, DeactivateGoal)
- `BudgetProgressResult` model — computed `Remaining` and `PercentUsed` properties; never stored as columns
- Goal Budget `GoalType` and `LinkedAccountId` — two archetypes: Spending (sums tagged transactions) and Savings (reads linked account balance)
- `BudgetService.GetProgressAsync` — returns `BudgetProgressResult` for a goal budget; routes to transaction-sum or account-balance query based on `GoalType`
- `DashboardApiController` at `/api/dashboard/category-budgets` and `/api/dashboard/goal-budgets` — JSON endpoints for React components
- React `CategoryBudgetBars` component — fetches category budgets, renders progress bars colored green/amber/red by percent used
- React `GoalBudgetBars` component — fetches goal budgets, renders progress bars in blue/green

**Views**
- Category Budgets CRUD views: Index, Create (expense categories only), Edit, Deactivate confirmation
- Goal Budgets CRUD views: Goals index, CreateGoal, EditGoal (with inline JS show/hide for LinkedAccount field), DeactivateGoal confirmation

**Architecture Decision Records**
- ADR-0056 — `GetActualSpendAsync` caller-specified year/month signature
- ADR-0057 — single `BudgetsController` with documented refactor trigger conditions

**Tests**
- `TestWebApplicationFactory` — custom `WebApplicationFactory<Program>` subclass that overrides `ConnectionStrings:DefaultConnection` to `project_ceres_test` in `ConfigureWebHost`; replaces bare `WebApplicationFactory<Program>` as the shared collection fixture, making test database isolation structural rather than per-class
- 11 integration tests for `CategoryBudgetService` (all guards, actual spend calculation, deactivate, getAll)
- 8 integration tests for `GoalBudgetService` (GoalType validation, GetProgressAsync for both archetypes)
- 2 `WebApplicationFactory` tests for `DashboardApiController` verifying JSON shape
- 4 Vitest component tests for `CategoryBudgetBars` and `GoalBudgetBars` (mocked fetch, async DOM assertions)

**Transfer Attachments**
- `IFileAttachmentService` extended with three new methods: `UploadForTransferAsync`, `GetTransferAttachmentAsync`, `DeleteTransferAttachmentAsync` — same MIME whitelist and size limit as transaction attachments; transfer files stored under `uploads/transfers/{transferId}/`
- `AttachmentsController` — three new actions: `UploadForTransfer` (POST), `DownloadTransfer` (GET), `DeleteTransfer` (POST)
- Attachment section added to `Views/Transfers/Edit.cshtml` — file list with download links and per-attachment Remove button; file input with accepted type hint; out-of-form delete forms linked via HTML `form=` attribute

**Docs**
- Developer guide updated: extending a service interface to support a second entity type (reuse vs. split decision), subdirectory isolation for multi-entity file storage

**Tests**
- `TransferAttachmentServiceTests` — 4 integration tests: upload persists DB record and writes file to disk, serve returns correct data and metadata, delete removes DB record and file from disk, spoofed file type rejected with "not allowed" message

#### Changed

**Transfer Attachments**
- `TransferEditViewModel` — added `IFormFile? Attachment` property
- `TransfersController` — injects `IFileAttachmentService`; Edit GET loads existing attachments into `ViewBag`; Edit POST handles optional file upload after record save; `enctype="multipart/form-data"` added to the Edit form

**CSV Import**
- `ProjectCeres.csproj` — CsvHelper 33.0.1 added

**Movements**
- `Movements/Index.cshtml` — static cleared/pending badge replaced with `ClearedBadge` React mount point for Transaction and Transfer rows; LiabilityPayment rows retain a static read-only badge
- `Transfers/Index.cshtml` — Status column added with `ClearedBadge` React mount point per row
- `Transactions/Index.cshtml` — Status column replaced with `ClearedBadge` React mount point; inline form toggle (ToggleCleared POST) removed from the Actions column
- Navbar updated: "Movements" added as a primary nav link between Dashboard and Accounts

**Transactions**
- `TransactionsController` Edit and Delete (GET + POST) accept optional `returnUrl` — redirects to it after success if local, falls back to `/Transactions` otherwise
- `Views/Transactions/Edit.cshtml` and `Delete.cshtml` — hidden `returnUrl` field added; Cancel link respects `returnUrl`
- Goal Budget dropdown on Transaction Create/Edit now filters to `GoalType == "Spending"` only — Savings goals track progress via account balance, not transaction tagging

**Transfers**
- `TransfersController` Edit and Delete (GET + POST) accept optional `returnUrl` — same pattern as Transactions
- `Views/Transfers/Edit.cshtml` and `Delete.cshtml` — hidden `returnUrl` field added; Cancel link respects `returnUrl`

**Navbar**
- Added Budgets and Import links between Categories and Reminders

**Data Models**
- `Budget` entity extended with `GoalType` (required) and `LinkedAccountId` (nullable FK to Account)

**Tests**
- `ApiInfrastructureTests`, `DashboardApiTests`, `MovementsApiTests`, `MovementsControllerTests` — updated to accept `TestWebApplicationFactory` instead of `WebApplicationFactory<Program>`; per-class `WithWebHostBuilder`/`UseSetting` overrides removed as redundant
- All 19 integration test classes annotated with `[Collection("IntegrationTests")]` — eliminates parallel races between `WebApplicationFactory` tests and `TestDbFixture` tests on `project_ceres_test`
- `MovementsControllerTests` WAF now overrides `ConnectionStrings:DefaultConnection` to target `project_ceres_test` instead of the dev database; seeded rows deleted via `ExecuteDeleteAsync` in `DisposeAsync`

#### Fixed

**Tests**
- WAF tests were seeding data into the dev database (`project_ceres`) because individual test classes forgot to call `WithWebHostBuilder`; structural fix via `TestWebApplicationFactory` subclass makes this impossible going forward; orphaned rows cleaned from dev database (6 transactions, 3 transfers, 12 accounts removed)
- `ReportServiceTests` and `MovementServiceTests` were flakily failing when run in parallel with WAF tests — WAF tests were writing to `project_ceres_test` concurrently with `TestDbFixture` rollback transactions; resolved by the `[Collection("IntegrationTests")]` grouping
- WAF tests were seeding data into the dev database (`project_ceres`) because no connection string override was in place; dev database cleaned (39 accounts, 6 transfers, 9 transactions removed)

**Movements**
- `ClearedBadge` was rendering with identical gray styling for both Cleared and Pending states because `badge-success` and `badge-warning` CSS classes were not defined; replaced with Tailwind utility classes (`bg-green-100 text-green-700` for Cleared, `bg-yellow-100 text-yellow-700` for Pending)

#### Removed

**Frontend**
- `HelloWorld` component and its test removed — React pipeline verification complete, component no longer needed

---

## [0.2.0] — 2026-04-21

### Phase 1

#### Added

**Project scaffold**
- ASP.NET Core MVC project scaffold (`ProjectCeres/`) with Controllers, Models, Views, Data, Services, ViewModels, Helpers, Filters, ModelBinders layers
- xUnit test project (`ProjectCeres.Tests/`) with solution file (`ProjectCeres.sln`)

**Data model**
- All 13 EF Core entity models: `Account`, `AccountType`, `Category`, `CategoryType`, `Currency`, `Transaction`, `TransactionAttachment`, `Transfer`, `Budget`, `CategoryBudget`, `RecurringTransaction`, `ReportType`, `SavedReport`, `Settings`
- `AppDbContext` with full Fluent API configuration, seed data for all system-defined lookup tables, and `Frequency` enum stored as string
- `LiabilityPayment` model — dedicated entity for recording debt payments from an asset account to a liability account (`Models/LiabilityPayment.cs`)

**Migrations**
- Initial EF Core migration (`InitialCreate`) applied to local PostgreSQL database
- Migration `RemoveDateSeparator` — dropped redundant `DateSeparator` column from `Settings`
- Migration `AddLiabilityPayment` — adds `LiabilityPayments` table with FK references to `Accounts`

**Features**
- Full CRUD for: Accounts, Categories, Transactions, Transfers, Recurring Transactions, Settings
- Deactivate (soft-delete) flows for Accounts and Categories
- Hard-delete with confirmation for Transactions and Transfers
- Four financial reports: Net Worth Statement, Income & Expense Summary, Expense Breakdown by Category, Transaction History
- Dashboard with month-to-date income/expense totals, savings rate, and pending recurring transaction reminders
- Opening balance management on Account Create and Edit — stored as a system-managed `Transaction` with `Category.IsSystem = true`, excluded from all reports
- File attachment upload, serve, and delete for transactions (`FileAttachmentService`) — magic-byte MIME validation via Mime-Detective, filesystem storage outside `wwwroot/`, system-generated storage paths, re-verification at serve time

**Accounts**
- Per-account balance ledger view at `/Accounts/{id}/Ledger` — shows every entry contributing to the account balance (opening balance, transactions, transfers, liability payments) in chronological order with a running balance column; linked from the Accounts index

**Transactions**
- File attachment field on the New Transaction form — optional, regular transactions only; validates magic bytes and file size before saving the transaction record
- File attachment upload on Edit Transaction — new attachment uploaded as part of the Save Changes submission; no separate Upload button required

**Liability payments**
- `ILiabilityPaymentService` / `LiabilityPaymentService` — full CRUD for liability payments with validation (currency match, account type guards, date-before-opening-balance guard)
- `TransactionListItemViewModel` — unified read model for the Transactions Index list that represents either a regular `Transaction` or a `LiabilityPayment` row via a `TransactionType` discriminator field
- `TransactionService.GetByIdForEditAsync` — returns a fully-populated `TransactionEditViewModel` covering both regular and liability-payment records
- `TransactionsController` — conditional server-side validation that removes `CategoryId` requirement for `LiabilityPayment` type and `LiabilityAccountId` requirement for regular transactions

**Number formatting**
- `NumberFormatHelper` — locale-aware decimal formatting for display (`FormatAmount`) and input pre-fill (`FormatInputValue`)
- `NumberFormatActionFilter` — global `IAsyncActionFilter` that injects `ViewData["NumberFormat"]` before every controller action
- `DecimalModelBinder` / `DecimalModelBinderProvider` — parses all `decimal` and `decimal?` form fields using the user's configured culture, with invariant-culture fallback for copy-pasted values

**Services**
- `IFileAttachmentService.ValidateAsync` — pre-save file validation method that runs size and magic-byte checks without writing anything; used by the Create flow to fail fast before any DB write

**Tests**
- Unit tests: `BalanceCalculationTests` (6 tests), `SavingsRateTests` (7 tests), `CategoryBudgetGuardTests`
- Unit tests for `NumberFormatHelper` — `FormatAmount`, `FormatInputValue`, and `TryParseDecimal` including round-trip correctness (12 tests in `NumberFormatHelperTests.cs`)
- Unit tests for decimal parsing — both number format modes, invariant-input tolerance, thousands separators, and invalid input (12 tests in `DecimalParsingTests.cs`)
- Integration tests: `TransferValidationTests` (3 tests) — real PostgreSQL database with per-test transaction rollback isolation via `TestDbFixture`
- Integration tests for `AccountService`, `CategoryService`, `TransactionService`, `TransferService`
- Integration tests for `BudgetService` — `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeactivateAsync`, and `GetActualSpendAsync`
- Integration tests for `RecurringTransactionService` — all 7 methods including `ConfirmAsync` and `DismissAsync`
- Integration tests for `ReportService` — all 4 report types with date range, account, category, and pagination filters
- Integration tests for `DashboardService` — MTD income/expense sums, savings rate, pending reminder count
- Integration tests for `SettingsService` — `GetAsync`, `UpdateAsync`, `EnsureExistsAsync` including create-from-scratch paths
- Integration tests for `FileAttachmentService` — `UploadAsync` (happy path + all validation guards), `GetAsync`, `DeleteAsync` using a per-test temp directory and `Mock<IWebHostEnvironment>`

**Frontend build**
- Tailwind CSS v3 build pipeline — pnpm + Tailwind CLI, input at `ProjectCeres/Styles/app.css`, output to `ProjectCeres/wwwroot/css/site.css`, wired into MSBuild pre-build target so `dotnet build` / `dotnet run` automatically regenerates CSS

**Documentation**
- Architecture Decision Records 0012–0031
- `docs/architecture.md` — layer model, request flow, phase evolution, frontend build pipeline section
- `docs/security-model.md` — threat model, data protection, access control
- `docs/api-contract.md` — API conventions, response shapes, versioning strategy
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan
- `docs/decisions/ADR-0004` — implementation note added documenting the tolerant decimal parsing fix and its implication for Phase 2 CSV/OFX import
- `docs/testing.md` — unit test priority list updated; Phase 1 unit and integration test coverage tables added
- `docs/guide/` — structured developer guide organized by stack topic; 23+ topic files across 7 modules
- `docs/roadmap-phase-one.md` — Phase 1 feature roadmap and verification checklist (fully checked)
- `docs/guide/07-testing/mocking-with-moq.md` — new guide file covering `Mock<T>`, `.Setup()`, `.Returns()`, and MIME detection test patterns
- `docs/guide/02-dotnet-platform/logging-with-ilogger.md` — new guide file covering `ILogger<T>` injection, log levels, structured placeholders
- `docs/guide/03-aspnetcore-mvc/model-binding-and-validation.md` — `DecimalModelBinder` section updated with tolerant parsing explanation and dry run of the corruption case
- `docs/guide/07-testing/test-structure-and-patterns.md` — new pattern added: extracting logic out of framework types for unit testability
- `dev-teacher` and `sync-docs` Claude Code skills added

#### Changed

**Transactions**
- `ITransactionService.CreateAsync` now returns `Guid` (the new record's ID) instead of `void`, enabling post-save operations like attaching a file
- Transaction form field order unified: Attachment field moved to after Description on both Create and Edit views
- Transaction Edit view: attachment upload merged into main form via `enctype="multipart/form-data"`; separate Upload form and button removed
- Transaction Edit view: per-attachment Remove forms moved outside the main `<form>` element and linked via HTML `form=` attribute — nested forms are silently ignored by browsers
- `TransactionService.DeleteAsync` now loads attachments and calls `FileAttachmentService.DeleteAsync` for each before removing the transaction row, ensuring disk cleanup on transaction delete
- `TransactionEditViewModel` — added `IFormFile? Attachment` property to support upload-on-save on the Edit flow
- `TransactionsController.Index` — now returns `IEnumerable<TransactionListItemViewModel>` (merged regular transactions + liability payments) instead of raw `Transaction` entities
- `TransactionsController.Create` POST — branches on `vm.TransactionType`; routes to `LiabilityPaymentService.CreateAsync` for `LiabilityPayment`, `TransactionService.CreateAsync` otherwise
- `TransactionCreateViewModel` / `TransactionEditViewModel` — added `TransactionType`, `LiabilityAccountId` fields to support the unified form

**Number formatting**
- `DecimalModelBinder` parsing logic extracted into `NumberFormatHelper.TryParseDecimal` — a pure static method with no framework dependencies, making it independently unit-testable
- All decimal display views updated to use `NumberFormatHelper.FormatAmount(...)` instead of `.ToString("N2")`
- All decimal input views updated to `type="text"` with explicit `value` pre-fill using `NumberFormatHelper.FormatInputValue(...)`

#### Fixed

**Number formatting**
- `DecimalModelBinder` silently corrupted amounts entered in invariant format (`100.00`) when the number format was set to `comma_decimal` — the period was interpreted as a thousands separator, producing `10000.00`; parsing order is now adjusted to detect and handle this case correctly
- `[Range(typeof(decimal), ...)]` attributes on ViewModels now use `ParseLimitsInInvariantCulture = true` — previously threw `FormatException` when the system locale used comma as decimal separator

**Recurring Transactions**
- Confirming a recurring reminder always reloaded the form without recording — `AccountId`, `CategoryId`, and `TransactionType` were missing from the Confirm view as hidden inputs, causing `ModelState.IsValid` to silently fail on every POST

**Transactions**
- Attachment upload section was absent from the New Transaction (Create) form — attachments could only be added by editing an existing transaction
- Remove button on Edit Transaction did not delete the file from disk or the DB record — the Remove `<form>` was nested inside the main edit `<form>`, causing browsers to silently discard it
- Deleting a transaction did not clean up its attached files from disk — `TransactionService.DeleteAsync` now iterates attachments and calls `FileAttachmentService.DeleteAsync` before removing the transaction row

**Settings**
- `SettingsService.UpdateAsync` — fixed dead guard (`if (settings.Id == 0)`) that prevented creating a settings row when none existed; replaced with an explicit `isNew` boolean

**Accounts**
- `AccountService.GetBalanceAsync` — transfer amounts are now correctly added/subtracted from account balances (`transfersIn` increases balance, `transfersOut` decreases it)
- `AccountService.GetBalanceAsync` — system (opening balance) transactions now always add to balance regardless of account type; regular transactions on liability accounts correctly apply inverted polarity

**Liability account balance**
- `ReportService` — liability account balances in Net Worth and Income & Expense reports now use the same polarity logic as `AccountService`, ensuring consistent figures across views
- Opening balance transactions on liability accounts were incorrectly being subtracted from the balance instead of added

**Reports**
- `GetTransactionHistoryAsync` was missing `!t.Category.IsSystem` filter — opening balance transactions were appearing in the Transaction History report

**Categories**
- Category Edit GET action was missing `PopulateViewBagAsync()` call — Lifestyle Tag dropdown rendered empty on the edit page

---

## [0.1.0] — 2026-01-01

### Added
- Initial project setup
- Full documentation: planning, models, legal, business model
- Architecture Decision Records 0001–0011
- Claude Code configuration (CLAUDE.md, sync-docs, hooks)
- `.env.example` with required environment variables
