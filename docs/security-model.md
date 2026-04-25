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

---

## Authentication and Session Rules

### Login

- **Account enumeration prevention:** login and password-reset endpoints return identical error messages and take identical wall-clock time regardless of whether the email exists. Always run Argon2id hash even when the user is not found — hash a dummy value and discard the result. The timing difference between "user not found" (no hash) and "wrong password" (Argon2id takes ~300ms) leaks whether an email is registered.
- **Rate limiting:** login and registration endpoints are rate-limited. Minimum: 10 requests per minute per IP. Supplement with account-level lockout: after N consecutive failed attempts on the same account (e.g. 10), lock for a fixed period (e.g. 15 minutes) and notify the user via email.
- **MFA:** mandatory TOTP for all users. No SMS — vulnerable to SIM-swap. See ADR rationale in `planning.md`.
- **TOTP replay prevention:** track recently accepted codes per user in a short-lived store. Reject any code used more than once within its validity window. ASP.NET Core Identity does not do this by default.

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
- `SameSite = Strict` or `Lax` — CSRF mitigation as a second layer alongside anti-forgery tokens

---

## Transport and Infrastructure Rules

### HTTPS

All traffic must use HTTPS. HTTP must redirect to HTTPS. HSTS (`Strict-Transport-Security`) must be configured once HTTPS is enforced. Configure in `Program.cs` via `UseHttpsRedirection()` and `UseHsts()`.

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
| `Content-Security-Policy` | Define before any JS is introduced | Restricts script/style sources, mitigates XSS |

Use `NetEscapades.AspNetCore.SecurityHeaders` or custom middleware.

### CORS

Required when the React SPA is on a different origin from the API (Phase 3). Configure via `AddCors` / `UseCors` in `Program.cs`. Whitelist only known frontend origins. **Never combine `AllowAnyOrigin` with `AllowCredentials`** — the CORS specification prohibits this and it collapses same-origin protection.

### CSRF

All state-changing forms must include CSRF anti-forgery tokens. ASP.NET Core's built-in anti-forgery middleware handles this via `[ValidateAntiForgeryToken]` on controllers and the `<form>` tag helper. JSON API endpoints authenticated via cookies also require anti-forgery protection — configure the anti-forgery middleware to validate on API routes.

---

## Input Validation Rules

- **Validate at the boundary:** all user input is validated at the controller/ViewModel level before reaching services. Services trust input from controllers but validate cross-entity business rules (e.g. currency matching).
- **ViewModels, not entities:** form inputs bind to ViewModel classes with validation attributes (`[Required]`, `[StringLength]`, etc.), not directly to EF Core entity classes. This prevents mass-assignment vulnerabilities.
- **Length limits:** define `[StringLength]` on all string fields in ViewModels to match the database column constraint. Never accept unbounded string input.
- **CSV export injection:** any exported CSV field must be sanitized. Values starting with `=`, `@`, `+`, or `-` are interpreted as formulas by spreadsheet applications. Prefix such values with a single quote to neutralize them.
- **Open redirect prevention:** any controller action that accepts a `returnUrl` parameter must validate it with `Url.IsLocalUrl(returnUrl)` before redirecting. If the value is not a local URL, fall back to the controller's own Index action. This applies to every Edit POST and Delete POST action that supports `returnUrl`. An unvalidated redirect allows an attacker to craft a link like `/Transactions/Edit/123?returnUrl=https://evil.com` that sends the user to an external site after a legitimate form submission.

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
| HSTS configured | — | — | Required |
| Argon2id for passwords | — | — | Required |
| TOTP mandatory | — | — | Required |
| TOTP replay prevention | — | — | Required |
| TOTP secrets encrypted at rest | — | — | Required |
| HTTP security headers | — | Required (CSP when JS added) | Required |
| CSRF anti-forgery tokens | Required | Required | Required |
| UUID primary keys | Required | Required | Required |
| IDOR prevention (UserId scoping) | — (single user) | — (single user) | Required |
| Account enumeration prevention | — | — | Required |
| Rate limiting on auth endpoints | — | — | Required |
| Account lockout after failed logins | — | — | Required |
| Session fixation prevention | — | — | Required |
| Secure cookie flags | — | — | Required |
| IP enforcement (user-configurable) | — | — | Required |
| IP blocking | — | — | Required |
| Active session list + revocation | — | — | Required |
| CORS policy | — | — (no separate origin) | Required |
| Forwarded headers middleware | — | — | Required |
| File upload MIME whitelist | Required (if built) | Required | Required |
| File upload magic bytes check | Required (if built) | Required | Required |
| File serving via authenticated action | Required (if built) | Required | Required |
| CSV export injection sanitization | — | Required (if export built) | Required |
| Open redirect prevention (`Url.IsLocalUrl`) | — | Required | Required |
| Dependency vulnerability scanning | Manual | Manual | CI pipeline |
| Database least privilege (DML user) | Recommended | Recommended | Required |
| GDPR compliance | — | — | Required before any external user |
| SPF DNS record active and passing | — | — | Required before Phase 3 launch |
| DKIM signing active and passing | — | — | Required before Phase 3 launch |
| DMARC policy configured (`p=none` minimum) | — | — | Required before Phase 3 launch |
| Email `To:` locked to authenticated user's own address | — | — | Required |
| User-controlled content sanitized before email render | — | — | Required |
| Email-triggering endpoints rate-limited | — | — | Required |
| Email service API key stored in secrets, send-only scope | — | — | Required |
| JWT access tokens (short-lived, signed, no DB storage) | — | — | Required |
| JWT secret stored outside DB (env/secrets store) | — | — | Required |
| Refresh token stored as hash only; rotated on each use | — | — | Required |
| `UserId` pseudonymised via HMAC in all data rows | — | — | Required |
| `USER_REF_SECRET` pepper stored outside DB | — | — | Required |
| HMAC rotation procedure documented before launch | — | — | Required before Phase 3 launch |
| Per-tenant payload encryption (amounts, descriptions) | — | — | Phase 4+ |

> Phase 1 and 2 are single-user and local. Many security controls are not required because there is no network exposure and no other users. All controls marked Required for Phase 3 must be in place before the app is reachable from outside the developer's machine.
