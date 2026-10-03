# Stage 14 — HTTP Security Headers + CORS — Design

**Date:** 2026-10-03
**Phase:** 3 (Hosted Beta)
**Roadmap:** `docs/roadmap-phase-three.md` § Stage 14
**Status:** Design approved; pending implementation plan.

## Goal

Every response carries the standard hardening headers; CORS is configured (inert
under the current same-origin deployment, ready to populate); reverse-proxy
forwarding is hardened against IP spoofing. Ships real security value now without
pre-committing Stage 16 hosting decisions.

## Context established by pre-design research

- **The SPA and API are same-origin** — one Kestrel host: SPA shell at `/` via
  `app.MapFallbackToFile("dist/app.html")`, API at `/api/*`, static assets at
  `/dist/*`. CORS (14.6) is therefore a near-no-op today.
- **The SPA shell is served as a static file** (not server-rendered), which is the
  deliberate, reasoned architecture for an authenticated-only app (ADR-0014,
  `architecture.md` § Phase 3): no SEO need behind the login wall, a reusable JSON
  API for a future mobile client, and simpler hosting. This is the correct
  long-term choice and is NOT revisited by this stage.
- **CSP must be hash-based, not nonce-based.** Industry standard (Google web.dev,
  OWASP, MDN) is explicit: nonce-based CSP requires per-request server rendering;
  hash-based is the recommended, scalable form for statically-served / cached SPA
  HTML. A nonce baked into a static file is a constant shipped to everyone — it
  defeats the mechanism. This **corrects** `security-model.md`'s current
  nonce-based skeleton.
- **Existing transport middleware:** only `app.UseHsts()` (gated `!IsDevelopment()`,
  framework-default max-age) and `app.UseHttpsRedirection()`. No CSP, header, CORS,
  or forwarded-headers code exists — 14.1–14.8 build from zero, except HSTS which is
  "tighten existing."
- **Mechanism:** `NetEscapades.AspNetCore.SecurityHeaders` (named in both
  `security-model.md` and `planning-phase3.md`; not yet a dependency — must be
  vetted against the `repo-hygiene` vuln-scan gate before committing).

## Scope

### In scope (ships this stage)

| # | Item | Notes |
|---|---|---|
| 14.1 | CSP — strict, hash-based | Allows the 2 inline bootstrap scripts (`app.html`/`dist/app.html` lines 10, 23) + recharts inline `<style>`. `/api/csp-report` collection endpoint. |
| 14.2 | `X-Content-Type-Options: nosniff` | |
| 14.3 | `X-Frame-Options: DENY` | Pairs with CSP `frame-ancestors 'none'` |
| 14.4 | `Referrer-Policy: strict-origin-when-cross-origin` | |
| — | `Permissions-Policy`, `Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Resource-Policy: same-origin` | Full header set per `security-model.md` §; COEP intentionally omitted |
| 14.8 | Authenticated-response cache headers | Path-aware matrix (below) + `Clear-Site-Data` on logout |
| 14.7 | Forwarded-headers middleware | Registered first; config-bound `KnownProxies` **empty today** (fails closed on .NET 10). Fixes latent rate-limiter partition bug once populated. |
| 14.6 | CORS | Config-bound allowed-origins; **empty/inert** under same-origin; wired and ready |

### Deferred to Stage 16 (already-scheduled — Hosting + ops)

These three items depend on infrastructure Stage 16 owns and can only be correctly
valued once its decisions land. **Deferred because already-scheduled** (Reason 2):
Stage 16 (Hosting + ops, `roadmap-phase-three.md` § Stage 16) is the stage whose
declared scope is HTTPS termination (16.2), the reverse proxy (16.4), and the
hosting origin model. Receiving `[ ]` checkboxes were added to the Stage 16
verification checklist in the same commit as this spec, each with a `// FIXME(Stage
16):` tripwire at the corresponding `Program.cs` config site:

- **14.5 HSTS `preload` + full `max-age`** — needs a stable HTTPS cert/domain;
  `preload` is slow to undo. `UseHsts()` stays at framework defaults now.
  → Stage 16 "HTTPS + TLS" checklist.
- **Populated `KnownProxies` list** — needs the chosen reverse proxy.
  → Stage 16 "Reverse proxy" checklist.
- **Real CORS origin** — only if Stage 16 introduces a separate SPA origin.
  → Stage 16 "Reverse proxy" checklist (closed as N/A if same-origin holds).

## Architecture — pipeline placement (`Program.cs`)

Three insertion points, all order-critical:

1. **Forwarded-headers — FIRST**, before all other middleware (so every downstream
   component sees the real client IP). `app.UseForwardedHeaders(...)` bound to a new
   `ForwardedHeaders` config section; `KnownProxies` empty today → forwarded headers
   ignored (safe-closed) until Stage 16.
2. **Security headers — early**, right after `UseHsts`/`UseHttpsRedirection`
   (~line 1048), **before `UseStaticFiles`** so static responses are covered too.
   One `app.UseSecurityHeaders(policy)` carrying CSP + nosniff + frame-options +
   referrer-policy + permissions-policy + COOP + CORP.
3. **CORS — after `UseRouting`, before `UseAuthentication`** (standard position).
   Config-bound origins list, empty/same-origin today; `app.UseCors(...)` inert.

### Cache-header matrix (14.8, path-aware)

| Path | Header | Reason |
|---|---|---|
| `/api/*`, authenticated | `Cache-Control: private, no-store` | Never cache user data |
| `/health/*` | `Cache-Control: no-store` | Always fresh |
| `/dist/*` | `Cache-Control: public, max-age=31536000, immutable` (preserve existing) | Content-hashed assets |
| logout response | `+ Clear-Site-Data: "cache","cookies","storage"` | Wipe session traces. Intentionally clears theme/sidebar localStorage keys — accepted. |

### CSP policy (hash-based, strict)

| Directive | Value |
|---|---|
| `default-src` | `'self'` |
| `script-src` | `'self' 'sha256-<hash1>' 'sha256-<hash2>'` — own bundle + 2 inline bootstrap scripts by hash. No `'unsafe-inline'`, no `'strict-dynamic'`. |
| `style-src` | `'self'` + recharts inline style — hash if stable, else `'unsafe-inline'` for **styles only** (lower XSS risk than scripts). Implementer verifies which is tightest-that-works. |
| `img-src` | `'self' data:` |
| `font-src` | `'self'` (fonts self-hosted via `@fontsource`, no CDN) |
| `connect-src` | `'self'` (API same-origin) |
| `object-src` | `'none'` |
| `base-uri` | `'self'` |
| `frame-ancestors` | `'none'` |
| `report-uri` / `report-to` | `/api/csp-report` |

### New components

- **`CspReportApiController`** — `POST /api/csp-report`, `[ApiController]`,
  `[Route("api/csp-report")]`, `[AllowAnonymous]`, `ControllerBase` (mirrors
  `HealthApiController`). Returns `204`. Logs the violation report. Gets a
  **dedicated named by-IP rate-limit policy** following the existing
  `AuthRateLimitPolicies.AuthCsrfByIp` pattern (NOT the global limiter) — it is an
  unauthenticated public POST and an abuse surface.
- **Build-time CSP hash step** — the two inline `app.html` scripts (+ recharts style
  if hashed) get their `sha256` hashes computed at build and fed into the CSP, so
  they never go stale. Preferred over manual pinning. A test also asserts the
  hashes match the served shell so drift fails CI either way.
- **New config sections** — `SecurityHeaders` / `Cors` / `ForwardedHeaders` in
  `appsettings.json`.

## Convention reconciliations (from verify-against-codebase)

### 1. `react/no-danger` on the shadcn chart primitive — verified false positive

`security-model.md:1071` states `dangerouslySetInnerHTML` is prohibited (ESLint
`react/no-danger`), and `:1658` lists it as a Required control. The rule's purpose
is to block XSS via arbitrary/user-controlled HTML injection.

**Root cause of the conflict (researched, not assumed):** shadcn's chart primitive
(`ProjectCeres.Client/src/components/ui/chart.tsx:82–104`) injects a `<style>`
element whose content is **generated entirely from a typed, developer-authored color
config** (`ChartConfig` → CSS custom properties). No user input, no network data,
and no untrusted string reaches the injected markup — it is static CSS derived from
code. This is therefore a **verified false positive** of `react/no-danger` for that
one vendored call site, not a defect to fix: there is no injection vector to close.

**Decision:** keep the rule enforced project-wide (its XSS value is real everywhere
else) and record the single sanctioned exception for `chart.tsx` in
`security-model.md` with this root-cause rationale, so the exception is documented
rather than silent. The CSP `style-src` must still accommodate that injected inline
`<style>` regardless of the lint rule (see CSP policy above). The precise mechanism
for the exception (file-scoped lint override vs. documented allow-entry) is an
implementation detail for the plan; the spec's commitment is that it is documented
with the false-positive reasoning, never suppressed blind.

### 2. CSP skeleton doc correction

`security-model.md`'s nonce-based CSP skeleton → hash-based, with the research
rationale (static-SPA + industry standard) recorded. Via `sync-docs`.

## Testing (tests-as-ship-gate — assert what it does NOT do)

Integration tests in `ProjectCeres.Tests/Integration/` via WebApplicationFactory
hitting the real pipeline (the `AppRoleTests` pattern):

- **Positive:** every response carries the expected header set.
- **Negative:** a spoofed `X-Forwarded-For` does NOT change `RemoteIpAddress` when
  `KnownProxies` is empty (the forwarded-headers safety property).
- **Negative:** CSP header is present and `script-src` does NOT contain
  `'unsafe-inline'` (pins the strict property).
- `/api/csp-report` accepts an anonymous POST, is rate-limited, returns `204`.
- Authenticated / `/api/*` response has `no-store`; `/dist/*` keeps
  `public, max-age`; two users' responses are not cross-cacheable.
- CSP script hashes match the inline scripts in the served `dist/app.html`
  (drift guard).
- **Environment caveat:** the WAF boots under `Testing`/`Development`, where
  `UseHsts` is skipped and cookie-secure policy is relaxed — header tests assert
  only environment-appropriate headers.

## Out of scope / non-goals

- No change to the static-SPA rendering architecture (reaffirmed as correct).
- No HTTPS/cert work (Stage 16).
- No reverse-proxy selection (Stage 16).
- COEP header (intentionally omitted per `security-model.md`).

## Open implementation decisions (resolved during the plan)

- `style-src`: hash recharts' injected style vs. `'unsafe-inline'` for styles only
  — pick the tightest that renders charts correctly.
- Exact build-time hash mechanism (Vite plugin vs. a small prebuild script) —
  whichever integrates cleanly with the existing `pnpm build` + bundle-size gate.
