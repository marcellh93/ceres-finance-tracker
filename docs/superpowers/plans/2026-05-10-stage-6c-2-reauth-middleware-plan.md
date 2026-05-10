# Stage 6c Sub-stage 6.13 — Reauth Middleware Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the reauthentication-for-sensitive-operations gate per `docs/superpowers/specs/2026-05-10-reauth-middleware-design.md` — `LastReauthAt` claim + `[RequireRecentAuth]` attribute + `/api/auth/reauth` endpoint + retroactive gating of three MFA endpoints + 32 ship-gate tests.

**Architecture:** New `RecentAuthRequirement` + `RecentAuthRequirementHandler` register as the `"RecentAuth"` authorization policy. New `[RequireRecentAuth]` attribute marks gated actions. Custom `IAuthorizationMiddlewareResultHandler` emits the `REAUTH_REQUIRED` envelope for `Forbidden + RecentAuthRequirement` failures, delegating other failures to the framework default. Login + login/totp + the new `/api/auth/reauth` endpoint stamp `HttpContext.Items[LastReauthAtItemKey]`; `ApplicationUserClaimsPrincipalFactory` copies it into the cookie's `LastReauthAt` claim. Retroactive gates on `/mfa/enroll`, `/mfa/enroll/verify`, `/mfa/backup-codes/regenerate` replace the in-body TOTP-code stopgap (Stage 6b.3 Gap 4).

**Tech Stack:** .NET 10, ASP.NET Core, Microsoft.AspNetCore.Identity, ASP.NET Core authorization (`AuthorizationHandler<TRequirement>`, `IAuthorizationMiddlewareResultHandler`), `IDataProtectionProvider` for test cookie minting, xUnit + FluentAssertions + Moq.

---

## File structure

### Files created

| Path | Responsibility |
|---|---|
| `ProjectCeres/Common/Authentication/RecentAuthRequirement.cs` | Marker requirement type with the 5-min `Window` constant |
| `ProjectCeres/Common/Authentication/RecentAuthRequirementHandler.cs` | Reads claim, checks freshness, calls `Succeed` |
| `ProjectCeres/Common/Authentication/RequireRecentAuthAttribute.cs` | `AuthorizeAttribute` subclass with `Policy = "RecentAuth"`, action-only |
| `ProjectCeres/Common/Authentication/RecentAuthMiddlewareResultHandler.cs` | Custom result handler emitting the `REAUTH_REQUIRED` envelope |
| `ProjectCeres/ViewModels/Auth/ReauthRequest.cs` | `{Password?, TotpCode?}` DTO |
| `ProjectCeres/Controllers/Api/ReauthController.cs` | `POST /api/auth/reauth` |
| `ProjectCeres.Tests/Integration/Authentication/ReauthEndpointTests.cs` | Tests #1–#13 (happy, failure, defensive accept) |
| `ProjectCeres.Tests/Integration/Authentication/ReauthRateLimitTests.cs` | Tests #14–#15 (rate limit) — `[Collection("RateLimitTests")]` |
| `ProjectCeres.Tests/Integration/Authentication/ReauthGateTests.cs` | Tests #16–#23 (gate behaviour) |
| `ProjectCeres.Tests/Integration/Authentication/ReauthMfaEndpointGatingTests.cs` | Tests #24–#26 (retroactive gating) |
| `ProjectCeres.Tests/Integration/Authentication/ReauthCrossFeatureTests.cs` | Tests #30–#32 (regression) |

### Files modified

| Path | Why |
|---|---|
| `ProjectCeres/Common/Authentication/SessionConstants.cs` | Add `LastReauthAtClaim` and `LastReauthAtItemKey` |
| `ProjectCeres/Common/Authentication/ApplicationUserClaimsPrincipalFactory.cs` | Copy item to claim |
| `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs` | Add `AuthReauthByUser` constant |
| `ProjectCeres/Controllers/Api/AuthController.cs` | Stamp `LastReauthAt` in login (line 121-area) + login/totp (line 242 + 273 areas) |
| `ProjectCeres/Controllers/Api/MfaController.cs` | `[RequireRecentAuth]` on 3 actions; drop in-body TOTP from regenerate |
| `ProjectCeres/Program.cs` | Register `"RecentAuth"` policy + handler + result-handler + rate-limit policy |
| `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs` | Re-register `AuthReauthByUser` |
| `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs` | Add `LoginViaHttpAsync` and `MintAuthCookieWithLastReauthAt` helpers; add tests #27–#29 to existing `ArchitectureTests.cs` |
| `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaRegenerateTests.cs` | Drop `{ totpCode }` body field; cover the new `[RequireRecentAuth]` contract via a fresh login |
| `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaCacheControlTests.cs` | Same — drop `totpCode` from regenerate body |
| `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaRegenerateRateLimitTests.cs` | Same — drop `totpCode` from regenerate body |
| `docs/api-contract.md` | Add `INVALID_REAUTH`, `REAUTH_REQUIRED` codes; add `POST /api/auth/reauth` to auth endpoints; update `/mfa/backup-codes/regenerate` row to drop the in-body `{totpCode}` (replaced by `[RequireRecentAuth]`) |
| `docs/security-model.md` | Stage 6c.2 banner; flip § Login → Reauthentication wording from "deferred to 6c" to "shipped" |
| `docs/roadmap-phase-three.md` | Stage 6c.2 banner; flip 3 reauth-related verification items |
| `docs/planning-phase3.md` | Mark Stage 6b.3 deferred-decisions item #1 resolved |
| `docs/planning-resolved.md` | Append entry |

### Files deleted

- `ProjectCeres/ViewModels/Auth/RegenerateBackupCodesRequest.cs` — empty after the field is dropped; the controller action no longer takes a body parameter.

---

## Task 1: Add `LastReauthAtClaim` + `LastReauthAtItemKey` constants

**Files:**
- Modify: `ProjectCeres/Common/Authentication/SessionConstants.cs`

- [ ] **Step 1: Append constants**

Open `ProjectCeres/Common/Authentication/SessionConstants.cs`. After the existing `PendingSessionItemKey` constant, add:

```csharp
public const string LastReauthAtClaim = "last_reauth_at";

/// <summary>HttpContext.Items key used by the login + reauth flows to pass the
/// freshness Unix-seconds string into ApplicationUserClaimsPrincipalFactory.</summary>
public const string LastReauthAtItemKey = "LastReauthAt";
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/SessionConstants.cs
git -C <repo> commit -m "feat(auth): add LastReauthAt claim + item key constants (Stage 6c.2)"
```

---

## Task 2: Extend `ApplicationUserClaimsPrincipalFactory` to stamp the claim

**Files:**
- Modify: `ProjectCeres/Common/Authentication/ApplicationUserClaimsPrincipalFactory.cs`

- [ ] **Step 1: Update `GenerateClaimsAsync`**

Open `ApplicationUserClaimsPrincipalFactory.cs`. Locate the existing block that copies `PendingSessionItemKey` into the `sid` claim (currently lines 30–40). Append a sibling block that copies `LastReauthAtItemKey` into the `LastReauthAt` claim:

```csharp
protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
{
    var identity = await base.GenerateClaimsAsync(user);

    var sessionId = _http.HttpContext?.Items[SessionConstants.PendingSessionItemKey] as Guid?;
    if (sessionId is { } sid)
    {
        identity.AddClaim(new Claim(SessionConstants.SessionIdClaim, sid.ToString()));
    }

    if (_http.HttpContext?.Items[SessionConstants.LastReauthAtItemKey] is string ts && ts.Length > 0)
    {
        identity.AddClaim(new Claim(SessionConstants.LastReauthAtClaim, ts));
    }

    return identity;
}
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/ApplicationUserClaimsPrincipalFactory.cs
git -C <repo> commit -m "feat(auth): claims factory copies LastReauthAt item to claim (Stage 6c.2)"
```

---

## Task 3: `RecentAuthRequirement` + handler

**Files:**
- Create: `ProjectCeres/Common/Authentication/RecentAuthRequirement.cs`
- Create: `ProjectCeres/Common/Authentication/RecentAuthRequirementHandler.cs`

- [ ] **Step 1: Write `RecentAuthRequirement`**

```csharp
using Microsoft.AspNetCore.Authorization;

namespace ProjectCeres.Common.Authentication;

public sealed class RecentAuthRequirement : IAuthorizationRequirement
{
    /// <summary>5-minute freshness window per security-model.md § Login → Reauthentication.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
}
```

- [ ] **Step 2: Write the handler**

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
            return Task.CompletedTask;

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var claimUnix))
            return Task.CompletedTask;

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ageSeconds = nowUnix - claimUnix;

        // Reject future claims (negative age). Boundary inclusive — a claim age == window passes.
        if (ageSeconds < 0) return Task.CompletedTask;
        if (ageSeconds <= (long)RecentAuthRequirement.Window.TotalSeconds)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/RecentAuthRequirement.cs ProjectCeres/Common/Authentication/RecentAuthRequirementHandler.cs
git -C <repo> commit -m "feat(auth): RecentAuth requirement + handler (Stage 6c.2)"
```

---

## Task 4: `[RequireRecentAuth]` attribute

**Files:**
- Create: `ProjectCeres/Common/Authentication/RequireRecentAuthAttribute.cs`

- [ ] **Step 1: Write the attribute**

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

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/RequireRecentAuthAttribute.cs
git -C <repo> commit -m "feat(auth): add RequireRecentAuth attribute (Stage 6c.2)"
```

---

## Task 5: Custom `IAuthorizationMiddlewareResultHandler`

**Files:**
- Create: `ProjectCeres/Common/Authentication/RecentAuthMiddlewareResultHandler.cs`

- [ ] **Step 1: Write the handler**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Wraps the framework default authorization middleware result handler. When authorization
/// fails specifically because of RecentAuthRequirement, emits 401 with the
/// REAUTH_REQUIRED envelope. All other failures (e.g. unauthenticated requests against
/// the global RequireAuthenticatedUser fallback) delegate to the default handler so the
/// existing OnRedirectToLogin → 401 path is preserved.
/// </summary>
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

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/RecentAuthMiddlewareResultHandler.cs
git -C <repo> commit -m "feat(auth): RecentAuth middleware result handler (Stage 6c.2)"
```

---

## Task 6: Add `AuthReauthByUser` rate-limit constant

**Files:**
- Modify: `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs`

- [ ] **Step 1: Append constant**

After the existing `AnonymousPasswordResetPartition` constant, add:

```csharp
/// <summary>10/min/user sliding window keyed off authenticated NameIdentifier claim.
/// Applied to POST /api/auth/reauth. Stage 6c.2.</summary>
public const string AuthReauthByUser = "auth-reauth-by-user";
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs
git -C <repo> commit -m "feat(auth): add AuthReauthByUser rate-limit constant (Stage 6c.2)"
```

---

## Task 7: `ReauthRequest` DTO

**Files:**
- Create: `ProjectCeres/ViewModels/Auth/ReauthRequest.cs`

- [ ] **Step 1: Write the DTO**

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class ReauthRequest
{
    /// <summary>Plain-text password. Required when the user does NOT have MFA enabled;
    /// ignored otherwise.</summary>
    [StringLength(128)]
    public string? Password { get; set; }

    /// <summary>TOTP code. Required when the user has TwoFactorEnabled = true;
    /// ignored otherwise. Backup codes are NOT accepted at reauth (recovery path is
    /// /login/totp). Permissive bound (32 chars) so service-side
    /// MfaConstants.TotpCodeShape rejects backup-code-shaped submissions with 401.</summary>
    [StringLength(32)]
    public string? TotpCode { get; set; }
}
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/ViewModels/Auth/ReauthRequest.cs
git -C <repo> commit -m "feat(auth): add ReauthRequest DTO (Stage 6c.2)"
```

---

## Task 8: `ReauthController`

**Files:**
- Create: `ProjectCeres/Controllers/Api/ReauthController.cs`

- [ ] **Step 1: Write the controller**

```csharp
using System.Globalization;
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
[Authorize]
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
                    code = "VALIDATION_ERROR",
                    message = "TOTP code required.",
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
                    code = "VALIDATION_ERROR",
                    message = "Password required.",
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

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/Controllers/Api/ReauthController.cs
git -C <repo> commit -m "feat(auth): ReauthController (Stage 6c.2)"
```

---

## Task 9: Wire DI registrations + rate-limit policy in `Program.cs`

**Files:**
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Update `AddAuthorization` block**

Locate the existing `builder.Services.AddAuthorization(options => { ... })` block (around line 185). Inside the lambda, after the existing `FallbackPolicy` assignment, add:

```csharp
options.AddPolicy(RequireRecentAuthAttribute.PolicyName, p =>
    p.AddRequirements(new RecentAuthRequirement()));
```

The block becomes:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy(RequireRecentAuthAttribute.PolicyName, p =>
        p.AddRequirements(new RecentAuthRequirement()));
});
```

- [ ] **Step 2: Register the requirement handler + custom result handler**

Find the section that registers other auth services (e.g., `PersistentTokenService`, `Argon2idPasswordHasher`). After the existing registrations, add:

```csharp
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, RecentAuthRequirementHandler>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.Policy.IAuthorizationMiddlewareResultHandler, RecentAuthMiddlewareResultHandler>();
```

- [ ] **Step 3: Register the `AuthReauthByUser` rate-limit policy**

Inside the existing `builder.Services.AddRateLimiter(options => { ... })` block, after the `AuthMfaByUser` registration (around line 243), add:

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

- [ ] **Step 4: Verify project builds**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres/Program.cs
git -C <repo> commit -m "feat(auth): register RecentAuth policy + handlers + reauth rate-limit (Stage 6c.2)"
```

---

## Task 10: Stamp `LastReauthAt` in login flows

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`

- [ ] **Step 1: Stamp in `Login` (no-MFA path)**

In `AuthController.Login`, locate the line:

```csharp
HttpContext.Items[SessionConstants.PendingSessionItemKey] = sessionId;
```

(currently around line 109). Add the freshness stamp on the following line:

```csharp
HttpContext.Items[SessionConstants.PendingSessionItemKey] = sessionId;
HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
```

- [ ] **Step 2: Stamp in `LoginTotp` (TOTP success branch)**

In `AuthController.LoginTotp`, locate the line that comes BEFORE `await _signInManager.SignInAsync(user, isPersistent: false);` on the **TOTP success branch** (currently around line 242, after `if (user.LockoutEnd.HasValue) { ... }`):

```csharp
await _signInManager.SignInAsync(user, isPersistent: false);
```

Add the stamp immediately above:

```csharp
HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
await _signInManager.SignInAsync(user, isPersistent: false);
```

- [ ] **Step 3: Stamp in `LoginTotp` (backup-code success branch)**

Same file, locate the SECOND occurrence of `await _signInManager.SignInAsync(user, isPersistent: false);` (currently around line 273, in the `if (MfaConstants.BackupCodeShape.IsMatch(stripped))` branch, after the lockout-clearing block):

```csharp
await _signInManager.SignInAsync(user, isPersistent: false);
```

Add the same stamp immediately above:

```csharp
HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
await _signInManager.SignInAsync(user, isPersistent: false);
```

- [ ] **Step 4: Verify project builds**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 5: Run the full Authentication test folder to ensure no login regressions**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~ProjectCeres.Tests.Integration.Authentication --blame-hang-timeout 180s --nologo`

Expected: all existing auth tests pass. No regressions.

- [ ] **Step 6: Commit**

```bash
git -C <repo> add ProjectCeres/Controllers/Api/AuthController.cs
git -C <repo> commit -m "feat(auth): stamp LastReauthAt in login + login/totp success branches (Stage 6c.2)"
```

---

## Task 11: Apply `[RequireRecentAuth]` to MFA endpoints + drop in-body TOTP

**Files:**
- Modify: `ProjectCeres/Controllers/Api/MfaController.cs`
- Delete: `ProjectCeres/ViewModels/Auth/RegenerateBackupCodesRequest.cs`

- [ ] **Step 1: Apply attribute to `Enroll`**

Open `ProjectCeres/Controllers/Api/MfaController.cs`. Locate the `Enroll` action (around line 24):

```csharp
[HttpPost("enroll")]
[EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
public async Task<IActionResult> Enroll()
```

Insert `[RequireRecentAuth]` between the existing attributes:

```csharp
[HttpPost("enroll")]
[RequireRecentAuth]
[EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
public async Task<IActionResult> Enroll()
```

- [ ] **Step 2: Apply attribute to `EnrollVerify`**

Same file, locate `EnrollVerify` (around line 47):

```csharp
[HttpPost("enroll/verify")]
[EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
public async Task<IActionResult> EnrollVerify(
```

Add the attribute:

```csharp
[HttpPost("enroll/verify")]
[RequireRecentAuth]
[EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
public async Task<IActionResult> EnrollVerify(
```

- [ ] **Step 3: Rewrite `RegenerateBackupCodes` — apply attribute + drop in-body TOTP**

Same file, locate the existing `RegenerateBackupCodes` action (around line 78–101). Replace the entire action with:

```csharp
[HttpPost("backup-codes/regenerate")]
[RequireRecentAuth]
[EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
public async Task<IActionResult> RegenerateBackupCodes(
    [FromServices] MfaBackupCodeService backupCodes)
{
    var user = await GetCurrentUserAsync();
    if (user is null) return Unauthorized();

    if (!user.TwoFactorEnabled)
        return Conflict(new { error = new { code = "MFA_NOT_ENABLED", message = "MFA must be enabled to regenerate backup codes." } });

    var codes = await backupCodes.RegenerateAsync(user.Id, HttpContext.RequestAborted);

    ApplyNoStoreHeaders();
    return Ok(new { backupCodes = codes });
}
```

- [ ] **Step 4: Delete the now-unused DTO**

```bash
git -C <repo> rm ProjectCeres/ViewModels/Auth/RegenerateBackupCodesRequest.cs
```

- [ ] **Step 5: Drop the now-unused `using` from `MfaController.cs`**

If the file's `using ProjectCeres.ViewModels.Auth;` was only consumed by `RegenerateBackupCodesRequest`, check whether other DTOs in that namespace are still referenced (`EnrollVerifyRequest` is — keep the using). If `EnrollVerifyRequest` was the only other consumer, leave the using as-is.

- [ ] **Step 6: Verify project builds**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git -C <repo> add ProjectCeres/Controllers/Api/MfaController.cs ProjectCeres/ViewModels/Auth/RegenerateBackupCodesRequest.cs
git -C <repo> commit -m "feat(auth): retroactive RequireRecentAuth on MFA endpoints; drop in-body TOTP from regenerate (Stage 6c.2)"
```

---

## Task 12: Update existing MFA regenerate tests — drop in-body TOTP

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaRegenerateTests.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaCacheControlTests.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaRegenerateRateLimitTests.cs`

The existing tests pass `{ totpCode }` as the body. After Task 11 the controller no longer accepts a body. The tests still need to:
1. Login (which stamps `LastReauthAt`).
2. Enroll MFA via the existing helper.
3. POST to `/api/auth/mfa/backup-codes/regenerate` with an EMPTY body (`null` or `{}`).

The login + enroll happen synchronously well within the 5-min freshness window, so the gate passes naturally. The test that previously asserted `Regenerate_WithoutTotpCode_Returns422` becomes obsolete — replace with a regenerate-with-empty-body-succeeds test.

- [ ] **Step 1: Read `MfaRegenerateTests.cs` to identify all `totpCode` usages**

Run: `grep -n "totpCode\|regenCode\|backup-codes/regenerate" <repo>/ProjectCeres.Tests/Integration/Authentication/Mfa/MfaRegenerateTests.cs`

For each test that POSTs to `/api/auth/mfa/backup-codes/regenerate`, change the request body from `JsonContent.Create(new { totpCode = regenCode })` to `JsonContent.Create(new { })` (empty object). Drop the `regenCode = AuthTestFixture.ComputeCurrentTotpCode(seed);` line where it was only used for that body field.

For the test method `Regenerate_WithoutTotpCode_Returns422` — this test asserts the OLD contract. Rename to `Regenerate_WithoutBody_Succeeds_AfterFreshLogin` and rewrite to assert 200 (the gate is fresh because login+enroll happened seconds earlier).

- [ ] **Step 2: Apply the same body change in `MfaCacheControlTests.cs`**

Run: `grep -n "totpCode\|backup-codes/regenerate" <repo>/ProjectCeres.Tests/Integration/Authentication/Mfa/MfaCacheControlTests.cs`

Replace the regenerate body with `null` or `JsonContent.Create(new { })`.

- [ ] **Step 3: Apply the same body change in `MfaRegenerateRateLimitTests.cs`**

Run: `grep -n "totpCode\|backup-codes/regenerate" <repo>/ProjectCeres.Tests/Integration/Authentication/Mfa/MfaRegenerateRateLimitTests.cs`

Same change. The test sends 11 rapid POSTs to exhaust the bucket — drop `totpCode` from each.

- [ ] **Step 4: Run the affected test classes**

Run:
```bash
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~MfaRegenerateTests|FullyQualifiedName~MfaCacheControlTests|FullyQualifiedName~MfaRegenerateRateLimitTests" --blame-hang-timeout 120s --nologo
```

Expected: all tests pass. If a test fails because the gate fires (claim stale by the time the test reaches the regenerate call), the test is taking too long between login and POST — diagnose and fix the test setup, not the gate.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/Mfa/MfaRegenerateTests.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaCacheControlTests.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaRegenerateRateLimitTests.cs
git -C <repo> commit -m "test(auth): drop in-body TOTP from MFA regenerate tests post-RequireRecentAuth (Stage 6c.2)"
```

---

## Task 13: Re-register `AuthReauthByUser` in `RateLimitedAuthTestWebApplicationFactory`

**Files:**
- Modify: `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs`

- [ ] **Step 1: Read the file to find the `AuthMfaByUser` re-registration block**

Run: `grep -n "AuthMfaByUser\|removeFromPolicy\|opts.AddPolicy" <repo>/ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs`

The factory currently strips the production-policy registrations and re-adds them so rate-limit tests have working buckets. The list of policies removed and re-added must include `AuthReauthByUser`.

- [ ] **Step 2: Add `AuthReauthByUser` to the removal loop**

In the `foreach (var name in new[] { ... })` array that lists policies to remove, add `AuthRateLimitPolicies.AuthReauthByUser` to the array.

- [ ] **Step 3: Add the AuthReauthByUser policy re-registration**

Below the existing `AuthMfaByUser` re-registration block, add:

```csharp
opts.AddPolicy(AuthRateLimitPolicies.AuthReauthByUser, httpContext =>
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

- [ ] **Step 4: Verify build**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs
git -C <repo> commit -m "test(auth): re-register AuthReauthByUser in RateLimitedAuthTestWebApplicationFactory (Stage 6c.2)"
```

---

## Task 14: Add `LoginViaHttpAsync` + `MintAuthCookieWithLastReauthAt` test helpers

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs`

These helpers are consumed by the gate tests in Task 16. `LoginViaHttpAsync` does a real POST to `/api/auth/login` and returns the session cookie value. `MintAuthCookieWithLastReauthAt` builds a cookie with a chosen `LastReauthAt` claim value (used to simulate stale/missing/future/malformed claims without real-time waits).

- [ ] **Step 1: Add `LoginViaHttpAsync`**

Append to `AuthTestFixture`:

```csharp
/// <summary>
/// POSTs /api/auth/login with the given credentials and returns the value of the
/// __Host-Session cookie. The user must already exist (call RegisterUserAsync first).
/// Stamps LastReauthAt on the cookie via the production login flow.
/// </summary>
public static async Task<string> LoginViaHttpAsync(
    AuthTestWebApplicationFactory factory, HttpClient client, string email,
    string password = ValidPassword, bool rememberMe = false)
{
    var (cookie, header) = MintCsrf(factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new { email, password, rememberMe }),
    };
    req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={cookie}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

    var setCookies = resp.Headers.GetValues("Set-Cookie");
    var sessionCookie = setCookies.First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));
    var value = sessionCookie.Split(';')[0].Substring(SessionConstants.SessionCookieName.Length + 1);
    return value;
}
```

Add `using System.Net.Http.Json;` at the top if absent.

- [ ] **Step 2: Add `MintAuthCookieWithLastReauthAt`**

Append:

```csharp
/// <summary>
/// Builds an authentication cookie value containing a ClaimsPrincipal for the given
/// user with a chosen LastReauthAt claim value (Unix seconds, or null to omit).
/// Used by gate tests that need a stale/future/missing/malformed claim without
/// waiting real wall-clock time.
///
/// Mechanism: build a ClaimsPrincipal via the registered ApplicationUserClaimsPrincipalFactory,
/// then encrypt a TicketDataFormat-compatible payload using the same IDataProtector purpose
/// strings the cookie middleware uses. Returns the cookie value to be sent in
/// the Cookie header on subsequent requests.
/// </summary>
public static async Task<string> MintAuthCookieWithLastReauthAt(
    AuthTestWebApplicationFactory factory, ApplicationUser user, long? lastReauthAtUnix)
{
    using var scope = factory.Services.CreateScope();
    var sp = scope.ServiceProvider;

    var http = new DefaultHttpContext { RequestServices = sp };
    if (lastReauthAtUnix is { } v)
    {
        http.Items[SessionConstants.LastReauthAtItemKey] = v.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
    }
    var sid = Guid.NewGuid();
    http.Items[SessionConstants.PendingSessionItemKey] = sid;

    var factoryFromDi = sp.GetRequiredService<
        Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<ApplicationUser>>();
    // The standard factory uses IHttpContextAccessor — set it for this scope.
    var accessor = sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
    if (accessor is not null) accessor.HttpContext = http;
    var principal = await factoryFromDi.CreateAsync(user);

    var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
        principal,
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = false },
        Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);

    // Insert a UserSession row so SessionRevocationValidator doesn't reject the cookie
    // on first use (the validator checks the row exists and is not revoked).
    var db = sp.GetRequiredService<ProjectCeres.Data.AppDbContext>();
    db.UserSessions.Add(new UserSession
    {
        Id = sid,
        UserId = user.Id,
        IpCreatedAt = "127.0.0.1",
        UserAgent = "test",
        CreatedAt = DateTime.UtcNow,
        LastUsedAt = DateTime.UtcNow,
        IsPersistent = false,
    });
    await db.SaveChangesAsync();

    var dpProvider = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
    var protector = dpProvider.CreateProtector(
        "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
        Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme,
        "v2");
    var format = new Microsoft.AspNetCore.Authentication.TicketDataFormat(protector);
    return format.Protect(ticket);
}
```

The `using` block already includes `Microsoft.AspNetCore.Http`; if not, add it. Also add `using ProjectCeres.Data;` if needed.

- [ ] **Step 3: Verify build**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj --nologo -v q`
Expected: build succeeds.

If `MintAuthCookieWithLastReauthAt` fails to compile because of unresolved types, the most common causes are:
- Missing `using Microsoft.AspNetCore.DataProtection;` for `IDataProtectionProvider`.
- Missing `using Microsoft.AspNetCore.Authentication;` for `TicketDataFormat`.

Add the `using`s as needed.

- [ ] **Step 4: Sanity-check the helpers with a tiny smoke test**

Append a simple integration test temporarily to `ReauthEndpointTests.cs` (next task creates the file) that asserts `LoginViaHttpAsync` returns a non-empty cookie. This is just a smoke check — keep it as test #0 to delete in the next task. Skip if you trust the helpers.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs
git -C <repo> commit -m "test(auth): add LoginViaHttpAsync + MintAuthCookieWithLastReauthAt test helpers (Stage 6c.2)"
```

---

## Task 15: ReauthEndpointTests (13 tests — happy + failure + defensive accept)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/ReauthEndpointTests.cs`

The 13 tests covered are #1–#13 per spec § 5.2.

- [ ] **Step 1: Create the test class with a strict-mock helper and `Timeout30s`**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class ReauthEndpointTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public ReauthEndpointTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private async Task<HttpClient> ClientWithLoggedInUserAsync(string email)
    {
        await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        return WrapClientWithCookie(client, sessionCookie);
    }

    private static HttpClient WrapClientWithCookie(HttpClient client, string sessionCookie)
    {
        // Hold on to the cookie value so subsequent requests carry it. Simplest approach:
        // each test attaches the cookie + CSRF on its own per-request basis. We just
        // return the original client and let tests construct requests manually.
        return client;
    }
}
```

> **Note on cookie handling:** the test client is created with `HandleCookies = false` so each test fully controls request headers. Tests build `HttpRequestMessage` manually, attach `Cookie: __Host-Session=…; __Host-XSRF=…` and `X-XSRF-TOKEN`, and invoke `client.SendAsync`.

- [ ] **Step 2: Add tests #1 + #2 (happy paths — no MFA, MFA)**

Append to the class:

```csharp
[Fact]
public async Task Reauth_with_correct_password_no_mfa_returns_204_and_stamps_claim()
{
    var email = $"reauth-pw-{Guid.NewGuid():N}@example.com";
    await AuthTestFixture.RegisterUserAsync(_factory, email);
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
    };
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
}

[Fact]
public async Task Reauth_with_correct_totp_mfa_returns_204_and_stamps_claim()
{
    var email = $"reauth-totp-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
    // After login (no MFA challenge — login doesn't prompt for TOTP because we already enrolled
    // and login returns 204 without TOTP since this is a seed-test; if PasswordSignInAsync
    // routes through RequiresTwoFactor, complete the second step too):
    // For safety, enroll BEFORE login if RequiresTwoFactor is encountered.

    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { totpCode = code }),
    };
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
}
```

> **Note on test #2:** if the user has MFA enabled at login time, `/api/auth/login` returns `200 { requiresTotp: true }` instead of 204. Either enroll MFA AFTER `LoginViaHttpAsync` (so login completes without MFA challenge) or run a second `/api/auth/login/totp` call to complete the chain. The simpler path is: register → login → enroll MFA via `EnrollUserMfaAsync` (which uses `UserManager` directly, no HTTP). The session cookie acquired before enrollment is still valid; the LastReauthAt claim was stamped at that login. This keeps the test focused on the reauth endpoint.

- [ ] **Step 3: Add tests #3 + #4 (login stamps the claim — gated endpoint succeeds without separate reauth)**

Append:

```csharp
[Fact]
public async Task Login_stamps_LastReauthAt_claim()
{
    var email = $"login-stamps-{Guid.NewGuid():N}@example.com";
    await AuthTestFixture.RegisterUserAsync(_factory, email);
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    // Hit a gated endpoint immediately. Should pass the gate because login was just now.
    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK,
        "fresh login should stamp LastReauthAt within the 5-min window, allowing /mfa/enroll to pass the gate");
}

[Fact]
public async Task LoginTotp_stamps_LastReauthAt_claim()
{
    var email = $"login-totp-stamps-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

    // Step 1: password login → 200 { requiresTotp: true }
    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
    };
    loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
    loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
    var loginResp = await client.SendAsync(loginReq);
    loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var twoFactorCookie = loginResp.Headers.GetValues("Set-Cookie")
        .First(c => c.StartsWith("Identity.TwoFactorUserId="))
        .Split(';')[0];

    // Step 2: TOTP completes login → 204 + __Host-Session
    var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory);
    var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
    {
        Content = JsonContent.Create(new { code = AuthTestFixture.ComputeCurrentTotpCode(seed) }),
    };
    totpReq.Headers.Add("Cookie", $"{twoFactorCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
    totpReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
    var totpResp = await client.SendAsync(totpReq);
    totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    var sessionCookie = totpResp.Headers.GetValues("Set-Cookie")
        .First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="))
        .Split(';')[0]
        .Substring(SessionConstants.SessionCookieName.Length + 1);

    // Hit a gated endpoint immediately.
    var (csrf3, header3) = AuthTestFixture.MintCsrf(_factory);
    var gatedReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    gatedReq.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf3}");
    gatedReq.Headers.Add(SessionConstants.CsrfHeaderName, header3);
    var gatedResp = await client.SendAsync(gatedReq);

    // The user has MFA already enrolled — Enroll returns 409 MFA_ALREADY_ENROLLED.
    // What we actually care about is the gate passing — 409 means the gate let us through.
    // 401 REAUTH_REQUIRED would mean the gate fired (failure).
    gatedResp.StatusCode.Should().Be(HttpStatusCode.Conflict,
        "TOTP login should stamp LastReauthAt — the gate must pass; the 409 MFA_ALREADY_ENROLLED proves we reached the action");
}
```

- [ ] **Step 4: Add test #5 (backup-code login also stamps)**

Append:

```csharp
[Fact]
public async Task LoginTotp_with_backup_code_also_stamps_LastReauthAt()
{
    var email = $"login-bk-stamps-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

    // Generate backup codes via the existing service so we have a valid one to use.
    string backupCode;
    using (var scope = _factory.Services.CreateScope())
    {
        var bcs = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
        var codes = await bcs.RegenerateAsync(user.Id, Timeout30s());
        backupCode = codes[0];
    }

    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

    // Step 1: password login
    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
    };
    loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
    loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
    var loginResp = await client.SendAsync(loginReq);
    var twoFactorCookie = loginResp.Headers.GetValues("Set-Cookie")
        .First(c => c.StartsWith("Identity.TwoFactorUserId=")).Split(';')[0];

    // Step 2: backup code completes login
    var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory);
    var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
    {
        Content = JsonContent.Create(new { code = backupCode }),
    };
    totpReq.Headers.Add("Cookie", $"{twoFactorCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
    totpReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
    var totpResp = await client.SendAsync(totpReq);
    totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var sessionCookie = totpResp.Headers.GetValues("Set-Cookie")
        .First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="))
        .Split(';')[0]
        .Substring(SessionConstants.SessionCookieName.Length + 1);

    // Hit a gated endpoint — should pass the gate via the just-stamped LastReauthAt.
    var (csrf3, header3) = AuthTestFixture.MintCsrf(_factory);
    var gatedReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    gatedReq.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf3}");
    gatedReq.Headers.Add(SessionConstants.CsrfHeaderName, header3);
    var gatedResp = await client.SendAsync(gatedReq);

    gatedResp.StatusCode.Should().Be(HttpStatusCode.Conflict,
        "backup-code login is a recovery path but DOES stamp LastReauthAt; the gate passes");
}
```

- [ ] **Step 5: Add tests #6, #7 (wrong-password failures + lockout)**

Append:

```csharp
[Fact]
public async Task Reauth_with_wrong_password_returns_401_INVALID_REAUTH_and_increments_AccessFailedCount()
{
    var email = $"reauth-wp-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { password = "wrong-password" }),
    };
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_REAUTH");

    using var scope = _factory.Services.CreateScope();
    var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var fresh = await um.FindByEmailAsync(email);
    (await um.GetAccessFailedCountAsync(fresh!)).Should().Be(1);
}

[Fact]
public async Task Reauth_with_wrong_password_can_lock_account_after_10_attempts()
{
    var email = $"reauth-lock-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    for (var i = 0; i < 10; i++)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = $"wrong-{i}" }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        await client.SendAsync(req);
    }

    using var scope = _factory.Services.CreateScope();
    var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var fresh = await um.FindByEmailAsync(email);
    (await um.IsLockedOutAsync(fresh!)).Should().BeTrue();
}
```

- [ ] **Step 6: Add tests #8, #9, #10 (TOTP failure paths)**

Append:

```csharp
[Fact]
public async Task Reauth_with_wrong_totp_returns_401_INVALID_REAUTH_and_does_not_increment_AccessFailedCount()
{
    var email = $"reauth-bt-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { totpCode = "000000" }),
    };
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_REAUTH");

    using var scope = _factory.Services.CreateScope();
    var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var fresh = await um.FindByEmailAsync(email);
    (await um.GetAccessFailedCountAsync(fresh!)).Should().Be(0,
        "wrong TOTP at reauth uses VerifyTwoFactorTokenAsync and does NOT poison the password lockout counter");
}

[Fact]
public async Task Reauth_with_replayed_totp_returns_401_INVALID_REAUTH()
{
    var email = $"reauth-rt-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    var code = AuthTestFixture.ComputeCurrentTotpCode(seed);

    // First reauth burns the code.
    var (csrf1, header1) = AuthTestFixture.MintCsrf(_factory);
    var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { totpCode = code }),
    };
    req1.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf1}");
    req1.Headers.Add(SessionConstants.CsrfHeaderName, header1);
    var resp1 = await client.SendAsync(req1);
    resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Replay the same code → 401.
    var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory);
    var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { totpCode = code }),
    };
    req2.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
    req2.Headers.Add(SessionConstants.CsrfHeaderName, header2);
    var resp2 = await client.SendAsync(req2);
    resp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp2.Content.ReadAsStringAsync()).Should().Contain("INVALID_REAUTH");
}

[Fact]
public async Task Reauth_with_backup_code_in_totpCode_returns_401_INVALID_REAUTH()
{
    var email = $"reauth-bk-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

    string backupCode;
    using (var scope = _factory.Services.CreateScope())
    {
        var bcs = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
        var codes = await bcs.RegenerateAsync(user.Id, Timeout30s());
        backupCode = codes[0];
    }

    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { totpCode = backupCode }),
    };
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_REAUTH");
}
```

- [ ] **Step 7: Add tests #11, #12, #13 (locked, missing-field, defensive accept)**

Append:

```csharp
[Fact]
public async Task Reauth_when_account_already_locked_returns_401_ACCOUNT_LOCKED_OUT()
{
    var email = $"reauth-loa-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    using (var scope = _factory.Services.CreateScope())
    {
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByEmailAsync(email);
        await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddHours(1));
    }

    var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
    };
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("ACCOUNT_LOCKED_OUT");
}

[Fact]
public async Task Reauth_with_missing_required_field_returns_422_VALIDATION_ERROR()
{
    var email = $"reauth-mf-{Guid.NewGuid():N}@example.com";
    await AuthTestFixture.RegisterUserAsync(_factory, email);
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { totpCode = "123456" }),  // user has no MFA → password is required
    };
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");
    (await resp.Content.ReadAsStringAsync()).Should().Contain("password");
}

[Fact]
public async Task Reauth_ignores_extraneous_field_based_on_user_MFA_state()
{
    var email = $"reauth-ext-{Guid.NewGuid():N}@example.com";
    await AuthTestFixture.RegisterUserAsync(_factory, email);
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

    var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword, totpCode = "123456" }),
    };
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
        "no-MFA user submitting both password and totpCode should succeed via password validation; totpCode is ignored");
}
```

- [ ] **Step 8: Run all tests in the file**

Run:
```bash
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~ReauthEndpointTests --blame-hang-timeout 120s --nologo
```

Expected: 13 passed.

If a test fails:
- Diagnose root cause; do NOT add retries or relax assertions.
- Common gotcha: the `WrapClientWithCookie` helper is a no-op — each test must build its own request with manual cookie+CSRF. Don't add cookie auto-attachment to the helper.
- If `MintCsrf` returns a token bound to the anonymous principal but the user is authenticated, the `MintCsrf(factory, userId)` overload exists for that case — use it when authentication context affects antiforgery. Most reauth tests are after login so the anonymous mint is fine, but watch for 400s on antiforgery validation.

- [ ] **Step 9: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/ReauthEndpointTests.cs
git -C <repo> commit -m "test(auth): reauth endpoint ship-gate tests #1-#13 (Stage 6c.2)"
```

---

## Task 16: ReauthGateTests (8 tests — gate behaviour)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/ReauthGateTests.cs`

The 8 tests covered are #16–#23 per spec § 5.2. These tests use `MintAuthCookieWithLastReauthAt` (Task 14) to simulate stale/missing/future/malformed claims without sleeping.

- [ ] **Step 1: Create the file with shared helpers**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class ReauthGateTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public ReauthGateTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> CallGatedEndpointAsync(
        ApplicationUser user, long? lastReauthAtUnix)
    {
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, lastReauthAtUnix);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }
}
```

- [ ] **Step 2: Add tests #16, #17, #18 (fresh, no claim, stale)**

```csharp
[Fact]
public async Task Gated_endpoint_with_fresh_claim_succeeds()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-fresh-{Guid.NewGuid():N}@example.com");
    var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    var resp = await CallGatedEndpointAsync(user, fresh);
    resp.StatusCode.Should().Be(HttpStatusCode.OK,
        "fresh claim within 5-min window should pass the gate; /mfa/enroll returns 200 OK");
}

[Fact]
public async Task Gated_endpoint_with_no_claim_returns_401_REAUTH_REQUIRED()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-no-{Guid.NewGuid():N}@example.com");

    var resp = await CallGatedEndpointAsync(user, null);
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
}

[Fact]
public async Task Gated_endpoint_with_stale_claim_returns_401_REAUTH_REQUIRED()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-stale-{Guid.NewGuid():N}@example.com");
    var stale = DateTimeOffset.UtcNow.AddSeconds(-301).ToUnixTimeSeconds();  // 1 second past the boundary

    var resp = await CallGatedEndpointAsync(user, stale);
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
}
```

- [ ] **Step 3: Add tests #19, #20, #21 (boundary, future, malformed)**

```csharp
[Fact]
public async Task Gated_endpoint_with_claim_at_boundary_succeeds()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-bnd-{Guid.NewGuid():N}@example.com");
    var boundary = DateTimeOffset.UtcNow.AddSeconds(-300).ToUnixTimeSeconds();  // exactly at the window

    var resp = await CallGatedEndpointAsync(user, boundary);
    resp.StatusCode.Should().Be(HttpStatusCode.OK,
        "claim at age=300s exactly should pass (boundary inclusive)");
}

[Fact]
public async Task Gated_endpoint_with_future_claim_returns_401_REAUTH_REQUIRED()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-fut-{Guid.NewGuid():N}@example.com");
    var future = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds();

    var resp = await CallGatedEndpointAsync(user, future);
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
}

[Fact]
public async Task Gated_endpoint_with_malformed_claim_returns_401_REAUTH_REQUIRED()
{
    // MintAuthCookieWithLastReauthAt only takes a long?. Build a separate helper inline:
    // mint a cookie that contains a non-numeric LastReauthAt claim.
    var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-mal-{Guid.NewGuid():N}@example.com");

    using var scope = _factory.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = sp };
    http.Items[SessionConstants.LastReauthAtItemKey] = "not-a-number";
    var sid = Guid.NewGuid();
    http.Items[SessionConstants.PendingSessionItemKey] = sid;
    var accessor = sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
    if (accessor is not null) accessor.HttpContext = http;

    var pcf = sp.GetRequiredService<Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<ApplicationUser>>();
    var principal = await pcf.CreateAsync(user);

    var db = sp.GetRequiredService<ProjectCeres.Data.AppDbContext>();
    db.UserSessions.Add(new UserSession
    {
        Id = sid, UserId = user.Id, IpCreatedAt = "127.0.0.1", UserAgent = "test",
        CreatedAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow, IsPersistent = false,
    });
    await db.SaveChangesAsync();

    var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
        principal,
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = false },
        Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
    var dpProvider = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
    var protector = dpProvider.CreateProtector(
        "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
        Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme,
        "v2");
    var format = new Microsoft.AspNetCore.Authentication.TicketDataFormat(protector);
    var cookie = format.Protect(ticket);

    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    req.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);
    var resp = await client.SendAsync(req);

    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
}
```

- [ ] **Step 4: Add tests #22, #23 (unauthenticated, post-reauth)**

```csharp
[Fact]
public async Task Gated_endpoint_unauthenticated_request_returns_401_with_default_handler_not_REAUTH_REQUIRED()
{
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    var body = await resp.Content.ReadAsStringAsync();
    body.Should().NotContain("REAUTH_REQUIRED",
        "unauthenticated requests should be handled by the default handler, not the RecentAuth envelope");
}

[Fact]
public async Task Gated_endpoint_after_successful_reauth_succeeds()
{
    var email = $"gate-pr-{Guid.NewGuid():N}@example.com";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

    // Stage 1: legacy session (no LastReauthAt). Gated endpoint should 401 REAUTH_REQUIRED.
    var noClaimCookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, null);
    var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

    var (csrf1, header1) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var gatedReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    gatedReq1.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={noClaimCookie}; {SessionConstants.CsrfCookieName}={csrf1}");
    gatedReq1.Headers.Add(SessionConstants.CsrfHeaderName, header1);
    var resp1 = await client.SendAsync(gatedReq1);
    resp1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await resp1.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");

    // Stage 2: step up via /api/auth/reauth.
    var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var reauthReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
    {
        Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
    };
    reauthReq.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={noClaimCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
    reauthReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
    var reauthResp = await client.SendAsync(reauthReq);
    reauthResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    var refreshedCookie = reauthResp.Headers.GetValues("Set-Cookie")
        .First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="))
        .Split(';')[0]
        .Substring(SessionConstants.SessionCookieName.Length + 1);

    // Stage 3: retry the gated request with the refreshed cookie.
    var (csrf3, header3) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var gatedReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    gatedReq2.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={refreshedCookie}; {SessionConstants.CsrfCookieName}={csrf3}");
    gatedReq2.Headers.Add(SessionConstants.CsrfHeaderName, header3);
    var resp2 = await client.SendAsync(gatedReq2);
    resp2.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

- [ ] **Step 5: Run the file**

Run:
```bash
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~ReauthGateTests --blame-hang-timeout 120s --nologo
```

Expected: 8 passed.

If `MintAuthCookieWithLastReauthAt` produces a cookie the middleware rejects (some tests may return 401 with no `REAUTH_REQUIRED` envelope, indicating a Challenged/unauthenticated outcome rather than Forbidden):
- Investigate the data-protection purpose strings — they must match exactly what `CookieAuthenticationHandler` uses internally.
- Inspect the framework default registration in this .NET version: open `dotnet --info` to confirm the version, then verify the purpose string list is `("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "Identity.Application", "v2")`.
- Do NOT relax test assertions; fix the helper.

- [ ] **Step 6: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/ReauthGateTests.cs
git -C <repo> commit -m "test(auth): reauth gate ship-gate tests #16-#23 (Stage 6c.2)"
```

---

## Task 17: ReauthMfaEndpointGatingTests (3 tests — retroactive gating)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/ReauthMfaEndpointGatingTests.cs`

Tests #24–#26: confirm the three retroactively-gated MFA endpoints actually require fresh `LastReauthAt`.

- [ ] **Step 1: Create the file**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class ReauthMfaEndpointGatingTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;
    public ReauthMfaEndpointGatingTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> HitWithStaleClaimAsync(
        ApplicationUser user, string url, object? body = null)
    {
        var stale = DateTimeOffset.UtcNow.AddSeconds(-301).ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, stale);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task MfaEnroll_now_requires_recent_auth()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-enr-{Guid.NewGuid():N}@example.com");
        var resp = await HitWithStaleClaimAsync(user, "/api/auth/mfa/enroll");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    [Fact]
    public async Task MfaEnrollVerify_now_requires_recent_auth()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-ver-{Guid.NewGuid():N}@example.com");
        var resp = await HitWithStaleClaimAsync(user, "/api/auth/mfa/enroll/verify", new { code = "123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    [Fact]
    public async Task MfaRegenerateBackupCodes_now_requires_recent_auth_not_in_body_totp()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-rgn-{Guid.NewGuid():N}@example.com");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var resp = await HitWithStaleClaimAsync(user, "/api/auth/mfa/backup-codes/regenerate");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }
}
```

- [ ] **Step 2: Run**

Run:
```bash
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~ReauthMfaEndpointGatingTests --blame-hang-timeout 120s --nologo
```

Expected: 3 passed.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/ReauthMfaEndpointGatingTests.cs
git -C <repo> commit -m "test(auth): MFA endpoints retroactive gating tests #24-#26 (Stage 6c.2)"
```

---

## Task 18: ReauthRateLimitTests (2 tests — rate limit)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/ReauthRateLimitTests.cs`

Tests #14–#15. `[Collection("RateLimitTests")]` collection.

- [ ] **Step 1: Create the file**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class ReauthRateLimitTests : IClassFixture<RateLimitedAuthTestWebApplicationFactory>
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;
    public ReauthRateLimitTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;

    private async Task<(HttpClient client, string sessionCookie, ApplicationUser user)>
        SetupAuthenticatedClientAsync(string emailPrefix)
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        return (client, sessionCookie, user);
    }

    [Fact]
    public async Task Reauth_per_user_limit_returns_429_at_11th_attempt()
    {
        var (client, sessionCookie, user) = await SetupAuthenticatedClientAsync("rl-pu");

        // First reauth uses correct password to anchor the user; subsequent ones use wrong password to
        // ensure they don't accidentally lock the account before we hit the rate limit (the 10/min limit
        // should fire first because lockout is also at 10 failures — they race; use correct password).
        for (var i = 0; i < 10; i++)
        {
            var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
            {
                Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
            };
            req.Headers.Add("Cookie",
                $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
            req.Headers.Add(SessionConstants.CsrfHeaderName, header);
            var r = await client.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var (csrf11, header11) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req11 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        req11.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf11}");
        req11.Headers.Add(SessionConstants.CsrfHeaderName, header11);
        var resp11 = await client.SendAsync(req11);
        resp11.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        resp11.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Reauth_per_user_partition_isolates_users()
    {
        var (clientA, sessionA, userA) = await SetupAuthenticatedClientAsync("rl-pa-a");
        var (clientB, sessionB, userB) = await SetupAuthenticatedClientAsync("rl-pa-b");

        // Burn user A's bucket.
        for (var i = 0; i < 10; i++)
        {
            var (csrf, header) = AuthTestFixture.MintCsrf(_factory, userA.Id);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
            {
                Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
            };
            req.Headers.Add("Cookie",
                $"{SessionConstants.SessionCookieName}={sessionA}; {SessionConstants.CsrfCookieName}={csrf}");
            req.Headers.Add(SessionConstants.CsrfHeaderName, header);
            await clientA.SendAsync(req);
        }

        // User B's bucket is independent — first reauth should succeed.
        var (csrfB, headerB) = AuthTestFixture.MintCsrf(_factory, userB.Id);
        var reqB = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        reqB.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionB}; {SessionConstants.CsrfCookieName}={csrfB}");
        reqB.Headers.Add(SessionConstants.CsrfHeaderName, headerB);
        var respB = await clientB.SendAsync(reqB);
        respB.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
```

- [ ] **Step 2: Run**

Run:
```bash
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~ReauthRateLimitTests --blame-hang-timeout 120s --nologo
```

Expected: 2 passed.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/ReauthRateLimitTests.cs
git -C <repo> commit -m "test(auth): reauth rate-limit tests #14-#15 (Stage 6c.2)"
```

---

## Task 19: ReauthCrossFeatureTests (3 tests)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/ReauthCrossFeatureTests.cs`

Tests #30, #31, #32.

- [ ] **Step 1: Write the file**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class ReauthCrossFeatureTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;
    public ReauthCrossFeatureTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Existing_login_flow_still_works_with_LastReauthAt_added()
    {
        var email = $"xf-login-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Session cookie issued and contains the LastReauthAt claim — implicit; the smoke is
        // "login still 204s and a session cookie is present."
        resp.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));
    }

    [Fact]
    public async Task RefreshSignInAsync_after_reauth_does_not_disrupt_persistent_cookie()
    {
        var email = $"xf-pers-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = true }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionCookie = loginResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="))
            .Split(';')[0]
            .Substring(SessionConstants.SessionCookieName.Length + 1);
        var persistentCookieBefore = loginResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{SessionConstants.PersistentCookieName}="));

        var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory);
        var reauthReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        reauthReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        reauthReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var reauthResp = await client.SendAsync(reauthReq);
        reauthResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The reauth response should NOT include a Set-Cookie for __Host-Persist;
        // RefreshSignInAsync only re-issues __Host-Session.
        reauthResp.Headers.TryGetValues("Set-Cookie", out var setCookies);
        if (setCookies is not null)
        {
            setCookies.Should().NotContain(c => c.StartsWith($"{SessionConstants.PersistentCookieName}="),
                "RefreshSignInAsync only re-issues the application cookie; persistent cookie should be untouched");
        }
    }

    [Fact]
    public async Task Reauth_when_user_is_deleted_returns_401_UNAUTHENTICATED()
    {
        var email = $"xf-del-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByEmailAsync(email);
            await um.DeleteAsync(fresh!);
        }

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        // SecurityStampValidator may reject the cookie before our handler runs.
        // Either way, the response is 401 (with or without an envelope).
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Run**

Run:
```bash
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~ReauthCrossFeatureTests --blame-hang-timeout 120s --nologo
```

Expected: 3 passed.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/ReauthCrossFeatureTests.cs
git -C <repo> commit -m "test(auth): reauth cross-feature regression tests #30-#32 (Stage 6c.2)"
```

---

## Task 20: Architecture tests (3 tests appended to existing file)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`

Tests #27, #28, #29.

- [ ] **Step 1: Append to `ArchitectureTests.cs`**

```csharp
[Fact]
public void RequireRecentAuth_attribute_is_only_on_action_methods_not_classes()
{
    var asm = typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute).Assembly;
    var controllerTypes = asm.GetTypes()
        .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t));

    foreach (var t in controllerTypes)
    {
        t.GetCustomAttributes(typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute), inherit: false)
            .Should().BeEmpty($"{t.Name} carries [RequireRecentAuth] at the class level — must be action-level only");
    }
}

[Fact]
public void RequireRecentAuth_attribute_is_never_combined_with_AllowAnonymous()
{
    var asm = typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute).Assembly;
    var actionMethods = asm.GetTypes()
        .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t))
        .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));

    foreach (var m in actionMethods)
    {
        var hasRecentAuth = m.GetCustomAttributes(typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute), inherit: false).Any();
        if (!hasRecentAuth) continue;
        var hasAllowAnon = m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: false).Any();
        hasAllowAnon.Should().BeFalse($"{m.DeclaringType!.Name}.{m.Name} carries both [RequireRecentAuth] and [AllowAnonymous] — these are mutually exclusive");
    }
}

[Fact]
public void Three_existing_MFA_endpoints_carry_RequireRecentAuth_attribute()
{
    var t = typeof(ProjectCeres.Controllers.Api.MfaController);
    foreach (var name in new[] { "Enroll", "EnrollVerify", "RegenerateBackupCodes" })
    {
        var m = t.GetMethod(name);
        m.Should().NotBeNull($"MfaController must expose {name}");
        m!.GetCustomAttributes(typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute), inherit: false)
            .Should().NotBeEmpty($"MfaController.{name} must carry [RequireRecentAuth] (Stage 6c.2)");
    }
}
```

- [ ] **Step 2: Run**

Run:
```bash
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ArchitectureTests.RequireRecentAuth|FullyQualifiedName~ArchitectureTests.Three_existing_MFA" --blame-hang-timeout 120s --nologo
```

Expected: 3 passed.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git -C <repo> commit -m "test(auth): reauth architecture tests #27-#29 (Stage 6c.2)"
```

---

## Task 21: Full suite + doc sync

**Files:**
- Modify: `docs/api-contract.md`
- Modify: `docs/security-model.md`
- Modify: `docs/roadmap-phase-three.md`
- Modify: `docs/planning-phase3.md`
- Modify: `docs/planning-resolved.md`

- [ ] **Step 1: Run the full test suite**

Run:
```bash
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --blame-hang-timeout 180s --nologo
```

Expected: all green.

- [ ] **Step 2: Update `docs/api-contract.md`**

Add two rows to the canonical-error-codes table (after `INVALID_RESET_TOKEN`):

```markdown
| `INVALID_REAUTH` | 401 | `POST /api/auth/reauth` | Submitted password / TOTP / backup-code-shape was rejected. Stage 6c.2. |
| `REAUTH_REQUIRED` | 401 | Any `[RequireRecentAuth]` action | Authenticated but `LastReauthAt` claim is missing or older than 5 minutes. Step up via `/api/auth/reauth`. Stage 6c.2. |
```

Add the reauth row to the auth endpoints table (after `/api/auth/mfa/backup-codes/regenerate`):

```markdown
| `/api/auth/reauth` | POST | Authenticated, CSRF-validated, per-user rate-limited (10/min) | `{ password? }` (no MFA) or `{ totpCode? }` (MFA) — server enforces required field based on `user.TwoFactorEnabled` | `204` on success — refreshes `__Host-Session` cookie with updated `LastReauthAt` claim. `401 INVALID_REAUTH` on bad credentials. `401 ACCOUNT_LOCKED_OUT` if locked. `422 VALIDATION_ERROR` with `details: [{field, message}]` on missing required field. Wrong password counts toward lockout (10/15min); wrong TOTP does not (TOTP miss uses `VerifyTwoFactorTokenAsync` directly). Backup codes are NOT accepted at reauth. Stage 6c.2. |
```

Update the `/api/auth/mfa/backup-codes/regenerate` row to drop the in-body TOTP requirement and reference `[RequireRecentAuth]`:

Change:
```markdown
| `/api/auth/mfa/backup-codes/regenerate` | POST | Authenticated (full step-up middleware deferred to 6c), CSRF-validated | `{ totpCode }` — current 6-digit TOTP code required (Stage 6b.3) | ...
```

To:
```markdown
| `/api/auth/mfa/backup-codes/regenerate` | POST | Authenticated, `[RequireRecentAuth]` (5-min freshness), CSRF-validated | (empty body) | `200 { backupCodes: [...10 strings] }` — invalidates all existing codes and returns a new batch once. `401 REAUTH_REQUIRED` if `LastReauthAt` claim is missing/stale (Stage 6c.2 replaces in-body TOTP). `409 MFA_NOT_ENABLED` if user has no MFA. `Cache-Control: no-store, no-cache`. |
```

- [ ] **Step 3: Update `docs/security-model.md`**

Locate the existing `Reauthentication for sensitive operations (NIST 800-63B §7.2):` bullet (around line 297). Add a status banner directly above the existing text:

```markdown
**Status: ✅ Shipped (Stage 6c.2, 2026-05-10).** `LastReauthAt` claim, `[RequireRecentAuth]` attribute, `POST /api/auth/reauth` endpoint, custom `IAuthorizationMiddlewareResultHandler` for the `REAUTH_REQUIRED` envelope. 5-minute freshness window. Login (no-MFA + MFA + backup-code paths) all stamp the claim. The three existing MFA endpoints (`/api/auth/mfa/enroll`, `/enroll/verify`, `/backup-codes/regenerate`) are retroactively gated. Replaces the in-body TOTP-code stopgap from Stage 6b.3 Gap 4. See `docs/superpowers/specs/2026-05-10-reauth-middleware-design.md`.
```

Update the Stage 6b.1 TOTP re-enrollment bullet (line 100) to reflect that the gate has shipped:

Change:
```markdown
- **TOTP re-enrollment:** ... **reauth gate is added in Stage 6c** alongside the reauth middleware.
```

To:
```markdown
- **TOTP re-enrollment:** ... **reauth gate added in Stage 6c.2 (`[RequireRecentAuth]` on `/enroll` and `/enroll/verify`)**.
```

Update the Stage 6b.3 backup-code-regen bullet (line 111):

Change:
```markdown
... (Stage 6b.3; full step-up middleware via `LastPasswordVerifiedAt` deferred to Stage 6c.)
```

To:
```markdown
... Stage 6c.2 replaced the in-body TOTP with the `[RequireRecentAuth]` attribute on the action.
```

- [ ] **Step 4: Update `docs/roadmap-phase-three.md`**

Add a status banner before the Stage 6 verification block (after the Stage 6c.1 banner):

```markdown
> **Stage 6c.2 (2026-05-10):** Reauth middleware shipped. New `LastReauthAt` claim stamped at login (no-MFA + MFA + backup-code paths), refreshed via `RefreshSignInAsync` after step-up. New `POST /api/auth/reauth` endpoint accepts `{password?}` or `{totpCode?}` based on `user.TwoFactorEnabled`. New `[RequireRecentAuth]` attribute backed by `RecentAuthRequirement` policy + custom `IAuthorizationMiddlewareResultHandler` emitting `401 REAUTH_REQUIRED`. 5-minute freshness window. Three existing MFA endpoints (`/enroll`, `/enroll/verify`, `/backup-codes/regenerate`) retroactively gated; in-body TOTP from `/backup-codes/regenerate` removed. 32-test ship-gate green. Stage 6c continues with email-change (6.12), audit log (6.14), and lockout self-service unlock.
```

Locate the Stage 6 verification checklist for "Reauthentication for sensitive operations" (around line 537–541). Flip the items:

Change:
```markdown
Reauthentication for sensitive operations:

- [ ] Fresh password entry required before: change password, change email, re-enrol TOTP, view active sessions, GDPR erasure
- [ ] Persistent sessions ("remember me") do NOT bypass reauthentication
- [ ] Reauthentication grant is short-lived (e.g., 5 minutes) and scoped to a single sensitive action
```

To:
```markdown
Reauthentication for sensitive operations (Stage 6c.2, shipped 2026-05-10):

- [x] Fresh password entry required before: re-enrol TOTP (`/mfa/enroll`, `/mfa/enroll/verify`), regenerate backup codes (`/mfa/backup-codes/regenerate`). Change-password and email-change land in 6.12; sessions list lands in Stage 12; GDPR erasure lands in Stage 13 — all will declare `[RequireRecentAuth]` when shipped. Architecture test #29 pins the three currently-gated endpoints.
- [x] Persistent sessions ("remember me") do NOT bypass reauthentication — the gate reads the `LastReauthAt` claim on the application cookie, which is independent of the `__Host-Persist` rotation. Test #31 (`RefreshSignInAsync_after_reauth_does_not_disrupt_persistent_cookie`) pins this.
- [x] Reauthentication grant is short-lived (5 minutes) and scoped to a single sensitive action — `RecentAuthRequirement.Window = 5 min`. Test #18 (stale at 301s) and #19 (boundary at 300s) pin the window.
```

Locate the "Reauthentication test" item in the "Tests required before Stage 7 begins" block:

Change:
```markdown
- [ ] Reauthentication test: changing password without fresh password entry returns 401, even with valid session
```

To:
```markdown
- [x] Reauthentication test: a gated endpoint without a fresh `LastReauthAt` claim returns 401 `REAUTH_REQUIRED` even with a valid session — Stage 6c.2 (`Gated_endpoint_with_stale_claim_returns_401_REAUTH_REQUIRED`, `Gated_endpoint_with_no_claim_returns_401_REAUTH_REQUIRED`, `MfaRegenerateBackupCodes_now_requires_recent_auth_not_in_body_totp`). Change-password specifically lands with 6.12.
```

- [ ] **Step 5: Update `docs/planning-phase3.md`**

Locate the "Stage 6b.3 deferred decisions" block (around line 440). Mark item #1 resolved:

Change:
```markdown
1. **Step-up middleware (`LastPasswordVerifiedAt` claim)** (Stage 6c) — Stage 6b.3 used an in-body TOTP code on backup-code regeneration as a targeted stopgap (Gap 4). The full reauth middleware that stamps a `LastPasswordVerifiedAt` claim and gates sensitive operations uniformly ships in Stage 6c alongside password-reset and email-change.
```

To:
```markdown
1. ~~**Step-up middleware (`LastPasswordVerifiedAt` claim)** (Stage 6c)~~ — **Resolved in Stage 6c.2 (2026-05-10).** Shipped as `LastReauthAt` claim + `[RequireRecentAuth]` attribute + `POST /api/auth/reauth` endpoint with 5-min freshness window. Three MFA endpoints retroactively gated; in-body TOTP from `/backup-codes/regenerate` removed. See `planning-resolved.md` and `docs/superpowers/specs/2026-05-10-reauth-middleware-design.md`.
```

- [ ] **Step 6: Update `docs/planning-resolved.md`**

Append after the password-reset entry:

```markdown
- [x] **Reauth middleware for sensitive operations (Phase 3, Stage 6c.2, shipped 2026-05-10)** — `LastReauthAt` claim (Unix seconds) stamped on the auth cookie at login (no-MFA + MFA + backup-code paths) and refreshed by a successful `POST /api/auth/reauth` step-up. `[RequireRecentAuth]` attribute backed by `RecentAuthRequirement` policy + custom `IAuthorizationMiddlewareResultHandler` emitting `401 REAUTH_REQUIRED` when the claim is missing, malformed, future, or older than 5 minutes. Reauth endpoint validates `password` (no MFA) or `totpCode` (MFA) based on `user.TwoFactorEnabled`. Backup codes are NOT accepted at reauth (recovery path is `/login/totp`). Wrong password counts toward lockout via `AccessFailedAsync`; wrong TOTP does not (uses `VerifyTwoFactorTokenAsync` directly). Per-user rate limit 10/min. Three existing MFA endpoints (`/api/auth/mfa/enroll`, `/enroll/verify`, `/backup-codes/regenerate`) retroactively gated; the in-body TOTP-code stopgap from Stage 6b.3 Gap 4 was removed and the `RegenerateBackupCodesRequest` DTO deleted. New canonical error codes: `INVALID_REAUTH`, `REAUTH_REQUIRED`. 32-test ship-gate. See `security-model.md → Login → Reauthentication`, `docs/superpowers/specs/2026-05-10-reauth-middleware-design.md`, and `docs/superpowers/plans/2026-05-10-stage-6c-2-reauth-middleware-plan.md`.
```

- [ ] **Step 7: Run `sync-docs` skill against the diff (optional cross-check)**

Invoke the `sync-docs` skill as a final consistency pass.

- [ ] **Step 8: Commit doc updates**

```bash
git -C <repo> add docs/
git -C <repo> commit -m "docs(sync): post-Stage-6c.2 living-doc sync (security-model, api-contract, roadmap, planning, planning-resolved)"
```

---

## Self-review checklist (run after the plan is written)

This was checked at plan-write time:

- **Spec coverage:** every section in spec § 5 (32 tests) maps to a task. Every spec § 7 file appears in the file structure or task list. ✅
- **Placeholder scan:** no `TBD`/`TODO` markers. The data-protection ticket-mint helper is fully documented in Task 14. ✅
- **Type consistency:** `LastReauthAtClaim`, `LastReauthAtItemKey`, `RecentAuthRequirement.Window`, `RequireRecentAuthAttribute.PolicyName` are stable across all tasks. ✅
- **Test ordering:** tests run incrementally; later tests depend on helpers added in Task 14 (LoginViaHttpAsync, MintAuthCookieWithLastReauthAt). Architecture tests in Task 20 don't depend on integration helpers. ✅
- **Production-code-touched commits:** Tasks 1–11 modify production. Tasks 12–20 modify tests only. Task 21 modifies docs only.
- **Implementation-blocker disclosure:** Task 14 step 4 calls out the data-protection helper as the highest-risk piece — the implementer must investigate and report `BLOCKED` if the purpose strings or ticket-format don't work in this .NET version. No false-positive shortcuts.
