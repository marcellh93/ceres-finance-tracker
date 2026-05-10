# Stage 6c Sub-stage 6.13 — Reauthentication Middleware

**Date:** 2026-05-10
**Stage:** Phase 3, Stage 6c (Identity infrastructure, Batch 3b continued)
**Sub-stage:** 6.13 (reauthentication middleware for sensitive operations)

## 1. Scope

Implement the reauthentication-for-sensitive-operations gate per `security-model.md` § Login → Reauthentication and ADR-0069. Replaces the in-body TOTP-code stopgap on `/api/auth/mfa/backup-codes/regenerate` (Stage 6b.3 Gap 4) with a uniform mechanism the rest of Stage 6c builds on (`6.12` email change, `6.14` audit log writes for sensitive events, Stage 12 sessions list, Stage 13 GDPR erasure).

In scope:
- A `LastReauthAt` claim stamped on the auth cookie at login, login/totp, and after a successful step-up.
- A new `POST /api/auth/reauth` endpoint accepting `{password?}` (no MFA) or `{totpCode?}` (MFA) and refreshing the claim.
- A `[RequireRecentAuth]` action-level attribute backed by an authorization policy; freshness window 5 minutes.
- A custom `IAuthorizationMiddlewareResultHandler` that emits `401 REAUTH_REQUIRED` when authorization fails specifically because of a stale/missing `LastReauthAt` claim.
- Retroactive gating of three existing endpoints: `/api/auth/mfa/enroll`, `/api/auth/mfa/enroll/verify`, `/api/auth/mfa/backup-codes/regenerate`. Removes the in-body `TotpCode` from `RegenerateBackupCodesRequest`.
- Per-user rate limit on `/api/auth/reauth` mirroring `AuthMfaByUser` (10/min keyed by NameIdentifier).

Out of scope (deferred to other 6c sub-stages):
- Email-address-change flow (6.12) — will declare `[RequireRecentAuth]` when shipped.
- `AuditLog` entity (6.14) — sensitive-event audit writes start when 6.14 lands.
- Sessions list (Stage 12) and GDPR erasure (Stage 13) — same.
- SPA dialog UI for the step-up prompt — frontend work, ships with Stage 9 auth pages.
- New-device step-up email-link flow for non-MFA accounts — separate from per-operation reauth; depends on Stage 8 email service.

---

## 2. Architecture & components

### 2.1 The claim

Add to `ProjectCeres/Common/Authentication/SessionConstants.cs`:

```csharp
public const string LastReauthAtClaim = "last_reauth_at";

/// <summary>HttpContext.Items key used by the login + reauth flows to pass
/// the freshness timestamp into ApplicationUserClaimsPrincipalFactory.</summary>
public const string LastReauthAtItemKey = "LastReauthAt";
```

The claim value is a **Unix seconds** string (e.g., `"1715347200"`). Unix seconds keep parsing trivial (`long.Parse`) and avoid timezone confusion. The factory writes the claim during `GenerateClaimsAsync`.

Window: **5 minutes (300 seconds)**, defined as a constant on the requirement type.

### 2.2 Claims-factory extension

`ApplicationUserClaimsPrincipalFactory.GenerateClaimsAsync` already copies `HttpContext.Items[PendingSessionItemKey]` into the `sid` claim. Extend identically:

```csharp
protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
{
    var identity = await base.GenerateClaimsAsync(user);

    var sessionId = _http.HttpContext?.Items[SessionConstants.PendingSessionItemKey] as Guid?;
    if (sessionId is { } sid)
        identity.AddClaim(new Claim(SessionConstants.SessionIdClaim, sid.ToString()));

    if (_http.HttpContext?.Items[SessionConstants.LastReauthAtItemKey] is string ts && ts.Length > 0)
        identity.AddClaim(new Claim(SessionConstants.LastReauthAtClaim, ts));

    return identity;
}
```

### 2.3 Login flows stamp the claim

`AuthController.Login` (no-MFA path): immediately after the `_loginLocks` semaphore is acquired and BEFORE `PasswordSignInAsync`, set:

```csharp
HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
```

The factory picks it up when Identity calls `SignInAsync` internally during `PasswordSignInAsync`. The existing `PendingSessionItemKey` is set on the same code path (line 109); `LastReauthAtItemKey` slots in next to it.

`AuthController.LoginTotp` (MFA path): set the same item just before the `_signInManager.SignInAsync(user, isPersistent: false)` call on the TOTP-success branch AND on the backup-code-success branch. Both branches stamp; backup-code recovery is a one-time recovery, not a reduced-trust login.

### 2.4 Authorization policy + handler

New file `ProjectCeres/Common/Authentication/RecentAuthRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace ProjectCeres.Common.Authentication;

public sealed class RecentAuthRequirement : IAuthorizationRequirement
{
    /// <summary>5-minute freshness window per security-model.md § Login → Reauthentication.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
}
```

New file `ProjectCeres/Common/Authentication/RecentAuthRequirementHandler.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Authorization;

namespace ProjectCeres.Common.Authentication;

public sealed class RecentAuthRequirementHandler : AuthorizationHandler<RecentAuthRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RecentAuthRequirement requirement)
    {
        var raw = context.User.FindFirst(SessionConstants.LastReauthAtClaim)?.Value;
        if (string.IsNullOrEmpty(raw))
            return Task.CompletedTask;  // missing claim → fail (don't call context.Succeed)

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var claimUnix))
            return Task.CompletedTask;  // malformed claim → fail

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ageSeconds = nowUnix - claimUnix;

        // Boundary inclusive (<= window) so claim at exactly now-300s still succeeds.
        // Reject future claims (negative ageSeconds) — server-issued cookies should never be ahead of now.
        if (ageSeconds < 0) return Task.CompletedTask;
        if (ageSeconds <= (long)RecentAuthRequirement.Window.TotalSeconds)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

### 2.5 Attribute

New file `ProjectCeres/Common/Authentication/RequireRecentAuthAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Apply on a controller action that requires a fresh password (or TOTP) proof within
/// the last 5 minutes. Backed by the "RecentAuth" authorization policy. Failed gate
/// returns 401 with error.code = "REAUTH_REQUIRED" via RecentAuthMiddlewareResultHandler.
/// Action-level only — see ReauthArchitectureTests.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequireRecentAuthAttribute : AuthorizeAttribute
{
    public const string PolicyName = "RecentAuth";
    public RequireRecentAuthAttribute() { Policy = PolicyName; }
}
```

`AttributeTargets.Method` makes class-level use a compile-time error. The architecture test §4.7 still asserts no class application defensively in case someone changes the targets.

### 2.6 Custom result handler for the envelope

ASP.NET's `AuthorizationMiddleware` calls a registered `IAuthorizationMiddlewareResultHandler` on every authorization outcome. We wrap the default with one that emits the envelope when (and only when) the failed requirement is `RecentAuthRequirement`. For all other failures (e.g., the global `RequireAuthenticatedUser` fallback), we delegate to the default handler so the existing `OnRedirectToLogin → 401` path is preserved.

New file `ProjectCeres/Common/Authentication/RecentAuthMiddlewareResultHandler.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Common.Authentication;

public sealed class RecentAuthMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context,
        AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden &&
            authorizeResult.AuthorizationFailure?.FailedRequirements
                .Any(r => r is RecentAuthRequirement) == true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "REAUTH_REQUIRED",
                    message = "Please confirm your identity to continue.",
                }
            });
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
```

Note: `authorizeResult.Forbidden` is true when authorization succeeded for one part (the user IS authenticated) but failed a specific requirement. `authorizeResult.Challenged` is true when the user wasn't authenticated at all — that path goes through the default handler, which triggers `OnRedirectToLogin` and emits the existing 401 (no envelope, matching prior behaviour for unauthenticated requests).

### 2.7 Reauth endpoint

New DTO `ProjectCeres/ViewModels/Auth/ReauthRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class ReauthRequest
{
    /// <summary>Plain-text password. Required when the user does NOT have MFA enabled;
    /// ignored otherwise. The DTO does not enforce required-vs-optional here because
    /// the required field depends on user state — service-side check produces the
    /// 422 with field-level details.</summary>
    [StringLength(128)]
    public string? Password { get; set; }

    /// <summary>Six-digit TOTP code. Required when the user has TwoFactorEnabled = true;
    /// ignored otherwise. Backup codes are NOT accepted at reauth (recovery path is
    /// /login/totp). Permissive bound matches password-reset DTO so service-side
    /// MfaConstants.TotpCodeShape rejects backup-code-shaped submissions with 401.</summary>
    [StringLength(32)]
    public string? TotpCode { get; set; }
}
```

New controller `ProjectCeres/Controllers/Api/ReauthController.cs`:

```csharp
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/reauth")]
[Authorize]  // global fallback already enforces this; explicit for clarity
public sealed class ReauthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly TotpReplayGuard _replayGuard;

    public ReauthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        TotpReplayGuard replayGuard)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _replayGuard = replayGuard;
    }

    [HttpPost]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthReauthByUser)]
    public async Task<IActionResult> Reauth([FromBody] ReauthRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return UnauthorizedEnvelope("UNAUTHENTICATED", "Authentication required.");

        if (await _userManager.IsLockedOutAsync(user))
            return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TotpCode))
                return UnprocessableEntity(new { error = new {
                    code = "VALIDATION_ERROR", message = "TOTP code required.",
                    details = new[] { new { field = "totpCode", message = "TOTP code is required for accounts with MFA enabled." } }
                }});

            if (!MfaConstants.TotpCodeShape.IsMatch(request.TotpCode))
                return UnauthorizedEnvelope("INVALID_REAUTH", "The verification code is invalid or expired.");

            var ok = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, request.TotpCode);
            if (!ok)
                return UnauthorizedEnvelope("INVALID_REAUTH", "The verification code is invalid or expired.");

            var accepted = await _replayGuard.TryAcceptAsync(user.Id, request.TotpCode, HttpContext.RequestAborted);
            if (!accepted)
                return UnauthorizedEnvelope("INVALID_REAUTH", "The verification code is invalid or expired.");
        }
        else
        {
            if (string.IsNullOrEmpty(request.Password))
                return UnprocessableEntity(new { error = new {
                    code = "VALIDATION_ERROR", message = "Password required.",
                    details = new[] { new { field = "password", message = "Password is required for accounts without MFA enabled." } }
                }});

            var ok = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!ok)
            {
                await _userManager.AccessFailedAsync(user);
                if (await _userManager.IsLockedOutAsync(user))
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
                return UnauthorizedEnvelope("INVALID_REAUTH", "Password is incorrect.");
            }
        }

        // Successful step-up: reset failed-access counter, stamp the freshness claim, refresh the cookie.
        await _userManager.ResetAccessFailedCountAsync(user);
        HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        await _signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    private IActionResult UnauthorizedEnvelope(string code, string message)
        => Unauthorized(new { error = new { code, message } });
}
```

`RefreshSignInAsync` re-runs the claims pipeline; the factory picks up `LastReauthAtItemKey` and writes the new claim into the regenerated cookie.

### 2.8 Per-user rate-limit policy

Append to `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs`:

```csharp
/// <summary>10/min/user sliding window keyed off authenticated NameIdentifier claim.
/// Applied to POST /api/auth/reauth. Stage 6c.2.</summary>
public const string AuthReauthByUser = "auth-reauth-by-user";
```

Register in `Program.cs` next to `AuthMfaByUser`:

```csharp
options.AddPolicy(AuthRateLimitPolicies.AuthReauthByUser, httpContext =>
{
    var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
              ?? "anonymous-reauth";
    return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 10,
        Window = TimeSpan.FromSeconds(60),
        SegmentsPerWindow = 4,
        QueueLimit = 0,
    });
});
```

`RateLimitedAuthTestWebApplicationFactory` must also register this policy when removing/replacing the no-op (mirroring how `AuthMfaByUser` is handled) — search the file for `AuthMfaByUser` and add an analogous block.

### 2.9 Retroactive gating

In `MfaController` add `[RequireRecentAuth]` to the three actions:

```csharp
[HttpPost("enroll")]
[RequireRecentAuth]
[EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
public async Task<IActionResult> Enroll(...)

[HttpPost("enroll/verify")]
[RequireRecentAuth]
[EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
public async Task<IActionResult> EnrollVerify(...)

[HttpPost("backup-codes/regenerate")]
[RequireRecentAuth]
[EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
public async Task<IActionResult> RegenerateBackupCodes([FromServices] MfaBackupCodeService backupCodes)
```

Drop the `[FromBody] RegenerateBackupCodesRequest request` parameter, the in-body TOTP-shape validation block, and the in-body `VerifyTwoFactorTokenAsync` block. These are now redundant — the reauth gate replaces them.

Delete `ProjectCeres/ViewModels/Auth/RegenerateBackupCodesRequest.cs`.

Update `MfaController`'s constructor / fields if the request DTO was the only consumer of any using directive.

### 2.10 DI wiring

In `Program.cs` `AddAuthorization` block, add the policy registration:

```csharp
options.AddPolicy(RequireRecentAuthAttribute.PolicyName, p => p.AddRequirements(new RecentAuthRequirement()));
```

Service registrations (alongside other singletons in the auth section):

```csharp
builder.Services.AddSingleton<IAuthorizationHandler, RecentAuthRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, RecentAuthMiddlewareResultHandler>();
```

The default `AuthorizationMiddlewareResultHandler` is registered by the framework; replacing it is supported via `AddSingleton<IAuthorizationMiddlewareResultHandler, ...>` (this overrides the default registration).

---

## 3. Data flow

### 3.1 Login stamps `LastReauthAt` (no-MFA)

1. POST `/api/auth/login` `{email, password, rememberMe}`.
2. Existing 6a flow validates credentials inside the per-user `_loginLocks` semaphore.
3. **NEW:** before `_signInManager.PasswordSignInAsync`, set:
   ```csharp
   HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
       DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
   ```
4. `PasswordSignInAsync` → Identity calls `SignInAsync` internally → `ApplicationUserClaimsPrincipalFactory.GenerateClaimsAsync` runs → factory copies the item into a `LastReauthAt` claim on the identity → cookie is written with both `sid` and `LastReauthAt`.
5. Response: 204 + `__Host-Session` cookie. Claim is now fresh.

### 3.2 Login/totp stamps `LastReauthAt` (MFA branch)

`AuthController.LoginTotp` already calls `_signInManager.SignInAsync(user, isPersistent: false)` on both the TOTP-success branch and the backup-code-success branch. Set `HttpContext.Items[LastReauthAtItemKey]` immediately before each `SignInAsync`.

### 3.3 Step-up via `/api/auth/reauth`

See § 2.7. The `RefreshSignInAsync` call rewrites the cookie with the updated claim. No DB writes (apart from `AccessFailedAsync` / `ResetAccessFailedCountAsync` on the password branch) and no semaphore.

### 3.4 Gate firing

1. Authenticated request hits `[RequireRecentAuth]` action.
2. `AuthorizationMiddleware` evaluates the `"RecentAuth"` policy.
3. `RecentAuthRequirementHandler.HandleRequirementAsync` runs:
   - claim missing → fail
   - claim malformed → fail
   - claim age `< 0` (future) → fail
   - claim age `<= 300s` → `context.Succeed`
   - claim age `> 300s` → fail
4. On fail, `RecentAuthMiddlewareResultHandler.HandleAsync` runs. Detects `RecentAuthRequirement` in `FailedRequirements` → emits 401 with `REAUTH_REQUIRED` envelope and short-circuits.
5. On success, the action runs.

### 3.5 Error envelope mapping

| Outcome | Status | Body |
|---|---|---|
| Reauth success | 204 | (none) |
| Reauth — wrong password | 401 | `{ error: { code: "INVALID_REAUTH", message: "Password is incorrect." } }` |
| Reauth — wrong TOTP / replayed TOTP / backup-code-shape | 401 | `{ error: { code: "INVALID_REAUTH", message: "The verification code is invalid or expired." } }` |
| Reauth — account locked | 401 | `{ error: { code: "ACCOUNT_LOCKED_OUT", message: "..." } }` (existing code) |
| Reauth — body missing required field | 422 | `VALIDATION_ERROR` with `details: [{ field, message }]` |
| Reauth — rate limited | 429 | `RATE_LIMITED` (existing global handler) |
| Gate fired (claim missing/stale/future/malformed) | 401 | `{ error: { code: "REAUTH_REQUIRED", message: "Please confirm your identity to continue." } }` |
| Unauthenticated request to gated action | 401 | (existing path — empty body, no envelope) |

Two new canonical error codes for `api-contract.md`: `INVALID_REAUTH` and `REAUTH_REQUIRED`.

---

## 4. Edge cases

### 4.1 Claim-state edges

- **No claim at all** (legacy session predating 6.13): treated as missing → 401 `REAUTH_REQUIRED`. Migration story is "users hit the gate on first sensitive action; step up; continue."
- **Malformed claim** (non-numeric): treated as missing.
- **Future claim** (`now - claim < 0`): rejected (Identity HMAC prevents forgery in practice; this is belt-and-suspenders).
- **Boundary claim** (`now - claim == 300`): inclusive — succeeds. Tested.
- **Claim from another user** (impossible — auth cookie HMAC binds claim set to user): not a real edge case.

### 4.2 Reauth-flow edges

- **Both fields submitted**: ignore the irrelevant one based on `user.TwoFactorEnabled`. Defensive accept.
- **Neither field submitted**: 422 with `details: [{field, message}]` for the relevant field.
- **Empty/whitespace password**: `CheckPasswordAsync` returns false → wrong-password path → `AccessFailedAsync` + 401.
- **Wrong password 11×**: per-user 10/min limit returns 429 on the 11th. The 10th still counts toward `AccessFailedCount`; if the running count crosses the lockout threshold, the next reauth returns `ACCOUNT_LOCKED_OUT`.
- **Account already locked at reauth time**: explicit `IsLockedOutAsync` pre-check returns `ACCOUNT_LOCKED_OUT` before validating credentials.
- **MFA-enabled user submits password instead of TOTP**: 422 missing-required-field on `totpCode`.
- **Backup code in `totpCode`**: shape regex rejects → 401 `INVALID_REAUTH`. Pin ADR-0069 scope.
- **TOTP code already used in `/login/totp` within 30s**: `TotpReplayGuard.TryAcceptAsync` rejects → 401 `INVALID_REAUTH`. Same shared guard.
- **MFA toggled mid-flow** (user enables/disables MFA in another tab): `user.TwoFactorEnabled` is read at reauth-call time, not at dialog-open time. Server uses the live value.
- **Successful reauth while account had failures**: `ResetAccessFailedCountAsync` is called on success. A failed-then-successful step-up shouldn't carry forward the failed count.

### 4.3 RefreshSignInAsync edges

- **Persistent cookie unaffected**: `RefreshSignInAsync` only re-issues `__Host-Session`. The `__Host-Persist` row is untouched. Tested explicitly.
- **In-flight requests on the same session**: stateless; in-flight requests carry the old claim set, complete normally. Subsequent requests pick up the new cookie.
- **MFA-pending state** (only `Identity.TwoFactorUserId` cookie set): `[Authorize]` rejects with 401 because the application-cookie scheme isn't authenticated. Reauth handler never runs.

### 4.4 Login → claim-stamping edges

- **Login fails before stamp**: nothing to stamp; no cookie issued.
- **Backup-code login at `/login/totp` stamps the claim**: per ADR-0069, backup codes are a recovery path, not a downgraded-trust login. After successful backup-code login the user should be able to do a sensitive operation immediately. Pinned in test #5.

### 4.5 Gate-firing edges

- **`[RequireRecentAuth]` on `[AllowAnonymous]` action**: forbidden. `AttributeUsage.AttributeTargets.Method` doesn't enforce this; an architecture test does (test #28).
- **Gate firing while another middleware also fails authorization**: e.g., the global session-revocation validator. If the cookie is invalid, `Challenged = true` and the result handler delegates to the default handler (returns the unauthenticated 401). The reauth envelope is reserved for `Forbidden + RecentAuthRequirement`.
- **Three currently-shipped MFA endpoints retroactively gated**: existing tests that hit them happen fast after login (within 5 min), so the claim is fresh. Tests on `RegenerateBackupCodes` that previously sent `{totpCode}` must drop that field. Rate-limit tests that burn a bucket of 11 calls complete well under 5 min.

### 4.6 Test-fixture edges

- **`RegisterUserAsync` doesn't login via HTTP** — it creates the user via `UserManager.CreateAsync` and confirms email. Tests that hit gated endpoints must perform a real HTTP login afterward to acquire a session cookie with the freshness claim.
- **Action item:** add `AuthTestFixture.LoginViaHttpAsync(factory, client, email, password)` that POSTs `/api/auth/login` and returns the `__Host-Session` cookie value. Several existing MFA test files have private `LoginAndGetSessionCookieAsync` helpers; they may migrate to the shared one in a future cleanup pass — not required for this stage. The new shared helper is consumed by the new reauth tests.

### 4.7 Cross-feature edges

- **User deleted mid-session**: `_userManager.GetUserAsync(User)` returns null at reauth → 401 `UNAUTHENTICATED`.
- **Session revoked**: `SessionRevocationValidator` (already wired via `OnValidatePrincipal`) rejects the cookie before reauth runs. 401 (no envelope).
- **Concurrent reauth from two tabs**: both succeed independently. Whichever response lands last wins on the cookie; both write equal-or-newer timestamps so the freshness window only ever extends.

---

## 5. Tests

Tests live in `ProjectCeres.Tests/Integration/Authentication/` (flat layout). No new collection needed for the gate/endpoint tests — `[Collection("IntegrationTests")]` is fine. The rate-limit test file must use `[Collection("RateLimitTests")]` and `IClassFixture<RateLimitedAuthTestWebApplicationFactory>`.

### 5.1 Test discipline (codified, non-negotiable)

1. No false-positive shortcuts. Diagnose root causes; do not relax assertions or `Skip`.
2. Strict `MockBehavior.Strict` for `IEmailService` (only relevant in cross-feature tests; reauth itself doesn't send email).
3. Hang prevention: every async test uses `var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token` for any DB call. Project-wide `--blame-hang-timeout 120s`.
4. No new IClock abstraction. Time-based tests construct claims with adjusted timestamps directly.
5. **Tests that need a non-fresh `LastReauthAt` claim** (#17 missing, #18 stale, #19 boundary, #20 future, #21 malformed) cannot wait real wall-clock time — sleeping 5+ minutes per test would blow the suite. The implementation MUST mint an auth cookie with a controlled `LastReauthAt` claim value via a new test helper. The mechanism: build a `ClaimsPrincipal` for the user with the desired `LastReauthAt` claim value, then use the existing `IDataProtectionProvider` (registered by ASP.NET Core when the auth cookie is configured) to encrypt a synthetic ticket with the same purpose strings the cookie middleware uses (`"Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware"`, `IdentityConstants.ApplicationScheme`, `"v2"`). Inject the resulting cookie value directly into the test request's `Cookie` header. This is the same technique many ASP.NET integration-test suites use; the plan task investigates and surfaces a blocker if the purpose strings or ticket-format change in this .NET version. **No real-time sleeps for time-based tests.**

### 5.2 Test list (32 tests)

#### Reauth endpoint — happy paths (5)

1. `Reauth_with_correct_password_no_mfa_returns_204_and_stamps_claim` — POST with `{password}`, assert 204, assert subsequent gated request succeeds.
2. `Reauth_with_correct_totp_mfa_returns_204_and_stamps_claim` — same with TOTP.
3. `Login_stamps_LastReauthAt_claim` — fresh login → gated endpoint (no separate reauth) → success.
4. `LoginTotp_stamps_LastReauthAt_claim` — same on MFA login path.
5. `LoginTotp_with_backup_code_also_stamps_LastReauthAt` — backup-code login → gated endpoint → success.

#### Reauth endpoint — failure paths (7)

6. `Reauth_with_wrong_password_returns_401_INVALID_REAUTH_and_increments_AccessFailedCount`.
7. `Reauth_with_wrong_password_can_lock_account_after_10_attempts`.
8. `Reauth_with_wrong_totp_returns_401_INVALID_REAUTH_and_does_not_increment_AccessFailedCount`.
9. `Reauth_with_replayed_totp_returns_401_INVALID_REAUTH`.
10. `Reauth_with_backup_code_in_totpCode_returns_401_INVALID_REAUTH`.
11. `Reauth_when_account_already_locked_returns_401_ACCOUNT_LOCKED_OUT`.
12. `Reauth_with_missing_required_field_returns_422_VALIDATION_ERROR`.

#### Reauth endpoint — defensive accept (1)

13. `Reauth_ignores_extraneous_field_based_on_user_MFA_state`.

#### Reauth endpoint — rate limit (2, in `RateLimitTests` collection)

14. `Reauth_per_user_limit_returns_429_at_11th_attempt`.
15. `Reauth_per_user_partition_isolates_users`.

#### Gate behaviour (8)

16. `Gated_endpoint_with_fresh_claim_succeeds`.
17. `Gated_endpoint_with_no_claim_returns_401_REAUTH_REQUIRED` (legacy-cookie sim — see §5.1.5).
18. `Gated_endpoint_with_stale_claim_returns_401_REAUTH_REQUIRED` (claim age = 301s).
19. `Gated_endpoint_with_claim_at_boundary_succeeds` (claim age = 300s exactly).
20. `Gated_endpoint_with_future_claim_returns_401_REAUTH_REQUIRED` (claim age = -60s).
21. `Gated_endpoint_with_malformed_claim_returns_401_REAUTH_REQUIRED`.
22. `Gated_endpoint_unauthenticated_request_returns_401_with_default_handler_not_REAUTH_REQUIRED`.
23. `Gated_endpoint_after_successful_reauth_succeeds`.

#### Retroactive gating (3)

24. `MfaRegenerateBackupCodes_now_requires_recent_auth_not_in_body_totp` — assert: with stale claim → 401 REAUTH_REQUIRED. After step-up, retry with empty body (no `totpCode` field) → success.
25. `MfaEnroll_now_requires_recent_auth`.
26. `MfaEnrollVerify_now_requires_recent_auth`.

#### Architecture (3)

27. `RequireRecentAuth_attribute_is_only_on_action_methods_not_classes` — reflection over `ProjectCeres.Controllers.Api.*` types.
28. `RequireRecentAuth_attribute_is_never_combined_with_AllowAnonymous` — every method carrying `RequireRecentAuth` must NOT also carry `AllowAnonymous`.
29. `Three_existing_MFA_endpoints_carry_RequireRecentAuth_attribute` — explicit listing of the three protected actions.

#### Cross-feature regressions (3)

30. `Existing_login_flow_still_works_with_LastReauthAt_added` — full login flow returns a session cookie that can hit a non-gated endpoint successfully.
31. `RefreshSignInAsync_after_reauth_does_not_disrupt_persistent_cookie`.
32. `Reauth_when_user_is_deleted_returns_401_UNAUTHENTICATED`.

**Total: 32 tests.**

---

## 6. Sequencing

1. `SessionConstants` — add `LastReauthAtClaim` and `LastReauthAtItemKey`.
2. `ApplicationUserClaimsPrincipalFactory` — copy item to claim.
3. `RecentAuthRequirement` + handler.
4. `RequireRecentAuthAttribute`.
5. `RecentAuthMiddlewareResultHandler`.
6. `Program.cs` — register policy, requirement handler, result handler.
7. `AuthRateLimitPolicies.AuthReauthByUser` constant + `Program.cs` policy registration + `RateLimitedAuthTestWebApplicationFactory` re-registration.
8. `ReauthRequest` DTO + `ReauthController`.
9. `AuthController.Login` + `AuthController.LoginTotp` — stamp `LastReauthAt`.
10. `MfaController` — apply `[RequireRecentAuth]` to three actions; drop `RegenerateBackupCodesRequest` parameter.
11. Delete `RegenerateBackupCodesRequest.cs`.
12. `AuthTestFixture.LoginViaHttpAsync` helper.
13. Tests added incrementally per section 5.2.
14. Update existing MFA tests that pass `{totpCode}` to `/backup-codes/regenerate` — drop the field, ensure they login fresh.
15. Doc sync — api-contract.md, security-model.md, roadmap, planning-resolved.md, planning-phase3.md (mark deferred-decisions item #1 resolved).

---

## 7. Files

### Created

- `ProjectCeres/Common/Authentication/RecentAuthRequirement.cs`
- `ProjectCeres/Common/Authentication/RecentAuthRequirementHandler.cs`
- `ProjectCeres/Common/Authentication/RequireRecentAuthAttribute.cs`
- `ProjectCeres/Common/Authentication/RecentAuthMiddlewareResultHandler.cs`
- `ProjectCeres/Controllers/Api/ReauthController.cs`
- `ProjectCeres/ViewModels/Auth/ReauthRequest.cs`
- 5 new test files under `ProjectCeres.Tests/Integration/Authentication/`:
  - `ReauthEndpointTests.cs` (sections 5.2 reauth happy/failure/defensive — 13 tests)
  - `ReauthRateLimitTests.cs` (section 5.2 rate limit — 2 tests, `[Collection("RateLimitTests")]`)
  - `ReauthGateTests.cs` (section 5.2 gate behaviour — 8 tests)
  - `ReauthMfaEndpointGatingTests.cs` (section 5.2 retroactive — 3 tests)
  - `ReauthCrossFeatureTests.cs` (section 5.2 regressions — 3 tests)
- Architecture tests (3) appended to existing `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`.

### Modified

- `ProjectCeres/Common/Authentication/SessionConstants.cs` — 2 new constants.
- `ProjectCeres/Common/Authentication/ApplicationUserClaimsPrincipalFactory.cs` — copy item to claim.
- `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs` — `AuthReauthByUser`.
- `ProjectCeres/Controllers/Api/AuthController.cs` — stamp `LastReauthAt` in login + login/totp.
- `ProjectCeres/Controllers/Api/MfaController.cs` — `[RequireRecentAuth]` on 3 actions; drop in-body TOTP from regenerate.
- `ProjectCeres/Program.cs` — policy + handler + result-handler + rate-limit registrations.
- `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs` — `LoginViaHttpAsync` helper, plus `MintAuthCookieWithClaim` if needed for tests #17/#20/#21.
- `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs` — register `AuthReauthByUser` policy.
- Existing tests on the three retroactively-gated endpoints — drop the in-body `TotpCode` for regenerate tests.
- Doc files per step 15.

### Deleted

- `ProjectCeres/ViewModels/Auth/RegenerateBackupCodesRequest.cs` — empty after dropping `TotpCode`.

---

## 8. What this sub-stage does NOT cover

- Email-address-change flow (6.12) — declares the attribute when shipped.
- Audit-log writes (6.14) — sensitive events log to the auditlog from 6.14 onwards.
- SPA dialog UI for the step-up — Stage 9.
- New-device email-link step-up for non-MFA accounts — Stage 8 dependency.
- Migrating existing MFA-test private `LoginAndGetSessionCookieAsync` helpers to the shared `AuthTestFixture.LoginViaHttpAsync` — left for a future cleanup pass.

## 9. Open items deferred to plan stage

- Confirm the data-protection ticket format works in this .NET version (purpose strings + ticket protocol). If the framework path produces an opaque cookie that the middleware doesn't accept, fall back to performing a real login then intercepting the response Set-Cookie header, decrypting via `IDataProtectionProvider`, mutating the `LastReauthAt` claim in the deserialized ticket, re-encrypting, and feeding the result back into subsequent test requests. **Sleep-based tests are explicitly forbidden** — a 301-second sleep per test would cripple the suite.
