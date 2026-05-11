# Stage 6.10 — Lockout Self-Service Unlock (Design Spec)

> **Diataxis type:** Reference — design for a single roadmap sub-stage, consumed by `superpowers:writing-plans` next.
>
> **Roadmap row:** `docs/roadmap-phase-three.md` § Stage 6 sub-stages — row 6.10 ("Account lockout policy + self-service unlock signed-token endpoint"). The lockout *policy* half (10 attempts / 15-min window, `lockoutOnFailure: true`, backup-code-during-lockout) shipped in Stage 6b.2. This spec ships the *self-service unlock signed-token endpoint* half — the second piece of the row.
>
> **Sequencing:** Lands inside Stage 6. The 15-min lockout-token cleanup sweep, EF global query filter, and FK to `AspNetUsers.Id` are all Stage 7+ (see § 8). The unlock UI is Stage 9 (see § 8). The DB-level `INSERT-only` runtime role grant is Stage 16 (see § 8). The ES email template variant is Stage 8 (real email provider + bilingual catalog).
>
> **Stage 6 close-out dependency:** the close-out flow diagrams in `security-model.md` (roadmap line 575) overlay lockout-unlock writes on the request flowcharts AFTER 6.10 lands.

---

## 1. Purpose

When a user (or attacker probing the user's password) trips the 10-failed-attempt lockout threshold on `/api/auth/login`, the account locks for 15 minutes. Currently the only way out is to wait. This spec adds the second escape hatch named in `security-model.md` § Login → Account lockout self-service unlock (line 295): a time-limited signed unlock link emailed at the moment of lockout, redeemable via `POST /api/auth/lockout-unlock` to clear `AccessFailedCount` + `LockoutEnd` immediately.

The link defends the *legitimate user* against an attacker-induced DoS — an attacker who knows the victim's email can otherwise re-trigger lockout continuously, freezing the account indefinitely. The unlock-link mechanism gives the legitimate user a one-click recovery path that the attacker can't observe (the attacker doesn't have the user's inbox).

This stage ships:

1. The `LockoutUnlockToken` entity + migration.
2. The `LockoutUnlockTokenGenerator` + `LockoutUnlockService` (`IssueAsync` + `ConfirmAsync`).
3. The `LockoutUnlockController` exposing `POST /api/auth/lockout-unlock`.
4. The lockout-notification email template (EN).
5. Retroactive wiring into `AuthController.Login`'s lockout-engaging branch with a transition guard so the email fires exactly once per lockout window.
6. Wires the `AuditLogAction.LockoutSelfServiceUnlock` enum value reserved-not-wired in Stage 6.14.
7. Tests (~15) — integration per call site + architecture (§ 6).
8. Doc updates (§ 9).

This stage explicitly does **not** ship: a UI screen, an ES email variant, a token-cleanup background sweep, the EF global query filter, the FK to `AspNetUsers.Id`, or the DB-level `INSERT-only` grant. Each is sequenced into a later stage and listed in § 8.

---

## 2. Decisions (locked during brainstorm)

| # | Decision | Rationale |
|---|---|---|
| D1 | **Email-DoS guard: transition-only issuance.** `LockoutUnlockService.IssueAsync` is called from `AuthController.Login` *only* when the failing call to `PasswordSignInAsync` flipped `LockoutEnd` from null → not null. Already-locked accounts that receive subsequent bad-password attempts do NOT trigger additional issuances. | Without this, an attacker who knows the victim's email can amplify their bad-password loop into an email flood on the victim's inbox. The transition-detection guard limits the unlock email to at most one per lockout window (15 min), so an attacker can send at most 4 emails/hour, vs. unbounded otherwise. |
| D2 | **Token expiry: 15 minutes** (matches lockout duration). | A longer-lived token has no marginal user value — once the lockout self-expires naturally at 15 min, an unlock link does nothing useful. Tightest GDPR/minimization posture. Matches the magic number readers already learn from `PasswordResetService.TokenLifetime`. |
| D3 | **No MFA gate on confirm.** Token alone unlocks. | `/confirm` does NOT change password, does NOT change email, does NOT log the user in. It only undoes a side effect of failed logins. The unlocked account is still password-protected and (if MFA enabled) still MFA-protected on the next login attempt. Requiring TOTP at the unlock step would protect against "attacker has the email link" — but an attacker who has the link still cannot log in without the password, so the marginal protection is weak. Mirrors `EmailChangeService.RevokeAsync` (token-only, no MFA, because the action is an undo). |
| D4 | **Side effects of unlock: clear lock only.** `AccessFailedCount = 0`, `LockoutEnd = null`. Do NOT revoke `UserSession` rows. Do NOT regenerate `SecurityStamp`. Do NOT touch `EmailConfirmed`. Do NOT sign anyone in. | The unlock is reversing a side-effect of failed-login attempts — it's not a credential change, not a recovery flow, not a sign of compromise. Any other side effect would punish the legitimate user (the much more likely actor) for being locked out. Mirrors `EmailChangeService.RevokeAsync`'s "undo-only operations don't move security state". |
| D5 | **Single endpoint: `POST /api/auth/lockout-unlock`.** No `/request` endpoint. | The token is issued involuntarily by the auth pipeline at the moment of lockout — there is no user-initiated "please send me an unlock token" path. A `/request` endpoint would just be another email-amplification vector that contradicts D1. The recovery path for "I didn't get the email" is `/password-reset`. |
| D6 | **Rate limit: existing `AuthLoginByIp` (10/min/IP).** | Same policy already applied to every other anonymous auth endpoint. No new policy to wire, no new arch-test row. The single-use token verify is the per-account defence; the transition-only issuance is the per-account email throttle. |
| D7 | **`AuditLogAction.LockoutSelfServiceUnlock` written on confirm success.** | Wires the enum value reserved-not-wired in Stage 6.14. The IP, action, and user are sufficient to surface "your account was self-service-unlocked" in the Stage 12 audit-log UI. |

No ADR is required: every decision is consistent with prior architecture (token shape mirrors `PasswordResetToken` per ADR-0065; anonymous endpoint is in the security-model.md § Global Authorization Policy whitelist line 416; audit-write reuses the 6.14 reserved enum value).

---

## 3. Entity schema

`ProjectCeres.Models.LockoutUnlockToken`:

| Column | Type (PG) | EF type | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `uuid` | `Guid` | PK | Generator assigns `Guid.NewGuid()` client-side. |
| `UserId` | `uuid` | `Guid` | NOT NULL | The user whose lockout this token can clear. **No FK in 6.10.** Stage 7 adds the FK alongside the global query filter. |
| `TokenHash` | `varchar(512)` | `string` | NOT NULL | Argon2id PHC string of the raw 256-bit base64url token. Same hasher pinned to `m=19456 t=2 p=1` as `PasswordResetToken`, `EmailChangeToken`, passwords, persistent-cookie tokens, and backup codes. |
| `CreatedAt` | `timestamp with time zone` | `DateTime` | NOT NULL | When the token was issued — i.e., the moment lockout engaged. |
| `ExpiresAt` | `timestamp with time zone` | `DateTime` | NOT NULL | `CreatedAt + 15min`. Confirm rejects tokens past this with `INVALID_LOCKOUT_UNLOCK_TOKEN`. |
| `ConsumedAt` | `timestamp with time zone` | `DateTime?` | nullable | Set on first successful confirm OR on supersession by a new issuance. Single-use. |

**No `MfaVerifiedAt` column** (deliberately diverges from `PasswordResetToken`) — D3 has no MFA gate on confirm.

**Indexes:**

- `IX_LockoutUnlockTokens_UserId_ConsumedAt` covering `(UserId, ConsumedAt)` — supports the supersede-prior-unused query in `IssueAsync`.
- `IX_LockoutUnlockTokens_ExpiresAt` covering `(ExpiresAt)` — supports the Stage 7+ cleanup sweep.

**Token format:** raw token is 32 bytes from `RandomNumberGenerator.GetBytes(32)`, base64url-encoded (≈43 chars). Carried in the unlock URL fragment (`/app/lockout-unlock#token=<base64url>`) so it never appears in server logs or `Referer` headers per `security-model.md` § Logging and PII Redaction.

**Multi-tenancy:** scoped per user. Stage 7's cutover adds the EF global query filter on `UserId`. Until then, all queries explicitly filter by `UserId`.

**Constant-time discipline:** `ConfirmAsync` runs at least one Argon2 verify even when zero candidates match, so timing doesn't reveal "no rows."

---

## 4. Generator + Service

### 4.1 `LockoutUnlockTokenGenerator`

Direct sibling of `PasswordResetTokenGenerator`: `Generate()` returns the raw base64url string, `Hash(rawToken)` returns the Argon2id PHC string, `Verify(rawToken, hash)` returns bool. The pattern is one generator per token table in the codebase — keeps the call sites grep-able and avoids retrofitting a shared abstraction across three token tables for marginal gain.

DI: `builder.Services.AddScoped<LockoutUnlockTokenGenerator>()` in `Program.cs` immediately after `EmailChangeTokenGenerator` on line 112.

### 4.2 `LockoutUnlockService`

Location: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs`. Scoped DI.

Dependencies (constructor): `UserManager<ApplicationUser>`, `AppDbContext`, `Argon2idPasswordHasher`, `LockoutUnlockTokenGenerator`, `IEmailService`, `ILogger<LockoutUnlockService>`, `IAuditLogWriter`.

DI: `builder.Services.AddScoped<LockoutUnlockService>()` immediately after `EmailChangeService` on line 113.

#### Public surface

```csharp
public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

public Task IssueAsync(Guid userId, string ip, string userAgent,
                       string unlockUrlBase, CancellationToken ct);

public Task<LockoutUnlockOutcome> ConfirmAsync(string rawToken, CancellationToken ct);
```

Outcomes:

```csharp
public abstract record LockoutUnlockOutcome
{
    public sealed record Success : LockoutUnlockOutcome;
    public sealed record InvalidToken : LockoutUnlockOutcome;
}
```

Just two outcomes — confirm has no MFA branch, no password-policy branch, no rate-limit-exceeded branch (per-IP rate limit is the controller's `EnableRateLimiting` attribute, not the service).

#### `IssueAsync` behaviour

Mirrors `PasswordResetService.RequestAsync`'s known-email branch:

1. Per-user `SemaphoreSlim` (private static `ConcurrentDictionary<Guid, SemaphoreSlim>` field — same pattern as `PasswordResetService._userLocks`).
2. **Inside the lock**:
   - Bulk-supersede any prior unconsumed tokens for this user: `_db.LockoutUnlockTokens.Where(t => t.UserId == userId && t.ConsumedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);`
   - Generate raw token + hash + insert new `LockoutUnlockToken` row (`CreatedAt = now`, `ExpiresAt = now + TokenLifetime`, `ConsumedAt = null`).
   - `SaveChangesAsync`.
3. **Outside the lock**:
   - Build the unlock URL: `$"{unlockUrlBase.TrimEnd('/')}/app/lockout-unlock#token={rawToken}"`.
   - Send email via `_email.SendAsync(BuildLockoutEmail(user.Email, unlockUrl, ip), ct)` inside try/catch + `_logger.LogError` on failure (token row already committed; email-send failures don't roll back the issuance).

**Caller passes `userEmail`** so the service doesn't take a redundant `UserManager.FindByIdAsync` DB hit just to build the email — `AuthController.Login` already has the `userStub` in hand. Final signature: `IssueAsync(Guid userId, string userEmail, string ip, string userAgent, string unlockUrlBase, CancellationToken ct)`. (`userAgent` is currently unused but kept for future symmetry with the failed-login recorders — drop at implementation review if it stays unused.)

#### `ConfirmAsync` behaviour

Mirrors `PasswordResetService.ConfirmAsync` (minus the MFA, password-policy, and supersession-on-success branches):

1. Empty/whitespace `rawToken` → run one dummy Argon2 hash (constant-time defence) + return `LockoutUnlockOutcome.InvalidToken`.
2. Load candidate rows: `WHERE ConsumedAt IS NULL AND ExpiresAt > now()`.
3. Loop and `_generator.Verify` against each. First match wins; break.
4. No match → if `candidates.Count == 0`, run one dummy hash. Return `InvalidToken`.
5. Per-user `SemaphoreSlim`. Inside the lock:
   - Re-read the matched row by `Id` with `AsNoTracking`. If it's been consumed or expired between match and lock acquisition, return `InvalidToken`.
   - `user = await _userManager.FindByIdAsync(match.UserId.ToString())`. If null, return `InvalidToken`.
   - `await _userManager.ResetAccessFailedCountAsync(user);`
   - `await _userManager.SetLockoutEndDateAsync(user, null);`
   - `await _db.LockoutUnlockTokens.Where(t => t.Id == match.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);`
   - `await _auditLog.RecordAsync(user.Id, AuditLogAction.LockoutSelfServiceUnlock, ct: ct);`
   - Return `LockoutUnlockOutcome.Success`.

**No notification email on success** — D4's "undo-only, doesn't escalate notifications" principle.

#### `BuildLockoutEmail`

Private static method on `LockoutUnlockService`:

```csharp
private static EmailMessage BuildLockoutEmail(string to, string unlockUrl, string ip)
{
    const string subject = "Your Project Ceres account was locked";
    var bodyText = $"""
        Your Project Ceres account was just locked after several failed sign-in
        attempts from IP address {ip}.

        If this was you, your account will automatically unlock in 15 minutes.
        You can also unlock it now by clicking or pasting this link:
        {unlockUrl}

        This link expires in 15 minutes and can only be used once.

        If you did not attempt to sign in, your password may have been guessed.
        We recommend resetting your password from the sign-in page.
        """;
    var bodyHtml = $"""
        <p>Your Project Ceres account was just locked after several failed
        sign-in attempts from IP address <code>{ip}</code>.</p>
        <p>If this was you, your account will automatically unlock in 15 minutes.
        You can also unlock it now: <a href="{unlockUrl}">Unlock account</a>.</p>
        <p>This link expires in 15 minutes and can only be used once.</p>
        <p>If you did not attempt to sign in, your password may have been guessed.
        We recommend resetting your password from the sign-in page.</p>
        """;
    return new EmailMessage(to, subject, bodyHtml, bodyText);
}
```

**No HTML escaping on `ip`** — `Connection.RemoteIpAddress.ToString()` yields IPv4 dotted-quad or canonical IPv6; neither shape contains `<`, `>`, `&`, or quote characters. If the email template later carries `userAgent` (which is user-controlled), that field will need escaping.

**EN only at ship.** The ES variant lands with Stage 8 (real email provider + bilingual catalog).

---

## 5. Controller wiring

### 5.1 `LockoutUnlockController` (new file)

Location: `ProjectCeres/Controllers/Api/LockoutUnlockController.cs`. Single-action controller, same shape as `PasswordResetController`.

```csharp
[ApiController]
[Route("api/auth/lockout-unlock")]
public sealed class LockoutUnlockController : ControllerBase
{
    private readonly LockoutUnlockService _service;
    public LockoutUnlockController(LockoutUnlockService service) => _service = service;

    [HttpPost(""), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Confirm([FromBody] LockoutUnlockRequest request)
    {
        // ModelState validation is handled by InvalidModelStateResponseFactory → 422.
        // Don't add a manual `if (!ModelState.IsValid) return ValidationProblem(...)` — that
        // would return 400 (the factory only fires on auto-rejection).
        var outcome = await _service.ConfirmAsync(request.Token, HttpContext.RequestAborted);
        return outcome switch
        {
            LockoutUnlockOutcome.Success => NoContent(),
            LockoutUnlockOutcome.InvalidToken =>
                Unauthorized(new { error = new { code = "INVALID_LOCKOUT_UNLOCK_TOKEN",
                                                 message = "The unlock link is invalid or expired." } }),
            _ => throw new InvalidOperationException($"Unhandled outcome {outcome.GetType().Name}"),
        };
    }
}
```

Request DTO `ProjectCeres/ViewModels/Auth/LockoutUnlockRequest.cs`:

```csharp
public sealed class LockoutUnlockRequest
{
    [Required, StringLength(128)]
    public string Token { get; set; } = "";
}
```

### 5.2 `AuthController.Login` wiring (issue side)

Exactly one call site: inside the `if (signIn.IsLockedOut)` branch (currently around line 164), gated by a transition check that runs inside the existing per-user `_loginLocks` semaphore.

Constructor gains `LockoutUnlockService _lockoutUnlock`.

Sketch (final code lands in the implementation plan):

```csharp
// Inside the per-user lock that already serialises PasswordSignInAsync:
var wasLockedBefore = userStub.LockoutEnd is not null && userStub.LockoutEnd > DateTimeOffset.UtcNow;
signIn = await _signInManager.PasswordSignInAsync(
    userStub, request.Password, isPersistent: false, lockoutOnFailure: true);
await _db.Entry(userStub).ReloadAsync();
var isLockedAfter = userStub.LockoutEnd is not null && userStub.LockoutEnd > DateTimeOffset.UtcNow;
var lockoutTransitioned = !wasLockedBefore && isLockedAfter;

// ... existing branches ...

if (signIn.IsLockedOut)
{
    HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
    var (ip, ua) = RequestContext();
    await _failedLogins.RecordAsync(request.Email, userStub.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
    if (lockoutTransitioned)
    {
        try
        {
            await _lockoutUnlock.IssueAsync(userStub.Id, userStub.Email!, ip, ua,
                BuildUnlockUrlBase(), HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // IssueAsync internally swallows email failures; this catch covers
            // an unexpected DB write failure. We don't want a token-issue failure
            // to mask the user-visible lockout response.
            _logger.LogError(ex, "Failed to issue lockout-unlock token");
        }
    }
    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
}
```

`BuildUnlockUrlBase()` is a small private helper on `AuthController`:

```csharp
private string BuildUnlockUrlBase() => $"{Request.Scheme}://{Request.Host}";
```

**`AuthController` does not gain an `ILogger` dependency just for the issue-failure catch** — it already has access to logging via the controller's framework-injected logger; check the existing constructor for an `ILogger<AuthController>` and add one if not present.

### 5.3 `LoginTotp` is NOT wired

The three `IsLockedOutAsync` branches in `LoginTotp` (currently lines 216, 231, 266) observe an already-locked state caused by an earlier `Login`-path attempt — they do NOT engage lockout themselves (Stage 6b.2 removed framework counter mutation from the TOTP path). The transition can only happen in `Login`'s `PasswordSignInAsync`. Therefore `LoginTotp` does NOT call `IssueAsync`. Test `LoginTotp_observing_locked_state_does_NOT_issue_token` pins this.

---

## 6. Tests

Two new files plus three architecture-test additions. Target: **18 ship-gate tests** (5 issuance + 10 confirm + 3 architecture).

### 6.1 `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs`

| # | Test | Asserts |
|---|---|---|
| L1 | `Login_transitioning_into_lockout_writes_token_row_AND_queues_email` | Drive 10 bad passwords; assert exactly one `LockoutUnlockToken` row for `user.Id` + exactly one captured email to `user.Email` with the URL fragment. |
| L2 | `Login_already_locked_does_NOT_issue_a_second_token` | Manually `SetLockoutEndDateAsync(user, +15min)`; post one bad password; assert no new token row, no new captured email. The email-DoS defence in action. |
| L3 | `LoginTotp_observing_locked_state_does_NOT_issue_token` | Enroll MFA; drive password counter via `Login` to lockout; post bad TOTP at `/login/totp`; assert exactly one token row exists (from the Login transition, not LoginTotp). |
| L4 | `Issue_supersedes_prior_unconsumed_tokens` | Pre-insert an unconsumed `LockoutUnlockToken` row; drive a fresh transition; assert old row's `ConsumedAt` is set + exactly one new unconsumed row exists. |
| L5 | `Issue_email_send_failure_does_not_roll_back_token_row` | Replace `IEmailService` with a throwing stub; drive transition; assert the token row is committed AND the lockout response is still 401 ACCOUNT_LOCKED_OUT (the user-facing flow doesn't break on email-send failure). |

### 6.2 `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs`

| # | Test | Asserts |
|---|---|---|
| C1 | `Confirm_with_valid_token_clears_AccessFailedCount_and_LockoutEnd` | Trigger transition, extract token from captured email, POST `/api/auth/lockout-unlock` with raw token; assert 204 + user's `AccessFailedCount = 0` + `LockoutEnd = null`. |
| C2 | `Confirm_consumes_the_token_single_use` | Confirm twice with the same token; second call returns 401 `INVALID_LOCKOUT_UNLOCK_TOKEN`. |
| C3 | `Confirm_with_unknown_token_returns_401_INVALID_LOCKOUT_UNLOCK_TOKEN` | Post a random base64url string; assert 401 + error envelope. |
| C4 | `Confirm_with_expired_token_returns_401` | Pre-insert a token row with `ExpiresAt = now - 1s`; POST with its raw value; assert 401. (Requires reading the raw value back, which the test fixture mints via the generator then stores both raw + hash, then asserts only via the raw.) |
| C5 | `Confirm_with_empty_token_returns_422_validation_error` | Empty body or `{"token":""}`; assert 422 (via `InvalidModelStateResponseFactory`). |
| C6 | `Confirm_does_NOT_revoke_UserSessions` | Pre-create an active `UserSession` row; confirm; assert the row's `RevokedAt` is still null. Pins D4. |
| C7 | `Confirm_does_NOT_change_SecurityStamp` | Capture user's `SecurityStamp` before + after confirm; assert equal. Pins D4. |
| C8 | `Confirm_writes_LockoutSelfServiceUnlock_audit_row` | Confirm; assert exactly one `AuditLog` row for the user with `Action = LockoutSelfServiceUnlock`. Pins the 6.14 reserved-enum wiring. |
| C9 | `Confirm_concurrent_two_callers_one_succeeds_one_returns_invalid_token` | Two `HttpClient`s POST the same raw token concurrently; assert exactly one 204 + exactly one 401 (single-use enforced under concurrent confirm). |
| C10 | `Confirm_with_unknown_token_runs_at_least_one_Argon2_verify` | Wall-clock duration of unknown-token branch within 200ms of known-token-rejected branch (constant-time enumeration defence). |

### 6.3 Architecture (folded into existing `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`)

| # | Test | Asserts |
|---|---|---|
| A1 | `LockoutUnlockController_action_has_AllowAnonymous` | The `Confirm` action carries `[AllowAnonymous]`. Matches the security-model.md § Global Authorization Policy whitelist (line 416). |
| A2 | `LockoutUnlockController_action_has_AuthLoginByIp_rate_limit` | The `Confirm` action carries `[EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]`. Pins the existing-policy-reuse decision. |
| A3 | `LockoutUnlockController_does_not_have_class_level_AllowAnonymous` | Class-level `[AllowAnonymous]` is forbidden by the project's "every action declares its own auth intent" rule (matches the existing `EmailChangeController_does_not_have_class_level_AllowAnonymous` test). |

---

## 7. Migration

Filename: `<timestamp>_AddLockoutUnlockToken.cs`. Mirrors `Add_PasswordResetToken` in shape.

`Up`:

```csharp
migrationBuilder.CreateTable(
    name: "LockoutUnlockTokens",
    columns: table => new
    {
        Id = table.Column<Guid>(type: "uuid", nullable: false),
        UserId = table.Column<Guid>(type: "uuid", nullable: false),
        TokenHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
        CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
    },
    constraints: table => table.PrimaryKey("PK_LockoutUnlockTokens", x => x.Id));

migrationBuilder.CreateIndex(
    name: "IX_LockoutUnlockTokens_ExpiresAt",
    table: "LockoutUnlockTokens",
    column: "ExpiresAt");

migrationBuilder.CreateIndex(
    name: "IX_LockoutUnlockTokens_UserId_ConsumedAt",
    table: "LockoutUnlockTokens",
    columns: new[] { "UserId", "ConsumedAt" });
```

`Down`: `DropTable("LockoutUnlockTokens")`.

Not in this migration: FK to `AspNetUsers.Id` (Stage 7), `GRANT INSERT / REVOKE UPDATE, DELETE` on the table (Stage 16), the EF global query filter (Stage 7).

---

## 8. Out-of-scope follow-ups (durable record)

| Item | Stage | Why deferred |
|---|---|---|
| 15-min cleanup sweep (`DELETE FROM LockoutUnlockTokens WHERE ConsumedAt IS NOT NULL OR ExpiresAt < now() - interval '1 day'`) | Stage 7+ | Token-cleanup sweeps for all auth tables consolidate when the background-runner abstraction ships per ADR-0067. The `(ExpiresAt)` index supports this query. Until then, expired-and-unconsumed rows accumulate — but the row count is bounded by failed-login volume × users, which is small at personal-finance scale. |
| EF global query filter on `LockoutUnlockToken.UserId` | Stage 7 | Per ADR-0065, every user-owned filter lands together in the Stage 7 cutover. |
| FK from `LockoutUnlockToken.UserId` → `AspNetUsers.Id` | Stage 7 | Same migration as every other user-owned FK at cutover time. |
| `/app/lockout-unlock` SPA page (parses the URL fragment, POSTs to `/api/auth/lockout-unlock`, shows success/error) | Stage 9 | Per roadmap row 9.5 ("Lockout / self-service unlock screen"). |
| ES variant of the lockout email template | Stage 8 | Per planning-phase3.md Stage 3d email-template catalog — bilingual variants ship when the real email provider lands. |
| Runtime DB role `GRANT INSERT / REVOKE UPDATE, DELETE` on `LockoutUnlockTokens` | Stage 16 | Per security-model.md line 1141 (DB least-privilege is a Phase 3 hosting concern). The audit log (Stage 6.14) defers the same way. |
| `LoginTotp` issuance — if a future change moves password-counter mutation into the TOTP path | Whenever | The transition-detection pattern in this spec is reusable; the `IssueAsync` call would clone into the `LoginTotp` `IsLockedOutAsync` branch. Not needed today. |

Each row is also written into `docs/planning-phase3.md` as part of this stage's doc-sync (§ 9), so the deferrals are grep-able from the planning doc, not only from this spec.

---

## 9. Doc updates that ship with the code

In the same commit:

- **`docs/models.md`** — new `LockoutUnlockToken (Phase 3, Stage 6.10)` section after `PasswordResetToken`. Same structure as `PasswordResetToken`. TOC entry next to `PasswordResetToken`.
- **`docs/roadmap-phase-three.md`** —
  - Verification checklist line 517 (`Lockout email includes a time-limited signed unlock link separate from the password-reset flow`) → flip `[x]` with `*(Stage 6.10)*` annotation.
  - Add a new Stage 6.10 status banner immediately after the Stage 6.14 banner.
- **`docs/security-model.md`** — append a short "**Shipped 2026-MM-DD (Stage 6.10).**" annotation to the relevant paragraph (line 292 lockout description and line 295 self-service-unlock description). No functional content change.
- **`docs/planning-phase3.md`** — flip the `LockoutSelfServiceUnlock call site → Stage 6.10 (enum value reserved in 6.14)` follow-up line (currently at line 460) to *shipped 2026-MM-DD*.
- **`docs/api-contract.md`** — add a new row to the Auth endpoints section: `POST /api/auth/lockout-unlock` — anonymous; body `{ token }`; returns 204 on success / 401 `INVALID_LOCKOUT_UNLOCK_TOKEN` on failure.

---

## 10. Ship-gate checklist

- [ ] `LockoutUnlockToken` entity in `ProjectCeres.Models`.
- [ ] `LockoutUnlockTokenGenerator` in `ProjectCeres/Common/Authentication/`.
- [ ] Migration `AddLockoutUnlockToken` matches § 7 (six columns, two indexes, no FK, no GRANT).
- [ ] `LockoutUnlockService` in `ProjectCeres/Common/Authentication/` with `IssueAsync` + `ConfirmAsync` + private `BuildLockoutEmail`.
- [ ] `LockoutUnlockOutcome` discriminated-union sealed records (`Success` + `InvalidToken`).
- [ ] `LockoutUnlockController` at `ProjectCeres/Controllers/Api/` with single `Confirm` action.
- [ ] `LockoutUnlockRequest` DTO at `ProjectCeres/ViewModels/Auth/`.
- [ ] DI registrations: `LockoutUnlockTokenGenerator` after `EmailChangeTokenGenerator`, `LockoutUnlockService` after `EmailChangeService` (lines 112–113 of `Program.cs`).
- [ ] `AuthController.Login` wired: constructor takes `LockoutUnlockService`; transition guard inside per-user semaphore; `IssueAsync` call inside `if (signIn.IsLockedOut)` branch only when `lockoutTransitioned` is true.
- [ ] `LoginTotp` is **not** wired (verified by test L3).
- [ ] Audit row written by `ConfirmAsync` with `Action = LockoutSelfServiceUnlock` (verified by test C8).
- [ ] All 15 issuance + confirm tests (5 + 10) green plus 3 architecture tests = 18 ship-gate tests.
- [ ] All doc updates in § 9 in the same commit.
- [ ] `dotnet test` clean. `pnpm test` unchanged.
- [ ] `planning-phase3.md` carries the full § 8 follow-up list.
