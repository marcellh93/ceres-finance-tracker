# Stage 9.11 — Playwright E2E Foundations (design)

**Date:** 2026-06-11
**Status:** Draft — pending user review
**Origin:** [ADR-0071](../../decisions/ADR-0071-e2e-testing-on-playwright.md) (tool locked), `roadmap-phase-three.md` § Stage 9.11
**User decisions (2026-06-10/11):** file-sink email capture · dedicated `project_ceres_e2e` database · custom `ASPNETCORE_ENVIRONMENT=E2E` serving the production bundle (honors ADR-0071's "production-built SPA artefact").

## 1. Goal

Five browser-driven golden-path suites — register→login, password-reset, TOTP-enrol→first-login, lockout→self-service-unlock, backup-code-recovery — running against the real app (Kestrel + production SPA bundle + real PostgreSQL + real RLS), green on Chromium/Firefox/WebKit, runnable with one command, documented in `docs/testing.md`.

**Non-goals:** CI wiring (Stage 16.16 per ADR-0070/0071) · per-edge-case browser coverage (stays in the integration suite per ADR-0071) · non-auth surfaces · email-change link-click E2E (no SPA route exists — deferred to Stage 12 with `[ ]` + FIXME tripwire, see § 9).

## 2. What already exists (the audit's biggest correction)

The roadmap's "install dep / create e2e/" framing is stale. Shipped 2026-05-26 with the 9.5a agent-walk infra:

- `@playwright/test ^1.60.0` + `tsx` devDeps; `pnpm e2e` + `pnpm agent-walk` scripts.
- `ProjectCeres.Client/e2e/` with `playwright.config.ts` (testDir `.`, testMatch `*.spec.ts`, `baseURL` from `APP_URL`, workers 1, `ignoreHTTPSErrors`, trace/screenshot on), `agent-walk.spec.ts`, page objects (`LoginPage`, `RegisterPage`, `DashboardPage`, `AuthLayoutPage`), `flows/auth.ts`, `e2e/.gitignore`.
- A custom-environment precedent: `ASPNETCORE_ENVIRONMENT=Smoke` (launchSettings profile + `tools/agent-env/up.sh`) boots the app for the agent-walk against a per-session schema inside `project_ceres`.

**9.11 therefore extends, not creates.** New suites build on the existing page objects and flows. The agent-walk harness must keep working unchanged.

**E2E vs Smoke:** Smoke is a lightweight route-smoke harness — schema-per-session inside the shared dev DB, Vite dev serving, non-destructive. E2E is the destructive full-flow harness — dedicated `project_ceres_e2e` DB (run-start wipes), production bundle, real email-token round-trips. Different blast radii justify different environments (same idiom as ADR-0072's Sandbox).

## 3. Two pre-existing bugs pulled into scope

The audit found both verify-email and unlock-account emails build **root-path URLs with no server route** — a real browser following them gets a 404 (the SPA shell is mapped only at `app/{*path}`):

| Builder | Today | Must be | Page exists at |
|---|---|---|---|
| `EmailConfirmationService.cs:134` | `{base}/email-verify#token=` | `{base}/app/email-verify#token=` | `/app/email-verify` (App.tsx:82) |
| `LockoutUnlockService.cs:131` | `{base}/account/unlock#token=` | `{base}/app/account/unlock#token=` | `/app/account/unlock` (App.tsx:83, regression from commit `98eef2d`'s route rename) |

(`PasswordResetService.cs:188` already correct: `{base}/app/password-reset#token=`.)

These are 9.11 work items, not deferrals — the register and lockout golden paths literally click these links. Fix = two one-line URL changes + updating the two pinning assertions (`EmailConfirmationUnderRlsTests.cs:69`, `LockoutUnlockIssuanceTests.cs:93`), tests first per TDD. The current substrings are strict subsets of the `/app`-prefixed URLs, so only the *updated* assertions go red pre-fix — which is the red the ship gate wants. Update the stale URL-shape comments alongside (`EmailConfirmationUnderRlsTests.cs:67`, `EmailConfirmationTests.cs:61` XML doc).

## 4. The E2E server environment

`tools/e2e/run-server.sh` (the Playwright `webServer` command) does, in order:

1. **DB bootstrap (idempotent):** create `project_ceres_e2e` if missing → `psql -d project_ceres_e2e -f scripts/setup-postgres-roles.sql` (grants are **per-database**; roles are cluster-level and already exist — skipping this step yields `permission denied` on every query) → `dotnet ef database update --project ProjectCeres --connection "<ceres_migrator @ project_ceres_e2e>"`. The `--connection` flag is mandatory: there is no design-time factory, so a bare `dotnet ef database update` would attempt DDL as `ceres_app` and fail with 42501.
2. **Run-start wipe — guarded.** Before any DELETE, the script asserts `SELECT current_database()` returns exactly `project_ceres_e2e` and aborts otherwise — a stale `ConnectionStrings__AdminConnection` env var must never point the wipe at `project_ceres` or anything else (security-reviewer F1, non-negotiable; aligns with the pinned never-delete-DB-state rule). Then DELETE (as `ceres_admin`) from every table in the `pg_policies` `user_isolation` set (kept in lockstep with the model by the existing parity check) **plus** the Identity/auth tables (`AspNetUsers` + satellites, `FailedLoginAttempts`, `EmailDeliveryEvents`), **sparing** migration-seeded lookups (`AccountTypes`, `CategoryTypes`, `Currencies`, `ReportTypes`) and `__EFMigrationsHistory`. No truncation helper exists today; the plan enumerates the table list against `RlsParityStartupCheck`'s source. (First run also deletes the 31 sentinel seed rows — expected and harmless; nothing reads them at runtime.)
3. **SPA bundle:** `pnpm build`, then place `dist/` (including `.vite/manifest.json`) at `ProjectCeres/wwwroot/dist` with `Vite:Base = "dist"` in `appsettings.E2E.json`. After the copy, the script **asserts the manifest contains BOTH `src/app/main.tsx` and `src/main.tsx` keys** and aborts otherwise — `src/app/main.tsx` is de-facto pinned by every suite, but nothing renders `_Layout`/the error page, so a missing `src/main.tsx` key would ship silently (test-audit Conflict 2). `wwwroot/dist` + the manifest are already gitignored (root `.gitignore:106,109`); generated-but-gitignored wwwroot content has precedent (`wwwroot/css/site.css`). **Never** point Vite's `emptyOutDir` at `wwwroot` root, and `build.sourcemap` stays at its default (false) so `.map` files with full TSX source never land in a publicly served directory.
4. **Secrets via env vars** (user-secrets do NOT load outside Development; `appsettings.E2E.json` is git-tracked — the gitignore covers only Production/Staging overlays — so it must stay secret-free, mirroring the Smoke/`up.sh` precedent): `ConnectionStrings__ApplicationConnection` (ceres_app @ e2e), `ConnectionStrings__AdminConnection` (ceres_admin @ e2e), `Authentication__TokenLookupSecret__Secret` (generated per run, `openssl rand -base64 32`; the base placeholder isn't valid base64 and throws on the **first token flow**, not at boot — a green boot proves nothing).
5. **Boot:** `ASPNETCORE_ENVIRONMENT=E2E dotnet run --project ProjectCeres --urls https://localhost:7299`. HTTPS is **mandatory**: cookie policy under E2E is `SameAsRequest` (keyed on `IsProduction`, Program.cs:215), and all three cookies are `__Host-` prefixed — real browsers silently drop them without Secure+https, so every flow dies over http. Port 7299 avoids dev's 5248/7081/5173 and Kestrel defaults 5000/5001. launchSettings does not apply (no `--launch-profile`); `--urls` is the Smoke-proven mechanism.

What's active under E2E (audit sweep of every env check): no Vite dev middleware, `UseExceptionHandler("/Home/Error")` + HSTS on (harmless on localhost; debugging leans on logs), `SecurePolicy=SameAsRequest`, startup RLS/privilege checks ON (`Stage75:SkipPrivilegeLeakCheck` stays absent). The startup checks do **not** enforce schema presence (empty-but-existing DB boots green, then 500s) — the wrapper script is the sole enforcement point for migrate-before-boot. `--seed-dev-user` hard-exits outside Development (SeedDevUser.cs:66) — E2E seeds users through the real registration flow instead (§ 7), which also satisfies testing.md's "don't set state the feature under test was supposed to set".

## 5. Production-code changes (all env-gated, all small)

1. **URL-builder fixes** (§ 3) — production behavior fix, not E2E-only.
2. **`FileSinkEmailService`** (`ProjectCeres/Common/Email/`): implements `Task SendAsync(EmailMessage, CancellationToken)`; writes one JSON file per message — `{ to, subject, bodyText, bodyHtml, sentAtUtc }` (note: `to` serializes `message.To.Address`; record order is To, Subject, **BodyHtml, BodyText**). Sink directory from `Email:FileSink:Directory`. Registered **Singleton** (matching LogOnly) when `builder.Environment.IsEnvironment("E2E")` — keyed on environment, *not* key-absence, so a machine-level `Email__Resend__ApiKey` env var can never flip E2E to real sends. Production fail-loud branch untouched. **Required tests** (security-reviewer F2 + test-audit Conflict 1): `ArchitectureTests.IEmailService_impls_are_LogOnly_Noop_or_Resend` gains `FileSinkEmailService` in `knownImpls` *with the same gating rigor Stage 8 applied* — new DI assertions pin that `IEmailService` resolves to FileSink under E2E **even with `Email__Resend__ApiKey` set**, and never under Development or Production; plus a unit test pinning the JSON field shape/ordering.
3. **Rate-limiter config binding.** Today every limit is a hardcoded literal and the in-process `PostConfigure<RateLimiterOptions>` test trick can't reach an out-of-process boot. Bind, with defaults equal to today's literals: `AuthLoginByIp` (10/60s — **one shared per-IP bucket across 8 endpoints**, and all Playwright traffic is one loopback partition; the lockout test alone burns the whole budget since lockout threshold 10 == limiter budget 10), `AuthCsrfByIp` (60/60s), and `EmailByIp` — which is enforced via the **`GlobalLimiter` lambda** (Program.cs:448-463), so the binding must parameterize *that*, not the test-only named policy at :470-480 (binding only the named policy is a silent no-op). `appsettings.E2E.json` raises: AuthLoginByIp 1000/60s, AuthCsrfByIp 600/60s, EmailByIp 1000/60min. `EmailByUser` (5/h per address) stays default — suites use unique addresses. Per-user TOTP/MFA limits stay default (happy paths stay under them). **Two pins** (security-reviewer F4): a test asserting that with no overrides the bound values equal today's documented literals (so the binding can never silently weaken production), and the rule that raised values live **only** in `appsettings.E2E.json` — never in base `appsettings.json`, which loads in every environment including Production. The de-facto runtime pin also holds: a silent no-op binding 429s the suite itself (lockout burns the default login budget; a 3-browser run sends ~21 emails against EmailByIp's default 10/hour).
4. **Deterministic `IBreachedPasswordChecker` for E2E** — registration calls the live HIBP API with `EnsureSuccessStatusCode` and no catch; an outage 500s the register endpoint. E2E registers an always-not-breached stub; the real checker keeps its existing test coverage. **Required test** (security-reviewer F3): no architecture test enumerates `IBreachedPasswordChecker` impls today, and a stub that ever resolves outside E2E silently disables a SHALL-level NIST control — add a mirror of the `IEmailService` impl-enumeration test plus a registration assertion that the stub resolves **only** under E2E.
5. **Vite manifest keys.** Today's build manifest is keyed by HTML inputs (`app.html`, `index.html`) but both Razor `vite-src` lookups ask for `src/app/main.tsx` (SPA host) and `src/main.tsx` (`_Layout`, which the E2E-active error page renders) — a miss is only *logged* and renders a dead script tag → blank page on a green boot. Fix: add both `.tsx` entries as rollup inputs; verify the emitted manifest contains both keys and `check-size` budgets still pass.
6. **`appsettings.E2E.json`** (tracked, secret-free): `Vite:Base`, `Email:FileSink:Directory`, rate-limit overrides, logging. Connection strings + TokenLookupSecret arrive via env vars (§ 4.4).
7. **E2E boot guard** (security-reviewer recommendation, adopted): under `IsEnvironment("E2E")` a startup check refuses to boot unless `SELECT current_database()` returns `project_ceres_e2e`. One guard collapses the whole env-flip class — a stray `ASPNETCORE_ENVIRONMENT=E2E` on a real host would otherwise simultaneously file-sink all emails (auth tokens to disk), raise auth limits ~100×, and disable HIBP screening; with the guard it refuses to start against any non-E2E database. Same idiom as the existing `RlsParityStartupCheck`/`PrivilegeLeakStartupCheck` fail-closed boots.

## 6. Playwright config evolution

- Existing `e2e/playwright.config.ts` narrows `testMatch` to `agent-walk.spec.ts` — agent-walk harness preserved byte-for-byte in behavior, reachable via a new `e2e:walk` script.
- New `e2e/playwright.golden.config.ts`: `testDir e2e/auth` · `webServer` = `tools/e2e/run-server.sh`, `url https://localhost:7299`, generous timeout (~180s: build + migrate), `reuseExistingServer: false` · projects Chromium/Firefox/WebKit · `retries: 0` (testing.md § Flaky tests: a flake is a failure until root-caused) · `workers: 1`, `fullyParallel: false` — deterministic email-sink polling and shared-bucket rate-limit behavior; parallelism/sharding is Stage 16.16's problem · trace + screenshot `retain-on-failure` → `e2e/.artifacts/` (gitignored alongside the email sink dir).
- `pnpm e2e` repoints to the golden config (this matches what `testing.md` promises the command does).
- **`tsconfig.e2e.json`** (types: node + playwright, include `e2e`, `@/*` alias) added to the solution `references` so `tsc -b` type-checks it — today `e2e/*.ts` is type-checked by *nothing*, while shared DTO types are ADR-0071's stated selling point.
- New devDep: `otpauth` (TOTP code generation; nothing in the repo computes codes today — `input-otp` and `qrcode.react` are UI-only).

## 7. Helpers (`e2e/support/`) and the five suites

Helpers (extend `flows/auth.ts` + existing page objects, don't duplicate):

- **`api.ts`** — CSRF-aware request helper. The request token comes from the `GET /api/auth/csrf` **response header** `X-XSRF-TOKEN` (never the `__Host-XSRF` cookie value — they're a cryptographic pair). The pair **rotates** on login-204, on login-200-`requiresTotp`, and on logout — re-handshake after each before the next mutating call, or the call 400s.
- **`emails.ts`** — `waitForEmail(to, matcher)` polls the sink dir; link extraction regex `#token=([A-Za-z0-9_-]{43})` (tokens are 32 random bytes base64url, exactly 43 chars — uniform across verify/reset/unlock).
- **`totp.ts`** — parses the **real** `otpauth://` URI from the enroll response (`page.waitForResponse`) rather than hardcoding SHA1/6/30; never submits the same code twice (server-side replay guard rejects reuse within a window — enroll-verify then login with the same 30s code 401s; helper waits for the next period boundary when needed).
- **`users.ts`** — `createVerifiedUser()`: register via API → `waitForEmail` → verify via API. Required because `RequireConfirmedEmail=true` blocks login until verified (401 `EMAIL_NOT_CONFIRMED`), and register returns **204 even for duplicates** (anti-enumeration) so the helper can't infer creation from status. Unique timestamp-suffixed addresses per test. Identity-policy failures return **400** RFC7807 (not the project 422 envelope — see § 8 docs note).

Suites (`e2e/auth/*.spec.ts`), golden paths + one cheap edge each:

1. **`register-login.spec.ts`** — UI register → "check your inbox" → emailed verify link (fixed § 3) → confirm page → login → dashboard. Asserts `__Host-Session` present via `context.cookies()`.
2. **`password-reset.spec.ts`** — `createVerifiedUser` → "Forgot password?" → emailed link (`/app/password-reset#token=`) → new password → `/login?reset=1` toast → login with new password. (No-MFA path; MFA-path permutations stay in the integration suite.)
3. **`totp-enrol-and-first-login.spec.ts`** — `createVerifiedUser` → login → `/app/security` → Enable → parse otpauth URI from the enroll response → verify code → backup codes step → confirm checkbox → sign out → login → `200 {requiresTotp}` → `/app/login/totp` → fresh code → dashboard. Constraint: the half-auth `Identity.TwoFactorUserId` cookie has a strict 5-min TTL — the challenge step must complete within it.
4. **`lockout-self-service.spec.ts`** — `createVerifiedUser` → 10 wrong passwords via the form (raised E2E limiter is what makes this runnable; the unlock email is issued only on the lockout *transition*, which the production limiter can starve) → locked message → emailed unlock link (fixed § 3) → button-press confirm → `/login?unlocked=1` toast → login.
5. **`backup-code-recovery.spec.ts`** — `createVerifiedUser` + API-driven enrolment (fresh login satisfies `[RequireRecentAuth]`; capture the returned codes) → login → TOTP challenge → "Use backup code" toggle → submit one code (same `/api/auth/login/totp` endpoint, shape-routed — there is no separate backup-code endpoint) → dashboard. Edge: re-login with the same code is rejected (single-use).

## 8. Documentation + close-out

- **`docs/testing.md`**: extend `### E2E Tool: Playwright (TypeScript)` with local-run docs inline (there is no § Running tests heading — run docs live in each test-type's subsection): prerequisites, `pnpm --dir ProjectCeres.Client e2e`, `e2e:walk`, where sink/trace artifacts land, `retries: 0` rationale, and **when the suite runs** (nothing runs it automatically — the Stop hook is tier 0 for `.ts` writes and the DoD tiers never invoke Playwright; the suite runs on demand and at auth-touching stage close-outs until 16.16 wires CI). Supersede the "Vite preview" wording (:110/:117 — the .NET app itself serves the built bundle) and the stale "prerequisites do not exist until that phase" paragraph (:96-98).
- **ADR-0071**: annotate the implementation-gates checklist (dep + first config shipped 2026-05-26 via 9.5a; webServer block lands here).
- **Roadmap Stage 9.11**: tick items as they land; correct the "Vite preview" line AND the config line still promising `fullyParallel: true` + video-on-failure + `playwright-report/` output (superseded by § 6: `workers: 1`, trace+screenshot retain-on-failure, `e2e/.artifacts/`) — close-out must not tick items that don't match what shipped.
- **`docs/api-contract.md`** + `AuthController.cs:109-112` comment: both claim Identity-policy register failures return 422; tests pin **400** (RFC7807). Fix both to match reality (or neither — but the audit says tests + behavior agree on 400, so docs move).
- **`vite.config.ts:33`**: stale `/api` proxy target `https://localhost:7001` → `7081` (one-line while in the file for the rollup-inputs change).

## 9. Deferral (gate run 2026-06-11)

**Email-change link-click E2E** — `EmailChangeService.cs:208-209` builds correct `/app/email-change/{confirm,revoke}#token=` URLs but no React route exists; the confirm link dead-ends a legitimate flow and the revoke link dead-ends a security affordance. Deferred because **already-scheduled as of this commit**: Stage 12 (Sessions + Support SPA pages) gains the `[ ]` line in the same commit as this spec. Tripwire: `// FIXME: re-surface in Stage 12 — no SPA route exists for this link target` at the builder lines. Until then, email-change remains API-drivable only.

## 10. Ship gates

- `pnpm e2e` green across all three browser projects, locally, from a clean `project_ceres_e2e`.
- `dotnet build`, `dotnet test`, `pnpm build`, `pnpm test` all exit 0; no test-count regression; agent-walk harness verified intact (`pnpm e2e:walk` against the Smoke env still passes).
- The two URL-builder fixes land tests-first (existing pinning assertions updated to the `/app`-prefixed shape and red before the fix).
- Roadmap 9.11 checklist items flipped only for what actually shipped; manual browser-only items stay `[ ]`.
