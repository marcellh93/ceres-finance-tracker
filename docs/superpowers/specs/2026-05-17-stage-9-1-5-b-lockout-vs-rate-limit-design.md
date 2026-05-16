# Stage 9.1.5.b — Lockout banner visible when rate-limit and lockout coincide

**Status:** Spec — pending user review, then writing-plans skill.
**Phase:** Phase 3, Stage 9.1.5 (Phase-1-discovered bugfix batch).
**Originating bug:** Roadmap line 1097 — "lockout doesn't trigger reliably at 6 attempts (rate-limit fires first)."
**Date:** 2026-05-17.

---

## 1. The bug, in plain English

The login endpoint has two unrelated safety mechanisms that both trip at the same numeric threshold:

| Mechanism | Value | Where set | What it does |
|---|---|---|---|
| Per-IP rate limit | **10 requests / 60 s** sliding window | `Program.cs:285-293` (`AuthLoginByIp`, `PermitLimit = 10`) | Middleware short-circuit. Once tripped, the rate-limit middleware fires `OnRejected` → returns 429 `RATE_LIMITED` BEFORE the controller runs. |
| Per-account lockout | **10 failed attempts** (no time window, counter persists) | `Program.cs:114` (`MaxFailedAccessAttempts = 10`) | Identity bookkeeping. `PasswordSignInAsync(..., lockoutOnFailure: true)` increments `AccessFailedCount`; at threshold, `LockoutEnd = now + 15 min` and subsequent calls return 401 `ACCOUNT_LOCKED_OUT`. |

When a user enters 10 wrong passwords in a row from one browser:

- Attempts 1–10 reach the controller, each returns 401 `INVALID_CREDENTIALS`, `AccessFailedCount` climbs 1→10.
- On attempt 10, the controller's post-call `ReloadAsync` sees `LockoutEnd` flipped to `now+15min`. Controller returns 401 `ACCOUNT_LOCKED_OUT` for this single response.
- On attempt 11+, the rate-limit middleware fires BEFORE the controller. `OnRejected` returns 429 `RATE_LIMITED`. The user never sees the lockout banner — only the generic "too many requests, retry later" toast — even though the account IS locked.

The roadmap entry's "documented threshold (6)" was a misframing. The actual everywhere-documented threshold is 10 (`security-model.md:298`, `roadmap-phase-three.md:461, 523, 571, 1009, 1730`).

## 2. Why we are NOT lowering `MaxFailedAccessAttempts`

`docs/security-model.md:298-299` pins:

> "Do not tighten lockout thresholds beyond the values stated below — overly aggressive lockouts become a DoS lever an attacker can pull against known emails."

Lowering threshold to 5 (or anywhere below 10) means: an attacker who knows a user's email can DoS-lock that account with 5 wrong-password POSTs from any IP. The current threshold of 10 is the documented floor. **The fix must work without changing this number.**

## 3. The fix

The user-facing problem is the **envelope** the user sees on attempt 11+, not the threshold. At attempt 11 we already have full knowledge that the account is locked (the controller wrote `LockoutEnd` to the DB on attempt 10) — we just can't surface that knowledge from `OnRejected` because the controller doesn't run.

The fix: when `OnRejected` fires for `/api/auth/login`, consult an in-memory cache that the controller seeded on the lockout response. If the cache says the requesting IP's last-attempted email is locked, return the `ACCOUNT_LOCKED_OUT` envelope (status 401) instead of `RATE_LIMITED` (status 429).

### 3.1 Mechanism

Three cache entries, all in `IMemoryCache`, all memory-only (no DB read in `OnRejected`):

| Key | Value | TTL | Written by | Read by |
|---|---|---|---|---|
| `lockout:{normalizedEmail}` | `DateTimeOffset` of `LockoutEnd` | `DefaultLockoutTimeSpan` (15 min) | `AuthController.Login` on `signIn.IsLockedOut` branch | `OnRejected` (via the per-IP pointer) |
| `last-login-email:{ip}` | `(string normalizedEmail, DateTimeOffset lockedAt)` | 60 s | `AuthController.Login` on `signIn.IsLockedOut` branch | `OnRejected` |
| (cache delete) | n/a | n/a | `LockoutUnlockService.ConfirmAsync` on successful unlock | n/a |

The per-IP pointer's TTL is 60s (= rate-limit window). `OnRejected` honors the pointer only if `lockedAt` is within the last 60 seconds AND the `lockout:{email}` entry exists. This bounds the "wrong banner for a different user on the same NAT" failure mode to ≤60s (see § 4.1).

### 3.2 Files touched

| File | Change |
|---|---|
| `ProjectCeres/Common/Authentication/LockoutCache.cs` | **NEW.** Thin `IMemoryCache` wrapper. Methods: `TryGetLockoutEnd(email)`, `SetLockoutEnd(email, until)`, `TryGetLastLockedEmailForIp(ip)`, `SetLastLockedEmailForIp(ip, email)`, `Remove(email)`. Takes `ILookupNormalizer` via DI to share Identity's email-normalization (same normalizer `UserManager.NormalizeEmail` uses). |
| `ProjectCeres/Program.cs:264-283` | Extend `OnRejected`: if `context.HttpContext.Request.Path.StartsWithSegments("/api/auth/login")` AND `LockoutCache.TryGetLastLockedEmailForIp(ip)` returns a non-expired email AND `TryGetLockoutEnd(email)` returns a non-null value → write 401 + `ACCOUNT_LOCKED_OUT` envelope. Else fall through to existing 429 + `RATE_LIMITED` envelope. |
| `ProjectCeres/Program.cs` (DI) | Register `LockoutCache` as a singleton. `IMemoryCache` is already registered globally. |
| `ProjectCeres/Controllers/Api/AuthController.cs:190-211` | On `signIn.IsLockedOut`, call `LockoutCache.SetLockoutEnd(request.Email, userStub.LockoutEnd!.Value)` and `SetLastLockedEmailForIp(ip, request.Email)`. Existing unlock-email dispatch unchanged. |
| `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` | On the success path inside `ConfirmAsync` (after `ResetAccessFailedCountAsync` + `SetLockoutEndDateAsync(null)`), call `_lockoutCache.Remove(user.Email)`. |
| `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs:46-51` | Replace the local `TruncateAndNormalize` lowercase+trim with a call through the same `ILookupNormalizer` `LockoutCache` uses. Single source of truth for email normalization across the auth surface. Truncation logic (256-char cap) stays. |
| `docs/planning-phase3.md:449` | Append one sentence to the Stage 16 entry: "Lockout-status cache (`LockoutCache`, currently `IMemoryCache`, single-host) also needs multi-host migration alongside `_loginLocks`." |
| `docs/roadmap-phase-three.md:1107` | Update the 9.1.5.b verification line to reflect the new contract (lockout banner on attempt 11+, not "ACCOUNT_LOCKED_OUT at 6 attempts"). |

### 3.3 Why this design — what was rejected

| Alternative | Why rejected |
|---|---|
| Lower `MaxFailedAccessAttempts` to 5 | Violates `security-model.md:298-299`. Doubles DoS-by-lockout amplification. |
| `OnRejected` reads request body to extract email | Requires `EnableBuffering()` + position reset + cancellation plumbing in middleware. Body read on every rejected request. DoS amplification target. |
| `OnRejected` reads DB to check lockout state for the IP's recent emails | DB query per rejected request — exactly the DoS amplification security-model warned against. |
| Reorder pipeline so lockout-check runs before rate-limit middleware | Requires re-architecting `AuthController.Login` into pre-auth middleware. Massive blast radius; touches every auth flow. |
| Per-account rate-limit policy in addition to per-IP | Spray attacks already caught by per-IP. Per-account adds a separate enumeration vector (different responses for known vs unknown emails). |
| Architecture-test on lockout-threshold-vs-rate-limit-ceiling | Prior-draft Test #3. Required reflection into `RateLimiterOptions.PolicyMap` private field + invoking the partition delegate. Brittle. Cleaner alternative (export the permit-limit as a shared constant) was considered but rejected — not needed once we stopped lowering the lockout threshold. |

## 4. Edge cases and mitigations

### 4.1 Different user from the same IP

Two locked accounts share an IP (corporate NAT, public WiFi). The per-IP pointer `last-login-email:{ip}` overwrites on each new lockout. A third (non-locked) user from that IP whose burst exhausts the per-IP bucket would, with a naive design, see `ACCOUNT_LOCKED_OUT` even though they aren't locked.

**Mitigation:** the pointer stores `lockedAt`. `OnRejected` honors the pointer only if `lockedAt` is within the last 60s.

**Residual risk:** within the 60s window, the third user sees the wrong banner. Acceptable because (a) the actual block is the IP rate-limit, not the envelope code; (b) on cooldown, the user retries, the controller is hit, and the correct response is returned; (c) the user's account isn't actually locked, so subsequent flows work normally.

### 4.2 Lockout window elapses while cache entry is warm

Cache entry written at minute 0 with TTL = 15 min. Identity auto-lifts `LockoutEnd` only on a successful login attempt — so at minute 16 the DB MAY still say locked (if no successful attempt has happened). The cache and DB agree at the boundary. If they diverge briefly, the controller is authoritative.

### 4.3 Successful manual unlock via email link

`LockoutUnlockService.ConfirmAsync` clears `LockoutEnd` in the DB at, say, minute 5. Without cache invalidation, the cache entry persists until minute 15 — `OnRejected` would say `ACCOUNT_LOCKED_OUT` for the user's next burst even though their account isn't locked anymore.

**Mitigation:** `LockoutUnlockService.ConfirmAsync` calls `LockoutCache.Remove(email)` on success.

### 4.4 Process restart

`IMemoryCache` is in-process. After restart, the cache is empty. The DB still has the locked state. The first burst attempt hits the controller (rate-limit budget intact post-restart), gets `ACCOUNT_LOCKED_OUT`, re-seeds the cache. Subsequent attempts in the same burst behave correctly. No bug.

### 4.5 Multi-host deployment (deferred, see § 6.3)

`IMemoryCache` is per-instance. Cache state diverges across hosts. Same single-host assumption as the existing `_loginLocks` semaphore in `AuthController.cs:27-28`. Already-scheduled deferral — see § 6.3.

### 4.6 Email-normalization drift

`LockoutCache`, `FailedLoginRecorder`, and `UserManager.NormalizeEmail` all need to produce the same key. This spec consolidates `FailedLoginRecorder.TruncateAndNormalize` (line 46) onto `ILookupNormalizer` so all three sites use Identity's canonical normalizer. Truncation (256-char cap) stays in `FailedLoginRecorder`.

### 4.7 Empty-email or malformed request

`[ApiController]` validation runs in the action-invocation pipeline, AFTER `[EnableRateLimiting]` has consumed the permit. An empty-email POST consumes a permit, returns 422, never reaches the lockout-seeding code. On attempt 11 of empty-email bursts: rate-limit fires, `OnRejected` finds no pointer (or expired pointer), falls through to `RATE_LIMITED`. Correct.

## 5. Tests

### 5.1 New tests

All in `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs` (`RateLimitTests` collection).

**Test #1 — load-bearing: post-lockout 429 must surface as 401 ACCOUNT_LOCKED_OUT.**

```csharp
[Fact]
public async Task RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited()
{
    await using var factory = _factory.WithFreshRateLimiter();
    var user = await AuthTestFixture.RegisterUserAsync(factory, "rl-locked@rl-test.local");
    var client = factory.CreateClient();

    HttpResponseMessage? tenth = null;
    for (int i = 1; i <= 10; i++)
    {
        tenth = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    }
    var tenthBody = await tenth!.Content.ReadFromJsonAsync<JsonElement>();
    tenthBody.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("ACCOUNT_LOCKED_OUT",
            "attempt 10 trips MaxFailedAccessAttempts; controller returns ACCOUNT_LOCKED_OUT and seeds the lockout-cache");

    var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
        new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    eleventh.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
        "post-lockout 429s must be replaced by 401 ACCOUNT_LOCKED_OUT");
    var eleventhBody = await eleventh.Content.ReadFromJsonAsync<JsonElement>();
    eleventhBody.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("ACCOUNT_LOCKED_OUT",
            "envelope must surface the lockout state so the SPA shows the locked-account banner");
}
```

**Test #2 — regression guard for non-login endpoints.**

```csharp
[Fact]
public async Task RateLimitRejection_OnNonLoginEndpoint_StillReturnsRateLimited()
{
    var client = _factory.CreateClient();
    HttpResponseMessage? rejected = null;
    for (int i = 0; i < 70; i++)
    {
        var resp = await client.GetAsync("/api/auth/csrf");
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            rejected = resp;
            break;
        }
    }
    rejected.Should().NotBeNull("CSRF endpoint must still rate-limit at 60/min");

    var body = await rejected!.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("RATE_LIMITED",
            "non-login endpoints must keep the original rate-limit envelope; the lockout-aware path is /api/auth/login only");
    rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
        "non-login endpoints must keep status 429");
}
```

**Test #3 — cache TTL behavior.**

```csharp
[Fact]
public async Task RateLimitRejection_AfterLockoutWindowExpires_FallsBackToRateLimitedEnvelope()
{
    await using var factory = _factory.WithShortLockoutCacheTtl();
    var user = await AuthTestFixture.RegisterUserAsync(factory, "rl-expire@rl-test.local");
    var client = factory.CreateClient();

    for (int i = 0; i < 10; i++)
    {
        await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    }

    await Task.Delay(TimeSpan.FromMilliseconds(1200));

    HttpResponseMessage? rejected = null;
    for (int i = 0; i < 15; i++)
    {
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = "nosuchuser@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            rejected = resp;
            break;
        }
    }
    rejected.Should().NotBeNull("rate-limit must still fire after cache TTL expires");

    var body = await rejected!.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("RATE_LIMITED",
            "after the lockout-cache TTL expires, OnRejected must fall back to the default RATE_LIMITED envelope");
}
```

**Test #4 — per-IP pointer expiry (different user from same IP).**

```csharp
[Fact]
public async Task RateLimitRejection_DifferentUserFromSameIp_OutsideWindow_ReturnsRateLimited()
{
    await using var factory = _factory.WithShortLockoutCacheTtl();
    var userA = await AuthTestFixture.RegisterUserAsync(factory, "rl-ip-a@rl-test.local");
    var userB = await AuthTestFixture.RegisterUserAsync(factory, "rl-ip-b@rl-test.local");
    var client = factory.CreateClient();

    for (int i = 0; i < 10; i++)
    {
        await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = userA.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    }

    await Task.Delay(TimeSpan.FromMilliseconds(1200));

    var bResp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
        new { email = userB.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    bResp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
        "outside the per-IP pointer window, a different user's 429 must keep its RATE_LIMITED shape");
    var body = await bResp.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("RATE_LIMITED",
            "user B is not locked; the per-IP pointer to user A must have expired");
}
```

### 5.2 Test-infrastructure additions

`ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs` — new method:

```csharp
/// <summary>
/// Returns a derived factory whose LockoutCache uses a 1-second TTL for both the
/// lockout-cache entries AND the per-IP last-login-email pointer (instead of the
/// production 15-minute and 60-second values respectively). Use ONLY for tests that
/// need the cache to expire within test execution.
/// </summary>
public WebApplicationFactory<Program> WithShortLockoutCacheTtl();
```

Implementation: post-configures `LockoutCacheOptions` (a new tiny options class added alongside `LockoutCache.cs`) to override the TTL values.

### 5.3 Existing tests re-framed

Three tests in `RateLimitedAuthEndpointTests.cs` currently register a user then drive 429 with wrong passwords. Under the new behavior, attempt 10 triggers lockout and attempt 11+ returns 401 `ACCOUNT_LOCKED_OUT` instead of 429 `RATE_LIMITED`. Re-frame each to use an unregistered email so the password-spray path is exercised without colliding with the new lockout-aware `OnRejected`:

| Test | Change |
|---|---|
| `Login_RequestsEventuallyReturn429WithRetryAfter` (line 47) | Drop `RegisterUserAsync` setup; change email to unregistered `"nouser-rl@rl-test.local"`. Assertion unchanged. |
| `RateLimit429_ResponseBodyMatchesApiContractEnvelope` (line 62) | Same. |
| `RateLimitOnRejected_ContentTypeIsApplicationJson` (line 117) | Same. |

### 5.4 Existing tests unchanged

- `LockoutBehaviorTests.WrongPasswordTenTimes_LocksAccount` (line 90) — still pins threshold = 10. **No change.**
- `LockoutBehaviorTests.RateLimitRejection_DoesNotCountTowardLockout` (line 131) — uses the no-op rate-limit factory (`IntegrationTests` collection). One-line docstring addition noting this means OnRejected never runs in this test, so the test exercises only the controller-level invariant.
- All other `LockoutBehaviorTests` (TOTP, backup-code, restart, auto-lift, reset-counter) — no interaction with the new mechanism.
- `LockoutUnlockConfirmTests` — `ConfirmAsync` now calls `LockoutCache.Remove(email)` on success. Existing tests assert DB state (AccessFailedCount = 0, LockoutEnd = null); they don't inspect cache state. Tests pass unchanged.

## 6. Scope guard

### 6.1 In scope

1. New `LockoutCache.cs` (~50 LOC).
2. New `LockoutCacheOptions.cs` alongside (TTL values for tests).
3. `Program.cs` `OnRejected` extension (~15 LOC change).
4. `AuthController.Login` cache-seed on `IsLockedOut` branch (~5 LOC change).
5. `LockoutUnlockService.ConfirmAsync` cache-invalidate (~1 LOC change).
6. `FailedLoginRecorder.TruncateAndNormalize` consolidated onto `ILookupNormalizer` (~3 LOC change).
7. `LockoutCache` DI registration (~1 LOC).
8. Four new tests + one new test-factory helper + three re-framed existing tests + one docstring update.
9. `planning-phase3.md:449` one-sentence Stage 16 amendment.
10. `roadmap-phase-three.md:1107` updated verification text.

### 6.2 Out of scope (design choices, NOT deferrals)

These are decisions made during brainstorming — they are not bugs being kicked down the road.

- Changing `MaxFailedAccessAttempts = 10` (security-model.md:298-299 invariant).
- Changing `AuthLoginByIp` permit limit or window (stays 10/60s).
- Reading the request body in `OnRejected` (avoided by per-IP pointer cache).
- Per-account rate-limit policy (anti-spray is per-IP only).
- Unknown-email enumeration prevention beyond existing `RunDummyHash` + per-IP rate-limit.
- `DefaultLockoutTimeSpan` changes.
- `LockoutUnlockService.IssueAsync` changes.
- Frontend changes (SPA already maps `ACCOUNT_LOCKED_OUT` to the locked-banner i18n key).
- Investigating the suite-wide auth-tier contention (Stage 9.1.5.a — separate batch line, separate fix; this spec's tests inherit that risk and acknowledge it in §7).

### 6.3 Deferred work (with required tripwire fields)

**Multi-host cache migration**

- **Cited reason (already-scheduled):** `planning-phase3.md:449` Stage 16 entry already tracks the multi-host migration for the `_loginLocks` semaphore. `LockoutCache` shares the same single-host assumption and migrates alongside the semaphore. Scoped under Stage 16's existing `[ ]` line.
- **Receiving-stage `[ ]` checkbox (same commit):** the existing Stage 16 entry's `[ ]` line. Amended this commit (§3.2) to explicitly include `LockoutCache`.
- **Mechanical tripwire:** Stage 16 itself is the tripwire — it's a stage with `[ ]` items that block Phase 3 shipping. The amendment to its description ensures the cache is part of the existing checklist that will be reviewed at Stage 16 close-out.

No other deferrals.

## 7. Verification checklist

- [ ] `LockoutCache.cs` created with all five methods; uses injected `ILookupNormalizer`.
- [ ] `LockoutCacheOptions.cs` created alongside (TTL values for tests).
- [ ] `Program.cs` `OnRejected` extended for `/api/auth/login` path; cache lookup path is memory-only (no DB).
- [ ] `LockoutCache` registered as singleton in `Program.cs`.
- [ ] `AuthController.cs:190-211` seeds both cache entries on `IsLockedOut` branch.
- [ ] `LockoutUnlockService.ConfirmAsync` calls `_lockoutCache.Remove(email)` on success.
- [ ] `FailedLoginRecorder.TruncateAndNormalize` replaced with `ILookupNormalizer.NormalizeEmail` + truncation; existing tests pass unchanged.
- [ ] Test #1 green: 10 wrong passwords → attempt 11 returns 401 `ACCOUNT_LOCKED_OUT`.
- [ ] Test #2 green: `/csrf` 429 still carries `RATE_LIMITED` envelope.
- [ ] Test #3 green: after cache TTL elapses, 429s revert to `RATE_LIMITED` envelope.
- [ ] Test #4 green: same-IP, different user, outside 60s window returns `RATE_LIMITED`.
- [ ] `MaxFailedAccessAttempts` is still 10 (no `Program.cs:114` change).
- [ ] `AuthLoginByIp` PermitLimit still 10, Window still 60s (no `Program.cs:285-293` change).
- [ ] `WithShortLockoutCacheTtl()` helper added to `RateLimitedAuthTestWebApplicationFactory`.
- [ ] 3 existing tests reframed to unregistered emails as listed in §5.3.
- [ ] `planning-phase3.md:449` Stage 16 entry updated with cache multi-host note.
- [ ] `roadmap-phase-three.md:1107` verification text updated.
- [ ] Full `dotnet test` exits 0 (modulo 9.1.5.a flakes which are pre-existing — Tests #1–#4 land in the `RateLimitTests` collection which is subject to the same architectural contention 9.1.5.a tracks).
- [ ] Manual browser verification: enter 10 wrong passwords against the dev account; on attempt 11+ the SPA shows the locked-account banner (i18n key `auth.errors.account_locked_out`), NOT the rate-limit toast.

## 8. Open questions

None at spec-write time. All design decisions resolved during brainstorming:

- Path 1 chosen (keep threshold = 10, change envelope priority) — confirmed against `security-model.md:298-299` invariant.
- Per-IP pointer with `lockedAt` timestamp and 60s honor-window — chosen over body-buffering and over DB-read-in-OnRejected.
- `LockoutCache.Remove(email)` on `LockoutUnlockService.ConfirmAsync` success — chosen to close the staleness window.
- `ILookupNormalizer` injection across `LockoutCache` + `FailedLoginRecorder` — chosen to consolidate normalization onto Identity's canonical normalizer.
- Multi-host deferral to Stage 16 — already-scheduled receiving entry, amended in §3.2 to include the cache.
