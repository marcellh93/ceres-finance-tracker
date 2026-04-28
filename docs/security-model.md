# Security Model

> **Diataxis type:** Reference + Explanation — defines what we are protecting against and the rules that enforce protection. Individual implementation details live in `planning.md` (Phase 3) and the relevant ADRs; this document is the unified view.

## Index

1. [Threat Model](#threat-model)
2. [Data Protection Rules](#data-protection-rules)
3. [Access Control Rules](#access-control-rules)
4. [Authentication and Session Rules](#authentication-and-session-rules)
5. [Transport and Infrastructure Rules](#transport-and-infrastructure-rules)
6. [Input Validation Rules](#input-validation-rules)
7. [File Handling Rules](#file-handling-rules)
8. [Email Security Rules](#email-security-rules)
9. [API Authentication and Identity Masking](#api-authentication-and-identity-masking)
10. [Security Rules by Phase](#security-rules-by-phase)

---

## Threat Model

### What we are protecting

This is a personal finance application. The data it holds is among the most sensitive personal data a user can have: account balances, income, spending habits, financial goals, and (in Phase 4) tax information. The threat impact is high even for a small user base.

**Primary assets:**
- Financial transaction records and account data
- Authentication credentials (passwords, TOTP secrets)
- Session tokens
- File attachments (receipts, invoices — may contain personal or tax-sensitive information)
- Audit logs (login history, IP addresses)

### Who the adversary is

| Adversary | Capability | Target |
|-----------|-----------|--------|
| **External attacker** | Network access to the hosted app | Credentials, other users' financial data |
| **Curious user** | Valid account, knowledge of URL patterns | Other users' data via ID manipulation |
| **Compromised session** | Stolen session cookie or token | All data accessible to the victim user |
| **Database dump attacker** | Read access to a database backup | Plaintext passwords, TOTP secrets, financial data |
| **Passive network observer** | Ability to intercept HTTP traffic | Credentials, session tokens (mitigated by HTTPS) |

### What we are not trying to defend against

- Malware running on the user's own device (out of scope for any web app)
- A malicious hosting platform administrator (trust the hosting provider)
- Quantum computing attacks on current cryptography (not a near-term threat at this scale)

---

## Data Protection Rules

### Passwords

- **Never store plaintext.** Hash with Argon2id using explicitly pinned parameters: `m=19456` (19 MB), `t=2` iterations, `p=1` parallelism (OWASP minimum baseline).
- **Never use bcrypt** for new implementations — Argon2id is the current standard.
- **Do not rely on library defaults** — pin the parameters explicitly. Default values in libraries may be weaker than the minimum.
- **Policy:** minimum 8 characters, no maximum below 64 (NIST SP 800-63B). No mandatory complexity rules (uppercase, symbols) — NIST advises against them. Check against a breached password list (Have I Been Pwned API or a local top-N list) on registration and password change.

### TOTP Secrets

- The TOTP seed (generated during MFA setup and encoded in the QR code) must be stored **encrypted at rest** in the database.
- A plaintext seed in a database dump allows offline generation of valid TOTP codes, bypassing MFA entirely.
- ASP.NET Core Identity stores TOTP secrets via `IUserTwoFactorTokenProvider` — verify that ASP.NET Core Data Protection encryption is applied before Phase 3 launch.
- **Data Protection key storage:** Data Protection keys encrypt TOTP secrets. By default they are written to the local filesystem — if the server is compromised, the attacker gets both the ciphertext and the decryption keys. Before Phase 3 launch, configure a Data Protection key storage backend external to the application server (Azure Key Vault, AWS KMS, or an encrypted volume with access controls independent of the app). Never leave Data Protection keys co-located with the data they protect.
- **TOTP seed exposure at enrollment:** the QR code response contains the raw `otpauth://` URI. This response must include `Cache-Control: no-store, no-cache` headers. The seed must never be logged (application logs, request logs, or error tracking). After the user completes enrollment verification, the seed must not be retrievable via any endpoint.
- **TOTP enrollment verification:** MFA must only be marked active (`MfaEnabled = true`) after the user successfully enters their first valid TOTP code against the new seed. A two-step flow is required: (1) display QR code, (2) require a valid code to confirm enrollment. Until step 2 succeeds, `MfaEnabled` remains false. This prevents a user from being locked out due to a misconfigured authenticator app.
- **TOTP re-enrollment:** when a user re-enrolls TOTP (e.g., new phone), the old TOTP secret must be invalidated before the new one is marked active. The user must reauthenticate (enter current password) before starting re-enrollment.

### TOTP Backup Codes

Backup codes are an account recovery path that permanently bypasses TOTP. Treat them with the same security requirements as passwords.

- **Storage:** hash each backup code with Argon2id before storage. Never store them in plaintext. Never log them.
- **Entropy:** generate 8–10 codes with at least 128 bits of entropy each (e.g. 10 characters from a base-32 alphabet ≈ 50 bits minimum; prefer 16 characters for 80 bits).
- **Single-use:** mark each code used in the database on first successful verification. A used code must never be accepted again.
- **Re-generation:** when new backup codes are generated (e.g. user requests fresh codes after using one), all existing codes for that user are immediately invalidated before new ones are issued.
- **Display:** backup codes are shown to the user exactly once, at the time of generation. After the user acknowledges them, they are never displayed again. The response must include `Cache-Control: no-store, no-cache`.
- **NIST requirement (800-63B §5.1.2):** each code must have at least 20 bits of entropy, must be single-use, and must be stored using approved cryptography.

### Session Tokens

- Persistent "remember me" tokens: store only a hash of the token in the database — never the raw value. Rotate the token on each use (issue new, invalidate old).
- Regular session tokens: managed by ASP.NET Core's cookie authentication. Regenerate immediately after login to prevent session fixation.
- On logout: mark the `UserSession` row as revoked in the database. Clearing the cookie alone is insufficient — a stolen cookie can still be replayed.

### Database Credentials

- The application's runtime user has DML rights only: `SELECT`, `INSERT`, `UPDATE`, `DELETE`.
- A separate migration user holds DDL rights and runs `dotnet ef database update`.
- Credentials are never stored in source control. Local development: `dotnet user-secrets`. Production: hosting platform secret store (Azure Key Vault, environment variables, or equivalent). See `planning.md → Secrets & Config`.

### Sensitive Fields at Rest

| Data | Protection |
|------|-----------|
| Passwords | Argon2id hash (never stored) |
| TOTP secrets | Encrypted at rest via ASP.NET Core Data Protection |
| Session tokens | Stored as hash; raw token in cookie only |
| Financial records | PostgreSQL at-rest encryption at the infrastructure layer (hosting platform responsibility) |
| File attachments | Stored outside `wwwroot` — not publicly accessible; served only through authenticated controller actions |
| IP addresses (audit log, UserSession) | Stored in plaintext — necessary for security features (IP enforcement, IP blocking, audit trail) |

---

## Access Control Rules

### IDOR Prevention (Insecure Direct Object Reference)

Every controller action that loads a resource by ID must scope the query to the authenticated user:

```
WHERE Id = ? AND UserId = currentUserId
```

This applies to: Transactions, Accounts, Transfers, Budgets, CategoryBudgets, SavedReports, RecurringTransactions, TransactionAttachments.

**When a resource is not found (either because it does not exist or belongs to another user): return `404`, not `403`.**

Returning `403` confirms the resource exists, which tells an attacker that their guessed ID was valid. A resource the user cannot see should appear not to exist.

### System-Level Controls (IsSystem)

Categories with `IsSystem = true` are seeded by the app and cannot be renamed or deleted by any user. This rule is enforced **server-side in the service layer**, not just hidden in the UI. A direct HTTP request bypassing the UI must be rejected. UI-only enforcement is not enforcement.

### Role Boundaries (Phase 3+)

Phase 3 is single-role (all authenticated users have the same permissions over their own data). The admin role is introduced in Phase 3 alongside user auth.

**Admin access model (ADR-0028):**

- Admin routes live under `/admin/*` in an ASP.NET Core Area, gated at the area level with `[Authorize(Roles = "Admin")]` — not per-controller
- Admins do not have direct read access to a user's transaction data. Support access goes through an **impersonation session**, which is fully audited (start, every action, end) via `AdminAuditLog`
- Every admin mutation writes an append-only `AdminAuditLog` record. No endpoint exposes edit or delete on this table — not even to admins
- Impersonation of other admin accounts is not permitted
- GDPR erasure flows are the only context where cascading deletes are permitted; they require an explicit confirmation step and produce an audit record
- Admin file serving (e.g. viewing a user's attachment during impersonation) must apply the same ownership-and-authentication checks as user-facing file access — admin role does not bypass file access controls

### File Access Control

Files stored in `uploads/` are never served directly by the web server. Every file download goes through an authenticated controller action that:
1. Verifies the requesting user owns the file (via the TransactionAttachment record)
2. Returns the file via `FileStreamResult` with `Content-Disposition: attachment; filename*=UTF-8''...` (RFC 5987 encoding for non-ASCII filenames)
3. Re-verifies the stored MIME type — do not trust the stored extension alone

**TransactionAttachment ownership:** attachment ownership is currently verified by joining through the parent `Transaction`. This is a fragile pattern — if any endpoint accepts an attachment ID without requiring the transaction ID in the path, the join is bypassed. To eliminate this fragility, add a `UserId` column directly to `TransactionAttachment`. This follows the same direct-scoping pattern as all other user-owned entities and removes dependence on the join for security.

**Per-user storage quota:** enforce a maximum total file storage per user (e.g. 500 MB for Phase 3 beta). Check the current total before writing any new attachment to disk. Return 422 with a clear error message when the quota is exceeded. Without a quota, a single user can exhaust server disk space and take down the application for all users. Log quota utilization for monitoring.

### List Endpoint Scoping

IDOR prevention applies equally to list endpoints (GET /api/v1/transactions, GET /api/v1/accounts, etc.) and to single-resource endpoints. An unfiltered list endpoint that returns all users' records is the same severity as a direct IDOR — it is just less obvious.

**Rule:** every list query must include a `WHERE UserId = currentUserId` clause (or its equivalent via EF Core Global Query Filters). This must be verified by integration tests for every entity type, not just by code review.

**Integration test requirement:** for every list endpoint, there must be a test that: (1) creates records belonging to User A and User B, (2) authenticates as User A, (3) calls the list endpoint, and (4) asserts the response contains only User A's records.

---

## Authentication and Session Rules

### Login

- **Account enumeration prevention:** login and password-reset endpoints return identical error messages and take identical wall-clock time regardless of whether the email exists. Always run Argon2id hash even when the user is not found — hash a dummy value and discard the result. The timing difference between "user not found" (no hash) and "wrong password" (Argon2id takes ~300ms) leaks whether an email is registered.
- **Rate limiting:** login and registration endpoints are rate-limited. Minimum: 10 requests per minute per IP. Supplement with account-level lockout: after N consecutive failed attempts on the same account (e.g. 10), lock for a fixed period (e.g. 15 minutes) and notify the user via email.
- **MFA:** mandatory TOTP for all users. No SMS — vulnerable to SIM-swap. See ADR rationale in `planning.md`.
- **TOTP replay prevention:** track recently accepted codes per user in a **persistent store** (database table or Redis — not an in-memory cache). An in-memory store loses replay history on application restart, allowing code reuse within the 30-second TOTP window after a restart. Store the accepted code hash and expiry timestamp; auto-purge entries older than 2 minutes.
- **Account lockout self-service unlock:** the lockout notification email must include a time-limited signed unlock link (separate from the password reset flow). This allows the legitimate user to self-recover without waiting for the lockout period to expire. Without this, an attacker who knows a target's email can re-trigger lockout continuously, effectively DoS-ing the account indefinitely. Additionally, a valid TOTP code should be accepted even during a lockout — the lockout protects against password guessing, not TOTP abuse.
- **Failed login logging:** every failed login attempt must be logged (without the attempted password) with timestamp, IP address, and whether the failure was credential-based or TOTP-based. This enables post-incident analysis (e.g., distributed credential stuffing detection across multiple accounts).
- **Reauthentication for sensitive operations (NIST 800-63B §7.2):** require fresh password entry (not just a valid session) before: changing password, changing email address, re-enrolling TOTP, viewing the active sessions list, and initiating GDPR erasure. Long persistent sessions must not bypass this requirement.

### Password Reset

Password reset is a high-risk flow because it is the most common path attackers use to bypass TOTP — many implementations skip MFA during reset.

- **Token format:** generate a cryptographically random 256-bit value. Store only its Argon2id hash in the database. The raw token is transmitted once in the reset email URL and never again.
- **Expiry:** 15 minutes maximum from the time of issue.
- **Single-use:** invalidate the token immediately upon first successful use. A second submission of the same token must fail.
- **Session revocation on reset:** on successful password reset, revoke all existing `UserSession` rows for that user. An attacker who triggered a reset while holding a stolen session loses access immediately.
- **MFA during reset:** the reset flow must require the user to enter a valid TOTP code before the new password is accepted. This prevents an attacker who has only the reset email (but not the TOTP device) from completing the takeover. If the user has lost their TOTP device, backup codes are the recovery path — not a TOTP bypass in the reset flow.
- **Account enumeration:** the password reset request endpoint must return an identical response (message and timing) regardless of whether the email address is registered. Always run the same code path — never short-circuit on "email not found."
- **Notification:** send an email to the account's address on any password reset request (whether or not the account exists — do not confirm existence). On successful reset, send a separate notification email informing the user their password was changed.

### Sessions

See `docs/decisions/ADR-0019-session-management-user-configurable-with-ip-controls.md` for the full decision.

Summary:
- Sessions are tracked server-side in `UserSession` (one row per active session per device)
- Session token regenerated on login (session fixation prevention) — always enforced
- Logout marks the server-side record as revoked — always enforced
- Session lifetime, IP enforcement, and IP blocking are user-configurable with risk disclosure

### Sessions

See `docs/decisions/ADR-0019-session-management-user-configurable-with-ip-controls.md` for the full decision.

Summary:
- Sessions are tracked server-side in `UserSession` (one row per active session per device)
- Session token regenerated on login (session fixation prevention) — always enforced
- Logout marks the server-side record as revoked — always enforced
- Session lifetime, IP enforcement, and IP blocking are user-configurable with risk disclosure

### Cookie Configuration

Authentication cookies must be set with:
- `HttpOnly = true` — blocks JavaScript access, mitigates XSS cookie theft
- `Secure = true` — HTTPS-only transmission
- `SameSite`: use `Strict` if social login is not implemented in this phase; use `Lax` if social login OAuth callbacks are in scope (Strict breaks the OAuth top-level navigation callback). This decision must be made before auth implementation begins — it affects CSRF posture. Record the choice in an ADR.

### Authentication Model — Cookie vs. JWT

The Phase 3 API uses **cookie-based authentication** (as specified in `api-contract.md`). The JWT/refresh token description in the API Authentication and Identity Masking section applies to Phase 4+ when a mobile client or third-party API consumer is introduced.

For Phase 3:
- Session state is tracked in `UserSession` via HttpOnly cookie
- CSRF protection is required via the XSRF-TOKEN double-submit pattern (see CSRF section)
- JWTs are not issued to the React SPA — the SPA relies on the session cookie
- No token is stored in localStorage, sessionStorage, or any JS-accessible global state

**Prohibition:** never store session tokens, JWTs, or user IDs in `localStorage` or `sessionStorage`. These are accessible to any script on the page. An XSS vulnerability combined with localStorage token storage yields account takeover. Preferences and UI state (e.g. collapsed sidebar) are acceptable in localStorage; credentials and session identifiers are not.

### Global Authorization Policy

Set a global fallback authorization policy in `AddAuthorization`:

```csharp
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

This means any endpoint without an explicit authorization attribute requires authentication by default. Apply `[AllowAnonymous]` only to: login, register, password reset, and the React SPA static file catch-all. This inverts the default — a forgotten `[Authorize]` attribute is safe rather than dangerous.

---

## Transport and Infrastructure Rules

### HTTPS and TLS

- All traffic must use HTTPS. HTTP must redirect to HTTPS. HSTS (`Strict-Transport-Security`) must be configured once HTTPS is enforced. Configure in `Program.cs` via `UseHttpsRedirection()` and `UseHsts()`.
- **TLS version:** require TLS 1.2 minimum; TLS 1.3 preferred. Explicitly disable TLS 1.0 and 1.1 in the hosting platform configuration. Verify with `testssl.sh` or SSL Labs before Phase 3 launch.
- **Cipher suites:** disable RC4, 3DES, NULL, EXPORT, and ANON cipher suites. The hosting platform (nginx, Caddy, or cloud load balancer) enforces this — document the required configuration.
- **HSTS preload:** submit the domain to the HSTS preload list at `hstspreload.org`. Preload requires `max-age >= 31536000; includeSubDomains; preload` in the header. Without preload, a first-time visitor before the first HTTPS response is still vulnerable to a downgrade attack.

### Reverse Proxy

When the app runs behind a reverse proxy (nginx, Caddy, or a hosting platform load balancer), register `app.UseForwardedHeaders()` with `ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto` **before all other middleware**. Without this, `Request.IsHttps` returns false, HSTS does not activate, and redirect-to-HTTPS logic fails silently. Restrict trusted proxy addresses via `KnownProxies` or `KnownNetworks`.

### HTTP Security Headers

Every response must include:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing |
| `X-Frame-Options` | `DENY` | Prevents clickjacking |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Limits referrer information leakage |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` | Enforces HTTPS after first visit |
| `Content-Security-Policy` | See skeleton below | Restricts script/style sources, mitigates XSS |

Use `NetEscapades.AspNetCore.SecurityHeaders` or custom middleware.

**CSP must be defined before any React routes go live** — not after. Even with HttpOnly cookies (which block cookie theft), a XSS payload can call authenticated API endpoints using the session cookie automatically. CSP is the primary control against this.

**Phase 3 CSP skeleton:**
```
default-src 'self';
script-src 'self';
style-src 'self' 'unsafe-inline';
img-src 'self' data:;
font-src 'self';
connect-src 'self';
object-src 'none';
frame-ancestors 'none';
form-action 'self';
base-uri 'self';
```

Notes:
- `unsafe-inline` for `style-src` may be required by Tailwind v4 — evaluate at implementation time; replace with a nonce if possible
- `frame-ancestors 'none'` supersedes `X-Frame-Options: DENY` in modern browsers — both must be present for full coverage
- Any CDN, web font provider, or analytics service requires an additional `src` directive — add only what is needed
- `dangerouslySetInnerHTML` is prohibited in React components. Enforce via ESLint `react/no-danger` rule. If rich text rendering is ever needed, use a sanitized markdown renderer (DOMPurify + marked)

### CORS

Required when the React SPA is on a different origin from the API (Phase 3). Configure via `AddCors` / `UseCors` in `Program.cs`. Whitelist only known frontend origins. **Never combine `AllowAnyOrigin` with `AllowCredentials`** — the CORS specification prohibits this and it collapses same-origin protection.

### CSRF

All state-changing API endpoints authenticated via cookies require CSRF protection. CORS does not prevent CSRF — it only blocks cross-origin reads, not cross-origin state-changing requests. `SameSite` cookies reduce the risk but are not a complete substitute for an explicit token.

**Pattern for the React SPA (double-submit cookie):**
1. The server sets a separate non-HttpOnly `XSRF-TOKEN` cookie on page load
2. The React client reads this cookie and includes it as an `X-XSRF-TOKEN` request header on all state-changing requests (POST, PUT, PATCH, DELETE)
3. The server validates that the header value matches the cookie value
4. Because cross-site requests cannot read the cookie value (same-origin policy), forged requests cannot supply the correct header

**Note:** `planning-phase3-spa-migration.md` previously stated that anti-forgery tokens are "replaced by CORS + HttpOnly cookie auth." This is incorrect — CORS does not prevent CSRF. The double-submit pattern above is the required approach. That statement in the migration doc has been corrected.

---

## Input Validation Rules

- **Validate at the boundary:** all user input is validated at the controller/ViewModel level before reaching services. Services trust input from controllers but validate cross-entity business rules (e.g. currency matching).
- **ViewModels, not entities:** form inputs bind to ViewModel classes with validation attributes (`[Required]`, `[StringLength]`, etc.), not directly to EF Core entity classes. This prevents mass-assignment vulnerabilities.
- **Length limits:** define `[StringLength]` on all string fields in ViewModels to match the database column constraint. Never accept unbounded string input.
- **CSV export injection:** any exported CSV field must be sanitized. Values starting with `=`, `@`, `+`, or `-` are interpreted as formulas by spreadsheet applications. Prefix such values with a single quote to neutralize them.
- **Open redirect prevention (server-side):** any controller action that accepts a `returnUrl` parameter must validate it with `Url.IsLocalUrl(returnUrl)` before redirecting. If the value is not a local URL, fall back to the controller's own Index action. This applies to every Edit POST and Delete POST action that supports `returnUrl`. An unvalidated redirect allows an attacker to craft a link like `/Transactions/Edit/123?returnUrl=https://evil.com` that sends the user to an external site after a legitimate form submission.
- **Open redirect prevention (client-side):** the React Router login component captures the pre-login URL from location state (`from`) and redirects there after authentication. This client-side redirect must also be validated against a known-safe path allowlist before use. Reject any destination that starts with `javascript:`, `data:`, `//`, or any protocol scheme other than a local path. Server-side `Url.IsLocalUrl()` does not cover this — the React component must do its own validation.
- **XXE injection prevention (XLSX import):** XLSX files are ZIP archives containing XML. Explicitly configure the XML parser used by ClosedXML with `DtdProcessing = DtdProcessing.Prohibit` and `XmlResolver = null`. Without this, a crafted XLSX file can use XML external entities to read arbitrary files from the server (application secrets, private keys) or make SSRF calls. Verify this configuration is in place before enabling XLSX import in production. Add a fuzz test with a crafted XXE payload as part of the import test suite.

---

## File Handling Rules

### Upload (validation)

1. **MIME whitelist:** accept only `image/jpeg`, `image/png`, `image/webp`, `application/pdf`. Reject all other MIME types.
2. **Magic bytes check:** verify the file's actual byte signature matches the declared MIME type. Do not trust the `Content-Type` header or the file extension — they can be spoofed.
3. **Size limit:** enforce a maximum file size (e.g. 10 MB) at the server level, not just in client-side form validation.
4. **Filename sanitization:** generate a new system-assigned filename (UUID + allowed extension) for storage. Store the original filename in `TransactionAttachment.FileName` for display only — never use it for filesystem paths. Prevents path traversal attacks.
5. **Polyglot awareness:** a file can have valid JPEG bytes at the start and contain embedded scripts later (a "polyglot file"). Magic bytes check reduces but does not eliminate this risk — the MIME whitelist and strict `Content-Disposition` headers on serving are the remaining controls.
6. **Pre-save validation (`ValidateAsync`):** when a file is submitted alongside a new transaction (Create form), `IFileAttachmentService.ValidateAsync` runs size and magic-byte checks before the transaction row is written. If validation fails, the form returns with an inline error and nothing is persisted. This prevents orphaned transactions with no valid attachment.

### Serving (security)

1. **Authentication required:** every file download goes through an authenticated controller action. Static file serving for the `uploads/` directory must be disabled.
2. **Ownership check:** verify the requesting user owns the file before streaming it.
3. **Content-Disposition header:** always set `Content-Disposition: attachment; filename*=UTF-8''...` (RFC 5987). This forces the browser to download the file rather than render it inline, mitigating stored XSS via uploaded HTML files.
4. **MIME re-verification:** use the stored MIME type from the database when setting the response `Content-Type` — do not re-derive it from the filename extension at serve time.

### Import files (upload-only, not stored)

Import files (CSV, XLSX) are parsed in memory and discarded. They are never written to
`uploads/` or any other filesystem path.

1. **File size limit:** enforce a maximum of 10 MB at the controller level before any
   parsing begins. Return 400 if exceeded. Prevents ClosedXML from loading an
   unbounded workbook into memory.

2. **XLSX magic bytes check:** XLSX files are ZIP archives with magic bytes `PK\x03\x04`
   (bytes 0–3). `ExcelImportParser` verifies this before opening with ClosedXML. A file
   with a `.xlsx` extension that fails the check throws `InvalidOperationException` with
   a user-facing message. This check runs before any library code processes the bytes.

3. **No filesystem persistence:** parsed `ParsedImportRow` objects are the only output
   that persists beyond the request — as `Transaction` rows in the database. The original
   file is never saved.

---

## Email Security Rules

These rules apply from Phase 3 onwards, when an email service is introduced.

### Layer 1 — DNS authentication (spoofing prevention)

These are DNS records configured once when the email service provider is chosen. They prevent anyone from sending mail that claims to be from the Ceres domain without access to the DNS zone.

| Control | What it does | How to configure |
|---------|-------------|-----------------|
| **SPF** | Lists the IP addresses and services authorized to send mail as the domain. Receiving servers reject or flag mail from unlisted senders. | Add a `TXT` record: `v=spf1 include:<provider> -all`. The exact `include:` value is given by the email provider. |
| **DKIM** | The email provider signs outgoing messages with a private key. The public key is published in DNS. Forged messages cannot produce a valid signature. | Add a `TXT` record at the CNAME or selector subdomain the provider specifies. Verify signing is active before going live. |
| **DMARC** | A policy that tells receiving servers what to do when SPF or DKIM fails. Also sends aggregate reports so you can detect unauthorized sending attempts. | Start with `p=none` (monitor only) to verify SPF and DKIM are passing cleanly, then advance to `p=quarantine` and eventually `p=reject`. Example: `v=DMARC1; p=quarantine; rua=mailto:dmarc@<domain>`. |

**Required before Phase 3 launch:** SPF and DKIM must both be active and passing. DMARC must be at minimum `p=none` at launch; advance to `p=reject` once aggregate reports confirm no legitimate sending sources are missed.

### Layer 2 — Application controls (abuse prevention)

These rules prevent a user from exploiting the app's own email-sending functionality.

- **Lock the `To:` address to the authenticated user's verified email.** Transactional emails (password reset, digest, session alert) must only send to the email address on the authenticated user's own account. Never accept a destination address from a request parameter — derive it server-side from the session.
- **Sanitize all user-controlled content before rendering it into an email.** Subject lines and body content that incorporate user-supplied strings (e.g. username, transaction description) must have HTML stripped and special characters escaped. Treat user input as untrusted even inside a plain-text email template.
- **Rate-limit all email-triggering endpoints.** Password reset and notification endpoints are already subject to the general rate-limiting rule. Apply a tighter per-user limit specifically to email sends (e.g. maximum 5 password-reset emails per hour per account) to prevent the app from being used as a spam relay.
- **Scope email triggers to the authenticated user's own data.** No endpoint should accept a `userId` or `email` parameter that allows one user to trigger an email send for another. All email dispatch is initiated from session context, not request parameters.

### Layer 3 — API key protection

The app holds an API key for the email service provider. A leaked key allows an attacker to send mail as the Ceres domain until the key is rotated.

- Store the API key in environment variables or the hosting platform's secret store. Never commit it to source control or log it in error output.
- Use a **send-only API key** if the provider supports permission scoping (Postmark, Resend, and SendGrid all support this). A send-only key cannot read inboxes, manage lists, or change account settings even if compromised.
- Rotate the key immediately on any suspected exposure. Document the rotation procedure before Phase 3 launch.

---

## Dependency Scanning and Secrets Hygiene

### Dependency Scanning

- **Server (.NET):** run `dotnet list package --vulnerable` in CI on every push. Treat Critical and High severity CVEs as build failures. Define a remediation SLA: Critical within 48 hours, High within 7 days.
- **Client (React):** run `pnpm audit --audit-level=moderate` in CI on every push. The React client has a completely separate dependency graph — .NET scanning does not cover it.
- **ClosedXML:** given its history of XXE-related CVEs, place it on a dedicated watch list. Subscribe to GitHub security advisories for the package.
- **Automated patch proposals:** configure Dependabot or Renovate to open PRs when new versions fix known vulnerabilities. Scanning that does not propose fixes requires manual triage on every alert.

### Secrets Scanning

- Add `truffleHog` or GitHub's built-in secret scanning to CI as a pre-push hook and on every PR. This prevents accidental commits of API keys, JWT secrets, database credentials, and TOTP seeds.
- The following secrets must never appear in source control, logs, or error output: JWT signing secret, `USER_REF_SECRET`, Data Protection keys, email service API key, database credentials, TOTP seeds, backup codes.

### Secrets Rotation Procedures

Document all rotation procedures before Phase 3 launch:

| Secret | Rotation procedure |
|--------|-------------------|
| JWT signing secret | (1) Generate new secret, (2) deploy with both old and new accepted (dual-validation window), (3) wait for all existing tokens to expire (15 min), (4) remove old secret |
| `USER_REF_SECRET` | Requires rewriting all `UserRef` columns in all data tables — plan as a scheduled maintenance window with a migration script. Rotation is a high-risk operation; document the rollback procedure |
| Email service API key | Rotate in provider dashboard, update secrets store, deploy, verify email delivery |
| Data Protection keys | Follow ASP.NET Core Data Protection key management docs — key ring automatically retains old keys for decryption while using the newest for encryption |

### Audit Log Security

- Audit log rows are insert-only. The runtime database user must have `INSERT` but not `UPDATE` or `DELETE` on the audit log table. This enforces tamper-evidence at the database level.
- **Scope extension:** the existing audit log covers login/logout, account creation, data export, and erasure requests. Extend to include: `TransactionCreated`, `TransactionDeleted`, `TransferCreated`, `TransferDeleted` (entity ID, timestamp, IP address — no financial amounts). This is required for dispute resolution.
- **New session alert email:** default to **enabled** (opt-out, not opt-in). For a financial application, a new sign-in from an unknown IP is always a security-relevant event. A user who never visits settings should still receive compromise notifications. Users may opt out from notification preferences with disclosure of the security implication.

---

## API Authentication and Identity Masking

These rules apply from Phase 3 onwards, when the app becomes multi-user and hosted.

### Layer 1 — API request authentication (JWT + tenant claim)

The React SPA and any future API consumer authenticate via short-lived **JWT access tokens**, not long-lived API keys stored in the database. Storing tokens in the database is the pattern this section is designed to avoid — a DB dump should not yield a usable credential.

**Token shape**

Each JWT carries two identity claims in its payload:

| Claim | Value | Purpose |
|-------|-------|---------|
| `sub` | User UUID | Identifies the authenticated user |
| `tid` | Tenant UUID | Identifies the tenant scope (Phase 3: one tenant per user; Phase 4+: shared tenants for teams) |
| `exp` | Unix timestamp | Short expiry — 15 minutes maximum |

**How it works**

- The server signs the JWT with a secret key held in the environment/secrets store — the secret never touches the database.
- On each API request the server validates the signature and reads `sub` and `tid` directly from the token payload — no database lookup required per request.
- Short expiry (15 min) limits the damage window if a token is intercepted. A **refresh token** (opaque, stored as a hash in `UserSession`) issues new access tokens without re-login.
- Refresh tokens rotate on each use: issue new, invalidate old. A stolen refresh token is detected on the next legitimate use (the old hash no longer matches).

**What never happens**

- API keys are never stored as plaintext in the database. The only token-related value written to the DB is the hash of the refresh token.
- The JWT secret is never committed to source control. It lives in the hosting platform's secret store.
- `exp` is always set — tokens without expiry are rejected at validation.

---

### Layer 2 — Identity pseudonymisation (breach mitigation)

Every data row (`Transactions`, `Accounts`, `Transfers`, etc.) stores a `UserId` FK. In a raw database dump this directly links financial records to real user identities. Pseudonymisation replaces that link with an opaque reference that is meaningless without the server-side secret.

**Mechanism — HMAC pseudonym**

Instead of writing the real `UserId` UUID into data rows, the application writes a deterministic HMAC of the UUID:

```
stored_user_ref = HMAC-SHA256(USER_REF_SECRET, userId)
```

- `USER_REF_SECRET` is a high-entropy random value held in the environment/secrets store — never in the database.
- The HMAC is deterministic: the same `userId` always produces the same `stored_user_ref`, so EF Core queries still work: `WHERE UserRef = HMAC(secret, currentUserId)`.
- A database dump exposes only opaque 32-byte values in the `UserRef` column — no real user UUIDs, no link to `AspNetUsers`.

**What this protects against**

A dump of the transactions table reveals that *some entity* has 847 transactions totalling €34,000 — but the `UserRef` value cannot be linked to a name, email, or identity without the server secret. The attacker needs both the database dump and the application secrets to correlate data to users.

**Limitations to document explicitly**

- **UUID search space is small.** If an attacker has both the DB dump and knows (or guesses) a target user's UUID, they can verify the match in one HMAC call. The `USER_REF_SECRET` pepper raises the cost — without it, HMAC over a UUID space is trivially brute-forced.
- **The secret is the single point of protection.** Rotate `USER_REF_SECRET` on any suspected exposure. Rotation requires rewriting all `UserRef` columns — plan a migration procedure before Phase 3 launch. Document this as a breach response step.
- **Admin cross-user queries** (e.g. GDPR erasure by email) must resolve the real `UserId` from `AspNetUsers` first, compute the HMAC, then query data tables. No raw-UUID shortcut exists. This is intentional — admin access to financial data is more expensive by design.
- **This is pseudonymisation, not anonymisation.** It satisfies GDPR pseudonymisation requirements (Art. 4(5)) when the secret is held separately from the data, but the data remains personal data and GDPR obligations still apply in full.

---

### Layer 3 — Payload encryption for high-sensitivity fields (Phase 4+)

The HMAC approach above masks *who owns* the data. It does not hide *what the data says*. For the financial payload columns (amount, description, merchant name), consider application-layer encryption in Phase 4 when tax and investment data is introduced — that is when the sensitivity of individual records rises significantly.

**Mechanism — per-tenant key derivation**

```
tenant_key = HKDF(MASTER_ENCRYPTION_KEY, tenantId)
ciphertext  = AES-256-GCM(tenant_key, plaintext_value)
```

- `MASTER_ENCRYPTION_KEY` lives in the secrets store — never the database.
- Each tenant's data is encrypted under a unique derived key. Compromising one tenant's key (e.g. via a targeted attack) does not expose other tenants.
- The `tenantId` used as the HKDF salt is the pseudonymised `TenantRef` (same HMAC pattern as `UserRef`) — so the derivation input is also opaque.

**Trade-offs to evaluate at Phase 4 kickoff**

- Encrypted columns cannot be used in SQL `ORDER BY`, `WHERE amount > X`, or aggregate (`SUM`, `AVG`) expressions. Sorting and filtering must move to application-layer post-decryption. For per-user datasets this is usually acceptable; for cross-user admin reports it requires a separate unencrypted summary table or a different approach.
- Key rotation requires re-encrypting every affected row. Plan a rotation procedure before enabling this.
- Do not implement before Phase 4 — the query complexity cost is not justified until high-sensitivity financial data (investment positions, tax figures) is present.

---

## Security Rules by Phase

| Rule | Phase 1 | Phase 2 | Phase 3 |
|------|---------|---------|---------|
| HTTPS enforced | — | — | Required |
| TLS 1.2 minimum, TLS 1.0/1.1 disabled | — | — | Required |
| HSTS configured | — | — | Required |
| HSTS preload list submission | — | — | Required before launch |
| Argon2id for passwords | — | — | Required |
| Password reset: 256-bit token, hashed, 15-min expiry, single-use, revokes all sessions | — | — | Required |
| TOTP mandatory | — | — | Required |
| TOTP enrollment verified (user enters first code before MfaEnabled=true) | — | — | Required |
| TOTP replay prevention (persistent store — DB or Redis, not in-memory) | — | — | Required |
| TOTP secrets encrypted at rest | — | — | Required |
| Data Protection keys stored in external backend (not local filesystem) | — | — | Required before launch |
| TOTP backup codes: Argon2id-hashed, 128-bit entropy, single-use | — | — | Required |
| Reauthentication required for sensitive operations | — | — | Required |
| Failed login attempts logged (no password, with timestamp + IP) | — | — | Required |
| Account lockout self-service unlock link in notification email | — | — | Required |
| HTTP security headers | — | Required (CSP when JS added) | Required |
| CSP defined before first React route goes live | — | — | Required |
| `frame-ancestors 'none'` in CSP (supplements X-Frame-Options) | — | — | Required |
| `dangerouslySetInnerHTML` prohibited; ESLint `react/no-danger` enforced | — | — | Required |
| CSRF double-submit XSRF-TOKEN pattern (not replaced by CORS) | Required | Required | Required |
| Global fallback authorization policy (`RequireAuthenticatedUser`) | — | — | Required |
| UUID primary keys | Required | Required | Required |
| IDOR prevention (UserId scoping on ID-based queries) | — (single user) | — (single user) | Required |
| List endpoint scoping (UserId filter on all list queries) | — | — | Required |
| List endpoint scope integration tests (all entity types) | — | — | Required |
| TransactionAttachment direct UserId column | — | — | Required |
| Per-user file storage quota enforced before write | — | — | Required |
| Account enumeration prevention | — | — | Required |
| Rate limiting on auth endpoints | — | — | Required |
| Account lockout after failed logins | — | — | Required |
| Session fixation prevention | — | — | Required |
| Secure cookie flags | — | — | Required |
| SameSite cookie value decided in ADR (Strict vs. Lax per social login scope) | — | — | Required |
| No tokens/credentials in localStorage or sessionStorage | — | — | Required |
| IP enforcement (user-configurable) | — | — | Required |
| IP blocking | — | — | Required |
| Active session list + revocation | — | — | Required |
| New session alert email: opt-out default (not opt-in) | — | — | Required |
| CORS policy | — | — (no separate origin) | Required |
| Forwarded headers middleware | — | — | Required |
| File upload MIME whitelist | Required (if built) | Required | Required |
| File upload magic bytes check | Required (if built) | Required | Required |
| File serving via authenticated action | Required (if built) | Required | Required |
| XXE prevention in XLSX import (DtdProcessing=Prohibit, XmlResolver=null) | — | Required | Required |
| Open redirect prevention: server-side (`Url.IsLocalUrl`) | — | Required | Required |
| Open redirect prevention: client-side (React Router `from` allowlist) | — | — | Required |
| CSV export injection sanitization | — | Required (if export built) | Required |
| Dependency vulnerability scanning (.NET) | Manual | Manual | CI pipeline — build fails on Critical/High |
| Dependency vulnerability scanning (React/pnpm) | — | Manual | CI pipeline — `pnpm audit` |
| Secrets scanning in CI (truffleHog or GitHub secret scanning) | — | — | Required |
| JWT secret rotation procedure documented | — | — | Required before launch |
| `USER_REF_SECRET` rotation procedure documented | — | — | Required before launch |
| Database least privilege (DML user) | Recommended | Recommended | Required |
| Audit log: insert-only enforced at DB level | — | — | Required |
| Audit log: extended to cover TransactionCreated/Deleted, TransferCreated/Deleted | — | — | Required |
| GDPR compliance | — | — | Required before any external user |
| IP address storage disclosed in privacy policy | — | — | Required before launch |
| SPF DNS record active and passing | — | — | Required before Phase 3 launch |
| DKIM signing active and passing | — | — | Required before Phase 3 launch |
| DMARC policy configured (`p=none` minimum) | — | — | Required before Phase 3 launch |
| Email `To:` locked to authenticated user's own address | — | — | Required |
| User-controlled content sanitized before email render | — | — | Required |
| Email-triggering endpoints rate-limited | — | — | Required |
| Email service API key stored in secrets, send-only scope | — | — | Required |
| Cookie-based auth for Phase 3 SPA (not JWT in localStorage) | — | — | Required |
| `UserId` pseudonymised via HMAC in all data rows | — | — | Required |
| `USER_REF_SECRET` pepper stored outside DB | — | — | Required |
| HMAC rotation procedure documented before launch | — | — | Required before Phase 3 launch |
| GDPR export generated asynchronously (background job, not synchronous HTTP) | — | — | Required |
| Per-tenant payload encryption (amounts, descriptions) | — | — | Phase 4+ |

> Phase 1 and 2 are single-user and local. Many security controls are not required because there is no network exposure and no other users. All controls marked Required for Phase 3 must be in place before the app is reachable from outside the developer's machine.
