# Stage 6b.3 — Security Hardening (post-6b.2 audit findings)

**Date:** 2026-05-10
**Stage:** Phase 3, Stage 6b.3 (Identity infrastructure, third sub-stage — hardening)
**Status:** Design

---

## Summary

A post-6b.2 security audit surfaced **11 distinct security gaps** (1 Critical, 4 High, 4 Medium, 2 Low) and **21 net-new edge cases** not incidentally covered by the gap fixes. Stage 6b.3 closes all 11 gaps with regression tests, then adds the 21 edge-case tests as defense-in-depth.

The 11 gaps are not future-work — several are real account-takeover vectors that must not survive into Stage 6c. They were missed in 6b.2 because the audit lens was scoped to the spec's items rather than to adversarial probing of the broader auth surface.

**Approach (b) is taken for Gaps #4 and #5** (the ones that would normally need a step-up middleware): require the user's current TOTP code in the regenerate request body (Gap #4); return 409 if MFA is already enrolled (Gap #5). The full fresh-auth/step-up middleware lives in Stage 6c.

---

## Why a 6b.3 instead of folding into 6c

Stage 6c covers password-reset, email-change, reauth middleware, and the audit log — all NEW endpoints and flows. Stage 6b.3 covers EXISTING endpoints and EXISTING flows that have hidden security gaps. Folding the two would smear the diff across two unrelated risk areas and make the post-merge regression risk for either much harder to triage.

Treat 6b.3 as the "we caught these before they shipped" sub-stage. It has 12 production-code commits + 1 doc commit + 1 edge-case test commit. About a quarter the diff size of 6b.2.

---

## The 11 gaps + fixes

### Gap 1 [Critical] — Backup-code consume race

**Threat:** `MfaBackupCodeService.VerifyAndConsumeAsync` has no per-user lock. Two parallel POSTs to `/api/auth/login/totp` with the same backup code (each carrying its own MFA-pending cookie) both find the row unused, both verify, both set `UsedAt`, both `SaveChangesAsync` succeed — yielding two simultaneous logins from one code.

**Fix:** Mirror `TotpReplayGuard`'s pattern. Add a private static `ConcurrentDictionary<Guid, SemaphoreSlim>` keyed by `userId`; acquire in `VerifyAndConsumeAsync` before the read, release after `SaveChangesAsync`. In-process serialization is single-host only — same Stage-16 follow-up as the TotpReplayGuard semaphore (already captured in `planning-phase3.md` § Stage 6b.2 deferred decisions item 7; 6b.3 extends that entry to cover backup codes too).

**Test:** `MfaBackupCodeServiceTests.VerifyAndConsumeAsync_concurrentSubmissionsOfSameCode_OnlyOneSucceeds` — fire two parallel `VerifyAndConsumeAsync` calls with the same code. Assert exactly one returns `true`. Assert `UserMfaBackupCodes.Count(c => c.UsedAt != null) == 1`.

### Gap 2 [High] — Persistent-cookie rotation race

**Threat:** Two parallel requests with the same `__Host-Persist` cookie hit `PersistentCookieRotationMiddleware` simultaneously. Both load candidates, both verify, both create new sessions. Result: two new sessions, two new persistent cookies, the old row revoked once.

**Fix:** Per-token (or per-matched-session-id) `SemaphoreSlim` around the rotation block. Same pattern as Gap 1.

**Test:** `PersistentCookieRotationTests.ConcurrentRequestsWithSameCookie_RotateExactlyOnce` — fire two parallel GETs with the same `__Host-Persist`. Assert exactly one new `UserSession` row created and exactly one Set-Cookie for `__Host-Persist`.

### Gap 3 [High] — Persistent-cookie pre-auth DoS via linear Argon2id scan

**Threat:** Both `PersistentCookieRotationMiddleware` and `AuthController.Logout` iterate every `IsPersistent && RevokedAt == null` session row and run `_tokens.Verify` (Argon2id) per row. With 1000 active rememberMe sessions, an unauthenticated attacker burns ~50 seconds of CPU per bogus cookie request. No rate limit applies at the middleware layer.

**Fix:** Encode the `UserSession.Id` into the persistent cookie value. New format: `{base64url(sessionIdBytes)}.{secret}`. Lookup becomes O(1) on indexed `Id`; only one Argon2id verify runs per request.

`PersistentTokenService` extensions:
- `string GenerateWithId(Guid sessionId, string secret)` — formats `{base64url(sessionId)}.{secret}`
- `(Guid sessionId, string secret)? TryParseCookie(string cookie)` — returns null on malformed input

`PersistentCookieRotationMiddleware.InvokeAsync` becomes:
1. `TryParseCookie` the value
2. If null → `await _next(context); return;`
3. Load `UserSession` by `Id`, filter `IsPersistent && RevokedAt == null`
4. `_tokens.Verify(secret, session.PersistentTokenHash)` — one hash, not N
5. Continue with rotation

`AuthController.Login` (rememberMe path) calls `_tokens.GenerateWithId(sessionId, _tokens.Generate())` and stores the same `secret`'s hash.

`AuthController.Logout` similarly parses + indexed-lookup.

**Migration:** existing `__Host-Persist` cookies (raw secret only) won't match the new parser. They'll fall through and force a fresh login. Acceptable for single-user beta; no DB migration needed.

**Test:** `PersistentCookieRotationTests.MalformedPersistentCookie_DoesNotRunArgon2idScan` — issue a junk `__Host-Persist` value; assert the middleware short-circuits without loading any session rows. Indirect timing test — assert request completes in under 50ms (well below one Argon2id verify).

### Gap 4 [High] — Backup-code regen requires current TOTP

**Threat:** A session-hijacked attacker calls `POST /api/auth/mfa/backup-codes/regenerate`; existing codes are wiped, the legitimate user is locked out of recovery.

**Fix (approach b):** Add a `totpCode` field to the regenerate request body. Server requires it; rejects if missing or invalid. Falls under the same `auth-totp-by-user` rate-limit protection.

```csharp
public sealed record RegenerateBackupCodesRequest([Required, RegularExpression(@"^\d{6}$")] string TotpCode);

[HttpPost("backup-codes/regenerate")]
public async Task<IActionResult> RegenerateBackupCodes(
    [FromBody] RegenerateBackupCodesRequest request,
    [FromServices] MfaBackupCodeService backupCodes)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);

    var user = await GetCurrentUserAsync();
    if (user is null) return Unauthorized();

    if (!user.TwoFactorEnabled)
        return Conflict(new { error = new { code = "MFA_NOT_ENABLED", message = "MFA must be enabled to regenerate backup codes." } });

    var ok = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.TotpCode);
    if (!ok)
        return Unauthorized(new { error = new { code = "INVALID_MFA_CODE", message = "The verification code is invalid or expired." } });

    var codes = await backupCodes.RegenerateAsync(user.Id, HttpContext.RequestAborted);
    ApplyNoStoreHeaders();
    return Ok(new { backupCodes = codes });
}
```

Note: the regenerate endpoint already inherits `[EnableRateLimiting(AuthTotpByUser)]`? **No** — `auth-totp-by-user` is partitioned by the MFA-pending cookie's `Name` claim, which only exists for unauthenticated half-logged-in users. Authenticated regenerate requests don't carry that cookie. **Decision:** introduce `auth-mfa-by-user` policy, partitioned by `ClaimTypes.NameIdentifier` from the authenticated principal; apply to all `MfaController` endpoints. Same shape as `auth-totp-by-user` (sliding window, 10/min/user).

**Test:** `MfaRegenerateTests.Regenerate_WithoutTotpCode_Returns422` — POST with `{}` body returns 422. `MfaRegenerateTests.Regenerate_WithInvalidTotp_Returns401_DoesNotWipeCodes` — 401 + DB rows unchanged. `MfaRegenerateTests.Regenerate_WithValidTotp_ReplacesCodes` — 200 + new codes returned + old codes rejected on subsequent login.

### Gap 5 [High] — MFA re-enroll silently rotates seed

**Threat:** `POST /api/auth/mfa/enroll` rotates `AuthenticatorKey` unconditionally. A session-hijacked attacker re-enrolls with their own authenticator app, completes verify, and the legitimate user is locked out.

**Fix (approach b):** Return 409 if `user.TwoFactorEnabled` is already true. Disable endpoint stays deferred to 6c; for now, the only path to re-enroll is a 6c-flow disable + new enroll with step-up gate.

```csharp
[HttpPost("enroll")]
public async Task<IActionResult> Enroll()
{
    var user = await GetCurrentUserAsync();
    if (user is null) return Unauthorized();

    if (user.TwoFactorEnabled)
        return Conflict(new { error = new { code = "MFA_ALREADY_ENROLLED", message = "MFA is already enabled. Disable MFA first to re-enroll." } });

    await _userManager.ResetAuthenticatorKeyAsync(user);
    // ... rest unchanged
}
```

**Test:** `MfaEnrollmentTests.Enroll_WhenAlreadyEnrolled_Returns409_AndAuthenticatorKeyUnchanged` — register, enroll, verify, then POST `/enroll` again; assert 409 with code `MFA_ALREADY_ENROLLED` and `await _userManager.GetAuthenticatorKeyAsync(user)` returns the SAME key as before.

### Gap 6 [Medium] — Register endpoint user-enumeration leak

**Threat:** `Register` returns 400 with the literal message `Username 'foo@bar.com' is already taken.` for duplicate emails. A scripted attacker iterates email lists, learning which addresses have accounts.

**Fix:** On Identity's `DuplicateUserName` failure, return 204 (same as success). All other validation failures (short password, breached, malformed) keep their existing 400/422 paths.

```csharp
var result = await _userManager.CreateAsync(user, request.Password);
if (!result.Succeeded)
{
    if (result.Errors.Any(e => e.Code == "DuplicateUserName"))
        return NoContent();   // user-enumeration prevention; 6c email-confirmation flow will notify owner

    foreach (var error in result.Errors)
        ModelState.AddModelError(error.Code, error.Description);
    return ValidationProblem(ModelState);
}
return NoContent();
```

**Existing test that asserts the LEAK** (`RegisterEndpointTests.Register_rejects_duplicate_email`) MUST be replaced. This is the one exception to the user's "don't modify existing passing tests" rule for the security fixes. The replacement test asserts no enumeration:

`RegisterEndpointTests.Register_DuplicateEmail_ReturnsSameShapeAsNewEmail` — register `a@x.com` (assert 204). Register `a@x.com` again (assert 204). Register `b@x.com` (assert 204). Body, status, and approximate timing of all three must be identical. (Also a Stage-6c follow-up: send a "someone tried to register with your email" notification on the duplicate path once email-send ships.)

### Gap 7 [Medium] — CSRF endpoint shares login rate-limit bucket

**Threat:** `/api/auth/csrf` is on the same 10/min/IP `auth-login-by-ip` bucket as `/login`. An attacker on a shared NAT (or a buggy SPA tab-flap) can burn the bucket via 10 `/csrf` GETs and lock the entire IP out of login.

**Fix:** New `auth-csrf-by-ip` policy at 60/min/IP (sliding window, 4 segments × 15s). Move `/csrf` off `auth-login-by-ip` onto its own bucket.

```csharp
public const string AuthCsrfByIp = "auth-csrf-by-ip";

// in AddRateLimiter:
options.AddPolicy(AuthRateLimitPolicies.AuthCsrfByIp, httpContext => {
    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 60, Window = TimeSpan.FromSeconds(60),
        SegmentsPerWindow = 4, QueueLimit = 0,
    });
});

// in AuthController:
[HttpGet("csrf"), AllowAnonymous]
[EnableRateLimiting(AuthRateLimitPolicies.AuthCsrfByIp)]
public IActionResult Csrf() { ... }
```

`Csrf_SharesAuthLoginByIpPolicy` test (which asserts shared bucket as DESIGN) must be replaced. The new test: `Csrf_HasItsOwnPolicy_DoesNotConsumeLoginBucket` — burn 30 `/csrf` calls; assert subsequent `/login` is not rate-limited.

### Gap 8 [Medium] — Logout rate limit + cookie deletion options

**Threat:** `Logout` has `[Authorize]` but no rate limit. Hammering it amplifies DB writes (one UPDATE per call). Pair with the linear Argon2id scan (Gap 3) for a multiplier. Separately, `Response.Cookies.Delete(SessionConstants.PersistentCookieName)` is called without `CookieOptions` — browsers require matching `Path` and `Secure` to delete a `__Host-` cookie, so the directive is silently ignored client-side.

**Fix:** Add `[EnableRateLimiting(AuthLoginByIp)]` to `Logout`. Pass `CookieOptions { Path = "/", Secure = Request.IsHttps, SameSite = SameSiteMode.Lax }` to the persistent-cookie delete.

**Tests:** `LogoutEndpointTests.Logout_DeletesPersistentCookieWithCorrectOptions` — Set-Cookie header for `__Host-Persist` includes `path=/` (and `secure` outside Production overridden by env). `LogoutEndpointTests.Logout_RateLimitedUnderBurst` — 11 `/logout` calls returns 429 (handled by attribute presence; full 429 envelope test already exists).

### Gap 9 [Medium] — SessionRevocationValidator write debounce

**Threat:** Every authenticated request issues UPDATE on `UserSessions.LastUsedAt`. 100 req/sec on any GET endpoint = 100 UPDATE/sec on the same row, causing PostgreSQL row-lock contention.

**Fix:** Skip the UPDATE if `session.LastUsedAt > now - 60s`. (60-second resolution is sufficient for "last used" telemetry.)

```csharp
if (session.LastUsedAt < DateTime.UtcNow - TimeSpan.FromSeconds(60))
{
    session.LastUsedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
}
// else: skip the write; LastUsedAt resolution is 60s
```

**Tests:** `SessionRevocationDebounceTests.LastUsedAt_DoesNotUpdate_WithinDebounceWindow` — make request, capture `LastUsedAt`; immediately make another; assert `LastUsedAt` unchanged. `SessionRevocationDebounceTests.LastUsedAt_UpdatesAfterDebounceWindow` — set `LastUsedAt = now - 90s`; make request; assert `LastUsedAt` is now-ish. (Use direct DB manipulation to avoid waiting 60s in the test.)

`docs/planning-future.md § Session-validation perf` should be updated to mark this as resolved (or removed entirely if the entry only described this exact debounce).

### Gap 10 [Low] — `MfaAwareLengthValidator` hardcoded `mfaEnrolled = false`

**Threat:** None at runtime — the validator always enforces 15-char minimum. But the design intent (let MFA-enrolled users use 8-char passwords) is silently disabled. Drift between code and spec is itself a quality smell.

**Fix:** Replace `var mfaEnrolled = false;` with `var mfaEnrolled = user?.TwoFactorEnabled == true;`. Drop the TODO comment.

**Tests:** `MfaAwareLengthValidatorTests.NonEnrolled_RejectsPasswordBelow15Chars` — already covered indirectly by `Register_rejects_password_below_pre_mfa_minimum_length`. Add `MfaAwareLengthValidatorTests.Enrolled_AcceptsPasswordAt8Chars` — set `user.TwoFactorEnabled = true`, run the validator with an 8-char password, assert success. Add `MfaAwareLengthValidatorTests.Enrolled_RejectsPasswordBelow8Chars` — same setup, 7-char password, assert failure with the appropriate identity-floor message.

### Gap 11 [Low] — Persistent-cookie rotation principal lacks SecurityStamp

**Threat:** The middleware manually constructs a `ClaimsPrincipal` with only `NameIdentifier` and `sid`. `SecurityStampValidator` cannot detect a stamp change for THIS request hop. If a 6c-era "revoke all sessions on password change" feature lands, an attacker timing their stolen `__Host-Persist` use against the password-change moment gets one free authenticated request.

**Fix:** After rotation, do NOT stamp `context.User` directly. Force the next request to authenticate via the freshly-set `__Host-Session` cookie. Drop lines 97-105 of `PersistentCookieRotationMiddleware`.

The trade-off: this request returns 401 (the cookie middleware sees no `__Host-Session`). The browser follows up with the just-issued cookie and the second request succeeds. One extra round-trip on rememberMe-bootstrap. Worth it — the middleware was issuing a principal that bypassed `SecurityStampValidator` AND `SessionRevocationValidator` for one request.

**Test:** `PersistentCookieRotationTests.RotationDoesNotAuthenticateCurrentRequest` — issue a rememberMe session; clear `__Host-Session`; GET an authenticated endpoint with only `__Host-Persist`. Assert 401, AND assert the response carries a fresh `__Host-Session` cookie. A subsequent request with the new cookie succeeds.

### Sub-gap (architecture-test extension)

`ArchitectureTests.AuthController_AllErrorReturnsUseEnvelopeShape` greps `AuthController.cs` only. Extend to grep `MfaController.cs` and any future `Controllers/Api/*.cs`. The MfaController has THREE flat-string error returns today (lines 55, 62, 81) that must be migrated to envelope shape during Gap 4/5 work.

---

## The 21 net-new edge cases (tests-only batch)

Added after the 11 fixes. Each is a `[Fact]` with no production-code change. Grouped by area:

**Login input edges** (5):
1. `LoginEndpointTests.Login_WithEmptyJsonBody_Returns422` — `{}` body
2. `LoginEndpointTests.Login_WithWrongContentType_Returns415` — `Content-Type: text/plain`
3. `LoginEndpointTests.Login_WithoutRememberMeField_DefaultsToFalse` — body omits `rememberMe`
4. `LoginEndpointTests.Login_WithEmptyPassword_Returns422` — `password: ""`
5. `LoginEndpointTests.Login_WithEmailAtBoundaryLengths_HandledGracefully` — empty, whitespace-only, 256+ chars

**Lockout** (1):
6. `LockoutBehaviorTests.LockoutAutoLifts_AfterDefaultLockoutTimeSpan_Elapses` — manipulate `LockoutEnd = now - 1ms`, attempt login, assert success

**Rate limiting** (3):
7. `RateLimitedAuthEndpointTests.LoginTotp_RejectionUsesEnvelopeShape` — TOTP 429 body matches envelope
8. `RateLimitedAuthEndpointTests.SpoofedXForwardedForHeader_DoesNotInfluencePartition` — send `X-Forwarded-For: 1.2.3.4` 11 times; assert 11th still uses real client IP partition (not spoof)
9. `RateLimitedAuthEndpointTests.Register_AlsoRateLimited` — burn 11 register attempts from same IP; 11th returns 429

**Cookie attributes** (1):
10. `CookieAttributesTests.TotpSuccess_SetsSessionCookieWithExpectedAttributes` — `__Host-Session` from TOTP-success response has same attributes as password-step response

**CSRF** (4):
11. `CsrfTests.CsrfCookiePresent_HeaderMissing_Returns400` — common attack shape
12. `CsrfTests.CsrfTokenFromUserA_OnUserBSession_Rejected` — cross-user CSRF token reuse
13. `CsrfTests.CsrfEndpoint_ResponseShape_HasTokenInCookie` — GET `/api/auth/csrf` issues `__Host-XSRF`
14. `CsrfTests.LoginTotp_WithoutCsrfHeader_Returns400` — TOTP step also requires CSRF

**MFA** (1):
15. `MfaBackupCodeServiceTests.VerifyAndConsumeAsync_RejectsInvalidCrockfordChars` — submit `"IIII-IIII-IIII-IIII"` (Crockford excludes I/L/O/U); regex rejects → false

**Recorder** (3):
16. `FailedLoginRecorderTests.SuccessfulLogin_DoesNotWriteRow` — happy path, recorder count unchanged
17. `FailedLoginRecorderTests.OccurredAt_IsUtc` — assert `Kind == DateTimeKind.Utc` on the stored row (or assert the value matches `DateTime.UtcNow` within tolerance)
18. `FailedLoginRecorderTests.SqlInjectionFlavoredInput_StoredAsLiteralString` — submit `' OR '1'='1` as email; assert stored value equals normalized literal

**Architecture** (2):
19. `ArchitectureTests.LoginTotp_OnlyAcceptsPost` — `GET /api/auth/login/totp` returns 405 (route table inspection)
20. `ArchitectureTests.MfaController_AllErrorReturnsUseEnvelopeShape` — extends existing AuthController grep to MfaController

**Register** (2):
21. `RegisterEndpointTests.Register_NoBody_Returns400` — empty POST body
22. `RegisterEndpointTests.Register_HibpServiceUnavailable_FailsClosed` — stub `IBreachedPasswordChecker` to throw; assert registration returns 500 (not 204) — confirms HIBP unavailability does NOT fail-open

(22 tests — one bonus on the "21 net-new" — `MfaBackupCodeServiceTests.VerifyAndConsumeAsync_RejectsInvalidCrockfordChars` is genuinely new and the inventory caught the bonus.)

---

## Tests required before this stage ships

**Per the tests-as-ship-gate memory rule, every fix above is paired with at least one negative-assertion test.** Concurrency edges (Gap 1, 2) get parallel-execution tests. Behavior changes (Gap 4, 5, 6, 7) get both happy-path AND regression tests. Cookie-deletion (Gap 8) gets a Set-Cookie-header inspection test. Drift fixes (Gap 10) get the documented behavior pinned.

**Total ship-gate test count for 6b.3:** ~22 new tests for fixes + 22 net-new edge-case tests = ~44 new tests.

---

## Files touched

**New:**
- `ProjectCeres/Common/Authentication/AuthCsrfRateLimitPolicy.cs` (or extend `AuthRateLimitPolicies.cs`) — `AuthCsrfByIp` constant, `AuthMfaByUser` constant
- `ProjectCeres/ViewModels/Auth/RegenerateBackupCodesRequest.cs` — `{ totpCode }`
- `ProjectCeres.Tests/Integration/Authentication/MfaRegenerateTests.cs` (new)
- `ProjectCeres.Tests/Integration/Authentication/SessionRevocationDebounceTests.cs` (new)
- `ProjectCeres.Tests/Unit/Authentication/MfaAwareLengthValidatorTests.cs` (new — first unit-test file under that path; create directory)

**Modified — production:**
- `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs` — per-user semaphore
- `ProjectCeres/Common/Authentication/PersistentCookieRotationMiddleware.cs` — sessionId-prefix lookup, drop manual principal stamp
- `ProjectCeres/Common/Authentication/PersistentTokenService.cs` — `GenerateWithId`, `TryParseCookie`
- `ProjectCeres/Common/Authentication/SessionRevocationValidator.cs` — 60s debounce
- `ProjectCeres/Common/Authentication/MfaAwareLengthValidator.cs` — wire to `user.TwoFactorEnabled`
- `ProjectCeres/Controllers/Api/AuthController.cs` — register-leak fix, logout rate-limit, cookie-delete options, login generates/parses sessionId-prefixed token
- `ProjectCeres/Controllers/Api/MfaController.cs` — 409 on already-enrolled, TOTP-gated regen, envelope shape on errors, MfaByUser rate-limit attribute
- `ProjectCeres/Program.cs` — `AuthCsrfByIp` policy, `AuthMfaByUser` policy registration; CSRF policy detached from login policy

**Modified — tests:**
- `ProjectCeres.Tests/Integration/Authentication/RegisterEndpointTests.cs` — REPLACE `Register_rejects_duplicate_email` (it asserts the leak we're fixing). Document inline why the change.
- `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs` — REPLACE `Csrf_SharesAuthLoginByIpPolicy` (asserts the shared-bucket design we're fixing) with `Csrf_HasItsOwnPolicy_DoesNotConsumeLoginBucket`. Document inline.
- `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — extend `AllErrorReturnsUseEnvelopeShape` to grep `MfaController.cs`. Add `LoginTotp_OnlyAcceptsPost`.
- All other 6b.2 tests are append-only.

**Modified — docs:**
- `docs/security-model.md` — register no longer enumerates; CSRF own policy; logout rate-limited; persistent-cookie format; MFA enroll requires not-already-enrolled; backup-code regen requires current TOTP
- `docs/api-contract.md` — add `409 MFA_ALREADY_ENROLLED`, `409 MFA_NOT_ENABLED`, `INVALID_MFA_CODE` error codes to the canonical list
- `docs/planning-phase3.md` — extend Stage 6b.2 deferred decisions item 7 (TOTP guard semaphore) to also cover backup-code semaphore
- `docs/planning-future.md` — mark "Session-validation perf" entry as RESOLVED in 6b.3 (or remove if it only described this exact debounce)
- `docs/roadmap-phase-three.md` — add Stage 6b.3 callout

---

## Out of scope (deferred to 6c)

- Full step-up middleware (a `LastPasswordVerifiedAt` claim minted on password step, validated by sensitive-action endpoints). Approach (b) above is the stopgap.
- Disable-MFA endpoint with full re-auth gate.
- Email notification on duplicate-email registration ("someone tried to register with your address").
- Audit log entity + writer (Stage 6c).
- Password reset flow (Stage 6c).

---

## Acceptance

Stage 6b.3 ships when:
- All 11 gap fixes have green regression tests.
- All 22 net-new edge cases have green tests.
- `AllErrorReturnsUseEnvelopeShape` extended to `MfaController` and passing.
- `Register_rejects_duplicate_email` REPLACED with `Register_DuplicateEmail_ReturnsSameShapeAsNewEmail`.
- `Csrf_SharesAuthLoginByIpPolicy` REPLACED with `Csrf_HasItsOwnPolicy_DoesNotConsumeLoginBucket`.
- `dotnet test` reports 690 + ~44 = ~734 tests, all green.
- `docs/security-model.md`, `api-contract.md`, `planning-phase3.md`, `planning-future.md`, `roadmap-phase-three.md` reflect the changes.
- `sync-docs` run.
