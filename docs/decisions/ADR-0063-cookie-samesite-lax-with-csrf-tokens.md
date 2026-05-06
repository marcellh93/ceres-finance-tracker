# ADR-0063 — Auth Cookie `SameSite=Lax` Paired with CSRF Tokens

**Status:** Accepted (Phase 3, Batch 3 — Auth)

**Date:** 2026-05-07

**Context:**

The Phase 3 cutover from sentinel-based pre-auth to real authentication requires deciding the `SameSite` attribute for the session cookie. `security-model.md` § Cookie Configuration explicitly defers this decision to ADR and notes that it must be made before auth implementation begins because it affects CSRF posture.

The choice is between:

- **`SameSite=Strict`** — the cookie is never attached to a cross-site request, including the first navigation from an external link (e.g. an email).
- **`SameSite=Lax`** — the cookie is attached on top-level navigations (link clicks) but not on cross-site form submissions or background requests.

`SameSite=None` is unsafe for an authenticated session cookie and is not considered.

Two factors push the decision:

1. The Phase 3 product sends a high volume of email-driven flows that rely on link clicks landing the user authenticated where possible (security event notifications, "this wasn't me" links, GDPR export ready, weekly digest, lockout self-service unlock). `Strict` breaks these flows by forcing a re-login on the first click.
2. Decision made in ADR-0064 keeps social login open for Phase 4. OAuth callbacks are top-level navigations from the provider back to the application — `Strict` would block them outright.

CSRF protection is required regardless of the `SameSite` choice (`security-model.md` § CSRF: "CORS does not prevent CSRF — do not remove anti-forgery protection").

**Decision:**

The authentication cookie uses **`SameSite=Lax`**, paired with the full defense-in-depth stack:

- `__Host-` cookie name prefix (forces `Secure`, no `Domain` attribute, `Path=/`)
- `HttpOnly = true`
- `Secure = true`
- XSRF-TOKEN double-submit pattern on every state-changing endpoint (non-`HttpOnly` `XSRF-TOKEN` cookie + `X-XSRF-TOKEN` request header)
- Re-authentication required for sensitive operations (change password, change email, re-enroll TOTP, view active sessions, GDPR erasure)
- Security stamp validation at 5-minute intervals

The CSRF token cookie itself is `Secure` and `SameSite=Lax` (it cannot be `HttpOnly` because the SPA must read it).

**Rationale:**

`SameSite=Lax` is the modern default in Chrome, Firefox, and Safari. It blocks the high-risk CSRF vectors (cross-site form submission, hidden background requests) while permitting normal link-click navigations. The remaining CSRF gap — a malicious link that triggers a state-changing request via top-level navigation — is fully closed by the XSRF-TOKEN double-submit pattern, which a malicious site cannot read.

`Strict` would add a small theoretical defense at the cost of breaking every email-driven flow in the product. For a beta personal-finance app with a solo support team, the friction of forced re-login on every email click outweighs the marginal security gain.

The choice also preserves architectural optionality for Phase 4 social login (ADR-0064) without requiring a future flip from `Strict` to `Lax`.

**Consequences:**

- Email-driven flows (security alerts, password reset, GDPR export ready, weekly digest, "this wasn't me" links) work as expected on first click.
- CSRF protection cannot be relaxed — anti-forgery middleware and the double-submit pattern are mandatory on all state-changing endpoints, and the SPA fetch wrapper must read the `XSRF-TOKEN` cookie and forward it as `X-XSRF-TOKEN`.
- Phase 4 social login is unblocked at the cookie layer.
- Session cookies are never accessible to JavaScript (`HttpOnly`), never transmitted over plaintext (`Secure`), and never sent to subdomains (`__Host-` prefix).
- The session token is stored only as a hash in `UserSession` (per ADR-0019) — even a leaked database does not yield raw session tokens.

**Cross-references:**

- `security-model.md` § Cookie Configuration — full cookie configuration spec
- `security-model.md` § CSRF — XSRF-TOKEN double-submit pattern
- ADR-0019 — `UserSession` table and session lifecycle
- ADR-0064 — Social login deferred to Phase 4 (the constraint that closes Lax as a forced choice)
