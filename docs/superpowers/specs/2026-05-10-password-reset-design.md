# Stage 6c Sub-stage 6.11 — Password Reset Flow

**Date:** 2026-05-10
**Stage:** Phase 3, Stage 6c (Identity infrastructure, Batch 3b continued)
**Sub-stage:** 6.11 (password reset)
**Author:** brainstormed with user, approved 2026-05-10

## 1. Scope

Implement the password-reset flow per `security-model.md` § Password Reset and ADR-0069 (MFA opt-in).

In scope:
- Two new API endpoints — `POST /api/auth/password-reset/request` and `POST /api/auth/password-reset/confirm`
- `PasswordResetToken` entity (Argon2id-hashed tokens, 15-min expiry, single-use)
- `PasswordResetService` orchestration
- `IEmailService` abstraction with a `LogOnlyEmailService` development implementation (Stage 8 will swap in the real provider)
- Per-user 5/hour and per-IP 10/min rate limits
- All-sessions-revoked-on-success
- MFA-conditional gating: TOTP required at confirm time when `user.TwoFactorEnabled = true`; backup codes are NOT accepted (per ADR-0069)
- Account-enumeration prevention: identical response and timing for known/unknown emails
- Notification emails (request + change-confirmed)

Out of scope (deferred to other 6c sub-stages):
- Lockout self-service unlock (separate spec — same email-tokens mechanic, different lifetimes/effects)
- Email-address-change flow (6.12)
- Reauthentication middleware for sensitive operations (6.13)
- `AuditLog` entity and writer service (6.14)
- Real email provider implementation (Stage 8)
- EN/ES `.resx` template files (Stage 8) — hand-written EN strings live inline until then

---

## 2. Architecture & components

### 2.1 New entity — `PasswordResetToken`

Location: `ProjectCeres/Models/PasswordResetToken.cs`.

```csharp
public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";   // Argon2id-hashed; max 512 chars
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? MfaVerifiedAt { get; set; }   // set after TOTP step (MFA users only)
}
```

DbContext registration follows the `TotpReplayEntry` / `UserMfaBackupCode` shape:

```csharp
modelBuilder.Entity<PasswordResetToken>(b =>
{
    b.HasKey(e => e.Id);
    b.HasIndex(e => new { e.UserId, e.ConsumedAt });
    b.HasIndex(e => e.ExpiresAt);
    b.Property(e => e.TokenHash).HasMaxLength(512);
});
```

Add `DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();` to `AppDbContext`.

### 2.2 New service — `PasswordResetService`

Location: `ProjectCeres/Common/Authentication/PasswordResetService.cs`.

Public surface (every method takes `CancellationToken`):

```csharp
Task RequestAsync(string email, string ip, string userAgent, CancellationToken ct);

Task<PasswordResetConfirmOutcome> ConfirmAsync(
    string rawToken, string newPassword, string? totpCode, CancellationToken ct);
```

`PasswordResetConfirmOutcome` is a discriminated outcome:

```csharp
public abstract record PasswordResetConfirmOutcome
{
    public sealed record Success : PasswordResetConfirmOutcome;
    public sealed record InvalidToken : PasswordResetConfirmOutcome;
    public sealed record RequiresTotp : PasswordResetConfirmOutcome;
    public sealed record InvalidTotp : PasswordResetConfirmOutcome;
    public sealed record PasswordPolicyViolation(IReadOnlyList<IdentityError> Errors) : PasswordResetConfirmOutcome;
}
```

### 2.3 New helper — `PasswordResetTokenGenerator`

Location: `ProjectCeres/Common/Authentication/PasswordResetTokenGenerator.cs`. Mirrors `PersistentTokenService` exactly (the API surface differs — there's no cookie-format step — but the token-generation and hashing surface is the same).

```csharp
public sealed class PasswordResetTokenGenerator
{
    private readonly Argon2idPasswordHasher _hasher;
    public PasswordResetTokenGenerator(Argon2idPasswordHasher hasher) => _hasher = hasher;

    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public string Hash(string token) => _hasher.HashPassword(new ApplicationUser(), token);

    public bool Verify(string token, string storedHash)
    {
        var result = _hasher.VerifyHashedPassword(new ApplicationUser(), storedHash, token);
        return result is PasswordVerificationResult.Success
                       or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
```

### 2.4 New abstraction — `IEmailService`

Location: `ProjectCeres/Common/Email/IEmailService.cs` and `ProjectCeres/Common/Email/EmailMessage.cs`.

```csharp
public sealed record EmailMessage(string To, string Subject, string BodyHtml, string BodyText);

public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
```

Two implementations register:

- `LogOnlyEmailService` — writes the message at `LogLevel.Information` to `ILogger<LogOnlyEmailService>`. Wired in Development. Body is logged in full, including the password-reset URL, so dev/test workflows can grab the link from console output.
- `NoopEmailService` — used by integration tests via `ConfigureTestServices` to silence log output. Strict-mock alternative is also acceptable when a test wants to verify the call.

In Production, `IEmailService` is intentionally not registered — DI resolution will throw at startup, blocking deployment until Stage 8 wires in the real provider. This is by design (see § 9 below).

### 2.5 New controller — `PasswordResetController`

Location: `ProjectCeres/Controllers/Api/PasswordResetController.cs`. Route prefix `/api/auth/password-reset`.

Two endpoints, both `[AllowAnonymous]`:

- `POST /request` — body `{email}`. Returns `204 No Content` always (constant-time, no enumeration leak).
- `POST /confirm` — body `{token, newPassword, totpCode?}`. Returns `204` on success, `200 {requiresTotp: true}` if MFA-enabled user submitted without a code, error envelope otherwise.

Both endpoints carry `[EnableRateLimiting(...)]` attributes — see § 3.4.

### 2.6 Reset URL format

Email body contains a link of the form:

```
https://{host}/app/password-reset#token={base64url}
```

The token lives in the URL **fragment**, never the query string. Browsers don't send fragments to servers and don't include them in `Referer` headers — this matches `security-model.md:599` ("Full URLs when they may contain tokens (e.g., password-reset links in Referer headers)" — referring to what to avoid).

The SPA reads `window.location.hash`, parses out `token=...`, and POSTs to `/api/auth/password-reset/confirm`.

`{host}` resolution: the controller reads `Request.Scheme` and `Request.Host` to construct the link. The Stage 9 frontend will live under the same origin.

---

## 3. Data flow

### 3.1 Request flow — `POST /api/auth/password-reset/request`

1. **Rate-limit gates** (in order):
   - `AuthRateLimitPolicies.AuthLoginByIp` — 10/min/IP sliding window (reused).
   - `AuthRateLimitPolicies.AuthPasswordResetByEmail` — 5/hour, key = lowercased trimmed email. Malformed emails fall through to a single shared anonymous bucket (`AnonymousPasswordResetPartition`) to avoid leaking via the bucket-creation pattern.
2. `[ApiController]` ModelState validation auto-422 catches empty/malformed `email`.
3. Controller calls `PasswordResetService.RequestAsync(email, ip, ua, ct)`.
4. Inside `RequestAsync` (single transaction where applicable):
   - `_userManager.FindByEmailAsync(email)`.
   - **Always** call `_argon.RunDummyHash()` (existing helper on `Argon2idPasswordHasher`) so wall-clock time is independent of branch.
   - **If user is null:** record a `FailedLoginAttempt` with new reason `PasswordResetUnknownEmail` (observability without leaking to the caller). Return without DB write or email send.
   - **If user is non-null:**
     - Inside per-user semaphore (`_resetLocks.GetOrAdd(user.Id, ...)`):
       - `UPDATE password_reset_tokens SET ConsumedAt = @now WHERE UserId = @id AND ConsumedAt IS NULL` (supersede prior unused tokens).
       - Generate raw token via `PasswordResetTokenGenerator.Generate()`.
       - Insert new row: `Id`, `UserId = user.Id`, `TokenHash = generator.Hash(raw)`, `CreatedAt = now`, `ExpiresAt = now + 15min`, `ConsumedAt = null`, `MfaVerifiedAt = null`.
       - `SaveChangesAsync(ct)`.
     - Build email: `EmailMessage(To = user.Email, Subject = "Reset your Project Ceres password", BodyText = ..., BodyHtml = ...)` with the reset URL embedded.
     - `_email.SendAsync(message, ct)` — wrapped in try/catch; failure is logged but never throws back to the caller (the DB row is already committed; user can request again).
5. Controller returns `204 No Content`.

### 3.2 Confirm flow (no MFA) — `POST /api/auth/password-reset/confirm` with `{token, newPassword}`

1. **Rate-limit:** `AuthLoginByIp` (10/min/IP). Confirm endpoint also gets per-IP coverage so an attacker holding a stolen email cannot brute-force TOTPs server-side faster than the 30-sec rotation. No per-user limit at confirm — the request endpoint already burns the per-user bucket.
2. ModelState validation: `token` non-empty, `newPassword` ≥ 8 chars (matches `MfaAwareLengthValidator`'s policy — Identity password validators run inside `AddPasswordAsync` anyway, but the front-line check produces a friendlier 422 with `details[]`).
3. `PasswordResetService.ConfirmAsync` body, in a single DB transaction with per-user semaphore:
   - Look up unconsumed, unexpired token rows. Token-hash lookup: load all candidates `WHERE ConsumedAt IS NULL AND ExpiresAt > @now`, run `generator.Verify(rawToken, candidate.TokenHash)`. Constant-time consideration: even with zero candidates, run at least one `Verify` against a dummy hash so timing is independent of token validity.
   - If no match → return `InvalidToken`.
   - Acquire per-user semaphore on the candidate user's `Id`.
   - Inside the semaphore, **re-read** the token row by Id with `AsNoTracking().FirstOrDefault(...)` to confirm `ConsumedAt is null` — if a concurrent confirm just won, return `InvalidToken`.
   - Load the user via `_userManager.FindByIdAsync`. If null → return `InvalidToken`.
   - If `user.TwoFactorEnabled && totpCode is null` → return `RequiresTotp`. Token NOT consumed.
   - **No MFA path:**
     - `await _userManager.RemovePasswordAsync(user)` → `await _userManager.AddPasswordAsync(user, newPassword)`. If second call fails (policy violation, breached password) → return `PasswordPolicyViolation(errors)`.
     - Set `EmailConfirmed = true` if not already (the reset email is itself proof of address ownership).
     - Mark token row `ConsumedAt = @now`. SaveChanges.
     - Bulk-revoke sessions: `UPDATE user_sessions SET RevokedAt = @now WHERE UserId = @id AND RevokedAt IS NULL`.
     - `await _userManager.UpdateSecurityStampAsync(user)` — invalidates any in-flight Identity cookies via `SecurityStampValidator` within the 5-min window.
     - Reset access-failed counter: `await _userManager.ResetAccessFailedCountAsync(user)`.
     - Clear lockout: `await _userManager.SetLockoutEndDateAsync(user, null)`.
     - `await _signInManager.SignOutAsync()` to clear any `Identity.TwoFactorUserId` cookie that the user might have on the browser doing the reset.
     - Queue notification email: `EmailMessage(To = user.Email, Subject = "Your Project Ceres password was changed", ...)`.
     - Return `Success`.
4. Controller maps outcome to HTTP response (see § 4).

### 3.3 Confirm flow (MFA-enabled user) — same endpoint with `{token, newPassword, totpCode}`

Same as § 3.2 with one extra step inserted **after** the user is loaded and **before** the password write:

- Validate code shape against `MfaConstants.TotpCodeShape` (six digits). Backup-code shape is rejected here per ADR-0069.
- `var ok = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, totpCode)`. If false → return `InvalidTotp`. Token NOT consumed; user can retry with a fresh code.
- `var accepted = await _replayGuard.TryAcceptAsync(user.Id, totpCode, ct)`. If false (replay) → return `InvalidTotp`. Same `TotpReplayGuard` instance the login flow uses; spans both flows.
- Set `MfaVerifiedAt = @now` on the token row.

Then proceed with the password write per § 3.2.

### 3.4 Rate-limit policies

Add to `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs`:

```csharp
/// <summary>5/hour per email sliding window. Applied to /password-reset/request.
/// Keyed by lowercased trimmed email so a single account can't be spammed with reset emails.</summary>
public const string AuthPasswordResetByEmail = "auth-password-reset-by-email";

/// <summary>Fallback partition key for /password-reset/request with malformed email.
/// Routes them into a single shared bucket so an attacker can't dodge the limit by sending junk.</summary>
public const string AnonymousPasswordResetPartition = "anonymous-password-reset";
```

Register in `Program.cs` alongside the existing policies:

```csharp
options.AddPolicy(AuthRateLimitPolicies.AuthPasswordResetByEmail, httpContext =>
{
    // Lazy-read email from JSON body. If parsing fails, route to anonymous bucket.
    var email = ReadEmailFromBody(httpContext) ?? AuthRateLimitPolicies.AnonymousPasswordResetPartition;
    return RateLimitPartition.GetSlidingWindowLimiter(email, _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 5,
        Window = TimeSpan.FromHours(1),
        SegmentsPerWindow = 6,   // 10-min segments
        QueueLimit = 0,
    });
});
```

`ReadEmailFromBody` uses `httpContext.Request.EnableBuffering()` + `JsonDocument.ParseAsync` (already a pattern in projects using rate-limit-by-body-field; if not, the spec falls back to `AuthLoginByIp` only and adds a service-side per-user rate gate). **Plan-time decision:** verify whether a body-reading partitioner is feasible vs. moving the per-email gate into `PasswordResetService.RequestAsync` directly via a `MemoryCache`-backed sliding window. The latter is simpler and stays out of the rate-limit middleware. **Spec'd default:** service-side per-email gate via `MemoryCache`, since reading the request body inside `AddPolicy` requires care with body-buffering + duplicate reads.

If the service-side gate is chosen: service holds a `MemoryCache` of `(email, count, windowStart)`; on each `RequestAsync` it increments the count for that email. If `count > 5 && now - windowStart < 1h` → throw `PasswordResetRateLimitException`, controller catches and returns 429 with the `RATE_LIMITED` envelope + `Retry-After` header.

### 3.5 Concurrency

`PasswordResetService` carries a static `ConcurrentDictionary<Guid, SemaphoreSlim>` keyed by `userId`, mirroring `AuthController._loginLocks`. Every mutation inside `RequestAsync` (when user is non-null) and every full `ConfirmAsync` body (after the user is identified) runs inside the user's semaphore.

This serialises:
- Two concurrent `/request` for the same email — first wins, second supersedes the first via the same `invalidate-prior` UPDATE.
- Two concurrent `/confirm` with the same token — first wins (sets `ConsumedAt` synchronously inside the transaction), second observes the consumed state and returns `InvalidToken`.
- `/request` racing `/confirm` — whoever acquires the semaphore first wins.

### 3.6 Error envelope mapping

Controller maps `PasswordResetConfirmOutcome` to HTTP responses using a private helper that mirrors `AuthController.UnauthorizedEnvelope`:

| Outcome | Status | Body |
|---|---|---|
| `Success` | `204 No Content` | (none) |
| `RequiresTotp` | `200 OK` | `{ "requiresTotp": true }` |
| `InvalidToken` | `401 Unauthorized` | `{ "error": { "code": "INVALID_RESET_TOKEN", "message": "The reset link is invalid or has expired." } }` |
| `InvalidTotp` | `401 Unauthorized` | `{ "error": { "code": "INVALID_MFA_CODE", "message": "The verification code is invalid or expired." } }` (existing code, reused) |
| `PasswordPolicyViolation(errors)` | `422 Unprocessable Entity` | `{ "error": { "code": "VALIDATION_ERROR", "message": "...", "details": [...] } }` |

Add `INVALID_RESET_TOKEN` to the canonical-error-codes table in `docs/api-contract.md` § Canonical error codes:

| Code | Status | Returned by | When |
|---|---|---|---|
| `INVALID_RESET_TOKEN` | 401 | `POST /api/auth/password-reset/confirm` | Reset token is unknown, malformed, expired, or already consumed. Stage 6c. |

---

## 4. Edge cases

This list is the source for the test list in § 5. Every entry below has at least one test pinning the behaviour.

### 4.1 Token-state edges

- **Expired token** (`ExpiresAt < now`): treated as `InvalidToken`. Identical 401 response to "token unknown" — does not reveal that "the token used to be valid."
- **Already-consumed token**: same — `InvalidToken`.
- **Superseded token** (request issued, then a second request issued before the first was used): the first token's `ConsumedAt` was set by the second request's invalidate-prior step. Falls through to "already consumed."
- **Token from a deleted user**: `FindByIdAsync` returns null → `InvalidToken`.
- **Token replay within the 15-min window**: blocked because `ConsumedAt` is set synchronously inside the same DB transaction as the password write.
- **Malformed token string** (not base64url, wrong length): `Verify` fails on every candidate → `InvalidToken`. Constant-time still applies (dummy verify if zero candidates).

### 4.2 Concurrency edges

- **Two `/confirm` calls with the same token simultaneously**: per-user semaphore + DB transaction → first wins, second returns `InvalidToken`.
- **`/request` racing `/confirm` for the same user**: per-user semaphore serialises. Whoever acquires first wins.
- **Concurrent `/request` for the same email**: per-user semaphore (or DB unique constraint); first wins, second supersedes.
- **`/request` for unknown email**: never enters per-user semaphore (no user → no key). Safe because nothing is written.

### 4.3 MFA edges

- **MFA disabled at request, enabled before confirm**: confirm enters Flow C (TOTP required). User without device must request a new reset and complete TOTP step.
- **MFA enabled at request, disabled before confirm**: confirm enters Flow B (no TOTP). Token works without TOTP. Deliberate consequence of MFA being opt-in.
- **MFA re-enrolled (new authenticator seed) after request**: TOTP code from old authenticator does not verify against new seed → `InvalidTotp`.
- **TOTP miss during reset**: does NOT increment `AccessFailedCount`. We use `VerifyTwoFactorTokenAsync` directly, same as 6b.2's `/login/totp` fix.
- **TOTP code already used in `/login/totp` within the 30-sec window**: `TotpReplayGuard.TryAcceptAsync` rejects → `InvalidTotp`.
- **Backup code submitted as `totpCode`**: rejected. Pin ADR-0069 in test.
- **No-MFA user submits a `totpCode` they shouldn't have**: ignored, not rejected. Defence-in-depth (no info leak about MFA state) and forgiving for stale SPA state.

### 4.4 Password-policy edges

- **New password matches current password**: not blocked. Identity has no built-in "different from previous" rule and we don't add one. (YAGNI; security model doesn't call it out.)
- **Policy violation** (too short, breached): token NOT consumed; user retries with a different password.
- **Empty/whitespace `newPassword`**: caught by `[Required]` → 422 from `InvalidModelStateResponseFactory`.
- **Pwned-password failure**: returned as `PasswordPolicyViolation` with the IdentityError.

### 4.5 Account-state edges

- **Account is locked when reset is requested**: request goes through. Reset is the canonical recovery path.
- **Account is locked when `/confirm` succeeds**: lockout is cleared (`AccessFailedCount = 0`, `LockoutEnd = null`) — same policy as backup-code-during-lockout in 6b.2.
- **Account does not have `EmailConfirmed = true`**: reset still allowed; on success, `EmailConfirmed` is flipped to true (the email itself is the proof of ownership).
- **Account targeted by `UserBlockedIpMiddleware`**: middleware returns 403 before the controller is reached. No special case.

### 4.6 Email-stub edges

- **`IEmailService.SendAsync` throws**: caught and logged. DB token row is already committed; user can request again.
- **Production with no `IEmailService` registered**: deliberate — DI resolution throws at startup, blocks deployment until Stage 8.

### 4.7 Session-revocation edges

- **User has `__Host-Session` cookie at reset time**: `UpdateSecurityStampAsync` regenerates the stamp; `SecurityStampValidator` invalidates the cookie within the 5-min validation interval.
- **User has persistent ("remember me") cookie**: the underlying `UserSession` row is bulk-revoked alongside ephemeral sessions.
- **User is mid-MFA-pending login (`Identity.TwoFactorUserId` cookie)**: `_signInManager.SignOutAsync()` clears it on the response.
- **Failed confirm**: no sessions are revoked. Pin in test.

### 4.8 Error-envelope edges

- All 401 returns use the shared envelope helper. Codes: `INVALID_RESET_TOKEN`, `INVALID_MFA_CODE`. (`INVALID_RESET_TOKEN` is new.)
- `PasswordPolicyViolation` returns 422 with `details[]` populated, matching the dual-shape contract in `api-contract.md` § 422 response body.
- Rate-limit rejections inherit the global `OnRejected` handler in `Program.cs` — no per-endpoint plumbing.

---

## 5. Tests

Tests live in `ProjectCeres.Tests/Integration/Authentication/` (flat layout, not subfolder, matching existing convention). File names use `PasswordReset` prefix.

### 5.1 Test discipline (codified in spec, enforced at review)

These rules are non-negotiable:

1. **No false-positive shortcuts.** If a test fails intermittently, treat it as a real bug. Do NOT add retries, do NOT relax assertions, do NOT mark `Skip`. Diagnose the race; fix the production code or the test setup. (Memory feedback `feedback_test_edge_cases_as_ship_gate.md`.)
2. **No `Verifiable()`-without-`Verify()`** mock patterns. Either read real DB state, or use `MockBehavior.Strict`. Loose mocks hide regressions.
3. **Hang prevention.** Every async test wraps its body with a `CancellationTokenSource(TimeSpan.FromSeconds(30))`. The token is passed through every call. If a test hangs, the token cancels and the test fails with `OperationCanceledException` — surfaces the deadlocked stack via xUnit's normal failure path. Project-wide `dotnet test` runs include `--blame-hang-timeout 120s` (memory `feedback_test_run_streaming_discipline.md`); a hang past that point dumps a sequence file naming the deadlocked thread.
4. **No new IClock abstraction.** The codebase uses `DateTime.UtcNow` directly (verified). Time-based tests work by inserting rows with adjusted timestamps (`ExpiresAt` already in the past for "expired" tests) or by `await Task.Delay(...)` only when sub-second. Introducing an `IClock` is scope creep beyond 6.11.
5. **Rate-limit tests live in their own xUnit collection** (`"RateLimitTests"` — same as 6b.2's existing collection). The in-memory partition state survives within a process; isolating to a collection prevents cross-test leak.
6. **Strict `IEmailService` mock** for assertions; `NoopEmailService` for tests where the email side-effect is incidental.
7. **Diagnostic procedure for hangs:** read the `TestResults/<id>/Sequence_*.xml` dump from `--blame-hang-timeout`. Identify the thread; locate the lock contention or deadlocked `await`. Fix the production code; do NOT increase the timeout to mask the hang.

### 5.2 Test list (46 total)

#### Happy path (6 tests)

1. `Request_with_known_email_issues_token_and_sends_email` — POST `/request`; assert DB row created with correct fields, `IEmailService` mock received exactly one `SendAsync`, response is 204.
2. `Confirm_no_mfa_succeeds` — full request → confirm cycle; assert password changed, `ConsumedAt` set, all `UserSession` rows revoked, password-changed email sent.
3. `Confirm_with_mfa_two_step_succeeds` — first confirm returns `200 {requiresTotp: true}` and token NOT consumed; second confirm with TOTP returns 204 and consumes.
4. `Confirm_clears_lockout` — lock user → reset → assert `AccessFailedCount == 0` and `LockoutEnd is null`.
5. `Confirm_no_mfa_promotes_EmailConfirmed_when_previously_false` — pin §4.5 behaviour.
6. `Confirm_succeeds_when_account_is_currently_locked` — lockout doesn't block reset.

#### Negative-assertion (11 tests)

7. `Request_with_unknown_email_returns_204_with_same_timing` — measure 5 known vs 5 unknown calls; assert mean diff < 50ms.
8. `Request_with_unknown_email_does_not_create_db_row`.
9. `Request_with_unknown_email_does_not_send_email` — strict mock asserts zero calls.
10. `Confirm_does_not_consume_token_on_password_policy_failure`.
11. `Confirm_does_not_consume_token_on_invalid_totp`.
12. `Confirm_does_not_increment_AccessFailedCount_on_invalid_totp` — pin 6b.2 contract.
13. `Confirm_does_not_accept_backup_code_in_place_of_totp` — pin ADR-0069.
14. `Request_does_not_log_user_email_or_password_to_default_logger` — capture log output, assert no PII.
15. `Confirm_does_not_log_new_password_anywhere` — scan all log levels; assert zero hits of literal `newPassword` value.
16. `Request_is_subject_to_user_blocked_ip_middleware` — confirm middleware fires before controller (no opt-out).
17. `Confirm_with_no_mfa_user_ignores_extraneous_totpCode` — pin §4.3 forgiving behaviour.

#### Single-use & supersession (6 tests)

18. `Token_replay_after_success_returns_401`.
19. `Concurrent_confirm_with_same_token_only_one_wins` — `Task.WhenAll` two `/confirm` calls; assert exactly one 204, exactly one 401, password changed exactly once.
20. `New_request_supersedes_prior_unused_token`.
21. `Expired_token_returns_401` — insert token row with `ExpiresAt = now - 1min`; assert 401.
22. `Token_resolution_uses_token_row_UserId_not_caller_supplied_id` — pin that the user being reset is resolved from the stored `PasswordResetToken.UserId` column, not from any caller-supplied parameter or session state. Test: insert a row for user A; call `/confirm` with that raw token; verify only user A's password changed and user B is untouched. Catches the regression where a future refactor would let an attacker influence which user gets reset.
23. `Concurrent_request_then_confirm_serialises_via_semaphore` — kicks both tasks at once, assert deterministic order via per-user lock.

#### Rate-limit (5 tests, in `RateLimitTests` collection)

24. `Request_per_ip_limit_returns_429_at_11th_attempt` — same IP, 11 calls; 11th returns 429 with `Retry-After`.
25. `Request_per_email_limit_returns_429_at_6th_attempt` — same email, 6 calls; 6th returns 429.
26. `Request_per_email_limit_does_not_leak_user_existence` — 5 unknown-email calls then 6th unknown-email call also 429s. Same bucket whether user exists.
27. `Request_per_email_bucket_resets_after_one_hour` — uses `MemoryCache` cache key + `Set/Remove` of internal counter for service-side gate (or window advancement on the limiter for middleware-side gate).
28. `Confirm_endpoint_per_ip_limit_returns_429_at_11th_attempt` — pin confirm-side rate limit too.

#### MFA-state-changed mid-flow (6 tests)

29. `Mfa_disabled_after_request_allows_no_totp_confirm` — pin §4.3.
30. `Mfa_enabled_after_request_requires_totp_confirm`.
31. `Mfa_re_enrolled_after_request_invalidates_old_authenticator_codes`.
32. `Confirm_with_totp_used_seconds_earlier_in_login_totp_is_replay_rejected` — `TotpReplayGuard` is shared.
33. `Confirm_first_call_returning_requiresTotp_does_count_against_per_ip_rate_limit` — pin the rate-limit decision in §3.2: probe DOES count toward the 10/min/IP limit. Simpler than carving out the requiresTotp branch.
34. `Confirm_with_totp_when_token_already_consumed_returns_INVALID_RESET_TOKEN_not_INVALID_MFA_CODE` — error-code precedence: token-validity check runs before TOTP check.

#### Session-revocation (5 tests)

35. `Successful_reset_revokes_all_user_sessions` — 3 active `UserSession` rows; assert all 3 revoked.
36. `Successful_reset_invalidates_in_flight_session_cookie_via_security_stamp`.
37. `Successful_reset_clears_TwoFactorPending_cookie`.
38. `Successful_reset_revokes_persistent_remember_me_cookie_session`.
39. `Failed_confirm_does_not_revoke_any_sessions`.

#### Architecture (5 tests, added to existing `ProjectCeres.ArchitectureTests`)

40. `PasswordResetController_methods_have_correct_attributes` — `[AllowAnonymous]` + `[EnableRateLimiting]` on both endpoints.
41. `PasswordResetController_does_not_call_PasswordHasher_directly` — only via `UserManager` and `Argon2idPasswordHasher`-via-DI.
42. `IEmailService_implementations_count` — exactly one Development implementation; Production has none registered (verified via `EnvironmentName == "Production"` test fixture or static check).
43. `PasswordResetService_methods_take_CancellationToken` — reflection-based check that every public async method's last parameter is `CancellationToken`. (Uses simple reflection if existing arch tests don't use a Roslyn walker.)
44. `PasswordResetController_actions_dont_log_request_body` — controller does not pass `request.NewPassword` or `request.Email` to any `ILogger` call. Implementation: a `MemoryTarget`-style `ILogger` capture in the integration-test harness; replay the full request, then assert captured log output contains neither the literal email nor the literal password. (Avoids static-analysis dependency; uses tools the test suite already has.)

#### Constant-time / dummy-hash discipline (2 tests)

45. `Confirm_with_unknown_token_runs_at_least_one_argon2_verify` — instrument the dummy-verify path; assert at least one Argon2 call regardless of zero candidates.
46. `Request_for_unknown_email_runs_argon2_dummy_hash` — confirm `RunDummyHash` is invoked even when user is null.

---

## 6. Sequencing

Implementation order:

1. **EF migration first** — `PasswordResetToken` entity + `AddPasswordResetTokens` migration. Run `dotnet ef database update` against local Postgres; verify schema by hand.
2. **`IEmailService` + `LogOnlyEmailService`** — interface, dev impl, DI registration.
3. **`PasswordResetService.RequestAsync`** — drives tests #1, #5, #7–#9, #14, #16, #46.
4. **`PasswordResetController` + `/request` endpoint** — wire DTO, rate-limit attribute (or service-side gate), controller method. Drives #1, #16, #24–#27.
5. **`PasswordResetService.ConfirmAsync` no-MFA path** — drives #2, #4, #5–#6, #10, #15, #17, #18, #20, #21, #28, #34, #35, #36, #38, #39, #45.
6. **`PasswordResetService.ConfirmAsync` MFA path** — drives #3, #11–#13, #19, #29–#33, #37.
7. **Concurrency hardening** — per-user semaphore + DB-transaction discipline. Drives #19, #22, #23.
8. **Architecture tests** — #40–#44.
9. **Roadmap-checklist update** — flip 6.11-related boxes in `roadmap-phase-three.md` Stage 6 verification list.
10. **Doc sync** — invoke `sync-docs` skill; updates `security-model.md`, `api-contract.md` (new error code), `planning-resolved.md` (resolution of password-reset deferred items).

---

## 7. Files

### Created

- `ProjectCeres/Models/PasswordResetToken.cs`
- `ProjectCeres/Common/Authentication/PasswordResetService.cs`
- `ProjectCeres/Common/Authentication/PasswordResetTokenGenerator.cs`
- `ProjectCeres/Common/Email/IEmailService.cs`
- `ProjectCeres/Common/Email/EmailMessage.cs`
- `ProjectCeres/Common/Email/LogOnlyEmailService.cs`
- `ProjectCeres/Common/Email/NoopEmailService.cs` (test fixture only)
- `ProjectCeres/Controllers/Api/PasswordResetController.cs`
- `ProjectCeres/ViewModels/Auth/PasswordResetRequest.cs`
- `ProjectCeres/ViewModels/Auth/PasswordResetConfirmRequest.cs`
- `ProjectCeres/Migrations/<timestamp>_AddPasswordResetTokens.cs`
- Tests under `ProjectCeres.Tests/Integration/Authentication/`:
  - `PasswordResetRequestTests.cs`
  - `PasswordResetConfirmNoMfaTests.cs`
  - `PasswordResetConfirmMfaTests.cs`
  - `PasswordResetConcurrencyTests.cs`
  - `PasswordResetSupersessionTests.cs`
  - `PasswordResetRateLimitTests.cs` (in `RateLimitTests` collection)
  - `PasswordResetSessionRevocationTests.cs`
  - `PasswordResetConstantTimeTests.cs`
  - Architecture-test additions land in existing `AuthArchitectureTests.cs`.

### Modified

- `ProjectCeres/Data/AppDbContext.cs` — add `DbSet<PasswordResetToken>` + entity config in a new `ConfigurePasswordResetEntities` private method.
- `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs` — add `AuthPasswordResetByEmail` + `AnonymousPasswordResetPartition` constants.
- `ProjectCeres/Program.cs` — register `PasswordResetService`, `PasswordResetTokenGenerator`, `IEmailService → LogOnlyEmailService` (Development only), new rate-limit policy.
- `ProjectCeres/Models/FailedLoginAttempt.cs` — add `PasswordResetUnknownEmail` to `FailedLoginReason` enum.
- `docs/api-contract.md` — add `INVALID_RESET_TOKEN` to canonical error codes.
- `docs/security-model.md` — flip the password-reset section status to shipped (date stamped at merge time, not pre-filled here), see this spec.
- `docs/roadmap-phase-three.md` — Stage 6c progress note + checkbox flips.
- `docs/planning-resolved.md` — record resolution of 6.11 deferred items.

---

## 8. What this sub-stage does NOT cover

Explicit deferral list, repeated for emphasis:

- Lockout self-service unlock — separate spec under 6c. Token mechanism is structurally similar; we'll generalise if it makes sense at that point, or copy-paste.
- Email-address-change flow — sub-stage 6.12.
- Reauthentication middleware for sensitive operations — sub-stage 6.13. Until 6.13 ships, the reset flow's MFA gate substitutes for reauth on this single sensitive operation.
- `AuditLog` entity — sub-stage 6.14. Password-reset events are recorded via `FailedLoginRecorder` for now (`PasswordResetUnknownEmail` reason); 6.14 will back-populate.
- Real `IEmailService` provider — Stage 8. `LogOnlyEmailService` is the bridge.
- EN/ES `.resx` template files — Stage 8. EN strings hand-written inline until then.

## 9. Production-deployment guard

Production does NOT have an `IEmailService` implementation registered. Resolving the service in production throws — this is intentional. It blocks Phase 3 production deployment until Stage 8 wires the real provider. A startup-validation hook in `Program.cs` could make this explicit (`if (env.IsProduction() && !services.Any(s => s.ServiceType == typeof(IEmailService))) throw ...`), but the DI container's natural failure mode achieves the same effect.

## 10. Open items deliberately deferred to plan stage

- Whether the per-email rate gate lives as a `RateLimitPartition` policy that reads the request body, or as a `MemoryCache`-backed gate inside `PasswordResetService.RequestAsync`. Spec'd default: service-side gate (simpler, less middleware fragility). The plan doc revisits this.
- Exact wording of EN strings in the email bodies. Plan-time decision; not a spec issue.
- Whether `SignInManager.SignOutAsync()` in the success path is sufficient to clear all auth cookies, or whether the controller must also explicitly delete the persistent cookie + `Identity.TwoFactorUserId` cookie. Plan-time investigation against the existing logout code in `AuthController.Logout`.
