# ADR-0071 — End-to-End Testing on Playwright

**Status:** Accepted (Phase 3)

**Date:** 2026-05-13

**Supersedes:**
- The "E2E testing" Open Question in `planning-phase3.md` § Open Questions ("E2E testing — Playwright chosen; implement after Phase 2 React migration stabilizes"). Playwright was the leading candidate; this ADR locks it.

## Context

Phase 3 ships the first user-visible auth flows (Stage 9) and the onboarding wizard (Stage 15.5). Both are end-to-end flows that exercise the browser, the SPA, the cookie-auth pipeline, and the database simultaneously — exactly the scenario unit and integration tests cannot cover alone.

The candidates considered were:

- **Playwright** — Microsoft, supports Chromium / Firefox / WebKit, first-class TypeScript, runs against the SPA's built artefact or against the dev server, network mocking and tracing are first-party, headed and headless modes, CI-friendly. Active development and well-maintained.
- **Cypress** — strong DX, large community. Architectural limitation: each test runs inside the browser, which makes cross-origin flows (cookie handshakes, auth redirects across subdomains) awkward and historically required workarounds. Headless cost is higher (per-spec runner spin-up).
- **Selenium / WebDriver.IO** — mature, broad browser support, but DX is worse than either Playwright or Cypress and the test-stability story under flake conditions is harder to harden.
- **Browser-native (Puppeteer, etc.)** — too low-level; would require building our own test framework on top.

Playwright wins on three axes for this codebase:

1. **TypeScript-first.** The SPA is TypeScript; tests live next to the SPA in `ProjectCeres.Client/`. Shared types between test fixtures and the application code reduce duplication.
2. **Cross-origin and cookie support.** Phase 3 auth flows use cookie auth (SameSite=Lax + CSRF token per ADR-0063) and need cross-domain assertions when CD points the SPA at a non-`localhost` host. Playwright's BrowserContext model handles this without architectural workarounds.
3. **CI ergonomics on GitHub Actions.** The official Playwright action installs browsers in cache, supports sharding across runner instances, and produces uploadable trace files for failed runs. Per ADR-0070 (CI/CD on GitHub Actions), this combination is the lowest-friction option.

## Decision

**End-to-end testing uses Playwright.**

The E2E suite lives at `ProjectCeres.Client/e2e/` (separate from the unit-test directory `ProjectCeres.Client/src/**/__tests__/`). Tests are written in TypeScript, run via `pnpm --dir ProjectCeres.Client e2e`, and exercise the production-built SPA artefact served against a real `dotnet run` instance with a real PostgreSQL database (the same fixture pattern integration tests use, but at the browser level).

The first E2E suite ships **after Stage 9** (auth SPA pages) because Stage 9 produces the first user-visible flow worth running through the browser. The minimum Stage-9-complete coverage:

- Register → email verification interstitial → login → dashboard golden path.
- Forgot-password → reset link → new-password → login golden path.
- TOTP enrolment → first-login-with-TOTP golden path.
- Lockout self-service unlock → unlock-email link → re-login.

Subsequent stages add E2E coverage as they introduce user-facing flows (Stage 15.5 onboarding wizard, Stage 11+ feature areas).

## Consequences

**Positive:**

- One test framework for unit (Vitest), integration (xUnit), and E2E (Playwright). No third runtime to learn.
- Stable cross-origin and cookie semantics — exactly the scenarios the cookie auth pipeline exercises.
- Playwright trace files attach to failed CI runs, making debugging across the SPA + API + database boundary tractable from the GitHub Actions log alone.
- Per-test isolation via fresh `BrowserContext` per test by default — no shared cookie state between tests.

**Negative:**

- Browser binaries (~250MB) get downloaded on first CI run; the GitHub Actions Playwright action caches them but the first cold start adds time. Manage with the `actions/cache` step.
- Headed runs locally need a display server (or the `--headed` flag with a real display). Acceptable trade-off; headless is the default for CI.
- E2E tests are slower than integration tests by an order of magnitude (~1–5 seconds per flow). Keep the suite focused on golden paths and a small number of edge cases per stage; per-edge-case coverage remains the integration suite's job.
- Coupling to Playwright. Migration to a different tool would require rewriting test files (TypeScript fluency partially mitigates this — most assertions transfer).

## Implementation gates

This ADR is locked. Implementation splits across two roadmap stages because `.github/workflows/ci.yml` does not exist until Stage 16 ships per [ADR-0070](decisions/ADR-0070-ci-cd-on-github-actions.md):

- **Stage 9.11 — Playwright E2E foundations** (`roadmap-phase-three.md`) — owns the dep install, `playwright.config.ts`, first golden-path suite, fixture decision, and local-run docs.
- **Stage 16.16 — Playwright E2E in CI** (`roadmap-phase-three.md`) — wires the suite into the CI workflow (sharded, browser cache via `actions/cache`, trace + report artefacts on failure).

Each stage's roadmap entry owns its own checklist. This section stays as the authoritative source of must-have items below; the two roadmap stages' `[ ]` lines mirror them split by execution boundary.

Pre-Stage-9.11-completion must-have:

- [x] `pnpm --dir ProjectCeres.Client add -D @playwright/test`
- [x] `playwright.config.ts` with `baseURL`, `webServer`, `projects` for Chromium / Firefox / WebKit. The `webServer` boots the production bundle via `tools/e2e/run-server.sh` (not "Vite preview"), and the config split into `playwright.golden.config.ts` (golden suites) + the narrowed `playwright.config.ts` (agent-walk smoke).
- [x] `e2e/auth/register-login.spec.ts` covers the golden path. All FIVE golden-path suites shipped (register-login, password-reset, totp-enrol-and-first-login, lockout-self-service, backup-code-recovery), not just register-login.

Pre-Stage-16.16-completion must-have:

- [ ] GitHub Actions step: `npx playwright install --with-deps` cached via `actions/cache`.
- [ ] Sharded across runner instances; matrix runs each shard.
- [ ] Trace files + HTML report uploaded as workflow artefact on failure.

## Cross-references

- `planning-phase3.md` § Open Questions — "E2E testing" resolved by this ADR.
- `planning-resolved.md` — entry added.
- `testing.md` § E2E tool — Playwright filename + directory convention named here become normative.
- ADR-0070 (CI/CD on GitHub Actions) — interlock for CI integration.
