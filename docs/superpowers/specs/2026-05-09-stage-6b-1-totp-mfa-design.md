# Stage 6b.1 — TOTP MFA (opt-in) (design)

**Date:** 2026-05-09
**Phase:** 3 — Hosted Beta
**Roadmap reference:** `docs/roadmap-phase-three.md` § Stage 6 (Identity infrastructure, Batch 3b)
**Sub-stage covered:** 6.4 (TOTP enrollment + verification + replay prevention + backup codes)
**Sub-stages deferred:**

- **6b.2** — Lockout enforcement, rate limiting, failed-login logging, lockout self-service unlock signed-token endpoint (6.8 / 6.9 / 6.10).
- **6c** — Password reset, email-change, reauthentication middleware, audit log (6.11 / 6.12 / 6.13 / 6.14).

**Policy authority:** [ADR-0069](../../decisions/ADR-0069-mfa-opt-in-for-personal-users.md). MFA is opt-in. Login does not block on enrollment. The TOTP infrastructure described below ships so users who *choose* to enable MFA have a working flow; users who don't enable it pass through the unchanged Stage 6a login flow.

---

## Goal

Wire ASP.NET Core Identity's built-in TOTP support for opt-in MFA. Add backup codes (Argon2id-hashed, single-use, regen-on-explicit-action). Add a persistent replay-prevention store. Add three enrollment endpoints under `/api/auth/mfa/`. Branch the Stage 6a login flow on `TwoFactorEnabled`: if true, return a short-lived `IDataProtector`-protected ticket and require a second `POST /api/auth/login/totp` call before issuing the session cookie. **No UI; all verification via xUnit + `WebApplicationFactory` in the existing `Integration/Authentication/Mfa/` test sub-folder.**

---

## Locked decisions

| Decision | Choice |
|---|---|
| MFA enforcement | **Opt-in** per ADR-0069. Login does not block on enrollment. No grace period. No deadline. |
| TOTP storage | ASP.NET Identity built-in (`UserManager.GenerateNewAuthenticatorKey`, `VerifyTwoFactorTokenAsync`, `AspNetUserTokens` row keyed under `[AspNetUserStore].AuthenticatorKey`). |
| TOTP encryption at rest | ASP.NET Data Protection (Identity uses it transparently). Local dev: filesystem default at `~/.aspnet/DataProtection-Keys`. Production hardening (KMS / encrypted external volume) is deferred to **Stage 16 (Hosting + ops)** and is a Phase 3 launch gate. |
| Mid-enrollment seed | Identity's built-in candidate-seed flow. The seed is written to `AspNetUserTokens` on `GenerateNewAuthenticatorKey` and only confirmed (`SetTwoFactorEnabledAsync(true)`) after the first verifying code passes. |
| Login flow shape | Two endpoints: `POST /api/auth/login` (credentials → 200+ticket if MFA enabled, else 204+session); `POST /api/auth/login/totp` (ticket+code → 204+session). |
| Login ticket | `IDataProtector`-wrapped `{ userId, issuedAt }` payload, 5-min TTL, base64url-encoded. No DB row. Distinct purpose-string from Identity's other tokens. |
| Backup-code shape | One row per code: `UserMfaBackupCode { Id, UserId, CodeHash, CreatedAt, UsedAt nullable, UsedFromIp nullable }`. |
| Backup-code hash | Argon2id via the existing `Argon2idPasswordHasher` (m=19456 t=2 p=1). Same hasher already used for passwords + persistent-cookie tokens. |
| Backup-code format | 16 chars Crockford base-32 (no I/L/O/U), formatted `XXXX-XXXX-XXXX-XXXX`. ~80 bits entropy. Hyphens stripped server-side before hashing. |
| Backup-code regen | Explicit user action only. Single-use marking sufficient; remaining codes stay valid. UX nudge ships in Stage 9 UI. |
| Backup-code login | Same `/api/auth/login/totp` endpoint; the value is shape-detected (6-digit → TOTP, 16-char base-32 → backup code). |
| Replay-prevention store | `TotpReplayEntry { Id, UserId, CodeHash, AcceptedAt }`. Argon2id-hashed via the existing hasher. Auto-purged after 2min via opportunistic delete on every accept call. |
| QR delivery | Server returns raw `otpauth://` URI + `manualEntryKey`; SPA renders QR client-side via `qrcode.react` (lands in Stage 9). Response carries `Cache-Control: no-store, no-cache`. |
| Endpoints | Three new MFA endpoints + one new login endpoint. All MFA endpoints require an authenticated session. Reauth gate on `enroll/verify` (re-enrollment) and `backup-codes/regenerate` is added in 6c when the reauth middleware lands. |
| Spelling | American English throughout: `enroll`, `enrollment`. Matches existing project style. |
| `ApplicationUser.CreatedAt` | Added in this stage for audit / analytics. **No behavioural role** in 6b.1's login flow (no grace cliff). |
| UI | None. Stage 9 ships React enrollment + login-TOTP + backup-codes display + recovery flow. 6b.1 verifies via xUnit. |

### Sub-decision worth recording: `TotpReplayEntry.CodeHash` uses Argon2id, not SHA-256

The first draft of this design proposed SHA-256 for the replay-entry hash to avoid a "perceptible" 50ms cost on TOTP verify. This was researched and rejected:

- **Nielsen's 100ms perception threshold** (and Normoyle et al. 2014's mean ~65ms) put 50ms below the threshold where users can detect latency. OWASP's *target* for password-verify is 200–500ms by design — the security-vs-UX trade-off is already calibrated for a slow hash.
- **SHA-256 has a hidden weakness in this context.** TOTP codes have only 1,000,000 possible values. SHA-256 unsalted (which is the natural shape if we hash the bare code) makes a database dump rainbow-tableable in seconds against the 1M space — leaking which codes were accepted recently. Argon2id with a per-row salt closes that gap entirely.
- **Decision: Argon2id**, reusing the existing `Argon2idPasswordHasher` (no new code, no new tuning surface). The marginal ~50ms on every TOTP-verify is invisible.

---

## Architecture

### 1. Packages

No new packages. ASP.NET Identity's built-in `TotpSecurityStampBasedTokenProvider` (registered by `AddDefaultTokenProviders()` from Stage 6a) provides `GenerateNewAuthenticatorKey` and `VerifyTwoFactorTokenAsync`. The Argon2id hasher already exists. Backup codes use a 16-char base-32 generator implemented inline with `RandomNumberGenerator.GetBytes`.

### 2. `ApplicationUser` change

Add a single property:

```csharp
public class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Migration `AddCreatedAtToApplicationUser`:

- Adds `CreatedAt timestamp with time zone NOT NULL DEFAULT now()`.
- Backfills existing rows with `current_timestamp` (no behavioural impact since 6b.1 doesn't act on `CreatedAt`).

`TwoFactorEnabled` already exists on `AspNetUsers` (Identity defaults). Identity manages the bit; we only flip it via `UserManager.SetTwoFactorEnabledAsync(user, true)` after a successful first-code verify.

### 3. New entities

```csharp
public sealed class UserMfaBackupCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";   // Argon2id PHC string
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? UsedFromIp { get; set; }
}

public sealed class TotpReplayEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";   // Argon2id PHC string
    public DateTime AcceptedAt { get; set; }
}
```

Migration `AddMfaBackupCodesAndReplayPrevention`:

- Creates `UserMfaBackupCodes` and `TotpReplayEntries`.
- Indexes:
  - `UserMfaBackupCode (UserId)` plus a Postgres partial index `WHERE "UsedAt" IS NULL` for the unused-codes lookup.
  - `TotpReplayEntry (UserId)` for per-user replay scan.
  - `TotpReplayEntry (AcceptedAt)` for the purge query.
- No FK to `AspNetUsers` (still deferred until Stage 7's data remap, consistent with the Stage 6a pattern for `UserSession` and `UserBlockedIp`).

### 4. Services

#### `MfaBackupCodeService`

```csharp
public sealed class MfaBackupCodeService
{
    private const int CodeCount = 10;
    private const int CharsPerCode = 16;
    private static readonly char[] Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ".ToCharArray();

    Task<IReadOnlyList<string>> GenerateAndPersistAsync(Guid userId, CancellationToken ct);
    Task<bool> VerifyAndConsumeAsync(Guid userId, string submittedCode, string clientIp, CancellationToken ct);
    Task RegenerateAsync(Guid userId, CancellationToken ct);
}
```

- `GenerateAndPersistAsync` produces 10 random Crockford-base-32 codes (16 chars each, ~80 bits entropy), formats them `XXXX-XXXX-XXXX-XXXX` for display, hashes the **un-formatted** raw code via `Argon2idPasswordHasher`, persists, returns the formatted strings to the caller. Caller is responsible for displaying once.
- `VerifyAndConsumeAsync` strips hyphens + uppercases the input, then linear-scans this user's not-yet-used codes (typically 0–10 rows) and Argon2id-verifies against each. On match: marks `UsedAt = now`, `UsedFromIp = clientIp`, returns true. On no match: returns false.
- `RegenerateAsync` deletes all rows for this user, calls `GenerateAndPersistAsync`. Used by the explicit regenerate endpoint.

#### `TotpReplayGuard`

```csharp
public sealed class TotpReplayGuard
{
    Task<bool> TryAcceptAsync(Guid userId, string code, CancellationToken ct);
}
```

- Hashes `code` with the existing Argon2id hasher.
- Linear-scans `TotpReplayEntries` for this user where `AcceptedAt > now - 2min`, Argon2id-verifies each candidate against the freshly-hashed input. (Argon2id verifies against arbitrary inputs by design; per-row salts mean we can't compute a single hash and lookup — we have to verify each candidate. Per-user typically 0–2 rows in the 2-min window.)
- If a match exists → returns false (replay).
- If no match → inserts a new `TotpReplayEntry { Id=NewGuid, UserId, CodeHash, AcceptedAt=now }`, runs an opportunistic purge `DELETE WHERE AcceptedAt < now - 2min` on every accept call (single SQL statement, idempotent), returns true.
- The opportunistic purge replaces a scheduled job — at single-user-beta scale it's strictly cheaper than running a background sweep.

#### `MfaTicketService`

```csharp
public sealed class MfaTicketService
{
    string Issue(Guid userId);
    Result<Guid> Verify(string ticket);
}
```

Wraps an `IDataProtector` purpose-keyed `Project Ceres / MFA Login Ticket / v1`. Issues a base64url-encoded payload `{ userId, issuedAt }` via `Protect`. Verifies via `Unprotect` + checks `now - issuedAt <= 5min`. The 5-min window is short enough to limit attack surface but long enough for a typing-impaired user to find their authenticator app. The data-protector ensures forgery requires the server's key material (the same key material that protects auth cookies — already a Phase 3 launch concern via the Data Protection key-storage hardening deferred to Stage 16).

### 5. Login flow changes

`POST /api/auth/login` is modified from Stage 6a:

```
POST /api/auth/login  { email, password, rememberMe }
  → SignInManager.PasswordSignInAsync (existing 6a logic)
    → on failure: return 401  (existing 6a behaviour, dummy-hash on user-not-found)
    → on success:
        → if user.TwoFactorEnabled is FALSE:
              → existing 6a path: insert UserSession, set __Host-Session cookie,
                                  optional __Host-Persist cookie if rememberMe,
                                  rotate CSRF cookie, return 204 No Content.
        → if user.TwoFactorEnabled is TRUE:
              → undo the SignInAsync that PasswordSignInAsync just did
                (no session cookie issued — credentials are valid but second
                 factor still required)
              → issue an MFA ticket via MfaTicketService.Issue(user.Id)
              → return 200 OK with body { ticket, ticketExpiresAt }
              → DO NOT set any session cookie yet.
              → DO rotate the CSRF cookie (the next call to /login/totp needs it).
```

**Implementation note:** `SignInManager.PasswordSignInAsync` always signs in on success — it's not parameterizable. The clean way to "validate without signing in" is to use `UserManager.CheckPasswordAsync(user, password)` plus manual lockout-counter handling. Stage 6a's `PasswordSignInAsync` includes lockout integration; Stage 6b.2 adds the lockout enforcement. For 6b.1: we keep `PasswordSignInAsync` for the credential check, and if MFA is enabled, immediately call `SignInManager.SignOutAsync()` to undo the cookie issue before the response is written. Net result is identical and simpler than rewriting the credential path. This is documented as a known-and-acceptable "issue then undo" pattern in the implementation.

```
POST /api/auth/login/totp  { ticket, code }
  → MfaTicketService.Verify(ticket)
    → on failure (invalid / expired): return 401
    → on success: extract userId
  → Detect code shape:
      → 6 digits → TOTP path:
            → UserManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)
              (Identity's own replay-window logic operates within a 30-second window;
               our TotpReplayGuard adds the cross-window "this exact code has been
               accepted recently" check)
            → on failure: return 401
            → TotpReplayGuard.TryAcceptAsync — if false (replay): return 401
            → on success: full Stage 6a session-issue path (insert UserSession,
              set cookies, rotate CSRF, return 204).
      → after the TOTP path failed to match (length != 6 or contains non-digit),
        check backup-code shape: strip `-` separators, then test
        `^[0-9A-HJKMNP-TV-Z]{16}$` (case-insensitive Crockford base-32 — excludes I/L/O/U, 16 chars).
        Match → backup-code path:
            → MfaBackupCodeService.VerifyAndConsumeAsync
            → on failure: return 401
            → on success: full Stage 6a session-issue path (insert UserSession,
              set cookies, rotate CSRF, return 204).
              The single-use marking happened inside VerifyAndConsumeAsync.
      → otherwise: return 401.
```

`POST /api/auth/login/totp` is `[AllowAnonymous]` + `[ValidateAntiForgeryToken]` — the user is not yet authenticated when calling it (the ticket is the auth proof, not a session cookie).

### 6. MFA enrollment endpoints

All under `/api/auth/mfa/` and require an authenticated session. Reauth gate on the latter two ships in 6c when reauth middleware lands.

#### `POST /api/auth/mfa/enroll`

- Authentication required.
- `UserManager.GenerateNewAuthenticatorKey(user)` writes the candidate seed to `AspNetUserTokens`.
- Read it back via `UserManager.GetAuthenticatorKeyAsync(user)`.
- Build `otpauthUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30"` where `issuer = "Project Ceres"`.
- `manualEntryKey` = the same secret formatted in groups of 4 chars for human typing (e.g. `XXXX XXXX XXXX XXXX XXXX XXXX XXXX XX`).
- Returns `200 OK { otpAuthUri, manualEntryKey }` with response headers `Cache-Control: no-store, no-cache` + `Pragma: no-cache`.
- This endpoint is **idempotent in spirit** but Identity's `GenerateNewAuthenticatorKey` overwrites the existing candidate. A user who calls `/enroll` twice without verifying gets a fresh seed each time — the previous candidate is invalidated. Consistent with the spec's "enrollment is verified before activation" rule.

#### `POST /api/auth/mfa/enroll/verify { code }`

- Authentication required.
- Find candidate seed via `UserManager.GetAuthenticatorKeyAsync(user)`. If absent: 400 (`"no enrollment in progress"`).
- `UserManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)`.
- On failure: 400 (`"code did not verify"`).
- On success:
  - `UserManager.SetTwoFactorEnabledAsync(user, true)` → flips the bit.
  - `MfaBackupCodeService.GenerateAndPersistAsync(userId)` → 10 fresh codes.
  - Returns `200 OK { backupCodes: [...10 strings] }` with `Cache-Control: no-store, no-cache`.

#### `POST /api/auth/mfa/backup-codes/regenerate`

- Authentication required. **Reauth gate added in 6c.**
- `MfaBackupCodeService.RegenerateAsync(userId)`.
- Returns `200 OK { backupCodes: [...10 strings] }` with `Cache-Control: no-store, no-cache`.

### 7. Data Protection key storage (production launch gate)

Identity uses `IDataProtector` to encrypt the TOTP seed in `AspNetUserTokens` (when properly wired). For Stage 6b.1:

- **Local dev + tests:** filesystem default at `~/.aspnet/DataProtection-Keys` is sufficient.
- **Production:** key storage must move to a separate trust boundary (Azure Key Vault / AWS KMS / encrypted external volume per `security-model.md` § TOTP Secrets). This is a **Stage 16 (Hosting + ops) concern and a Phase 3 launch gate.**

Stage 6b.1 does not block on the production hardening; the spec calls it out so it doesn't get forgotten.

### 8. Configuration

No new `appsettings.json` keys. Identity's existing options (set in Stage 6a) cover everything; the MFA-specific behaviour is hardcoded:

- Backup-code count: 10 (constant in `MfaBackupCodeService`).
- Backup-code length: 16 chars (constant).
- Replay window: 2 minutes (constant in `TotpReplayGuard`).
- Ticket TTL: 5 minutes (constant in `MfaTicketService`).
- Crockford alphabet: constant.

If any of these need tuning later, a single named-options class can be introduced. Not in 6b.1.

---

## Files to create / modify

### Production code (`ProjectCeres/`)

- Modify `Models/ApplicationUser.cs` — add `CreatedAt`.
- Create `Models/UserMfaBackupCode.cs`.
- Create `Models/TotpReplayEntry.cs`.
- Modify `Data/AppDbContext.cs` — register the two new entities, indexes, partial-index.
- Create `Migrations/<ts>_AddCreatedAtToApplicationUser.cs` (auto-generated).
- Create `Migrations/<ts>_AddMfaBackupCodesAndReplayPrevention.cs` (auto-generated).
- Create `Common/Authentication/MfaBackupCodeService.cs`.
- Create `Common/Authentication/TotpReplayGuard.cs`.
- Create `Common/Authentication/MfaTicketService.cs`.
- Create `Controllers/Api/MfaController.cs` — three enrollment endpoints.
- Modify `Controllers/Api/AuthController.cs` — branch login on `TwoFactorEnabled`; add `/login/totp` action.
- Modify `Program.cs` — register `MfaBackupCodeService`, `TotpReplayGuard`, `MfaTicketService`. Configure `IDataProtector` purpose-key for `MfaTicketService` (named `Project Ceres / MFA Login Ticket / v1`).

### Test code (`ProjectCeres.Tests/Integration/Authentication/Mfa/`)

New sub-folder under existing `Authentication/`:

- `MfaEnrollmentTests` — `/enroll` returns valid otpauth URI + manual key; `/enroll/verify` with correct code flips `TwoFactorEnabled` + returns 10 backup codes; `/enroll/verify` with wrong code keeps `TwoFactorEnabled = false`; calling `/enroll` twice invalidates the first candidate.
- `MfaBackupCodeTests` — `GenerateAndPersistAsync` produces 10 unique codes; persisted codes are Argon2id-hashed (`$argon2id$v=19$m=19456,t=2,p=1$` prefix); `VerifyAndConsumeAsync` marks `UsedAt`; second consume of the same code returns false; `RegenerateAsync` deletes existing rows + inserts 10 new.
- `LoginWithTotpTests` — login with `TwoFactorEnabled=true` returns 200 + ticket (no session cookie set); `/login/totp` with valid TOTP issues session; `/login/totp` with replayed code returns 401; `/login/totp` with valid backup code issues session + marks code used; `/login/totp` with expired ticket returns 401; `/login/totp` with ticket from another user returns 401 on totp-verify.
- `LoginWithoutMfaTests` — login with `TwoFactorEnabled=false` returns 204 + session cookie (confirms ADR-0069 opt-in: no grace cliff); login works regardless of `CreatedAt` age.
- `TotpReplayGuardTests` — same code accepted once then rejected on second accept; rows older than 2min are purged on the next accept call; entries for one user don't block another user's same-numeric-code (per-user scoping).
- `MfaCacheControlTests` — `/enroll`, `/enroll/verify`, and `/backup-codes/regenerate` responses all carry `Cache-Control: no-store, no-cache` and `Pragma: no-cache`.
- `MfaTicketServiceTests` — Issue/Verify round-trips for the same user; expired ticket (synthesise via mocked clock) rejected; ticket-payload tampering rejected (corrupt the base64 → Verify fails); ticket cannot be cross-applied to a different user (the userId is in the protected payload).

### Test fixture additions

- `AuthTestFixture.EnrollUserMfaAsync(factory, user)` helper — calls `GenerateNewAuthenticatorKey`, computes a current TOTP code via Identity's own algorithm, calls `SetTwoFactorEnabledAsync(true)`, returns the seed (so tests can compute fresh codes for subsequent steps).

### Files NOT touched

- `WafCollection.cs` / `TestAuthenticationHandler.cs` — the two-factory split from 6a still applies. Pre-Stage-6a CRUD tests use `TestWebApplicationFactory` (auto-auth bypasses MFA entirely — no TOTP factor expected). Auth tests use `AuthTestWebApplicationFactory` (real pipeline). 6b.1 tests use the latter.
- Stage 6a's `AuthController.Register`, `AuthController.Logout`, `AuthController.Csrf` — no changes.

---

## Migration safety

Both migrations run cleanly against the existing dev + test DBs:

- `AddCreatedAtToApplicationUser` — single column add with a default. Existing rows backfilled with `current_timestamp` at migration time.
- `AddMfaBackupCodesAndReplayPrevention` — new tables with indexes. No FK to `AspNetUsers` yet (Stage 7's data remap adds those).

No data migration is needed. No production data exists to migrate.

---

## Out of scope for 6b.1

Per ADR-0069 + the roadmap split:

**Deferred to 6b.2:**
- Lockout enforcement (the option is set in 6a; the *enforcement* path runs here through `PasswordSignInAsync`'s `lockoutOnFailure: true` argument, but the test "user is locked out after N failed attempts" is in 6b.2 along with the lockout email).
- Rate limiting on `/login`, `/login/totp`, `/register`, `/mfa/*`.
- Failed-login logging table.
- Lockout self-service unlock signed-token endpoint.
- Test: "backup codes accepted during lockout" (cross-stage, 6b.2 territory).

**Deferred to 6c:**
- MFA disable endpoint.
- Reauth gate on `/mfa/enroll/verify` (re-enrollment) and `/mfa/backup-codes/regenerate`.
- Password-reset MFA gate (now conditional on user's MFA-enabled state per the doc sync).
- Email-change flow.
- Audit log writes (login, MFA enroll/disable, backup-code use, etc.).
- New-device email-link step-up for non-MFA users (Bitwarden pattern).
- New-device login email alert.

**Deferred to Stage 9:**
- React UI for enrollment, login TOTP screen, backup-codes display, backup-code regenerate flow, recovery flow.

**Deferred to Stage 16:**
- Production Data Protection key storage (KMS / encrypted external volume).

---

## Verification

```bash
# Local dev DB — apply migrations
dotnet ef database update --project ProjectCeres
dotnet ef database update --project ProjectCeres \
  --connection "Host=localhost;Database=project_ceres_test;Username=postgres;Password=postgres"

# Tests (foreground per project memory)
dotnet test --filter "FullyQualifiedName~Authentication"   # Stage 6a (24) + 6b.1 (~30+) tests
dotnet test                                                 # full suite — every pre-6a test still green

# Release build
dotnet build -c Release
```

Acceptance criteria:

- All 6b.1 tests pass; the existing 618 from 6a stay green.
- Migrations apply cleanly to a fresh dev + test DB.
- `Cache-Control: no-store, no-cache` confirmed on all three MFA endpoint responses + on `/enroll/verify`'s backup-codes response.
- `dotnet build -c Release` is zero-warnings, zero-errors.
- No `[HttpGet]` action whose name starts with a write verb (the existing architecture test enforces this; new MFA endpoints are all `[HttpPost]`).
- No class-level `[AllowAnonymous]` on the new `MfaController` (the existing architecture test enforces this).

Stage 6 verification checklist updates in `roadmap-phase-three.md`:

- TOTP block: items related to enrollment + replay + backup codes flip to `[x]`.
- The "TOTP enrollment is opt-in" line stays (already updated by the supersession sweep).
- Items related to lockout / rate limiting / failed-login stay `[ ]` (those are 6b.2).

---

## Open questions

None. All design decisions resolved during brainstorming on 2026-05-09 and locked via ADR-0069 + the supersession sweep.

---

## References

- ADR-0069 — MFA opt-in for personal users (the authoritative policy decision)
- `docs/superpowers/specs/2026-05-09-stage-6a-identity-foundation-design.md` — the predecessor stage's design
- `docs/security-model.md` § TOTP Secrets, § TOTP Backup Codes, § Login → MFA, § Required matrix
- `docs/planning-phase3.md` § Authentication, § MFA
- `docs/roadmap-phase-three.md` § Stage 6 (Identity infrastructure, Batch 3b)
- NIST SP 800-63B-4 (final 2025-07-31): https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-63B-4.pdf
- RFC 6238 — TOTP: Time-Based One-Time Password Algorithm: https://datatracker.ietf.org/doc/html/rfc6238
- RFC 4648 — Base 16, Base 32, and Base 64 Data Encodings: https://datatracker.ietf.org/doc/html/rfc4648 (Crockford base-32 is a variant)
