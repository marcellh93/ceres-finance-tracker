# Stage 6c Sub-stage 6.11 — Password Reset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the password-reset flow per the spec at `docs/superpowers/specs/2026-05-10-password-reset-design.md` — two endpoints, Argon2id-hashed single-use tokens, MFA-conditional gating, all-sessions-revoked-on-success, 46 tests as ship-gate.

**Architecture:** New `PasswordResetController` exposes `/request` and `/confirm` under `/api/auth/password-reset/`. New `PasswordResetService` owns the orchestration (token issuance, supersession, validation, password write, session revocation, lockout clearing). New `PasswordResetTokenGenerator` mirrors `PersistentTokenService` for token RNG + Argon2id hashing. New `PasswordResetToken` entity carries hashed tokens with `ExpiresAt`/`ConsumedAt`/`MfaVerifiedAt` discipline. New `IEmailService` abstraction with `LogOnlyEmailService` development implementation; production deliberately unwired until Stage 8. Per-user `SemaphoreSlim` mirrors `AuthController._loginLocks`. Service-side `MemoryCache` per-email rate gate (5/hour); per-IP via existing `AuthLoginByIp` policy.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Postgres/Npgsql), Microsoft.AspNetCore.Identity, Konscious.Security.Cryptography (Argon2id, already wired), xUnit + FluentAssertions + Moq integration tests, `WebApplicationFactory`-based harness already in place (`AuthTestWebApplicationFactory`, `RateLimitedAuthTestWebApplicationFactory`).

---

## File structure

### Files created

| Path | Responsibility |
|---|---|
| `ProjectCeres/Models/PasswordResetToken.cs` | Entity: hashed token row with state |
| `ProjectCeres/Common/Authentication/PasswordResetTokenGenerator.cs` | RNG + Argon2id hash + verify (mirrors `PersistentTokenService`) |
| `ProjectCeres/Common/Authentication/PasswordResetConfirmOutcome.cs` | Outcome discriminated union for `ConfirmAsync` |
| `ProjectCeres/Common/Authentication/PasswordResetService.cs` | Request + confirm orchestration; per-user semaphore; per-email rate gate |
| `ProjectCeres/Common/Email/EmailMessage.cs` | Plain DTO record |
| `ProjectCeres/Common/Email/IEmailService.cs` | Send abstraction |
| `ProjectCeres/Common/Email/LogOnlyEmailService.cs` | Dev impl: writes message at Information level |
| `ProjectCeres/Common/Email/NoopEmailService.cs` | Test fixture: silent no-op |
| `ProjectCeres/ViewModels/Auth/PasswordResetRequest.cs` | DTO `{Email}` with validation attrs |
| `ProjectCeres/ViewModels/Auth/PasswordResetConfirmRequest.cs` | DTO `{Token, NewPassword, TotpCode?}` |
| `ProjectCeres/Controllers/Api/PasswordResetController.cs` | HTTP surface, error envelope mapping |
| `ProjectCeres/Migrations/<timestamp>_AddPasswordResetTokens.cs` | EF migration (auto-generated) |
| `ProjectCeres.Tests/Integration/Authentication/PasswordResetRequestTests.cs` | Tests #1, #5–#9, #14, #16, #46 |
| `ProjectCeres.Tests/Integration/Authentication/PasswordResetConfirmNoMfaTests.cs` | Tests #2, #4, #6, #10, #15, #17, #18, #20, #21, #38, #39, #45 |
| `ProjectCeres.Tests/Integration/Authentication/PasswordResetConfirmMfaTests.cs` | Tests #3, #11–#13, #29–#32, #34, #37 |
| `ProjectCeres.Tests/Integration/Authentication/PasswordResetConcurrencyTests.cs` | Tests #19, #22, #23 |
| `ProjectCeres.Tests/Integration/Authentication/PasswordResetRateLimitTests.cs` | Tests #24–#28, #33 — `[Collection("RateLimitTests")]` |
| `ProjectCeres.Tests/Integration/Authentication/PasswordResetSessionRevocationTests.cs` | Tests #35, #36 |

### Files modified

| Path | Why |
|---|---|
| `ProjectCeres/Data/AppDbContext.cs` | Register `DbSet<PasswordResetToken>` + entity config |
| `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs` | Add `AuthPasswordResetByEmail` + `AnonymousPasswordResetPartition` constants (kept for future; service-side gate is the active path) |
| `ProjectCeres/Models/FailedLoginAttempt.cs` | Add `PasswordResetUnknownEmail` to enum |
| `ProjectCeres/Program.cs` | DI registrations (`PasswordResetService`, `PasswordResetTokenGenerator`, `IEmailService → LogOnlyEmailService` in Development, `IMemoryCache`) |
| `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` | Add tests #40–#44 (architecture rules) |
| `docs/api-contract.md` | Add `INVALID_RESET_TOKEN` row to canonical-error-codes table |
| `docs/security-model.md` | Flip Password Reset section status to shipped |
| `docs/roadmap-phase-three.md` | Flip 6.11 checkbox items |
| `docs/planning-resolved.md` | Record resolution of 6.11 deferred items |

---

## Task 1: Add `PasswordResetToken` entity

**Files:**
- Create: `ProjectCeres/Models/PasswordResetToken.cs`

- [ ] **Step 1: Write the entity**

```csharp
namespace ProjectCeres.Models;

/// <summary>
/// One row per active or recently-consumed password reset token.
/// TokenHash is Argon2id-hashed (per-row salt). Single-use: ConsumedAt is
/// set synchronously inside the same DB transaction as the password write.
/// MfaVerifiedAt is set after the TOTP step succeeds for MFA-enabled users.
/// </summary>
public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? MfaVerifiedAt { get; set; }
}
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Models/PasswordResetToken.cs
git commit -m "feat(auth): add PasswordResetToken entity (Stage 6c.1)"
```

---

## Task 2: Register `PasswordResetToken` on `AppDbContext`

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs`

- [ ] **Step 1: Add `DbSet`**

In `AppDbContext`, locate the auth-related `DbSet` block (around line 34–38, after `UserSessions`/`UserMfaBackupCodes`/`TotpReplayEntries`/`FailedLoginAttempts`). Add:

```csharp
public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
```

- [ ] **Step 2: Add fluent config**

Add a private method following the same shape as `ConfigureMfaEntities`:

```csharp
private static void ConfigurePasswordResetEntities(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<PasswordResetToken>(b =>
    {
        b.HasKey(e => e.Id);
        b.HasIndex(e => new { e.UserId, e.ConsumedAt });
        b.HasIndex(e => e.ExpiresAt);
        b.Property(e => e.TokenHash).HasMaxLength(512);
    });
}
```

Find the existing `OnModelCreating` body that calls `ConfigureMfaEntities(modelBuilder)`. Add `ConfigurePasswordResetEntities(modelBuilder);` immediately after it.

- [ ] **Step 3: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Data/AppDbContext.cs
git commit -m "feat(auth): register PasswordResetToken on AppDbContext (Stage 6c.1)"
```

---

## Task 3: Create EF migration for `PasswordResetToken`

**Files:**
- Create: `ProjectCeres/Migrations/<timestamp>_AddPasswordResetTokens.cs` (auto-generated)
- Create: `ProjectCeres/Migrations/<timestamp>_AddPasswordResetTokens.Designer.cs` (auto-generated)
- Modify: `ProjectCeres/Migrations/AppDbContextModelSnapshot.cs` (auto-updated)

- [ ] **Step 1: Generate the migration**

Run from repo root:

```bash
dotnet ef migrations add AddPasswordResetTokens --project ProjectCeres --output-dir Migrations
```

Expected: three files written/updated; the new migration's `Up` method emits `migrationBuilder.CreateTable("PasswordResetTokens", ...)` with the columns from Task 1 and indexes `IX_PasswordResetTokens_UserId_ConsumedAt` and `IX_PasswordResetTokens_ExpiresAt`.

- [ ] **Step 2: Inspect the generated SQL**

Run: `dotnet ef migrations script <previous-migration> AddPasswordResetTokens --project ProjectCeres`
Expected: a single `CREATE TABLE` and two `CREATE INDEX` statements, no other schema changes.

- [ ] **Step 3: Apply the migration to local Postgres**

Run: `dotnet ef database update --project ProjectCeres`
Expected: "Done." with no errors. Then verify schema:

```bash
psql -d project_ceres -c "\d \"PasswordResetTokens\""
```

Expected output lists columns `Id`, `UserId`, `TokenHash`, `CreatedAt`, `ExpiresAt`, `ConsumedAt`, `MfaVerifiedAt` and the two indexes.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Migrations/
git commit -m "feat(auth): EF migration AddPasswordResetTokens (Stage 6c.1)"
```

---

## Task 4: Add `PasswordResetTokenGenerator` helper

**Files:**
- Create: `ProjectCeres/Common/Authentication/PasswordResetTokenGenerator.cs`

- [ ] **Step 1: Write the class (mirrors `PersistentTokenService`)**

```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Generates 256-bit RNG password-reset tokens, base64url-encoded, and hashes them
/// via the project Argon2id hasher. Verify reuses the same hasher so timing matches
/// the rest of the auth pipeline.
/// </summary>
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

    public string Hash(string token) =>
        _hasher.HashPassword(new ApplicationUser(), token);

    public bool Verify(string token, string storedHash)
    {
        var result = _hasher.VerifyHashedPassword(new ApplicationUser(), storedHash, token);
        return result is PasswordVerificationResult.Success
                       or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Common/Authentication/PasswordResetTokenGenerator.cs
git commit -m "feat(auth): add PasswordResetTokenGenerator (Stage 6c.1)"
```

---

## Task 5: Add `IEmailService` abstraction + dev/test implementations

**Files:**
- Create: `ProjectCeres/Common/Email/EmailMessage.cs`
- Create: `ProjectCeres/Common/Email/IEmailService.cs`
- Create: `ProjectCeres/Common/Email/LogOnlyEmailService.cs`
- Create: `ProjectCeres/Common/Email/NoopEmailService.cs`

- [ ] **Step 1: Write `EmailMessage`**

```csharp
namespace ProjectCeres.Common.Email;

public sealed record EmailMessage(string To, string Subject, string BodyHtml, string BodyText);
```

- [ ] **Step 2: Write `IEmailService`**

```csharp
namespace ProjectCeres.Common.Email;

public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
```

- [ ] **Step 3: Write `LogOnlyEmailService`**

```csharp
using Microsoft.Extensions.Logging;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Development implementation: writes the message at Information level.
/// Dev/test workflows can grab the body (including a password-reset URL) from
/// console output. Stage 8 will replace this with a real provider.
/// </summary>
public sealed class LogOnlyEmailService : IEmailService
{
    private readonly ILogger<LogOnlyEmailService> _logger;

    public LogOnlyEmailService(ILogger<LogOnlyEmailService> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "[email/dev] To={To} Subject={Subject} BodyText={BodyText}",
            message.To, message.Subject, message.BodyText);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Write `NoopEmailService`**

```csharp
namespace ProjectCeres.Common.Email;

/// <summary>
/// Test fixture: silent no-op. Use via ConfigureTestServices when a test does
/// not care about the email side-effect. Tests that care about email should
/// use a strict Moq instead.
/// </summary>
public sealed class NoopEmailService : IEmailService
{
    public Task SendAsync(EmailMessage message, CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 5: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Common/Email/
git commit -m "feat(email): add IEmailService abstraction + LogOnly/Noop impls (Stage 6c.1)"
```

---

## Task 6: Add `PasswordResetUnknownEmail` to `FailedLoginReason`

**Files:**
- Modify: `ProjectCeres/Models/FailedLoginAttempt.cs`

- [ ] **Step 1: Add enum value**

In the `FailedLoginReason` enum, add `PasswordResetUnknownEmail` as the last member (preserves stable ordering for existing rows persisted as strings via `HasConversion<string>`):

```csharp
public enum FailedLoginReason
{
    BadCredentials,
    BadTotp,
    BadBackupCode,
    LockedOut,
    UnknownUser,
    PasswordResetUnknownEmail,
}
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Models/FailedLoginAttempt.cs
git commit -m "feat(auth): add PasswordResetUnknownEmail to FailedLoginReason (Stage 6c.1)"
```

---

## Task 7: Add rate-limit constants

**Files:**
- Modify: `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs`

- [ ] **Step 1: Append constants**

After the existing `AuthMfaByUser` constant, add:

```csharp
/// <summary>5/hour per email sliding window. Applied via service-side MemoryCache gate
/// inside PasswordResetService.RequestAsync. Keyed by lowercased trimmed email so a
/// single account can't be spammed with reset emails. Stage 6c.1.</summary>
public const string AuthPasswordResetByEmail = "auth-password-reset-by-email";

/// <summary>Fallback partition key for /password-reset/request with malformed email.
/// Routes them into a single shared bucket so an attacker can't dodge the limit by
/// sending junk. Stage 6c.1.</summary>
public const string AnonymousPasswordResetPartition = "anonymous-password-reset";
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs
git commit -m "feat(auth): add password-reset rate-limit constants (Stage 6c.1)"
```

---

## Task 8: Add request/confirm DTOs

**Files:**
- Create: `ProjectCeres/ViewModels/Auth/PasswordResetRequest.cs`
- Create: `ProjectCeres/ViewModels/Auth/PasswordResetConfirmRequest.cs`

- [ ] **Step 1: Write `PasswordResetRequest`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class PasswordResetRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = "";
}
```

- [ ] **Step 2: Write `PasswordResetConfirmRequest`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class PasswordResetConfirmRequest
{
    [Required]
    [StringLength(128)]
    public string Token { get; set; } = "";

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = "";

    /// <summary>Six-digit TOTP code. Required when the user has TwoFactorEnabled = true.
    /// Backup codes are NOT accepted in the reset flow per ADR-0069.</summary>
    [StringLength(8)]
    public string? TotpCode { get; set; }
}
```

- [ ] **Step 3: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/ViewModels/Auth/PasswordResetRequest.cs ProjectCeres/ViewModels/Auth/PasswordResetConfirmRequest.cs
git commit -m "feat(auth): add password-reset DTOs (Stage 6c.1)"
```

---

## Task 9: Add `PasswordResetConfirmOutcome` discriminated union

**Files:**
- Create: `ProjectCeres/Common/Authentication/PasswordResetConfirmOutcome.cs`

- [ ] **Step 1: Write the type**

```csharp
using Microsoft.AspNetCore.Identity;

namespace ProjectCeres.Common.Authentication;

public abstract record PasswordResetConfirmOutcome
{
    public sealed record Success : PasswordResetConfirmOutcome;
    public sealed record InvalidToken : PasswordResetConfirmOutcome;
    public sealed record RequiresTotp : PasswordResetConfirmOutcome;
    public sealed record InvalidTotp : PasswordResetConfirmOutcome;
    public sealed record PasswordPolicyViolation(IReadOnlyList<IdentityError> Errors) : PasswordResetConfirmOutcome;
}
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Common/Authentication/PasswordResetConfirmOutcome.cs
git commit -m "feat(auth): add PasswordResetConfirmOutcome union (Stage 6c.1)"
```

---

## Task 10: Write `PasswordResetService` skeleton with `RequestAsync`

**Files:**
- Modify: `ProjectCeres/Common/Authentication/PasswordResetService.cs` (create)

This task implements only `RequestAsync`. `ConfirmAsync` is added in Task 12 / 14.

- [ ] **Step 1: Write the service**

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Orchestrates the password-reset flow per docs/superpowers/specs/2026-05-10-password-reset-design.md.
/// Per-user semaphore mirrors AuthController._loginLocks. Per-email rate gate is service-side
/// (MemoryCache); per-IP gate is the existing AuthLoginByIp policy applied at the controller.
/// </summary>
public sealed class PasswordResetService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _emailLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _argon;
    private readonly PasswordResetTokenGenerator _tokens;
    private readonly TotpReplayGuard _replayGuard;
    private readonly FailedLoginRecorder _failedLogins;
    private readonly IEmailService _email;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        PasswordResetTokenGenerator tokens,
        TotpReplayGuard replayGuard,
        FailedLoginRecorder failedLogins,
        IEmailService email,
        IMemoryCache cache,
        ILogger<PasswordResetService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _replayGuard = replayGuard;
        _failedLogins = failedLogins;
        _email = email;
        _cache = cache;
        _logger = logger;
    }

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds) =>
            RetryAfterSeconds = retryAfterSeconds;
    }

    public async Task RequestAsync(
        string email, string ip, string userAgent, string resetUrlBase, CancellationToken ct)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            _argon.RunDummyHash();
            return;
        }

        // Per-email rate gate (5/hour). Holds counters in MemoryCache keyed by email.
        // Throws RateLimitedException; controller maps to 429.
        EnforceEmailRateLimit(normalized);

        var user = await _userManager.FindByEmailAsync(normalized);

        // Always pay the Argon2id cost — equalise wall-clock time across known/unknown branches.
        _argon.RunDummyHash();

        if (user is null)
        {
            await _failedLogins.RecordAsync(
                normalized, null, FailedLoginReason.PasswordResetUnknownEmail, ip, userAgent, ct);
            return;
        }

        var sem = _userLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        string rawToken;
        try
        {
            // Supersede prior unused tokens.
            await _db.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            rawToken = _tokens.Generate();
            var hash = _tokens.Hash(rawToken);

            var now = DateTime.UtcNow;
            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = hash,
                CreatedAt = now,
                ExpiresAt = now + TokenLifetime,
                ConsumedAt = null,
                MfaVerifiedAt = null,
            });
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            sem.Release();
        }

        var resetUrl = $"{resetUrlBase.TrimEnd('/')}/app/password-reset#token={rawToken}";

        try
        {
            await _email.SendAsync(BuildRequestEmail(user.Email!, resetUrl), ct);
        }
        catch (Exception ex)
        {
            // Per spec: failed sends are logged but never block the user-facing request.
            _logger.LogError(ex, "Failed to send password-reset email; token row already committed.");
        }
    }

    private void EnforceEmailRateLimit(string normalizedEmail)
    {
        // Sliding-window-ish: if the cached counter for this email reaches the limit,
        // reject. Cache TTL = the window length, so the bucket auto-resets.
        var sem = _emailLocks.GetOrAdd(normalizedEmail, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        try
        {
            var key = $"pwreset:rate:{normalizedEmail}";
            var entry = _cache.Get<RateBucket>(key);
            if (entry is null || entry.WindowStart + EmailRateWindow <= DateTime.UtcNow)
            {
                entry = new RateBucket { WindowStart = DateTime.UtcNow, Count = 1 };
                _cache.Set(key, entry, EmailRateWindow);
                return;
            }

            if (entry.Count >= EmailRateLimit)
            {
                var elapsed = DateTime.UtcNow - entry.WindowStart;
                var remaining = (int)Math.Ceiling((EmailRateWindow - elapsed).TotalSeconds);
                throw new RateLimitedException(Math.Max(remaining, 1));
            }

            entry.Count += 1;
            _cache.Set(key, entry, entry.WindowStart + EmailRateWindow - DateTime.UtcNow);
        }
        finally
        {
            sem.Release();
        }
    }

    private sealed class RateBucket
    {
        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
    }

    private static EmailMessage BuildRequestEmail(string to, string resetUrl)
    {
        const string subject = "Reset your Project Ceres password";
        var bodyText = $"""
            We received a request to reset your Project Ceres password.

            Click or paste this link into your browser to set a new password:
            {resetUrl}

            This link expires in 15 minutes and can only be used once.
            If you did not request a reset, you can ignore this email.
            """;
        var bodyHtml = $"""
            <p>We received a request to reset your Project Ceres password.</p>
            <p><a href="{resetUrl}">Reset your password</a></p>
            <p>This link expires in 15 minutes and can only be used once.</p>
            <p>If you did not request a reset, you can ignore this email.</p>
            """;
        return new EmailMessage(to, subject, bodyHtml, bodyText);
    }
}
```

- [ ] **Step 2: Verify project compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Common/Authentication/PasswordResetService.cs
git commit -m "feat(auth): PasswordResetService.RequestAsync (Stage 6c.1)"
```

---

## Task 11: Wire DI registrations + endpoint shell

**Files:**
- Modify: `ProjectCeres/Program.cs`
- Create: `ProjectCeres/Controllers/Api/PasswordResetController.cs`

- [ ] **Step 1: Add DI registrations**

In `Program.cs`, locate the auth-services block (search for `PersistentTokenService`). After the `PersistentTokenService` registration, add:

```csharp
builder.Services.AddScoped<PasswordResetTokenGenerator>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddMemoryCache();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IEmailService, LogOnlyEmailService>();
}
// Production deliberately has no IEmailService implementation registered.
// DI will throw at startup until Stage 8 wires the real provider.
```

Also ensure the relevant `using` directives are present:

```csharp
using ProjectCeres.Common.Email;
```

- [ ] **Step 2: Write the controller shell (request endpoint only — confirm comes in Task 13)**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/password-reset")]
public sealed class PasswordResetController : ControllerBase
{
    private readonly PasswordResetService _service;

    public PasswordResetController(PasswordResetService service) => _service = service;

    [HttpPost("request"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Request([FromBody] PasswordResetRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();
        var resetUrlBase = $"{Request.Scheme}://{Request.Host}";

        try
        {
            await _service.RequestAsync(request.Email, ip, ua, resetUrlBase, HttpContext.RequestAborted);
        }
        catch (PasswordResetService.RateLimitedException ex)
        {
            Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = new { code = "RATE_LIMITED", message = "Too many requests. Please retry shortly." }
            });
        }

        return NoContent();
    }
}
```

- [ ] **Step 3: Verify project builds and runs**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

Run a quick local smoke (optional, kills via Ctrl+C): `dotnet run --project ProjectCeres &` then `curl -X POST http://localhost:5000/api/auth/password-reset/request -H 'Content-Type: application/json' -d '{"email":"nobody@example.com"}'`. Expected: `204 No Content`.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Program.cs ProjectCeres/Controllers/Api/PasswordResetController.cs
git commit -m "feat(auth): wire PasswordResetController + DI (Stage 6c.1)"
```

---

## Task 12: Tests for `RequestAsync` — `PasswordResetRequestTests.cs`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/PasswordResetRequestTests.cs`

This file gathers the request-only ship-gate tests: #1, #5 (deferred to confirm tests), #7, #8, #9, #14, #16, #46. Test #5 (`Confirm_no_mfa_promotes_EmailConfirmed_when_previously_false`) lives in the no-MFA confirm test file. Test #16 (UserBlockedIpMiddleware fires) belongs here because it's a request-side middleware concern.

- [ ] **Step 1: Add the test class skeleton + happy path**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

public class PasswordResetRequestTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetRequestTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task Request_with_known_email_issues_token_and_sends_email()
    {
        // Use a strict mock so any unanticipated call fails the test.
        var emailMock = new Mock<IEmailService>(MockBehavior.Strict);
        emailMock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var client = factory.CreateClient();

        var email = $"req-known-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id)
            .SingleAsync(Timeout30s());

        token.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(14));
        token.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddMinutes(16));
        token.ConsumedAt.Should().BeNull();
        token.MfaVerifiedAt.Should().BeNull();

        emailMock.Verify(e => e.SendAsync(
            It.Is<EmailMessage>(m => m.To == email && m.Subject.Contains("Reset")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

> **Note on `WithReplacedService`:** the existing test factory has a helper for swapping a single registered service. If it doesn't exist by that name, fall back to subclassing the factory inline with `ConfigureTestServices(s => { s.RemoveAll<IEmailService>(); s.AddSingleton(emailMock.Object); })`. Verify by searching for `WithReplacedService` in `ProjectCeres.Tests/Integration/`; if absent, refactor the test setup with the inline form.

- [ ] **Step 2: Run the test and verify it passes**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetRequestTests.Request_with_known_email_issues_token_and_sends_email --blame-hang-timeout 120s --nologo`
Expected: 1 passed.

- [ ] **Step 3: Add timing-equality test (#7)**

Append:

```csharp
    [Fact]
    public async Task Request_with_unknown_email_returns_204_with_same_timing()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var knownEmail = $"req-known-timing-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, knownEmail);

        // Warm-up: discard the first measurement of each branch (JIT, cache fill).
        await Hit(client, knownEmail);
        await Hit(client, $"unknown-{Guid.NewGuid():N}@example.com");

        var knownTimings = new List<long>();
        var unknownTimings = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            knownTimings.Add(await Measure(client, knownEmail));
            unknownTimings.Add(await Measure(client, $"unknown-{Guid.NewGuid():N}@example.com"));
        }

        var meanKnown = knownTimings.Average();
        var meanUnknown = unknownTimings.Average();
        var diffMs = Math.Abs(meanKnown - meanUnknown);

        diffMs.Should().BeLessThan(50,
            "constant-time defence requires |mean diff| < 50ms; got known={0}ms unknown={1}ms",
            meanKnown, meanUnknown);
    }

    private async Task<long> Measure(HttpClient client, string email)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await Hit(client, email);
        sw.Stop();
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return sw.ElapsedMilliseconds;
    }

    private Task<HttpResponseMessage> Hit(HttpClient client, string email) =>
        AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/password-reset/request", new { email });
```

- [ ] **Step 4: Run the test**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetRequestTests.Request_with_unknown_email_returns_204_with_same_timing --blame-hang-timeout 120s --nologo`
Expected: 1 passed. If it fails with timing > 50ms, do not relax the threshold — investigate whether `RunDummyHash` is being called on every branch.

- [ ] **Step 5: Add the no-DB-write + no-email-send tests (#8, #9)**

```csharp
    [Fact]
    public async Task Request_with_unknown_email_does_not_create_db_row()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var unknownEmail = $"never-registered-{Guid.NewGuid():N}@example.com";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PasswordResetTokens.AnyAsync(Timeout30s())).Should().BeFalse(
                "test pre-condition: no rows for the unknown email");
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email = unknownEmail });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PasswordResetTokens.AnyAsync(Timeout30s())).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Request_with_unknown_email_does_not_send_email()
    {
        var emailMock = new Mock<IEmailService>(MockBehavior.Strict);
        // No Setup — strict mock fails on any call.

        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var client = factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request",
            new { email = $"unknown-{Guid.NewGuid():N}@example.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        emailMock.VerifyNoOtherCalls();
    }
```

- [ ] **Step 6: Run both tests**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~PasswordResetRequestTests.Request_with_unknown" --blame-hang-timeout 120s --nologo`
Expected: 2 passed.

- [ ] **Step 7: Add PII-redaction tests (#14)**

```csharp
    [Fact]
    public async Task Request_does_not_log_user_email_or_password_to_default_logger()
    {
        var capturedLogs = new List<string>();
        await using var factory = _factory.WithCapturedLogger(capturedLogs)
                                          .WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var email = $"pii-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, email);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The LogOnlyEmailService DOES log the body, but only at Information when active.
        // The default logger (production) is the one we assert against. With NoopEmailService
        // the email body never reaches a logger at all.
        capturedLogs.Should().NotContain(s => s.Contains(email),
            "no production code path should log the user's email");
    }
```

> **`WithCapturedLogger`:** if the test factory does not already expose a captured-logger helper, add one in this task. Implement it as `ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(new InMemoryLoggerProvider(capturedLogs)));` where `InMemoryLoggerProvider` writes each log message into the supplied list. Place it in `ProjectCeres.Tests/Integration/InMemoryLoggerProvider.cs`.

- [ ] **Step 8: Run the test**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetRequestTests.Request_does_not_log_user_email --blame-hang-timeout 120s --nologo`
Expected: 1 passed.

- [ ] **Step 9: Add UserBlockedIp middleware test (#16) and constant-time dummy-hash test (#46)**

```csharp
    [Fact]
    public async Task Request_is_subject_to_user_blocked_ip_middleware()
    {
        // Register a user, then add a blocked-IP entry pointing at the test client's loopback IP.
        // Subsequent /password-reset/request from that IP must be 403 (UserBlockedIpMiddleware fires
        // before the controller).
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var email = $"blocked-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserBlockedIps.Add(new UserBlockedIp
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                IpAddress = "127.0.0.1",
                Reason = "test",
                BlockedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Request_for_unknown_email_runs_argon2_dummy_hash()
    {
        // Verify the timing path: the unknown branch calls RunDummyHash. We assert by
        // measuring elapsed time — RunDummyHash takes ~100ms by Argon2id design at the
        // pinned m=19456,t=2,p=1 parameters, so a request that omits it would land
        // closer to single-digit ms.
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request",
            new { email = $"unknown-{Guid.NewGuid():N}@example.com" });
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(50,
            "Argon2id dummy hash should dominate wall-clock time on the unknown branch");
    }
}
```

- [ ] **Step 10: Run the full file**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetRequestTests --blame-hang-timeout 120s --nologo`
Expected: 7 passed.

- [ ] **Step 11: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/PasswordResetRequestTests.cs ProjectCeres.Tests/Integration/InMemoryLoggerProvider.cs
git commit -m "test(auth): password-reset request endpoint ship-gate tests (Stage 6c.1)"
```

---

## Task 13: Implement `PasswordResetService.ConfirmAsync` no-MFA path + controller `/confirm` endpoint

**Files:**
- Modify: `ProjectCeres/Common/Authentication/PasswordResetService.cs`
- Modify: `ProjectCeres/Controllers/Api/PasswordResetController.cs`

- [ ] **Step 1: Add `ConfirmAsync` to the service**

Append to `PasswordResetService`:

```csharp
public async Task<PasswordResetConfirmOutcome> ConfirmAsync(
    string rawToken, string newPassword, string? totpCode, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(rawToken))
    {
        // Constant-time: still pay one Argon2 verify against a dummy hash.
        _argon.RunDummyHash();
        return new PasswordResetConfirmOutcome.InvalidToken();
    }

    var now = DateTime.UtcNow;
    var candidates = await _db.PasswordResetTokens
        .Where(t => t.ConsumedAt == null && t.ExpiresAt > now)
        .ToListAsync(ct);

    PasswordResetToken? match = null;
    foreach (var candidate in candidates)
    {
        if (_tokens.Verify(rawToken, candidate.TokenHash))
        {
            match = candidate;
            break;
        }
    }

    if (match is null)
    {
        // Even with zero candidates, run one verify so timing doesn't reveal "no candidates".
        if (candidates.Count == 0) _argon.RunDummyHash();
        return new PasswordResetConfirmOutcome.InvalidToken();
    }

    var sem = _userLocks.GetOrAdd(match.UserId, _ => new SemaphoreSlim(1, 1));
    await sem.WaitAsync(ct);
    try
    {
        // Re-read the token row inside the lock; another concurrent caller may have consumed it.
        var current = await _db.PasswordResetTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
        if (current is null || current.ConsumedAt != null || current.ExpiresAt <= DateTime.UtcNow)
        {
            return new PasswordResetConfirmOutcome.InvalidToken();
        }

        var user = await _userManager.FindByIdAsync(match.UserId.ToString());
        if (user is null)
        {
            return new PasswordResetConfirmOutcome.InvalidToken();
        }

        // MFA gate: if user has TwoFactorEnabled, a totpCode is required.
        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(totpCode))
            {
                return new PasswordResetConfirmOutcome.RequiresTotp();
            }

            // Backup codes are NOT accepted at reset time per ADR-0069.
            // The reset endpoint accepts only six-digit authenticator-app codes.
            if (!MfaConstants.TotpCodeShape.IsMatch(totpCode))
            {
                return new PasswordResetConfirmOutcome.InvalidTotp();
            }

            var ok = await _userManager.VerifyTwoFactorTokenAsync(
                user, Microsoft.AspNetCore.Identity.TokenOptions.DefaultAuthenticatorProvider, totpCode);
            if (!ok)
            {
                return new PasswordResetConfirmOutcome.InvalidTotp();
            }

            var accepted = await _replayGuard.TryAcceptAsync(user.Id, totpCode, ct);
            if (!accepted)
            {
                return new PasswordResetConfirmOutcome.InvalidTotp();
            }

            current.MfaVerifiedAt = DateTime.UtcNow;
        }

        // Password write: remove + add (re-runs all Identity password validators).
        var remove = await _userManager.RemovePasswordAsync(user);
        if (!remove.Succeeded)
        {
            return new PasswordResetConfirmOutcome.PasswordPolicyViolation(remove.Errors.ToList());
        }
        var add = await _userManager.AddPasswordAsync(user, newPassword);
        if (!add.Succeeded)
        {
            // Token NOT consumed; user can retry with a different password.
            return new PasswordResetConfirmOutcome.PasswordPolicyViolation(add.Errors.ToList());
        }

        // Promote EmailConfirmed (the email itself is proof of address ownership).
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        // Mark token consumed.
        await _db.PasswordResetTokens
            .Where(t => t.Id == match.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

        // Bulk-revoke all sessions for this user.
        await _db.UserSessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow), ct);

        // SecurityStamp regen — invalidates any in-flight Identity cookies via SecurityStampValidator.
        await _userManager.UpdateSecurityStampAsync(user);

        // Clear lockout if any.
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        // Clear any half-authenticated MFA-pending cookie on the caller's browser.
        await _signInManager.SignOutAsync();

        // Notification email — never blocks the return.
        try
        {
            await _email.SendAsync(BuildChangedEmail(user.Email!), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password-changed notification email.");
        }

        return new PasswordResetConfirmOutcome.Success();
    }
    finally
    {
        sem.Release();
    }
}

private static EmailMessage BuildChangedEmail(string to)
{
    const string subject = "Your Project Ceres password was changed";
    const string bodyText = """
        Your Project Ceres password was just changed.

        If this was you, no further action is needed. All other active sessions
        have been signed out as a precaution.

        If you did not change your password, contact support immediately.
        """;
    var bodyHtml = """
        <p>Your Project Ceres password was just changed.</p>
        <p>If this was you, no further action is needed. All other active
        sessions have been signed out as a precaution.</p>
        <p>If you did not change your password, contact support immediately.</p>
        """;
    return new EmailMessage(to, subject, bodyHtml, bodyText);
}
```

Add the missing `using` if necessary:

```csharp
using ProjectCeres.Common.Authentication; // already in same namespace; for MfaConstants only
```

`MfaConstants` already lives in this namespace; no extra using needed.

- [ ] **Step 2: Add `/confirm` endpoint to controller**

Append to `PasswordResetController`:

```csharp
[HttpPost("confirm"), AllowAnonymous]
[EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
public async Task<IActionResult> Confirm([FromBody] PasswordResetConfirmRequest request)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);

    var outcome = await _service.ConfirmAsync(
        request.Token, request.NewPassword, request.TotpCode, HttpContext.RequestAborted);

    return outcome switch
    {
        PasswordResetConfirmOutcome.Success => NoContent(),
        PasswordResetConfirmOutcome.RequiresTotp => Ok(new { requiresTotp = true }),
        PasswordResetConfirmOutcome.InvalidToken =>
            Unauthorized(new { error = new { code = "INVALID_RESET_TOKEN", message = "The reset link is invalid or has expired." } }),
        PasswordResetConfirmOutcome.InvalidTotp =>
            Unauthorized(new { error = new { code = "INVALID_MFA_CODE", message = "The verification code is invalid or expired." } }),
        PasswordResetConfirmOutcome.PasswordPolicyViolation policyOutcome =>
            UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "The new password does not meet the policy.",
                    details = policyOutcome.Errors
                        .Select(e => new { field = "newPassword", message = e.Description })
                        .ToArray(),
                }
            }),
        _ => throw new InvalidOperationException($"unhandled outcome: {outcome.GetType().Name}"),
    };
}
```

- [ ] **Step 3: Verify project builds**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v q`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Common/Authentication/PasswordResetService.cs ProjectCeres/Controllers/Api/PasswordResetController.cs
git commit -m "feat(auth): PasswordResetService.ConfirmAsync + /confirm endpoint (Stage 6c.1)"
```

---

## Task 14: Tests for no-MFA confirm — `PasswordResetConfirmNoMfaTests.cs`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/PasswordResetConfirmNoMfaTests.cs`

This file covers tests #2, #4, #5, #6, #10, #15, #17, #18, #20, #21, #38, #39, #45.

> **Helper for grabbing the reset token:** the `LogOnlyEmailService` writes the body to `ILogger`. For tests, use the strict-mock pattern: capture the `EmailMessage` argument and parse the `BodyText` for `token=...`. A small extraction helper in `AuthTestFixture` keeps tests clean.

- [ ] **Step 1: Add token-extraction helper to `AuthTestFixture`**

In `AuthTestFixture.cs`, add:

```csharp
public static string ExtractResetTokenFromMessage(EmailMessage message)
{
    var marker = "token=";
    var idx = message.BodyText.IndexOf(marker, StringComparison.Ordinal);
    if (idx < 0) throw new InvalidOperationException($"no token marker in body: {message.BodyText}");
    var start = idx + marker.Length;
    var end = message.BodyText.IndexOfAny(new[] { '\r', '\n', ' ' }, start);
    return end < 0 ? message.BodyText[start..] : message.BodyText[start..end];
}
```

Add `using ProjectCeres.Common.Email;` at the top if not already present.

- [ ] **Step 2: Add the happy path (#2) and lockout-clearing (#4)**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

public class PasswordResetConfirmNoMfaTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetConfirmNoMfaTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private (Mock<IEmailService> mock, List<EmailMessage> captured) StrictEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                captured.Add(m);
                return Task.CompletedTask;
            });
        return (mock, captured);
    }

    private async Task<(string email, string token)> RequestResetAsync(
        AuthTestWebApplicationFactory factory, HttpClient client, List<EmailMessage> captured, string? email = null)
    {
        email ??= $"reset-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, email);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().HaveCount(1);
        return (email, AuthTestFixture.ExtractResetTokenFromMessage(captured[0]));
    }

    [Fact]
    public async Task Confirm_no_mfa_succeeds()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 1) Password actually changed: old password fails, new password succeeds.
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();
            (await userManager.CheckPasswordAsync(user!, AuthTestFixture.ValidPassword)).Should().BeFalse();
            (await userManager.CheckPasswordAsync(user!, "fresh horse battery staple")).Should().BeTrue();
        }

        // 2) Token consumed.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens.SingleAsync(Timeout30s());
            row.ConsumedAt.Should().NotBeNull();
        }

        // 3) Notification email queued.
        captured.Should().HaveCount(2);
        captured[1].Subject.Should().Contain("password was changed");
    }

    [Fact]
    public async Task Confirm_clears_lockout()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        // Lock the account by hand.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            user.Should().NotBeNull();
            for (var i = 0; i < 10; i++) await um.AccessFailedAsync(user!);
            (await um.IsLockedOutAsync(user!)).Should().BeTrue();
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            (await um.IsLockedOutAsync(user!)).Should().BeFalse();
            (await um.GetAccessFailedCountAsync(user!)).Should().Be(0);
        }
    }
}
```

- [ ] **Step 3: Run those two tests**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~PasswordResetConfirmNoMfaTests.Confirm_no_mfa_succeeds|FullyQualifiedName~PasswordResetConfirmNoMfaTests.Confirm_clears_lockout" --blame-hang-timeout 120s --nologo`
Expected: 2 passed.

- [ ] **Step 4: Add `EmailConfirmed` promotion (#5) + locked-account-still-allowed (#6) tests**

```csharp
    [Fact]
    public async Task Confirm_no_mfa_promotes_EmailConfirmed_when_previously_false()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var email = $"unconfirmed-{Guid.NewGuid():N}@example.com";
        // Register without confirming the email — bypass AuthTestFixture which auto-confirms.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email };
            (await um.CreateAsync(user, AuthTestFixture.ValidPassword)).Succeeded.Should().BeTrue();
            // Do NOT call ConfirmEmailAsync. user.EmailConfirmed = false at this point.
        }

        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);
        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            user!.EmailConfirmed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Confirm_succeeds_when_account_is_currently_locked()
    {
        // Same as Confirm_clears_lockout but specifically asserts the success path
        // is not gated on lockout — the request itself should not be blocked.
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            await um.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1));
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
```

- [ ] **Step 5: Run those two tests**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~PasswordResetConfirmNoMfaTests.Confirm_no_mfa_promotes|FullyQualifiedName~PasswordResetConfirmNoMfaTests.Confirm_succeeds_when_account_is_currently_locked" --blame-hang-timeout 120s --nologo`
Expected: 2 passed.

- [ ] **Step 6: Add password-policy + new-password-not-logged tests (#10, #15)**

```csharp
    [Fact]
    public async Task Confirm_does_not_consume_token_on_password_policy_failure()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        // Submit a too-short password. Policy violation, token NOT consumed.
        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "short" });
        resp1.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens.SingleAsync(Timeout30s());
            row.ConsumedAt.Should().BeNull("token must remain usable after a policy rejection");
        }

        // Retry with a valid password — must succeed.
        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Confirm_does_not_log_new_password_anywhere()
    {
        var capturedLogs = new List<string>();
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithCapturedLogger(capturedLogs)
                                          .WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        var sentinelPassword = $"sentinel-{Guid.NewGuid():N}-passw0rd";
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = sentinelPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        capturedLogs.Should().NotContain(s => s.Contains(sentinelPassword),
            "new password must not appear anywhere in logs");
    }
```

- [ ] **Step 7: Run those two tests**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~PasswordResetConfirmNoMfaTests.Confirm_does_not_consume|FullyQualifiedName~PasswordResetConfirmNoMfaTests.Confirm_does_not_log" --blame-hang-timeout 120s --nologo`
Expected: 2 passed.

- [ ] **Step 8: Add ignore-extraneous-totp (#17), replay (#18), expired (#21), supersession (#20), no-failure-revocation (#39), persistent-cookie (#38), constant-time (#45) tests**

```csharp
    [Fact]
    public async Task Confirm_with_no_mfa_user_ignores_extraneous_totpCode()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (_, token) = await RequestResetAsync(factory, client, captured);

        // No-MFA user submits a totpCode. Service must ignore it and proceed.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = "123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Token_replay_after_success_returns_401()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (_, token) = await RequestResetAsync(factory, client, captured);

        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "another fresh horse" });
        resp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp2.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_RESET_TOKEN");
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (_, token) = await RequestResetAsync(factory, client, captured);

        // Force-expire the row in DB.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens.SingleAsync(Timeout30s());
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task New_request_supersedes_prior_unused_token()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token1) = await RequestResetAsync(factory, client, captured);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().HaveCount(2);
        var token2 = AuthTestFixture.ExtractResetTokenFromMessage(captured[1]);
        token2.Should().NotBe(token1);

        // Old token rejected.
        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = token1, newPassword = "fresh horse battery staple" });
        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // New token accepted.
        var accepted = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = token2, newPassword = "fresh horse battery staple" });
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Failed_confirm_does_not_revoke_any_sessions()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, _) = await RequestResetAsync(factory, client, captured);

        // Plant 3 active sessions for the user.
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await um.FindByEmailAsync(email))!.Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.UserSessions.Add(new UserSession
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    IpCreatedAt = "127.0.0.1",
                    UserAgent = "test",
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    IsPersistent = false,
                });
            }
            await db.SaveChangesAsync(Timeout30s());
        }

        // Submit an invalid token.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = "not-a-real-token", newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // None of the 3 sessions should have been revoked.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var revoked = await db.UserSessions.CountAsync(s => s.UserId == userId && s.RevokedAt != null, Timeout30s());
            revoked.Should().Be(0);
        }
    }

    [Fact]
    public async Task Successful_reset_revokes_persistent_remember_me_cookie_session()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        // Plant a persistent (remember-me) session.
        Guid persistentSessionId;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var userId = (await um.FindByEmailAsync(email))!.Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            persistentSessionId = Guid.NewGuid();
            db.UserSessions.Add(new UserSession
            {
                Id = persistentSessionId,
                UserId = userId,
                IpCreatedAt = "127.0.0.1",
                UserAgent = "test",
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                IsPersistent = true,
                PersistentTokenHash = "fake-hash-for-test",
            });
            await db.SaveChangesAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.UserSessions.FirstAsync(s => s.Id == persistentSessionId, Timeout30s());
            session.RevokedAt.Should().NotBeNull("persistent session must be revoked on reset");
        }
    }

    [Fact]
    public async Task Confirm_with_unknown_token_runs_at_least_one_argon2_verify()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", newPassword = "fresh horse battery staple" });
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(50,
            "Argon2id verify should dominate wall-clock time on the unknown-token branch");
    }
}
```

- [ ] **Step 9: Run all tests in the file**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetConfirmNoMfaTests --blame-hang-timeout 120s --nologo`
Expected: 13 passed.

If any test hangs and `--blame-hang-timeout` fires, read the dump in `TestResults/<id>/Sequence_*.xml`, identify the deadlocked thread, fix the root cause. Do not increase the timeout.

- [ ] **Step 10: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/PasswordResetConfirmNoMfaTests.cs ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs
git commit -m "test(auth): password-reset confirm no-MFA path ship-gate tests (Stage 6c.1)"
```

---

## Task 15: Tests for MFA confirm — `PasswordResetConfirmMfaTests.cs`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/PasswordResetConfirmMfaTests.cs`

This file covers tests #3, #11, #12, #13, #29, #30, #31, #32, #34, #37.

- [ ] **Step 1: Add the file with the happy-path two-step (#3)**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

public class PasswordResetConfirmMfaTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetConfirmMfaTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private (Mock<IEmailService> mock, List<EmailMessage> captured) StrictEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });
        return (mock, captured);
    }

    private async Task<(string email, string token, string seed)> SetupMfaUserAndRequestResetAsync(
        AuthTestWebApplicationFactory factory, HttpClient client, List<EmailMessage> captured)
    {
        var email = $"mfa-reset-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(factory, user);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().HaveCount(1);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);
        return (email, token, seed);
    }

    [Fact]
    public async Task Confirm_with_mfa_two_step_succeeds()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, seed) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        // First confirm: no totpCode → 200 {requiresTotp: true}, token NOT consumed.
        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp1.Content.ReadAsStringAsync()).Should().Contain("requiresTotp");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens.SingleAsync(Timeout30s());
            row.ConsumedAt.Should().BeNull("token must NOT be consumed at the probe step");
        }

        // Second confirm: with TOTP → 204.
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = code });
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens.SingleAsync(Timeout30s());
            row.ConsumedAt.Should().NotBeNull();
            row.MfaVerifiedAt.Should().NotBeNull();
        }
    }
```

- [ ] **Step 2: Add tests #11 (no consume on bad TOTP), #12 (no AccessFailed bump), #13 (no backup-code), #34 (consumed-token error precedence)**

```csharp
    [Fact]
    public async Task Confirm_does_not_consume_token_on_invalid_totp()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (_, token, _) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = "000000" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_MFA_CODE");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens.SingleAsync(Timeout30s());
            row.ConsumedAt.Should().BeNull();
            row.MfaVerifiedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task Confirm_does_not_increment_AccessFailedCount_on_invalid_totp()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, _) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/confirm",
                new { token, newPassword = "fresh horse battery staple", totpCode = "000000" });
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            (await um.GetAccessFailedCountAsync(user!)).Should().Be(0,
                "TOTP miss in reset flow must NOT poison the password lockout counter");
        }
    }

    [Fact]
    public async Task Confirm_does_not_accept_backup_code_in_place_of_totp()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, _) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        // Generate a real backup code via the existing service.
        string backupCode;
        using (var scope = factory.Services.CreateScope())
        {
            var bcs = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            var codes = await bcs.RegenerateAsync(user!.Id, "127.0.0.1", Timeout30s());
            backupCode = codes[0];
        }

        // Submit the backup code as totpCode. Must be rejected.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = backupCode });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_MFA_CODE");

        // Backup code MUST still be unused — we never consumed it on this path.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unused = await db.UserMfaBackupCodes.CountAsync(c => c.UsedAt == null, Timeout30s());
            unused.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task Confirm_with_totp_when_token_already_consumed_returns_INVALID_RESET_TOKEN_not_INVALID_MFA_CODE()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (_, token, seed) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        // Use the token successfully first.
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = code });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Replay with a fresh TOTP code on the consumed token. Must surface
        // the token error, not the MFA error — token check runs first.
        await Task.Delay(31_000); // allow next 30s window so the new code differs
        var freshCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "another fresh horse", totpCode = freshCode });
        resp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp2.Content.ReadAsStringAsync()).Should().Contain("INVALID_RESET_TOKEN");
    }
```

> **Note on the 31-second delay in test #34:** this is the only TOTP test that genuinely needs to cross the 30-second window. Mark it explicitly so reviewers don't see it as accidental.

- [ ] **Step 3: Add MFA-state-changed tests (#29, #30, #31), shared replay (#32), session-fixation cookie clear (#37)**

```csharp
    [Fact]
    public async Task Mfa_disabled_after_request_allows_no_totp_confirm()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, _) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        // Disable MFA between request and confirm.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            await um.SetTwoFactorEnabledAsync(user!, false);
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Mfa_enabled_after_request_requires_totp_confirm()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var email = $"late-mfa-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // Enable MFA after the token was issued.
        await AuthTestFixture.EnrollUserMfaAsync(factory, user);

        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp2.Content.ReadAsStringAsync()).Should().Contain("requiresTotp");
    }

    [Fact]
    public async Task Mfa_re_enrolled_after_request_invalidates_old_authenticator_codes()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, oldSeed) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        // Re-enroll with a new seed.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            await um.SetTwoFactorEnabledAsync(user!, false);
            await um.ResetAuthenticatorKeyAsync(user!);
            // New seed is now in place; we don't need it for the test, just need it to differ.
            await um.SetTwoFactorEnabledAsync(user!, true);
        }

        var oldCode = AuthTestFixture.ComputeCurrentTotpCode(oldSeed);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = oldCode });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirm_with_totp_used_seconds_earlier_in_login_totp_is_replay_rejected()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, seed) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        // Burn a TOTP code via the login-TOTP flow, then attempt to reuse it in reset.
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);

        // Login first leg (password) to set up TwoFactorPending cookie.
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/login", new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var totpLoginResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/login/totp", new { code });
        totpLoginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Now attempt to reuse the same code in reset. TotpReplayGuard rejects.
        var resetResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = code });
        resetResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resetResp.Content.ReadAsStringAsync()).Should().Contain("INVALID_MFA_CODE");
    }

    [Fact]
    public async Task Successful_reset_clears_TwoFactorPending_cookie()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, seed) = await SetupMfaUserAndRequestResetAsync(factory, client, captured);

        // Get the user mid-MFA-pending state by completing only the password step.
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/login", new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        loginResp.Headers.TryGetValues("Set-Cookie", out var setCookies1).Should().BeTrue();
        setCookies1!.Any(c => c.Contains("Identity.TwoFactorUserId")).Should().BeTrue();

        // Now run reset on this same client with the (TwoFactorPending-bearing) cookie.
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = code });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Response must clear the TwoFactorPending cookie.
        resp.Headers.TryGetValues("Set-Cookie", out var setCookies2).Should().BeTrue();
        setCookies2!.Any(c => c.Contains("Identity.TwoFactorUserId") &&
                              (c.Contains("expires=Thu, 01 Jan 1970") || c.Contains("Max-Age=0")))
            .Should().BeTrue("SignOutAsync clears the TwoFactorPending cookie on success");
    }
}
```

- [ ] **Step 4: Run all tests in the file**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetConfirmMfaTests --blame-hang-timeout 180s --nologo`
Expected: 10 passed. (Hang timeout bumped to 180s because the precedence test contains a deliberate 31s `Task.Delay`.)

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/PasswordResetConfirmMfaTests.cs
git commit -m "test(auth): password-reset confirm MFA path ship-gate tests (Stage 6c.1)"
```

---

## Task 16: Concurrency tests — `PasswordResetConcurrencyTests.cs`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/PasswordResetConcurrencyTests.cs`

This file covers tests #19, #22, #23.

- [ ] **Step 1: Write the file**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

public class PasswordResetConcurrencyTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetConcurrencyTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task Concurrent_confirm_with_same_token_only_one_wins()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);

        var email = $"concurrent-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        // Issue a token.
        var clientA = factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientA, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // Two parallel confirm calls with the same token.
        var clientB = factory.CreateClient();
        var task1 = AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientA, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        var task2 = AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientB, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        var results = await Task.WhenAll(task1, task2).WaitAsync(TimeSpan.FromSeconds(30));

        results.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var consumed = await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.ConsumedAt != null).CountAsync(Timeout30s());
            consumed.Should().Be(1);
        }
    }

    [Fact]
    public async Task Token_resolution_uses_token_row_UserId_not_caller_supplied_id()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var emailA = $"isolation-a-{Guid.NewGuid():N}@example.com";
        var emailB = $"isolation-b-{Guid.NewGuid():N}@example.com";
        var userA = await AuthTestFixture.RegisterUserAsync(factory, emailA);
        var userB = await AuthTestFixture.RegisterUserAsync(factory, emailB);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email = emailA });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var tokenA = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // Submit token A. Resolve to user A. User B's password must remain intact.
        var confirm = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = tokenA, newPassword = "fresh horse battery staple" });
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var freshA = await um.FindByEmailAsync(emailA);
            var freshB = await um.FindByEmailAsync(emailB);

            (await um.CheckPasswordAsync(freshA!, "fresh horse battery staple")).Should().BeTrue();
            (await um.CheckPasswordAsync(freshB!, AuthTestFixture.ValidPassword)).Should().BeTrue(
                "user B's password must remain unchanged when only A's token was used");
        }
    }

    [Fact]
    public async Task Concurrent_request_then_confirm_serialises_via_semaphore()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var clientA = factory.CreateClient();
        var clientB = factory.CreateClient();

        var email = $"serialise-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, email);

        // Issue first token.
        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientA, "/api/auth/password-reset/request", new { email });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token1 = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // Fire (a) confirm with token1 and (b) a new request, in parallel.
        var taskConfirm = AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientA, "/api/auth/password-reset/confirm",
            new { token = token1, newPassword = "fresh horse battery staple" });
        var taskRequest = AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientB, "/api/auth/password-reset/request", new { email });

        var results = await Task.WhenAll(taskConfirm, taskRequest).WaitAsync(TimeSpan.FromSeconds(30));
        var confirmResult = results[0];
        var requestResult = results[1];
        requestResult.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm must be either Success (it acquired the lock first) or Unauthorized
        // (the new request superseded it). Either is correct; the assertion is that
        // both serialised cleanly without throwing.
        confirmResult.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Run the file**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetConcurrencyTests --blame-hang-timeout 120s --nologo`
Expected: 3 passed.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/PasswordResetConcurrencyTests.cs
git commit -m "test(auth): password-reset concurrency ship-gate tests (Stage 6c.1)"
```

---

## Task 17: Rate-limit tests — `PasswordResetRateLimitTests.cs`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/PasswordResetRateLimitTests.cs`

This file covers tests #24, #25, #26, #27, #28, #33. Lives in the `RateLimitTests` xUnit collection so partition state doesn't leak across files.

> **Note on test #27:** the per-email service-side gate uses `MemoryCache` keyed on the email. To "reset" the bucket without waiting an hour, we replace the `IMemoryCache` registration in the factory with a fresh instance for that test.

- [ ] **Step 1: Write the file**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class PasswordResetRateLimitTests : IClassFixture<RateLimitedAuthTestWebApplicationFactory>
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;

    public PasswordResetRateLimitTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_per_ip_limit_returns_429_at_11th_attempt()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/request",
                new { email = $"distinct-{i}-{Guid.NewGuid():N}@example.com" });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request",
            new { email = $"distinct-11-{Guid.NewGuid():N}@example.com" });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Request_per_email_limit_returns_429_at_6th_attempt()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService())
                                          .WithFreshMemoryCache();
        var client = factory.CreateClient();

        var email = $"per-email-limit-{Guid.NewGuid():N}@example.com";

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/request", new { email });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Request_per_email_limit_does_not_leak_user_existence()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService())
                                          .WithFreshMemoryCache();
        var client = factory.CreateClient();

        var unknownEmail = $"never-existed-{Guid.NewGuid():N}@example.com";

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/request", new { email = unknownEmail });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email = unknownEmail });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "unknown emails burn the per-email bucket the same way as known ones");
    }

    [Fact]
    public async Task Request_per_email_bucket_resets_after_one_hour()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService())
                                          .WithFreshMemoryCache();
        var client = factory.CreateClient();

        var email = $"reset-bucket-{Guid.NewGuid():N}@example.com";

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/request", new { email });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Drop the entire IMemoryCache to simulate an elapsed window without a 1-hour wait.
        factory.ResetMemoryCache();

        var afterReset = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        afterReset.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Confirm_endpoint_per_ip_limit_returns_429_at_11th_attempt()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/confirm",
                new { token = $"definitely-not-a-token-{i}", newPassword = "something fresh and long" });
            // Not 429 yet — we expect 401 INVALID_RESET_TOKEN.
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = "yet-another", newPassword = "something fresh and long" });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Confirm_first_call_returning_requiresTotp_does_count_against_per_ip_rate_limit()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var email = $"probe-counts-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        await AuthTestFixture.EnrollUserMfaAsync(factory, user);

        var reqResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        reqResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // 10 probe calls (no totpCode → 200 requiresTotp). This burns 10/10 of the IP bucket.
        for (var i = 0; i < 10; i++)
        {
            var probe = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/confirm",
                new { token, newPassword = "fresh horse battery staple" });
            probe.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 11th call returns 429 — pin: probe DOES count.
        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
```

> **`WithFreshMemoryCache` and `ResetMemoryCache`:** these helpers don't yet exist on the test factory. Add them in this task. Implementation: a `ConfigureTestServices(s => { s.RemoveAll<IMemoryCache>(); s.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions())); })` for `WithFreshMemoryCache`; `ResetMemoryCache()` calls into the factory's `Services` and disposes/replaces the singleton via the same swap. If the test factory's `Services` is read-only, the cleanest path is for `ResetMemoryCache` to call `((MemoryCache)factory.Services.GetRequiredService<IMemoryCache>()).Compact(1.0)` — Compact(1.0) flushes 100% of entries. Use that approach if the swap path is awkward.

- [ ] **Step 2: Run the file**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetRateLimitTests --blame-hang-timeout 120s --nologo`
Expected: 6 passed.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/PasswordResetRateLimitTests.cs
git commit -m "test(auth): password-reset rate-limit ship-gate tests (Stage 6c.1)"
```

---

## Task 18: Session-revocation tests — `PasswordResetSessionRevocationTests.cs`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/PasswordResetSessionRevocationTests.cs`

This file covers tests #35, #36.

- [ ] **Step 1: Write the file**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

public class PasswordResetSessionRevocationTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetSessionRevocationTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task Successful_reset_revokes_all_user_sessions()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var email = $"sessrev-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        // Plant 3 active sessions.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.UserSessions.Add(new UserSession
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    IpCreatedAt = $"127.0.0.{i+1}",
                    UserAgent = "test",
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    IsPersistent = i == 2,
                });
            }
            await db.SaveChangesAsync(Timeout30s());
        }

        // Reset.
        await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/password-reset/request", new { email });
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = await db.UserSessions
                .Where(s => s.UserId == user.Id)
                .ToListAsync(Timeout30s());
            sessions.Should().HaveCount(3);
            sessions.Should().OnlyContain(s => s.RevokedAt != null);
        }
    }

    [Fact]
    public async Task Successful_reset_invalidates_in_flight_session_cookie_via_security_stamp()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);

        var email = $"stamp-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        string oldStamp;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByEmailAsync(email);
            oldStamp = await um.GetSecurityStampAsync(fresh!);
        }

        var client = factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/password-reset/request", new { email });
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByEmailAsync(email);
            var newStamp = await um.GetSecurityStampAsync(fresh!);
            newStamp.Should().NotBe(oldStamp,
                "SecurityStamp regen on reset is what invalidates in-flight cookies via SecurityStampValidator");
        }
    }
}
```

- [ ] **Step 2: Run the file**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter FullyQualifiedName~PasswordResetSessionRevocationTests --blame-hang-timeout 120s --nologo`
Expected: 2 passed.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/PasswordResetSessionRevocationTests.cs
git commit -m "test(auth): password-reset session revocation ship-gate tests (Stage 6c.1)"
```

---

## Task 19: Architecture tests

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`

Tests #40, #41, #42, #43, #44.

- [ ] **Step 1: Add tests #40 and #41**

Append to `ArchitectureTests`:

```csharp
[Fact]
public void PasswordResetController_methods_have_correct_attributes()
{
    var type = typeof(ProjectCeres.Controllers.Api.PasswordResetController);
    foreach (var method in new[] { "Request", "Confirm" })
    {
        var mi = type.GetMethod(method);
        mi.Should().NotBeNull($"PasswordResetController must define {method}");
        mi!.GetCustomAttributes(true)
            .Any(a => a is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute)
            .Should().BeTrue($"{method} must be [AllowAnonymous]");
        mi.GetCustomAttributes(true)
            .Any(a => a is Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute)
            .Should().BeTrue($"{method} must be [EnableRateLimiting]");
    }
}

[Fact]
public void PasswordResetController_does_not_call_PasswordHasher_directly()
{
    // The controller field list should hold only PasswordResetService, not Argon2idPasswordHasher.
    var type = typeof(ProjectCeres.Controllers.Api.PasswordResetController);
    var fields = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    fields.Select(f => f.FieldType).Should().NotContain(typeof(ProjectCeres.Common.Authentication.Argon2idPasswordHasher));
}
```

- [ ] **Step 2: Add test #42 (IEmailService impl count) and test #43 (CT discipline)**

```csharp
[Fact]
public void IEmailService_dev_impl_is_LogOnly()
{
    // We don't have access to the live DI container in arch tests, but we can assert
    // the registration shape by inspecting the Program.cs source or by reflecting
    // available implementations. Cleanest: confirm LogOnlyEmailService is the only
    // production-source implementor of IEmailService (Noop is test-only).
    var asm = typeof(ProjectCeres.Common.Email.IEmailService).Assembly;
    var impls = asm.GetTypes()
        .Where(t => typeof(ProjectCeres.Common.Email.IEmailService).IsAssignableFrom(t)
                 && !t.IsAbstract && !t.IsInterface)
        .ToList();
    impls.Should().ContainSingle(t => t == typeof(ProjectCeres.Common.Email.LogOnlyEmailService));
}

[Fact]
public void PasswordResetService_methods_take_CancellationToken()
{
    var type = typeof(ProjectCeres.Common.Authentication.PasswordResetService);
    var publicMethods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .Where(m => m.DeclaringType == type && m.Name is nameof(ProjectCeres.Common.Authentication.PasswordResetService.RequestAsync)
                                                       or nameof(ProjectCeres.Common.Authentication.PasswordResetService.ConfirmAsync));
    foreach (var m in publicMethods)
    {
        var lastParam = m.GetParameters().LastOrDefault();
        lastParam.Should().NotBeNull();
        lastParam!.ParameterType.Should().Be(typeof(CancellationToken),
            $"{m.Name} must end with CancellationToken so callers can cancel the operation");
    }
}
```

- [ ] **Step 3: Add test #44 (no PII in logs at controller level)**

This test exercises the live request — keep it as an end-to-end assertion rather than reflection:

```csharp
[Fact]
public async Task PasswordResetController_actions_dont_log_request_body()
{
    // Implemented as integration assertion: replay a request, then confirm captured
    // logs contain neither the literal email nor the literal password.
    var capturedLogs = new List<string>();
    var factory = new AuthTestWebApplicationFactory()
        .WithCapturedLogger(capturedLogs)
        .WithReplacedService<ProjectCeres.Common.Email.IEmailService>(new ProjectCeres.Common.Email.NoopEmailService());
    await using var f = factory;
    var client = f.CreateClient();

    var email = $"arch-pii-{Guid.NewGuid():N}@example.com";
    await AuthTestFixture.RegisterUserAsync(f, email);
    var sentinelPassword = $"sentinel-{Guid.NewGuid():N}";

    await AuthTestFixture.PostJsonWithCsrfAsync(f, client, "/api/auth/password-reset/request", new { email });
    await AuthTestFixture.PostJsonWithCsrfAsync(f, client, "/api/auth/password-reset/confirm",
        new { token = "irrelevant-token", newPassword = sentinelPassword });

    capturedLogs.Should().NotContain(s => s.Contains(email));
    capturedLogs.Should().NotContain(s => s.Contains(sentinelPassword));
}
```

- [ ] **Step 4: Run all five new tests**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ArchitectureTests.PasswordReset|FullyQualifiedName~ArchitectureTests.IEmailService|FullyQualifiedName~ArchitectureTests.PasswordResetController" --blame-hang-timeout 120s --nologo`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git commit -m "test(auth): password-reset architecture rules (Stage 6c.1)"
```

---

## Task 20: Run the full test suite + close out

**Files:**
- Modify: `docs/api-contract.md`
- Modify: `docs/security-model.md`
- Modify: `docs/roadmap-phase-three.md`
- Modify: `docs/planning-resolved.md`

- [ ] **Step 1: Run the full ProjectCeres.Tests suite**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --blame-hang-timeout 180s --nologo`
Expected: all green; password-reset family contributes ~46 new tests.

If any unrelated test regresses (e.g., login or MFA tests because of DI changes), pause and diagnose — do NOT silence the test.

- [ ] **Step 2: Update `docs/api-contract.md`**

Locate the canonical-error-codes table (search for `INVALID_MFA_CODE`). Add a row immediately after `NO_ENROLLMENT_IN_PROGRESS`:

```markdown
| `INVALID_RESET_TOKEN` | 401 | `POST /api/auth/password-reset/confirm` | Reset token is unknown, malformed, expired, or already consumed. Stage 6c.1. |
```

- [ ] **Step 3: Update `docs/security-model.md`**

Locate the `### Password Reset` section. Add a "Status:" line immediately under the heading:

```markdown
### Password Reset

**Status: ✅ Shipped (Stage 6c.1).** See `docs/superpowers/specs/2026-05-10-password-reset-design.md` and the corresponding implementation plan.
```

- [ ] **Step 4: Update `docs/roadmap-phase-three.md`**

Find the Stage 6 verification block. Flip the password-reset items to `[x]`:

- "Password reset endpoints always run Argon2id hash (against dummy if user not found) — constant-time enumeration prevention" — `[x]` (was `[ ]` deferred to 6c)
- "`/password-reset` endpoint: 10 requests/min/IP minimum, plus per-account rate limit" — `[x]`
- Under "Tests required before Stage 7 begins":
  - "Password reset happy-path integration test (request → email → click link → enter TOTP → set new password → all sessions revoked)" — `[x]`
  - "Password reset enumeration test: same response + timing whether email exists or not" — `[x]`
- Under "Password Reset" sub-block (lines 519–527 in the doc): every item except those that explicitly depend on Stage 8 email infra → `[x]`. (Items requiring real email send remain `[ ]` and are flagged as "shipped at Stage 8".)

Add a status note above the Stage 6c sub-stage table:

```markdown
> **Stage 6c.1 (2026-05-10):** Password-reset flow shipped — 2 endpoints, Argon2id-hashed single-use tokens, MFA-conditional gating, per-IP + per-email rate limits, all-sessions-revoked-on-success, 46 ship-gate tests. Email delivery via dev-only LogOnlyEmailService; Stage 8 swaps in real provider. Stage 6c continues with email-change, reauth middleware, audit log.
```

- [ ] **Step 5: Update `docs/planning-resolved.md`**

Append a row to the resolved-questions section (look for the most recent date entry; add a new entry under 2026-05-10):

```markdown
**2026-05-10 — Stage 6c.1 password reset (shipped):** Token format = 256-bit RNG, base64url, Argon2id-hashed in-DB. Lifetime 15 min, single-use, supersession-on-new-request. MFA gating conditional on TwoFactorEnabled per ADR-0069 (TOTP only — backup codes rejected at reset time). All-sessions-revoked + lockout-cleared + EmailConfirmed-promoted on success. Rate limits: 10/min/IP via existing AuthLoginByIp + 5/hour/email via service-side MemoryCache gate. Email via IEmailService abstraction with LogOnlyEmailService dev impl; Stage 8 will register a real provider. See `docs/superpowers/specs/2026-05-10-password-reset-design.md` and `docs/superpowers/plans/2026-05-10-stage-6c-1-password-reset-plan.md`.
```

- [ ] **Step 6: Run sync-docs as a final pass**

Invoke the `sync-docs` skill from your conversation. Pipe the diff of this branch's changes; expect it to confirm `security-model.md`, `api-contract.md`, `roadmap-phase-three.md`, and `planning-resolved.md` are aligned with the implementation.

- [ ] **Step 7: Commit doc updates**

```bash
git add docs/
git commit -m "docs(sync): post-Stage-6c.1 living-doc sync (api-contract, security-model, roadmap, planning-resolved)"
```

---

## Self-review checklist (run after the plan is written, before handoff)

This was checked at plan-write time:

- **Spec coverage:** every item in spec § 4 (edge cases) has a corresponding test in the plan. Every item in spec § 7 (files created/modified) has a task. Every test in spec § 5.2 (46 numbered tests) is accounted for in tasks 12–19. ✅
- **Placeholder scan:** no `TBD`/`TODO`/`fill in details` strings. The migration filename `<timestamp>` is an EF auto-substitution, not a placeholder. ✅
- **Type consistency:** `PasswordResetConfirmOutcome` shape and case names are stable across Tasks 9, 13, 14, 15. `IEmailService.SendAsync` signature stable across Tasks 5, 10, 13. `PasswordResetService` ctor parameter list stable. ✅
- **Test naming consistency:** test names match between spec § 5.2 and plan tasks 12–19. Spec test #5 was originally listed in two files (Request + ConfirmNoMfa); plan locates it firmly in the no-MFA confirm file. Spec test #16 was originally listed in confirm; plan locates it in request (matches the middleware fire site). Differences are noted inline; behaviour is identical.
- **Helper contract:** `WithReplacedService<T>`, `WithCapturedLogger`, `WithFreshMemoryCache`, `ResetMemoryCache`, `ExtractResetTokenFromMessage` are all surfaced in tasks; if any don't exist on the test factory, the relevant task adds them.
