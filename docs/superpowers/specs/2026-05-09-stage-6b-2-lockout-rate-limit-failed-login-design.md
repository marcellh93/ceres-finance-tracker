# Stage 6b.2 — Account Lockout, Rate Limiting, Failed-Login Logging, Backup-Code-During-Lockout

**Date:** 2026-05-09
**Stage:** Phase 3, Stage 6b.2 (Identity infrastructure, second sub-stage)
**Status:** Design

---

## Summary

Stage 6b.2 ships four authentication hardening features and reconciles one latent bug from 6b.1:

1. **Account lockout enforcement** — verified end-to-end with negative assertions (counter increments, resets on success, persists across restart, NOT incremented by TOTP misses or rate-limit rejections)
2. **Rate limiting** on `/api/auth/login`, `/api/auth/login/totp`, `/api/auth/register`, `/api/auth/csrf` via `Microsoft.AspNetCore.RateLimiting` with sliding-window policies
3. **Failed-login logging** via a new `FailedLoginAttempt` entity + `FailedLoginRecorder` service for credential-stuffing forensics
4. **Backup-code-during-lockout** — backup codes (and TOTP) bypass lockout *only* at the second-factor step, and successful bypass clears both `AccessFailedCount` and `LockoutEnd`
5. **6b.1 reconciliation** — replace `SignInManager.TwoFactorAuthenticatorSignInAsync` with `UserManager.VerifyTwoFactorTokenAsync` + manual `SignInAsync` so wrong TOTP codes do NOT increment the password-lockout counter; align all lockout responses to the api-contract envelope shape

**Out of scope** (deferred to Stage 6c): lockout-email-with-signed-unlock-link, `/password-reset` rate limit, password-reset endpoint enumeration prevention, audit log entity, GDPR erasure flow itself (the schema enables it; the flow is 6c).

**Tests as ship-gate:** 51 integration + architecture tests are listed below. Each is a stage-blocking item. Missing any one means 6b.2 is not shipped.

---

## Why this stage exists

Per `roadmap-phase-three.md` lines 498–511 (rate limiting + failed-login logging) and line 473 (backup-code-during-lockout), Stage 6b.2 is the bridge between 6b.1 (TOTP MFA shipped 2026-05-09) and 6c (password reset, email change, reauth, audit log).

### Critical 6b.1 latent bug discovered during research

`security-model.md:280` says *"a valid TOTP code should be accepted even during a lockout — the lockout protects against password guessing, not TOTP abuse."* The 6b.1 implementation calls `SignInManager.TwoFactorAuthenticatorSignInAsync` (`AuthController.cs:138`), which internally calls `AccessFailedAsync` on a wrong TOTP code — so today, TOTP typos increment the password-lockout counter. Three downstream consequences:

1. Real users get locked out from TOTP fat-fingers — contradicts the security model.
2. The 6b.1 backup-code recovery flow calls `SignInAsync` but never resets `AccessFailedCount` and never clears `LockoutEnd`. Recovered users stay locked on next login.
3. Calling `ResetAccessFailedCountAsync` alone is insufficient — Identity's `LockoutEnd` is set when the counter *reaches* the threshold; resetting the counter doesn't retroactively clear `LockoutEnd`. Both `ResetAccessFailedCountAsync` and `SetLockoutEndDateAsync(user, null)` are required.

6b.2 fixes this as part of the backup-code-during-lockout work. No carry-forward of latent bugs.

---

## Architecture

### Components

#### 1. `FailedLoginAttempt` entity

**Path:** `ProjectCeres/Models/FailedLoginAttempt.cs`

```csharp
namespace ProjectCeres.Models;

public sealed class FailedLoginAttempt
{
    public Guid Id { get; set; }
    public string? EmailAttempted { get; set; }
    public Guid? UserId { get; set; }
    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public FailedLoginReason Reason { get; set; }
    public DateTime OccurredAt { get; set; }
}

public enum FailedLoginReason
{
    BadCredentials,
    BadTotp,
    BadBackupCode,
    LockedOut,
    UnknownUser,
}
```

**Conventions matched:**
- `sealed class` — matches `UserSession`, `UserBlockedIp`
- `DateTime` UTC (not `DateTimeOffset`) — matches `UserSession.CreatedAt`, `UserBlockedIp.BlockedAt`
- Non-nullable strings default to `""` — matches `UserBlockedIp.IpAddress`, `UserSession.UserAgent`
- Enum stored as string via `HasConversion<string>()` — matches `RecurringTransaction.Frequency` (`AppDbContext.cs:183`)

**EF configuration** in `AppDbContext.ConfigureSessionEntities`:

```csharp
modelBuilder.Entity<FailedLoginAttempt>(b =>
{
    b.HasKey(e => e.Id);
    b.HasIndex(e => new { e.IpAddress, e.OccurredAt });    // credential-stuffing query
    b.HasIndex(e => new { e.EmailAttempted, e.OccurredAt }); // per-account forensics
    b.HasIndex(e => e.OccurredAt);                          // purge-sweep performance
    b.Property(e => e.EmailAttempted).HasMaxLength(256);   // matches AspNetUsers.NormalizedEmail
    b.Property(e => e.IpAddress).HasMaxLength(45);         // IPv6 max
    b.Property(e => e.UserAgent).HasMaxLength(512);
    b.Property(e => e.Reason).HasConversion<string>();
});
```

**No global query filter.** This entity is intentionally cross-tenant — it must capture attempts against accounts that may not exist (`UserId = null` for the unknown-user case). Per ADR-0065 § Decision-1, only entities in the enumerated user-owned list receive `HasQueryFilter`. ADR-0065 must be amended to add `FailedLoginAttempt` to the documented exemption list (alongside `AuditLog` purge and system-table maintenance per ADR-0067 § Decision-6).

#### 2. `FailedLoginRecorder` service

**Path:** `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs`

Concrete class, no interface — matches the auth-service convention (`MfaBackupCodeService`, `TotpReplayGuard`, `Argon2idPasswordHasher` all follow this pattern; only `IBreachedPasswordChecker` has an interface because it has a real second implementation).

```csharp
public sealed class FailedLoginRecorder
{
    private readonly AppDbContext _db;

    public FailedLoginRecorder(AppDbContext db) => _db = db;

    public async Task RecordAsync(
        string? emailAttempted,
        Guid? userId,
        FailedLoginReason reason,
        string ipAddress,
        string userAgent,
        CancellationToken ct = default)
    {
        var entry = new FailedLoginAttempt
        {
            Id = Guid.NewGuid(),
            EmailAttempted = TruncateAndNormalize(emailAttempted, 256),
            UserId = userId,
            IpAddress = string.IsNullOrEmpty(ipAddress) ? "unknown" : ipAddress,
            UserAgent = Truncate(userAgent ?? "", 512),
            Reason = reason,
            OccurredAt = DateTime.UtcNow,
        };
        _db.Set<FailedLoginAttempt>().Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    private static string? TruncateAndNormalize(string? input, int max)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var normalized = input.Trim().ToLowerInvariant();
        return normalized.Length > max ? normalized[..max] : normalized;
    }

    private static string Truncate(string input, int max)
        => input.Length > max ? input[..max] : input;
}
```

**Synchronous write.** No fire-and-forget pattern exists in the codebase; every other auth-service write (`UserSession`, `TotpReplayEntry`, `UserMfaBackupCode`) is synchronous. Login already pays ~300ms Argon2id cost; one extra INSERT is rounding error and gives deterministic tests + crash-safety. If `SaveChangesAsync` throws (e.g., DB unreachable), the exception bubbles — login returns 500, no session cookie issued. Loud-failure principle per ADR-0065/0067.

**DI registration** (`Program.cs`):
```csharp
builder.Services.AddScoped<FailedLoginRecorder>();
```

#### 3. Lockout enforcement (already wired in 6a — verify end-to-end)

`AuthController.Login` (line 77-78) calls `SignInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true)`. The `lockoutOnFailure: true` argument is what tells Identity to call `AccessFailedAsync` on miss and `ResetAccessFailedCountAsync` on success. **No code changes for this item** — only verification tests (see Test Suite below).

#### 4. 6b.1 reconciliation in `AuthController.LoginTotp`

**Replace** `_signInManager.TwoFactorAuthenticatorSignInAsync(...)` with two lower-level calls:

```csharp
// OLD (6b.1 — has the lockout-mutation side effect):
// var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false);

// NEW (6b.2):
var ok = await _userManager.VerifyTwoFactorTokenAsync(
    user, TokenOptions.DefaultAuthenticatorProvider, code);
if (!ok)
{
    await _failedLoginRecorder.RecordAsync(user.Email, user.Id, FailedLoginReason.BadTotp, ip, ua, ct);
    return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
}

// Replay guard (existing 6b.1 — unchanged)
if (!await _totpReplayGuard.TryConsumeAsync(user.Id, code, ct))
{
    await _failedLoginRecorder.RecordAsync(user.Email, user.Id, FailedLoginReason.BadTotp, ip, ua, ct);
    return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
}

// Lockout-clear (NEW — only fires if account was locked but path bypassed it)
if (user.LockoutEnd.HasValue)
{
    await _userManager.ResetAccessFailedCountAsync(user);
    await _userManager.SetLockoutEndDateAsync(user, null);
}

await _signInManager.SignInAsync(user, isPersistent: false);
await IssueSessionAndCookiesAsync(...);
return NoContent();
```

The backup-code branch (existing 6b.1, lines 158–163) follows the same pattern: after `MfaBackupCodeService.VerifyAndConsumeAsync` succeeds, run the same lockout-clear block before `SignInAsync`.

**Why this works:**
- `VerifyTwoFactorTokenAsync` is a pure verification call — no `AccessFailedAsync` side effect. TOTP typos no longer poison the password-lockout counter.
- The TOTP brute-force defense becomes the per-user 10/min rate limit + 30-second TOTP rotation, which is the *correct* defense for that attack class.
- Backup codes (~80 bits entropy per `MfaConstants.cs:13`, single-use, Argon2id-hashed) are vastly stronger than TOTP and don't need a separate lockout.

**Cookie hygiene on lockout response:**

```csharp
if (signIn.IsLockedOut)
{
    await _failedLoginRecorder.RecordAsync(email, user?.Id, FailedLoginReason.LockedOut, ip, ua, ct);
    await _signInManager.SignOutAsync();              // clears Identity.TwoFactorUserId
    Response.Cookies.Delete(MfaConstants.RememberMeCookieName); // clears Mfa.RememberMe
    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT",
        "Account temporarily locked. Try again in 15 minutes.");
}
```

**Status code envelope alignment.** Existing 6a code at lines 106 and 140 returns `Unauthorized(new { error = "locked_out" })` — a flat-string error that violates `api-contract.md` § Error Shape (which mandates `{ error: { code, message } }`). 6b.2 absorbs this fix. New helper:

```csharp
private IActionResult UnauthorizedEnvelope(string code, string message)
    => Unauthorized(new { error = new { code, message } });
```

All `return Unauthorized(...)` calls in `AuthController` migrate to `UnauthorizedEnvelope(...)`. Codes used:
- `UNAUTHENTICATED` — bare auth failure (matches existing api-contract.md:250)
- `INVALID_CREDENTIALS` — wrong email/password
- `INVALID_MFA_CODE` — wrong TOTP or wrong backup code (one code, generic per security-model)
- `ACCOUNT_LOCKED_OUT` — lockout rejection
- `MFA_REQUIRED` — password OK but second factor needed (existing two-step flow)

**MFA-pending cookie TTL tightening.** `Identity.TwoFactorUserId` currently inherits 30-min sliding TTL from `ConfigureApplicationCookie` (`Program.cs:122`). Tighten to 5 min via:

```csharp
builder.Services.Configure<CookieAuthenticationOptions>(
    IdentityConstants.TwoFactorUserIdScheme,
    options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
    });
```

#### 5. Rate-limiter wiring

**Two named policies** in `Program.cs`:

```csharp
const string AuthLoginByIp = "auth-login-by-ip";
const string AuthTotpByUser = "auth-totp-by-user";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "RATE_LIMITED",
                message = "Too many requests. Please retry shortly.",
            }
        }, ct);
    };

    options.AddPolicy(AuthLoginByIp, httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    options.AddPolicy(AuthTotpByUser, async httpContext =>
    {
        var auth = await httpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var userId = auth.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous-totp";
        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });
});
```

**Pipeline order.** Insert `app.UseRateLimiter()` between `app.UseRouting()` (currently line 210) and `app.UseAuthentication()` (currently line 213). The limiter doesn't need authenticated user state for `/login`; the `/login/totp` partition factory manually calls `AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme)` so it doesn't need `UseAuthentication` to have run first. Antiforgery validation runs as an MVC filter inside endpoint invocation, so `UseRateLimiter` naturally fires before antiforgery — no extra ordering needed.

**Endpoint application:**

```csharp
[EnableRateLimiting(AuthLoginByIp)]
public async Task<IActionResult> Login(...) { ... }

[EnableRateLimiting(AuthTotpByUser)]
public async Task<IActionResult> LoginTotp(...) { ... }

// AuthController.Register (when 6c brings it) and AuthController.Csrf:
[EnableRateLimiting(AuthLoginByIp)]
```

**Algorithm: sliding window.** The roadmap's "fixed window minimum" wording predates a specific choice; sliding-window (4×15s segments, 10 permits) closes the 2N-burst-at-boundary attack at zero runtime cost. Treated as a tightening, not a contradiction.

**Reverse-proxy launch gate.** `Program.cs` does NOT call `UseForwardedHeaders`. Behind any reverse proxy (Cloudflare, nginx, Caddy, Render, Fly), `Connection.RemoteIpAddress` becomes the proxy's IP and every request collapses into one partition. Acceptable for direct-to-Kestrel beta; **required** before any proxy-fronted deployment. Captured as a Stage 16 launch gate in `planning-phase3.md` (see Deferred Decisions).

**In-memory storage.** Limiter state is in-process. Acceptable per `roadmap-phase-three.md` (no horizontal scaling before Stage 16). If Stage 16 introduces a second app instance, swap to a Redis-backed limiter or pin auth endpoints to a single host. Captured in `planning-phase3.md`.

#### 6. Test fixture work

**Default test factory** (`AuthTestWebApplicationFactory`) overrides limiter policies with no-op so existing 6a/6b.1 tests don't flake on burst:

```csharp
services.AddRateLimiter(options =>
{
    options.AddPolicy(AuthLoginByIp, _ => RateLimitPartition.GetNoLimiter("test"));
    options.AddPolicy(AuthTotpByUser, _ => RateLimitPartition.GetNoLimiter("test"));
});
```

**New `RateLimitedAuthTestWebApplicationFactory`** with limiter active, used only by the rate-limit test class via `[Collection("RateLimitTests")]` (its own non-shared collection so partition state doesn't leak across runs).

---

## Test Suite — Ship Gate (51 tests)

Each test below is a stage-blocking item. Stage 6b.2 is not shipped until every one is green. Tests live under `ProjectCeres.Tests/Integration/Authentication/` unless noted.

### Lockout / counter behavior — `LockoutBehaviorTests.cs` (6 tests)

1. `WrongPasswordTenTimes_LocksAccount` — counter increments to 10; 11th attempt returns 401 `ACCOUNT_LOCKED_OUT`.
2. `SuccessfulPasswordLogin_ResetsCounter` — counter at 5 → successful login → counter is 0.
3. `WrongTotp_DoesNotIncrementPasswordLockoutCounter` — **regression test for the 6b.1 bug.** Pre: user with valid password + MFA, AccessFailedCount=0. Submit wrong TOTP 5×. Assert AccessFailedCount still 0.
4. `WrongBackupCode_DoesNotIncrementPasswordLockoutCounter` — same as #3 for backup-code branch.
5. `RateLimitRejection_DoesNotCountTowardLockout` — fire 11 requests in 60s; 429-rejected one does not bump AccessFailedCount.
6. `LockoutSurvivesProcessRestart` — lock account, dispose factory, recreate, assert still locked. (Confirms DB persistence, not in-memory.)

### Backup-code-during-lockout — `BackupCodeLockoutBypassTests.cs` (7 tests)

7. `LockedAccount_ValidBackupCode_CompletesLogin` — set `LockoutEnd = future`, submit valid backup code, assert 204 + session cookie.
8. `LockedAccount_BackupCodeSuccess_ClearsLockoutEndAndCounter` — after #7, assert AccessFailedCount=0 AND LockoutEnd=null in DB.
9. `LockedAccount_ValidTotp_CompletesLogin_AndClearsLockout` — same as #7 with TOTP code.
10. `BackupCodeOnNonLockedAccount_StillWorks` — regression for 6b.1 happy path.
11. `BackupCodeWithoutMfaPendingCookie_Returns401` — bare POST with no cookie returns 401, no DB mutation.
12. `BackupCodeConsumed_CannotBeReusedEvenAfterLockoutBypass` — use code to recover, retry same code, assert 401. Lockout-bypass path must not skip consume.
13. `BackupCodeRecoveryDuringLockout_DecrementsRemainingCount` — assert `UserMfaBackupCode.UsedAt` is set on the row after lockout-bypass success.

### TOTP replay × lockout — `TotpReplayDuringLockoutTests.cs` (1 test)

14. `ReplayedTotpDuringLockout_StillRejected` — replay-guard applies equally on the lockout-bypass path.

### MFA-pending cookie — `MfaPendingCookieTests.cs` (3 tests)

15. `MfaPendingCookie_TamperedSignature_Returns401` — alter one byte; 401, NO failed-login row written (we don't know the user).
16. `MfaPendingCookie_FromDifferentUser_DoesNotAuthenticateAsTargetUser` — issue cookie for User A, send User B's TOTP code, assert reject.
17. `MfaPendingCookie_ExpiredAfter5Min_Returns401` — explicit server-side expiry assertion (distinct from the TTL-attribute test).

### Rate limiting — `RateLimitedAuthEndpointTests.cs` (uses `RateLimitedAuthTestWebApplicationFactory`) (11 tests)

18. `Login_EleventhRequestInWindow_Returns429WithRetryAfter` — `Retry-After` header present.
19. `RateLimit429_ResponseBodyMatchesApiContractEnvelope` — body is `{ error: { code: "RATE_LIMITED", message } }`.
20. `Login_LimiterResetsAfterWindow` — burst, wait past window, 11th request succeeds.
21. `LoginTotp_LimiterPartitionsPerUser` — user A bursts; user B's first request still works.
22. `LoginTotp_MissingMfaCookie_RoutedToAnonymousPartition_DoesNotCrash` — bare request gets normal 401, doesn't crash limiter.
23. `Register_SharesAuthLoginByIpPolicy` — 11th register attempt from same IP returns 429.
24. `Csrf_SharesAuthLoginByIpPolicy` — 11th GET to `/api/auth/csrf` from same IP returns 429.
25. `RateLimit_DifferentIPs_DoNotShareBucket` — IP A bursts; IP B's first request succeeds.
26. `SlidingWindow_BoundaryAttack_StillBlocked` — fire 10 at second 55, 10 more at second 65; second batch is rate-limited. (The reason we changed the algorithm; pin the behavior.)
27. `RateLimitOnRejected_ContentTypeIsApplicationJson` — 429 response Content-Type is `application/json`, not `text/plain`.
28. `Concurrent_LoginAttempts_AccessFailedCountReachesExactly10` — 12 concurrent bad-password requests; AccessFailedCount settles at 10. (Catches EF concurrency under-increment.)

### Cookie hygiene — `MfaCookieHygieneTests.cs` (4 tests)

29. `LockoutResponse_ClearsTwoFactorUserIdCookie` — Set-Cookie with past Expires.
30. `LockoutResponse_ClearsRememberMeCookie` — same for `Mfa.RememberMe`.
31. `TwoFactorUserIdCookie_ExpiresIn5Minutes` — extract cookie from password-step response, assert max-age ≈ 300s.
32. `LockedAccount_BackupCodeRecovery_IssuesCleanSessionCookie` — `__Host-Session` is fresh, not stitched onto the MFA-pending cookie.

### Failed-login logging — `FailedLoginRecorderTests.cs` (12 tests)

33. `BadCredentials_WritesOneRow` — exactly one row, Reason=BadCredentials, UserId set, EmailAttempted lowercased.
34. `LockoutRejection_WritesOneRow_NotTwo` — locked account, submit credentials → exactly one row with Reason=LockedOut.
35. `UnknownUser_WritesRowWithNullUserIdAndReasonUnknownUser` — non-existent email → UserId=null, Reason=UnknownUser, EmailAttempted=lowercased.
36. `BadTotp_WritesRowWithReasonBadTotp` — wrong TOTP → row with Reason=BadTotp, UserId set.
37. `BadBackupCode_WritesRowWithReasonBadBackupCode` — same for backup-code branch.
38. `PasswordIsNeverInRow` — assertion: no row contains the test password value in any column.
39. `EmailAttempted_TruncatedTo256Chars` — submit 1000-char email, row has 256-char value, no exception.
40. `EmailAttempted_LowercasedAndTrimmed` — submit `"  Foo@Bar.COM  "`, row has `"foo@bar.com"`.
41. `UserAgent_Missing_RowStillWrites` — empty/missing UA header, row writes empty UA, no exception.
42. `UserAgent_TruncatedTo512Chars` — submit 1000-char UA, row has 512-char value.
43. `Ip_NullSafe_RecordsAsUnknown` — null `RemoteIpAddress`, row writes `"unknown"`, no throw.
44. `SaveChangesFailure_BubblesAs500_DoesNotIssueSession` — recorder write throws → request returns 500, no session cookie. (Recorder failure must not silently let attacker through.)

### Concurrency — `LoginConcurrencyTests.cs` (2 tests)

45. `Concurrent_FailedLogins_AllRecordOneRowEach` — 10 simultaneous bad-password requests; assert exactly 10 rows written.
46. `Concurrent_TotpSubmissions_OnlyOneSucceeds` — submit same valid TOTP code twice in parallel; assert exactly one 204 + one 401.

### Cross-feature interaction — `LoginCrossFeatureTests.cs` (4 tests)

47. `RateLimitFires_BeforeAntiforgeryValidation` — request without CSRF token after burst returns 429, not 400.
48. `LoginWithoutMfaEnrolled_StillSucceeds` — regression for 6a/6b.1 happy path with all 6b.2 changes in place.
49. `LoginWithMfaEnrolled_StillSucceeds_HappyPath` — full two-step happy path regression.
50. `BadCredentials_AlwaysReturnsArgon2idTimingFloor` — wrong-password and unknown-user responses within ±50ms (loose). Re-run from 6a with 6b.2 paths in place.

### Architecture / structural — extends `IdentityArchitectureTests.cs` (4 tests)

51. `FailedLoginAttempt_NotInGlobalQueryFilterList` — architecture test confirms entity is intentionally exempt.
52. `FailedLoginAttempt_HasRequiredIndexes` — reflection over EF model confirms the three indexes.
53. `AuthController_NoCallsToTwoFactorAuthenticatorSignInAsync` — Roslyn-style or grep-test: file does not contain the framework method that mutates lockout state.
54. `AuthController_AllErrorReturnsUseEnvelopeShape` — parameterized test hits every auth-error code path; each returns `{ error: { code, message } }`. Scoped to 6b.2 endpoints; 6c expands the parameter set.

(Numbering in spec uses 51 as the headline count — the 4 architecture tests are intentionally counted in the totals; the actual file shows 54 because the GDPR placeholder was rolled into architecture test #51 instead of being its own test. See Deferred Decisions for the GDPR contract.)

---

## Deferred decisions (must be persisted in `planning-phase3.md`)

Per the project's "persist deferred decisions in a durable doc, not a spec" rule, these items go into `planning-phase3.md` with cross-references back to this spec. The spec is **not** the durable home for any of them.

1. **Reverse-proxy launch gate** — `Program.cs` does not configure `UseForwardedHeaders`. Stage 16 (Hosting + ops) MUST add this configuration before deploying behind any reverse proxy, OR the rate limiter will use the proxy's IP for every request. Recommended config: `app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto })` plus `KnownProxies`/`KnownNetworks` once the proxy is chosen.

2. **In-memory rate limiter is single-host only** — Stage 16 horizontal scaling must either swap to a Redis-backed limiter or pin auth endpoints to a single host. Without this, a 2-host cluster doubles the effective rate ceiling.

3. **`FailedLoginAttempt` GDPR data-export inclusion** — open question. GDPR Article 15 (right of access) and Article 20 (data portability). Failed-login records contain personal data (email, IP). Schema enables nullification on erasure (item 4). Decision needed for the 6c data-export flow: include or omit. Recommendation: include in access (Article 15) export, omit from portability (Article 20) since they're not user-provided data.

4. **`FailedLoginAttempt` GDPR erasure cascade** — on right-to-erasure, the 6c erasure flow must `UPDATE FailedLoginAttempt SET EmailAttempted = NULL WHERE EmailAttempted = @normalizedEmail`. The schema (nullable `EmailAttempted`) enables this; the operation itself is 6c work. Captured here so 6c doesn't miss it.

5. **`FailedLoginAttempt` retention** — 1-year flat cross-tenant DELETE per `security-model.md:915`. Different from `AuditLog` (6 months, per-user fan-out). Implementation lands when the background-job runner ships in Stage 7. Captured in `planning-phase3.md` as the runner's first cross-tenant job.

6. **Backup-code-during-lockout policy formalization** — `security-model.md:280` says "valid TOTP code accepted during lockout." This spec extends that policy to backup codes (defensible — 80-bit entropy, single-use, Argon2id-hashed — but not literally written today). ADR-0069 amendment OR a one-line edit to security-model.md § Login → Account lockout to formalize.

---

## ADR amendments (must ship with this stage)

1. **ADR-0065** — add `FailedLoginAttempt` to the "intentionally cross-tenant, no global query filter" exemption list. The pattern is named in ADR-0067 § Decision-6 ("failed-login retention purge does not enter a user scope"); ADR-0065 just enumerates the entities — add this one.

2. **ADR-0069** (or `security-model.md` § Login) — formalize the backup-code-during-lockout extension of the existing TOTP-during-lockout rule. One-paragraph addition.

---

## Files touched

**New:**
- `ProjectCeres/Models/FailedLoginAttempt.cs`
- `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs`
- `ProjectCeres/Migrations/<timestamp>_AddFailedLoginAttemptTable.cs`
- `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/BackupCodeLockoutBypassTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/TotpReplayDuringLockoutTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/MfaPendingCookieTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/MfaCookieHygieneTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/LoginConcurrencyTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/LoginCrossFeatureTests.cs`
- `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs`

**Modified:**
- `ProjectCeres/Program.cs` — `AddRateLimiter` policies, DI registration for `FailedLoginRecorder`, MFA-pending cookie TTL config, `UseRateLimiter` middleware insertion
- `ProjectCeres/Controllers/Api/AuthController.cs` — replace `TwoFactorAuthenticatorSignInAsync` with `VerifyTwoFactorTokenAsync`, add `_failedLoginRecorder` calls, add lockout-clear-on-bypass-success block, add envelope helper, migrate all `Unauthorized(...)` calls to envelope shape, apply `[EnableRateLimiting]` attributes
- `ProjectCeres/Data/AppDbContext.cs` — `FailedLoginAttempt` entity config in `ConfigureSessionEntities`
- `ProjectCeres.Tests/Integration/AuthTestWebApplicationFactory.cs` — no-op rate limiter overrides
- `ProjectCeres.Tests/Architecture/IdentityArchitectureTests.cs` — add the four 6b.2 architecture assertions
- `docs/decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md` — exemption list amendment
- `docs/decisions/ADR-0069-mfa-opt-in-for-personal-users.md` (or `docs/security-model.md`) — backup-code-during-lockout formalization
- `docs/planning-phase3.md` — six deferred decisions captured
- `docs/roadmap-phase-three.md` — mark 6b.2 verification-checklist items shipped

---

## Out of scope (deferred to 6c)

- Password-reset endpoint + flow (`/api/auth/password-reset`)
- `/password-reset` rate limiting (per-IP + per-account)
- Lockout email with signed unlock-token endpoint
- Email-address change flow
- Reauthentication middleware
- AuditLog entity + writer service
- GDPR erasure/export *flows* (this spec ensures the schema enables them)
- Self-service unlock UI

---

## Acceptance

Stage 6b.2 ships when:
- All 51 (54 with architecture) tests are green in `dotnet test`
- ADR-0065 and ADR-0069 (or security-model.md) amendments are committed
- `planning-phase3.md` has all six deferred decisions captured with cross-references
- `roadmap-phase-three.md` Stage 6b.2 verification checklist items are marked `[x]` for shipped items, `[ ]` for items that remain in 6c
- `sync-docs` has been run against the diff
