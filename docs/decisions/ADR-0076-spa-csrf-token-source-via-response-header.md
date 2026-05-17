# ADR-0076 — SPA reads the CSRF request token from a response header, not the cookie

## Status: Accepted (2026-05-17, Stage 9.1.5 close-out — retroactively documents the decision shipped in commit `ce2d9e9`)

## Context

Project Ceres' SPA uses cookie-authenticated session tokens (`__Host-Session`) and relies on ASP.NET Core's `IAntiforgery` for CSRF defense. The standard `IAntiforgery` setup is a **double-submit cookie** pattern, which the framework implements as a **cryptographic pair** of tokens:

- A **cookie token** — set in `__Host-XSRF` (`HttpOnly = false`, `SameSite = Lax`, `Secure = true`).
- A **request token** — a separate value the client must include in the `X-XSRF-TOKEN` request header on every POST/PUT/PATCH/DELETE.

The two tokens are different values, generated together by `_antiforgery.GetAndStoreTokens(HttpContext)`. The cookie protects against an attacker forging the request from a different origin (they can't read the cookie). The header protects against an attacker who somehow obtained the cookie value alone (they still don't have the request token). Server-side `IAntiforgery.ValidateRequestAsync` checks that BOTH tokens are present AND that they form the matching pair generated together. Sending the cookie value verbatim as the header value fails validation — they're not the same value.

### The bug commit `ce2d9e9` fixed

Through early Stage 9, the SPA's `csrf.ts` module called `readXsrfToken()`, which read the `__Host-XSRF` cookie value and used it as the `X-XSRF-TOKEN` header on subsequent state-changing requests. The `/api/auth/csrf` endpoint returned `204 NoContent` plus `Set-Cookie: __Host-XSRF=…` — but the **request token had no exit channel** in the response. The SPA had no way to read it, so it fell back to using the cookie value.

Result: every SPA login `POST /api/auth/login` returned **400 Bad Request** from `AutoValidateAntiforgeryTokenAttribute` (the response shape ASP.NET emits when antiforgery validation fails). The browser saw "Bad Request" with no useful detail; the integration test suite passed because `AuthTestFixture` constructed the request token directly via `IAntiforgery.GetAndStoreTokens` rather than going through the SPA's cookie-as-header code path.

Also: the existing `docs/security-model.md` § CSRF described the SPA pattern as "The React client reads this cookie and includes it as an `X-XSRF-TOKEN` request header." That description was wrong for this server's `IAntiforgery` configuration (which generates distinct cookie + request tokens), and it set the wrong expectation for `apiFetch`'s implementation.

### What `ce2d9e9` actually changed

Three files, both sides of the wire:

**Server (`ProjectCeres/Controllers/Api/AuthController.cs`)** — `[HttpGet("csrf")] Csrf()`:

```csharp
var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
Response.Headers[SessionConstants.CsrfHeaderName] = tokens.RequestToken;
return NoContent();
```

The endpoint still returns 204 + `Set-Cookie` (unchanged), but now ALSO writes the request token to a response header (constant `SessionConstants.CsrfHeaderName = "X-XSRF-TOKEN"`). The same header name is read back as the request header on subsequent state-changing calls — re-use of the name is intentional and conventional.

**Client (`ProjectCeres.Client/src/app/auth/csrf.ts`)** — removed cookie-reading helpers, added a module-level memo:

```ts
let cachedRequestToken: string | null = null;
export function getCachedXsrfRequestToken(): string | null { return cachedRequestToken; }
export function setCachedXsrfRequestToken(token: string | null): void { cachedRequestToken = token; }
```

**Client (`ProjectCeres.Client/src/app/lib/api-client.ts`)** — `ensureCsrfToken()` populates the memo from the `/api/auth/csrf` response header:

```ts
const response = await fetch('/api/auth/csrf', { method: 'GET', credentials: 'include' });
const token = response.headers.get('X-XSRF-TOKEN');
if (token) setCachedXsrfRequestToken(token);
```

`apiFetch` injects the cached token into the `X-XSRF-TOKEN` request header on every state-changing call.

## Decision

**The CSRF request token is delivered to the SPA via the `X-XSRF-TOKEN` response header on `GET /api/auth/csrf` and cached in a module-level memo in `csrf.ts`. The SPA never reads the cookie value for CSRF purposes; the cookie is the cookie-side half of the pair and is invisible to the SPA.**

The same `X-XSRF-TOKEN` header name is used for both directions (server → client on `/api/auth/csrf`, client → server on every state-changing call). The cookie continues to be set automatically by ASP.NET's `IAntiforgery` middleware as configured in `Program.cs`.

This decision applies to every state-changing SPA endpoint inherited across Phases 2, 3, and 4 (register, password reset, email change, MFA enroll/disable, all CRUD on Movements/Accounts/Categories/Budgets/Recurring once those migrate to the SPA, etc.). The pattern is: one CSRF handshake per session (deduplicated via the module-level `csrfHandshakeInFlight` promise), then the cached token rides every subsequent POST/PUT/PATCH/DELETE until logout invalidates the cache (see [`feedback_no_unjustified_deferrals`](../../.../) — the logout cache-clear was added 2026-05-17 after a browser-found regression where a stale cached token caused 400 on relogin).

## Alternatives considered

### Use the cookie value as the header value (rejected — the bug `ce2d9e9` fixed)

The pre-fix code did exactly this: `readXsrfToken()` parsed `document.cookie` for `__Host-XSRF` and used the cookie value as both the cookie AND the header. ASP.NET's `IAntiforgery` validation requires the cookie token and request token to be the matching pair generated by the same `GetAndStoreTokens` call — those tokens have different values. Reusing the cookie value as the header fails validation 100% of the time.

This is the [explicit anti-pattern documented in Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery) for `IAntiforgery`:

> "The cookie and the header are different values."

### Custom `IAntiforgery` that derives the request token deterministically from the cookie (rejected)

In principle, the server could implement a custom `IAntiforgery` that lets the SPA use the cookie value alone. Rejected because:

- It reimplements security-critical framework code with no benefit. The standard double-submit pattern is well-audited.
- The reason to derive request from cookie would be to avoid the round-trip for the request token. But the request token already piggy-backs on the same `GET /api/auth/csrf` response that set the cookie — there is no extra round-trip.
- Any custom implementation would have to be re-audited any time we touch antiforgery options (e.g. when adding `HeaderName` or `FormFieldName` overrides for non-SPA contexts).

### Embed the request token in `index.html` as a `<meta>` tag, hydrated at app boot (rejected)

A common pattern in MVC + jQuery codebases: server renders `<meta name="csrf-token" content="…">` into the HTML, JavaScript reads it on load. Rejected because:

- The SPA's `index.html` is served as a static Vite-built file in production (or via `Vite.AspNetCore` middleware in dev). It is not server-rendered per request. Injecting a meta tag would mean making the index dynamic, which kills static-asset caching and requires either a custom middleware or a Razor wrapper page — a larger architectural change for no security benefit over the response-header approach.
- The token would be embedded at first page load and would not refresh on token rotation (e.g. after the logout cache-clear). The SPA would still need an `/api/auth/csrf` round-trip on rotation, so the meta-tag mechanism would be additional code paying for nothing.

### `Microsoft.AspNetCore.Authentication.Cookies` with `RedirectToLogin` disabled, no antiforgery (rejected)

An "API-only auth, no antiforgery" stance — rely on `SameSite=Lax` cookies + same-origin requirement. Rejected because:

- CORS does not prevent CSRF. Same-origin requirement DOES prevent CSRF for fetch-style requests, but does NOT prevent it for top-level form submissions (`<form method="POST" action="https://app.ceres/api/transactions">`). The browser sends the cookie on top-level navigation regardless of origin. `SameSite=Lax` blocks the cookie on form submissions from other origins — so in principle Lax + form-submission resistance would suffice.
- BUT `security-model.md` § Cookies + ADR-0063 explicitly documented that CSRF tokens are required IN ADDITION to `SameSite=Lax`, citing several edge cases (top-level GET vulnerabilities, the SameSite-Lax-two-minute-grace-period CVE-2024-XXXX, and the general "defense in depth" stance for the auth surface). Removing the antiforgery layer would contradict that documented decision without a separate ADR.

## Consequences

### What this commits the codebase to

- **Every state-changing API endpoint that the SPA calls REQUIRES this pattern.** The `apiFetch` wrapper handles it transparently for all callers — there is no per-endpoint configuration. New endpoints inherit it for free as long as they go through `apiFetch`.
- **Server-side, the `/api/auth/csrf` endpoint is special** — it's the only endpoint that BOTH sets the cookie AND emits the request token in a response header. Other endpoints that rotate the token (logout: `AuthController.Logout` calls `_antiforgery.GetAndStoreTokens(HttpContext)`) do NOT emit the response header; they only refresh the cookie. The SPA is expected to clear its cache (`setCachedXsrfRequestToken(null)`) and let the next state-changing call trigger a fresh handshake — see the logout flow at `AuthContext.logout()` in `ProjectCeres.Client/src/app/auth/auth-context.tsx`.
- **The constant `SessionConstants.CsrfHeaderName`** must stay `"X-XSRF-TOKEN"` to remain compatible with `IAntiforgery`'s default `HeaderName` configured in `Program.cs`. Changing it requires updating both ends in the same commit; the antiforgery framework will silently 400 every request if they drift.
- **The module-level memo in `csrf.ts` is not multi-tab safe.** If a user opens the app in two tabs simultaneously, each tab maintains its own memo. The shared cookie covers the cookie-side validation, but the request tokens in the two tabs were obtained from two separate `/api/auth/csrf` calls (each returning a distinct request token from a different `GetAndStoreTokens` call). ASP.NET's antiforgery accepts any token paired correctly against the current cookie, so cross-tab usage works in practice. If we ever migrate to `BroadcastChannel` for cross-tab state sync (Phase 4+), this should be revisited — but is not a Phase 3 concern.

### Knock-on changes shipped or required

- `docs/security-model.md` § CSRF (currently lines ~993-1003) needs an inline correction noting that the "client reads the cookie value as the header" description was the **pre-`ce2d9e9` broken behavior** and pointing to this ADR for the correct mechanism. This ADR commit includes that edit.
- Logout cache-clear was added in commit `2d978d5` (Stage 9.1.5.f follow-up) after a browser-found regression — `AuthContext.logout()` now calls `setCachedXsrfRequestToken(null)` so the next login triggers a fresh handshake.
- Future test harness — every `WebApplicationFactory`-based integration test that hits state-changing endpoints either (a) seeds the token directly via `IAntiforgery.GetAndStoreTokens` and reads it from the test's `HttpContext` (the current pattern in `AuthTestFixture`), OR (b) goes through a real `/api/auth/csrf` round-trip first. Tests that try shortcut #3 (reusing the cookie value as the header) will return 400.

## References

- ASP.NET Core antiforgery — Microsoft Learn: <https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery>
- `IAntiforgery.GetAndStoreTokens` API: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.antiforgery.iantiforgery.getandstoretokens>
- OWASP Cross-Site Request Forgery Prevention Cheat Sheet, "Synchronizer Token Pattern": <https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html#synchronizer-token-pattern>
- Project commits this ADR documents:
  - `ce2d9e9` — `fix(stage-9): emit + consume CSRF request-token via response header` — the original fix.
  - `2d978d5` — `fix(stage-9.1.5.f): clear cached CSRF request token on logout` — the regression follow-up.
- Related ADRs:
  - [ADR-0063 — Cookie SameSite=Lax with CSRF tokens](ADR-0063-cookie-samesite-lax-with-csrf-tokens.md) — establishes that `SameSite=Lax` is paired with antiforgery, not a replacement for it.
  - [ADR-0019 — Session management user-configurable with IP controls](ADR-0019-session-management-user-configurable-with-ip-controls.md) — broader session-cookie design context.
