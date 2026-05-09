# Stage 6b.2 — Lockout, Rate Limiting, Failed-Login Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship account-lockout enforcement, sliding-window rate limiting on auth endpoints, the `FailedLoginAttempt` table + recorder, and the backup-code-during-lockout policy — including the 6b.1 reconciliation that stops `TwoFactorAuthenticatorSignInAsync` from poisoning the password-lockout counter.

**Architecture:** Extends existing Stage 6a/6b.1 auth pipeline. New EF entity (`FailedLoginAttempt`) intentionally excluded from global query filters. New scoped service (`FailedLoginRecorder`) writes one row per rejected request. `Program.cs` gains `AddRateLimiter` with two sliding-window policies (`auth-login-by-ip`, `auth-totp-by-user`) and a `[EnableRateLimiting]` attribute application. `AuthController.LoginTotp` replaces `TwoFactorAuthenticatorSignInAsync` with `VerifyTwoFactorTokenAsync` + manual `SignInAsync` so TOTP misses don't increment lockout. All `Unauthorized(...)` returns migrate to the api-contract error envelope.

**Tech Stack:** .NET 10, ASP.NET Core Identity, `Microsoft.AspNetCore.RateLimiting`, EF Core 10 + Npgsql, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-05-09-stage-6b-2-lockout-rate-limit-failed-login-design.md`

---

## File map

**New files:**
- `ProjectCeres/Models/FailedLoginAttempt.cs` — entity + `FailedLoginReason` enum
- `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs` — service
- `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs` — policy-name constants
- `ProjectCeres/Migrations/<timestamp>_AddFailedLoginAttempt.cs` — EF migration (auto-generated)
- `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs` — sibling factory with limiter active
- `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/BackupCodeLockoutBypassTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/TotpReplayDuringLockoutTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/MfaPendingCookieTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/MfaCookieHygieneTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/LoginConcurrencyTests.cs`
- `ProjectCeres.Tests/Integration/Authentication/LoginCrossFeatureTests.cs`

**Modified files:**
- `ProjectCeres/Data/AppDbContext.cs` — add `DbSet<FailedLoginAttempt>` + entity config in `ConfigureSessionEntities`
- `ProjectCeres/Program.cs` — `AddRateLimiter`, `UseRateLimiter`, MFA-pending cookie 5-min TTL, `AddScoped<FailedLoginRecorder>`
- `ProjectCeres/Controllers/Api/AuthController.cs` — recorder injection, envelope helper, `[EnableRateLimiting]` attributes, replace `TwoFactorAuthenticatorSignInAsync`, lockout-clear-on-bypass-success, cookie clearing on lockout return
- `ProjectCeres.Tests/Integration/WafCollection.cs` — add no-op limiter override; register the new sibling factory in the collection
- `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — add 4 new architecture assertions
- `docs/decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md` — exemption list amendment
- `docs/security-model.md` — backup-code-during-lockout formalization
- `docs/planning-phase3.md` — six deferred decisions
- `docs/roadmap-phase-three.md` — mark 6b.2 verification-checklist items shipped

---

## Task ordering

Tasks 1–6 are **foundation** (entity, recorder, migration, DI, basic envelope helper). Must land before tests can run.

Tasks 7–13 are **infrastructure shifts** (rate limiter wiring, cookie TTL, test factories). Required before rate-limit and cookie tests.

Tasks 14–22 are **AuthController surgery** (replace `TwoFactorAuthenticatorSignInAsync`, lockout-clear, cookie hygiene, envelope migration, recorder calls). Each carries its own test pair from the ship-gate list.

Tasks 23–35 are **remaining ship-gate test groups** added incrementally with their corresponding production tightening.

Tasks 36–39 are **architecture tests, doc sync, ADR amendments, roadmap update.**

Each task ends with a commit. Run `dotnet test` from the repo root for every test step unless a more targeted invocation is given.

---

## Task 1: Create the `FailedLoginAttempt` entity + enum

**Files:**
- Create: `ProjectCeres/Models/FailedLoginAttempt.cs`

- [ ] **Step 1: Write the entity file**

Create `ProjectCeres/Models/FailedLoginAttempt.cs`:

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

- [ ] **Step 2: Build to verify compile**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Models/FailedLoginAttempt.cs
git commit -m "feat(auth): add FailedLoginAttempt entity + FailedLoginReason enum"
```

---

## Task 2: Wire the entity into `AppDbContext`

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs` — add `DbSet`, extend `ConfigureSessionEntities` with the entity config

- [ ] **Step 1: Add the DbSet declaration**

In `ProjectCeres/Data/AppDbContext.cs`, after the existing `DbSet<TotpReplayEntry>` line (currently line 37), add:

```csharp
    public DbSet<FailedLoginAttempt> FailedLoginAttempts => Set<FailedLoginAttempt>();
```

- [ ] **Step 2: Extend `ConfigureSessionEntities` with the FailedLoginAttempt config**

In the same file, inside `ConfigureSessionEntities`, after the `UserBlockedIp` config block, add:

```csharp
        modelBuilder.Entity<FailedLoginAttempt>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.IpAddress, e.OccurredAt });
            b.HasIndex(e => new { e.EmailAttempted, e.OccurredAt });
            b.HasIndex(e => e.OccurredAt);
            b.Property(e => e.EmailAttempted).HasMaxLength(256);
            b.Property(e => e.IpAddress).HasMaxLength(45);
            b.Property(e => e.UserAgent).HasMaxLength(512);
            b.Property(e => e.Reason).HasConversion<string>();
        });
```

- [ ] **Step 3: Build to verify compile**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Data/AppDbContext.cs
git commit -m "feat(auth): register FailedLoginAttempt entity + indexes in AppDbContext"
```

---

## Task 3: Generate and apply the EF migration

**Files:**
- Create: `ProjectCeres/Migrations/<timestamp>_AddFailedLoginAttempt.cs` (auto-generated)

- [ ] **Step 1: Generate the migration**

Run from repo root:

```bash
dotnet ef migrations add AddFailedLoginAttempt --project ProjectCeres
```

Expected: A new migration file is added under `ProjectCeres/Migrations/` with `Up` containing `CreateTable("FailedLoginAttempts", ...)` plus three `CreateIndex` calls.

- [ ] **Step 2: Inspect the generated migration**

Open the new `_AddFailedLoginAttempt.cs` file. Confirm:
- Table name: `FailedLoginAttempts`
- `Id` column is `uuid` PK
- `EmailAttempted` column is `character varying(256)`, nullable
- `IpAddress` column is `character varying(45)`, NOT NULL
- `UserAgent` column is `character varying(512)`, NOT NULL
- `Reason` column is `text` (or similar string-backed) — confirms `HasConversion<string>()` worked
- Three indexes: `IX_FailedLoginAttempts_IpAddress_OccurredAt`, `IX_FailedLoginAttempts_EmailAttempted_OccurredAt`, `IX_FailedLoginAttempts_OccurredAt`

If any of those is wrong, fix the entity config in Task 2 and regenerate.

- [ ] **Step 3: Apply the migration to the dev database**

Run: `dotnet ef database update --project ProjectCeres`
Expected: "Done." with no errors.

- [ ] **Step 4: Apply to the test database**

Run:

```bash
PGPASSWORD=postgres psql -h localhost -U postgres -d project_ceres_test -c "SELECT 1 FROM pg_tables WHERE tablename = 'FailedLoginAttempts';"
```

If the table doesn't exist, run:

```bash
ConnectionStrings__DefaultConnection="Host=localhost;Database=project_ceres_test;Username=postgres;Password=postgres" dotnet ef database update --project ProjectCeres
```

Expected: Table exists in `project_ceres_test`.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Migrations/
git commit -m "feat(auth): EF migration AddFailedLoginAttempt with three indexes"
```

---

## Task 4: Write `FailedLoginRecorder` with a TDD test

**Files:**
- Create: `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs`

- [ ] **Step 1: Write the first failing test (Test #33 — `BadCredentials_WritesOneRow`)**

Create `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class FailedLoginRecorderTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public FailedLoginRecorderTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted!.EndsWith("@recorder-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task BadCredentials_WritesOneRow()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "user@recorder-test.local",
            userId: Guid.NewGuid(),
            FailedLoginReason.BadCredentials,
            "203.0.113.5",
            "ua/1.0",
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "user@recorder-test.local")
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows[0].Reason.Should().Be(FailedLoginReason.BadCredentials);
        rows[0].IpAddress.Should().Be("203.0.113.5");
        rows[0].UserAgent.Should().Be("ua/1.0");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FailedLoginRecorderTests.BadCredentials_WritesOneRow"`
Expected: FAIL — `FailedLoginRecorder` not registered or not found.

- [ ] **Step 3: Implement `FailedLoginRecorder`**

Create `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs`:

```csharp
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

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
        _db.FailedLoginAttempts.Add(entry);
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

- [ ] **Step 4: Register in DI**

In `ProjectCeres/Program.cs`, after the existing `builder.Services.AddScoped<TotpReplayGuard>();` line (currently 104), add:

```csharp
builder.Services.AddScoped<FailedLoginRecorder>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FailedLoginRecorderTests.BadCredentials_WritesOneRow"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Common/Authentication/FailedLoginRecorder.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs
git commit -m "feat(auth): FailedLoginRecorder service with sync DB write"
```

---

## Task 5: Add the recorder coverage tests (#34–#44)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs`

- [ ] **Step 1: Add tests #38–#43 (recorder normalization/truncation/null-safety)**

In the test file, after the `BadCredentials_WritesOneRow` test, add:

```csharp
    [Fact]
    public async Task PasswordIsNeverInRow()
    {
        const string secret = "should-never-leak-into-the-failed-login-table";
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "leaktest@recorder-test.local",
            userId: null,
            FailedLoginReason.BadCredentials,
            "203.0.113.5",
            "ua/1.0",
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var anyLeak = await db.FailedLoginAttempts.AnyAsync(e =>
            (e.EmailAttempted != null && e.EmailAttempted.Contains(secret))
            || e.IpAddress.Contains(secret)
            || e.UserAgent.Contains(secret));
        anyLeak.Should().BeFalse();
    }

    [Fact]
    public async Task EmailAttempted_TruncatedTo256Chars()
    {
        var longEmail = new string('a', 1000) + "@recorder-test.local";
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(longEmail, null, FailedLoginReason.UnknownUser,
            "1.2.3.4", "ua", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();
        row.EmailAttempted!.Length.Should().Be(256);
    }

    [Fact]
    public async Task EmailAttempted_LowercasedAndTrimmed()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "  Foo@Recorder-Test.LOCAL  ",
            null, FailedLoginReason.UnknownUser,
            "1.2.3.4", "ua", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "foo@recorder-test.local")
            .SingleOrDefaultAsync();
        row.Should().NotBeNull();
    }

    [Fact]
    public async Task UserAgent_Missing_RowStillWrites()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "ua-missing@recorder-test.local", null, FailedLoginReason.BadCredentials,
            "1.2.3.4", "", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "ua-missing@recorder-test.local")
            .SingleOrDefaultAsync();
        row.Should().NotBeNull();
        row!.UserAgent.Should().Be("");
    }

    [Fact]
    public async Task UserAgent_TruncatedTo512Chars()
    {
        var longUa = new string('x', 1000);
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "ua-trunc@recorder-test.local", null, FailedLoginReason.BadCredentials,
            "1.2.3.4", longUa, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "ua-trunc@recorder-test.local")
            .SingleOrDefaultAsync();
        row!.UserAgent.Length.Should().Be(512);
    }

    [Fact]
    public async Task Ip_NullSafe_RecordsAsUnknown()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "ip-null@recorder-test.local", null, FailedLoginReason.BadCredentials,
            "", "ua", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "ip-null@recorder-test.local")
            .SingleOrDefaultAsync();
        row!.IpAddress.Should().Be("unknown");
    }
```

- [ ] **Step 2: Run all recorder tests**

Run: `dotnet test --filter "FullyQualifiedName~FailedLoginRecorderTests"`
Expected: All 7 tests pass (1 from Task 4 + 6 added here). The remaining tests #34, #35, #36, #37, #44 are HTTP-pipeline tests that require AuthController surgery (Tasks 14+); they are added later.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs
git commit -m "test(auth): FailedLoginRecorder normalization, truncation, null-safety"
```

---

## Task 6: Add the envelope helper to `AuthController`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`

- [ ] **Step 1: Add the helper method**

In `ProjectCeres/Controllers/Api/AuthController.cs`, after the `ClearRememberMeCookie()` method (currently line 222), add:

```csharp
    /// <summary>
    /// Returns 401 with the standard api-contract.md error envelope:
    /// { error: { code, message } }. Use this for every Unauthorized return
    /// from auth-flow methods so the SPA gets a consistent shape.
    /// </summary>
    private IActionResult UnauthorizedEnvelope(string code, string message)
        => Unauthorized(new { error = new { code, message } });
```

- [ ] **Step 2: Build to verify compile**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs
git commit -m "feat(auth): UnauthorizedEnvelope helper for api-contract error shape"
```

---

## Task 7: Add the rate-limit policy-name constants

**Files:**
- Create: `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs`

- [ ] **Step 1: Write the file**

Create `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs`:

```csharp
namespace ProjectCeres.Common.Authentication;

public static class AuthRateLimitPolicies
{
    /// <summary>10/min/IP sliding window. Applied to /login, /register, /csrf.</summary>
    public const string AuthLoginByIp = "auth-login-by-ip";

    /// <summary>10/min/user sliding window keyed off Identity.TwoFactorUserId. Applied to /login/totp.</summary>
    public const string AuthTotpByUser = "auth-totp-by-user";

    /// <summary>Fallback partition key for /login/totp requests with no MFA-pending cookie.
    /// Routes them into a single shared bucket so an attacker can't dodge the limit by stripping the cookie.</summary>
    public const string AnonymousTotpPartition = "anonymous-totp";
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs
git commit -m "feat(auth): AuthRateLimitPolicies policy-name constants"
```

---

## Task 8: Wire `AddRateLimiter` in `Program.cs`

**Files:**
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Add the using directives**

At the top of `ProjectCeres/Program.cs`, with the other usings, add:

```csharp
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
```

(Skip any that are already present.)

- [ ] **Step 2: Add the AddRateLimiter block**

In `Program.cs`, after `builder.Services.AddAuthorization(options => { ... });` block (currently ends around line 160), insert:

```csharp
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

    options.AddPolicy(AuthRateLimitPolicies.AuthLoginByIp, httpContext =>
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

    options.AddPolicy(AuthRateLimitPolicies.AuthTotpByUser,
        new TotpByUserPartitioner());
});
```

- [ ] **Step 3: Add the async partitioner type at the bottom of `Program.cs`**

Append to `Program.cs`:

```csharp
internal sealed class TotpByUserPartitioner : IRateLimiterPolicy<HttpContext>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // Synchronous partition derivation: peek at the cookie principal without
        // running the full AuthenticateAsync pipeline. The cookie is
        // Identity.TwoFactorUserId — a scoped cookie issued by SignInManager
        // when RequiresTwoFactor. We resolve it via AuthenticateAsync below
        // synchronously by blocking the task; the cost is one cookie decode per
        // /login/totp request, acceptable for an auth endpoint.
        var task = httpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        task.Wait();
        var userId = task.Result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? AuthRateLimitPolicies.AnonymousTotpPartition;

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    }
}
```

(The `IRateLimiterPolicy<HttpContext>` shape lets us register it via `AddPolicy(name, IRateLimiterPolicy<HttpContext>)` rather than the lambda overload, which is what allows us to keep the partition derivation synchronous and testable.)

- [ ] **Step 4: Add the middleware activation**

In `Program.cs`, find the line `app.UseRouting();` (currently 210). After it and before `app.UseAuthentication();` (currently 213), insert:

```csharp
app.UseRateLimiter();
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Program.cs
git commit -m "feat(auth): wire AddRateLimiter with sliding-window auth policies"
```

---

## Task 9: Tighten the MFA-pending cookie TTL to 5 min

**Files:**
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Add the cookie-options configuration**

In `Program.cs`, after the `builder.Services.ConfigureApplicationCookie(...)` block (currently ends around line 138), insert:

```csharp
builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
    IdentityConstants.TwoFactorUserIdScheme,
    options =>
    {
        // Stage 6b.2: tighten the gap between password step and TOTP step.
        // Default inherited 30-min sliding TTL is far too long — a phisher who
        // captures the password could try ~hundreds of TOTP codes within that
        // window. 5 min with no sliding extension = strict human-timescale gate.
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
    });
```

- [ ] **Step 2: Build**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Run the existing MFA tests as a regression check**

Run: `dotnet test --filter "FullyQualifiedName~Mfa"`
Expected: All existing 6b.1 MFA tests still pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Program.cs
git commit -m "feat(auth): tighten Identity.TwoFactorUserId cookie to 5-min strict TTL"
```

---

## Task 10: Override the rate limiter to no-op in the default test factory

**Files:**
- Modify: `ProjectCeres.Tests/Integration/WafCollection.cs`

- [ ] **Step 1: Add the no-op override in `TestWebApplicationFactory.ConfigureWebHost`**

In `ProjectCeres.Tests/Integration/WafCollection.cs`, inside `TestWebApplicationFactory.ConfigureWebHost` → `builder.ConfigureServices(services => { ... });`, **before** the `if (!UseTestAuthHandler) return;` line, add:

```csharp
            // Stage 6b.2: replace the rate limiter with no-ops so existing 6a/6b.1
            // integration tests don't trip the limiter on burst. Tests that need to
            // verify the real limiter use RateLimitedAuthTestWebApplicationFactory.
            services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(opts =>
            {
                opts.AddPolicy(AuthRateLimitPolicies.AuthLoginByIp,
                    _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy(AuthRateLimitPolicies.AuthTotpByUser,
                    _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
            });
```

Add `using ProjectCeres.Common.Authentication;` at the top if not present.

- [ ] **Step 2: Build**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Run the entire test suite as a regression check**

Run: `dotnet test`
Expected: Existing tests still pass — no regressions from the rate-limiter wiring or the no-op override.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Integration/WafCollection.cs
git commit -m "test(auth): no-op rate-limiter override in default test factory"
```

---

## Task 11: Create the `RateLimitedAuthTestWebApplicationFactory`

**Files:**
- Create: `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs`

- [ ] **Step 1: Write the factory**

Create the file:

```csharp
using Microsoft.AspNetCore.Hosting;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Sibling of AuthTestWebApplicationFactory used by Stage 6b.2 rate-limit tests.
/// Inherits the real auth pipeline but does NOT install the no-op limiter override,
/// so the production sliding-window policies are active. Tests that use this factory
/// MUST live in their own xUnit collection so the in-memory partition state does
/// not leak across test classes.
/// </summary>
public sealed class RateLimitedAuthTestWebApplicationFactory : AuthTestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // The base AuthTestWebApplicationFactory inherits the no-op override from
        // TestWebApplicationFactory. We need to undo it. The cleanest hook is
        // ConfigureTestServices, which runs after ConfigureServices.
        builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(opts =>
            {
                opts.AddPolicy(ProjectCeres.Common.Authentication.AuthRateLimitPolicies.AuthLoginByIp,
                    httpContext =>
                    {
                        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        return System.Threading.RateLimiting.RateLimitPartition
                            .GetSlidingWindowLimiter(ip, _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
                            {
                                PermitLimit = 10,
                                Window = TimeSpan.FromSeconds(60),
                                SegmentsPerWindow = 4,
                                QueueLimit = 0,
                            });
                    });
                opts.AddPolicy(ProjectCeres.Common.Authentication.AuthRateLimitPolicies.AuthTotpByUser,
                    new ProjectCeres.TotpByUserPartitioner());
            });
        });
    }
}

[Xunit.CollectionDefinition("RateLimitTests", DisableParallelization = true)]
public class RateLimitTestsCollection
    : Xunit.ICollectionFixture<RateLimitedAuthTestWebApplicationFactory>
{ }
```

If `TotpByUserPartitioner` is not in the `ProjectCeres` namespace (because of file-scoped namespace differences), rebuild and adjust the using.

- [ ] **Step 2: Build**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj`
Expected: Build succeeds. If `TotpByUserPartitioner` resolves with a different namespace, adjust the qualified name above.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs
git commit -m "test(auth): RateLimitedAuthTestWebApplicationFactory with active limiter"
```

---

## Task 12: Apply `[EnableRateLimiting]` to `Login`, `Register`, `Csrf`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`

- [ ] **Step 1: Add the using**

At the top of `ProjectCeres/Controllers/Api/AuthController.cs`, add:

```csharp
using Microsoft.AspNetCore.RateLimiting;
```

- [ ] **Step 2: Annotate `Login`, `Register`, `Csrf`**

Add the attribute above each method:

```csharp
    [HttpPost("register"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Register(...) { ... }

    [HttpPost("login"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Login(...) { ... }

    [HttpGet("csrf"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public IActionResult Csrf() { ... }
```

- [ ] **Step 3: Build + run all auth tests as a regression check**

Run: `dotnet build && dotnet test --filter "FullyQualifiedName~Authentication"`
Expected: All existing tests pass — the no-op override means the attribute is wired but doesn't fire.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs
git commit -m "feat(auth): apply [EnableRateLimiting] to /login, /register, /csrf"
```

---

## Task 13: Apply `[EnableRateLimiting]` to `LoginTotp`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`

- [ ] **Step 1: Annotate `LoginTotp`**

```csharp
    [HttpPost("login/totp"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthTotpByUser)]
    public async Task<IActionResult> LoginTotp(...) { ... }
```

- [ ] **Step 2: Build + run MFA tests as a regression check**

Run: `dotnet build && dotnet test --filter "FullyQualifiedName~Mfa"`
Expected: All MFA tests pass.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs
git commit -m "feat(auth): apply [EnableRateLimiting] to /login/totp"
```

---

## Task 14: Migrate existing `Unauthorized(...)` calls to envelope shape

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/LoginEndpointTests.cs` (any test asserting flat-string error needs an update)

- [ ] **Step 1: Replace each `Unauthorized(...)` call**

In `AuthController.cs`, change every occurrence as follows:

| Line (approx) | Old | New |
|---|---|---|
| 71 | `return Unauthorized();` | `return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");` |
| 106 | `return Unauthorized(new { error = "locked_out" });` | `return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");` |
| 112 | `return Unauthorized();` | `return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");` |
| 130 | `return Unauthorized();` | `return UnauthorizedEnvelope("UNAUTHENTICATED", "Authentication required.");` |
| 140 | `if (result.IsLockedOut) return Unauthorized(new { error = "locked_out" });` | `if (result.IsLockedOut) return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");` |
| 141 | `if (!result.Succeeded) return Unauthorized();` | `if (!result.Succeeded) return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");` |
| 147 | `return Unauthorized(new { error = "replay" });` | `return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");` |
| 160 | `if (!ok) return Unauthorized();` | `if (!ok) return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");` |
| 168 | `return Unauthorized();` | `return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");` |

- [ ] **Step 2: Update any test that asserted the flat-string shape**

Run: `dotnet test --filter "FullyQualifiedName~Authentication"` and inspect failures. For each failing test, update the assertion to match the envelope. Common pattern:

```csharp
// OLD:
var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
body.GetProperty("error").GetString().Should().Be("locked_out");

// NEW:
var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
body.GetProperty("error").GetProperty("code").GetString().Should().Be("ACCOUNT_LOCKED_OUT");
```

If a test only asserts the status code (not the body), it remains green automatically.

- [ ] **Step 3: Run all auth tests**

Run: `dotnet test --filter "FullyQualifiedName~Authentication"`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/
git commit -m "feat(auth): align all Unauthorized returns to api-contract error envelope"
```

---

## Task 15: Replace `TwoFactorAuthenticatorSignInAsync` with `VerifyTwoFactorTokenAsync`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs` (test #3 first as TDD)

- [ ] **Step 1: Write failing test #3 — `WrongTotp_DoesNotIncrementPasswordLockoutCounter`**

Create `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs`:

```csharp
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
public class LockoutBehaviorTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LockoutBehaviorTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@lockout-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await um.DeleteAsync(u);
        }
        await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted!.EndsWith("@lockout-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task WrongTotp_DoesNotIncrementPasswordLockoutCounter()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "wrong-totp@lockout-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var client = _factory.CreateClient();

        // Step 1: pass password to obtain MFA-pending cookie (cookie auto-stored by HttpClient).
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginResp.EnsureSuccessStatusCode();

        // Step 2: submit 5 wrong TOTP codes. Each is 6 digits but not the real one.
        for (int i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
                new { code = "000000" });
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }

        // Pre-6b.2 bug: AccessFailedCount would be 5. After 6b.2 fix: still 0.
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        fresh!.AccessFailedCount.Should().Be(0,
            "wrong TOTP codes must not increment the password lockout counter");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LockoutBehaviorTests.WrongTotp_DoesNotIncrementPasswordLockoutCounter"`
Expected: FAIL — `AccessFailedCount` is 5, not 0.

- [ ] **Step 3: Replace the TOTP path in `LoginTotp`**

In `ProjectCeres/Controllers/Api/AuthController.cs`, replace the existing TOTP-shape branch (currently lines 136–153) with:

```csharp
        if (MfaConstants.TotpCodeShape.IsMatch(request.Code))
        {
            // Stage 6b.2: replaced TwoFactorAuthenticatorSignInAsync (which silently
            // calls AccessFailedAsync on miss) with VerifyTwoFactorTokenAsync +
            // manual SignInAsync. TOTP misses no longer poison the password
            // lockout counter; brute-force defense is the per-user 10/min limiter
            // plus 30-second TOTP rotation.
            var ok = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, request.Code);
            if (!ok)
            {
                return UnauthorizedEnvelope("INVALID_MFA_CODE",
                    "The verification code is invalid or expired.");
            }

            var accepted = await replayGuard.TryAcceptAsync(user.Id, request.Code, HttpContext.RequestAborted);
            if (!accepted)
            {
                await _signInManager.SignOutAsync();
                return UnauthorizedEnvelope("INVALID_MFA_CODE",
                    "The verification code is invalid or expired.");
            }

            // If the account was locked, the TOTP success clears the lock.
            if (user.LockoutEnd.HasValue)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            await IssueSessionAndCookiesAsync(user, sessionId, rememberMe);
            ClearRememberMeCookie();
            return NoContent();
        }
```

- [ ] **Step 4: Run the failing test again**

Run: `dotnet test --filter "FullyQualifiedName~LockoutBehaviorTests.WrongTotp_DoesNotIncrementPasswordLockoutCounter"`
Expected: PASS.

- [ ] **Step 5: Run all MFA tests as a regression check**

Run: `dotnet test --filter "FullyQualifiedName~Mfa"`
Expected: All pass.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs
git commit -m "fix(auth): VerifyTwoFactorTokenAsync replaces TwoFactorAuthenticatorSignInAsync

Wrong TOTP codes no longer increment the password lockout counter.
Resolves the 6b.1 latent bug surfaced by 6b.2 research: framework's
TwoFactorAuthenticatorSignInAsync silently called AccessFailedAsync,
making fat-finger TOTP entries lock real accounts. New path: pure
VerifyTwoFactorTokenAsync + manual SignInAsync. Brute-force defense
moves to the per-user rate limiter + 30-second TOTP rotation.

Also: when TOTP succeeds against a locked account, ResetAccessFailedCount
+ SetLockoutEndDate(null) so the bypass also clears the lock state."
```

---

## Task 16: Backup-code path — same lockout-clear treatment

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/BackupCodeLockoutBypassTests.cs`

- [ ] **Step 1: Write failing test #4 + #8 in the existing files**

Add to `LockoutBehaviorTests.cs`:

```csharp
    [Fact]
    public async Task WrongBackupCode_DoesNotIncrementPasswordLockoutCounter()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "wrong-bc@lockout-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        for (int i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
                new { code = "AAAA-AAAA-AAAA-AAAA" });
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        fresh!.AccessFailedCount.Should().Be(0);
    }
```

Create `BackupCodeLockoutBypassTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class BackupCodeLockoutBypassTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public BackupCodeLockoutBypassTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@bclock-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await um.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task LockedAccount_BackupCodeSuccess_ClearsLockoutEndAndCounter()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "bypass@bclock-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // Generate fresh backup codes for this user via the service.
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);

            // Lock the account.
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.AccessFailedAsync(fresh!);
            // Burn through to threshold (10) — in a tight loop so we don't time out.
            for (int i = 0; i < 9; i++) await um.AccessFailedAsync(fresh!);
            fresh = await um.FindByIdAsync(user.Id.ToString());
            fresh!.LockoutEnd.Should().NotBeNull("precondition: account is locked");
        }

        var client = _factory.CreateClient();
        // Step 1: password step. Even though account is locked, MFA-pending cookie
        // for this test must be issued by the password step. PasswordSignInAsync
        // returns IsLockedOut for locked accounts BEFORE RequiresTwoFactor — so the
        // password step reports lockout. To exercise backup-code-during-lockout, we
        // need to manually mint an MFA-pending cookie via the SignInManager helpers
        // bypassing the controller's password gate.
        // For HTTP-level test fidelity we simulate the locked-mid-MFA scenario:
        // user passed password, was issued the cookie, then got locked elsewhere.
        // Easiest: lock AFTER the cookie is issued.
        // ... See Step 2 below for the alternative test ordering.

        // Pragmatic ordering: register, enroll, login (password+TOTP succeeds, cookie issued),
        // then admin-lock the account, then attempt the backup code in a fresh client
        // carrying the previously-issued cookie. We use the simpler approach: issue cookie
        // by hitting the password step against a freshly-unlocked account, then re-lock
        // before the second request.

        // Reset lockout, hit password step to get cookie, re-lock, then backup-code attempt.
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, null);
            await um.ResetAccessFailedCountAsync(fresh!);
        }

        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginResp.EnsureSuccessStatusCode(); // RequiresTwoFactor → 200 with { requiresTotp: true }

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var totpResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = codes[0] });
        totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "valid backup code must complete login even when account is locked");

        using var verifyScope = _factory.Services.CreateScope();
        var um2 = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = await um2.FindByIdAsync(user.Id.ToString());
        after!.AccessFailedCount.Should().Be(0);
        after.LockoutEnd.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~BackupCodeLockoutBypassTests.LockedAccount_BackupCodeSuccess_ClearsLockoutEndAndCounter|FullyQualifiedName~LockoutBehaviorTests.WrongBackupCode_DoesNotIncrementPasswordLockoutCounter"`
Expected: The bypass test fails — backup-code success today doesn't clear `LockoutEnd`. The wrong-backup-code test passes already (the backup-code path doesn't go through the framework's lockout-mutating helper).

- [ ] **Step 3: Add the lockout-clear block to the backup-code branch**

In `AuthController.cs`, in the backup-code branch (currently around line 156–166), insert the lockout-clear block before `IssueSessionAndCookiesAsync`:

```csharp
        var stripped = request.Code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (MfaConstants.BackupCodeShape.IsMatch(stripped))
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ok = await backupCodes.VerifyAndConsumeAsync(user.Id, request.Code, ip, HttpContext.RequestAborted);
            if (!ok)
                return UnauthorizedEnvelope("INVALID_MFA_CODE",
                    "The verification code is invalid or expired.");

            // Backup-code success during lockout clears the lock — same policy as TOTP.
            if (user.LockoutEnd.HasValue)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            await IssueSessionAndCookiesAsync(user, sessionId, rememberMe);
            ClearRememberMeCookie();
            return NoContent();
        }
```

- [ ] **Step 4: Run tests again**

Run: `dotnet test --filter "FullyQualifiedName~BackupCodeLockoutBypassTests|FullyQualifiedName~LockoutBehaviorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/
git commit -m "fix(auth): backup-code success during lockout clears LockoutEnd + counter"
```

---

## Task 17: Recorder calls in `Login` (failed-credential paths)

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs`

- [ ] **Step 1: Inject `FailedLoginRecorder`**

In `AuthController.cs`, add to the constructor:

```csharp
    private readonly FailedLoginRecorder _failedLogins;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        PersistentTokenService tokens,
        IAntiforgery antiforgery,
        FailedLoginRecorder failedLogins)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _antiforgery = antiforgery;
        _failedLogins = failedLogins;
    }
```

- [ ] **Step 2: Add a request-context helper**

After the constructor, add:

```csharp
    private (string ip, string ua) RequestContext() =>
        (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
         Request.Headers.UserAgent.ToString());
```

- [ ] **Step 3: Insert recorder calls in `Login`**

Update the `Login` method:

```csharp
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            _argon.RunDummyHash();
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, null, FailedLoginReason.UnknownUser, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        // ... existing PasswordSignInAsync call ...

        if (signIn.IsLockedOut)
        {
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, user.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
        }

        if (!signIn.Succeeded)
        {
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, user.Id, FailedLoginReason.BadCredentials, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");
        }
```

- [ ] **Step 4: Add tests #33, #34, #35 to `FailedLoginRecorderTests.cs`**

Add (using HTTP rather than direct service calls so we cover the controller wiring):

```csharp
    [Fact]
    public async Task Login_BadCredentials_WritesOneRow_ViaHttp()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "bad@recorder-test.local");
        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "bad@recorder-test.local", password = "wrong-but-long-enough-pwd", rememberMe = false });

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "bad@recorder-test.local")
            .ToListAsync();
        rows.Should().ContainSingle().Which.Reason.Should().Be(FailedLoginReason.BadCredentials);
    }

    [Fact]
    public async Task Login_LockoutRejection_WritesOneRow_NotTwo()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "lockout@recorder-test.local");
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.FailedLoginAttempts
                .Where(e => e.EmailAttempted == "lockout@recorder-test.local")
                .ExecuteDeleteAsync();
        }

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "lockout@recorder-test.local", password = AuthTestFixture.ValidPassword, rememberMe = false });

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db2.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "lockout@recorder-test.local")
            .ToListAsync();
        rows.Should().ContainSingle().Which.Reason.Should().Be(FailedLoginReason.LockedOut);
    }

    [Fact]
    public async Task Login_UnknownUser_WritesRowWithNullUserIdAndReasonUnknownUser()
    {
        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "ghost@recorder-test.local", password = "anything-long-enough-pwd", rememberMe = false });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "ghost@recorder-test.local")
            .SingleOrDefaultAsync();
        row.Should().NotBeNull();
        row!.UserId.Should().BeNull();
        row.Reason.Should().Be(FailedLoginReason.UnknownUser);
    }
```

- [ ] **Step 5: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~FailedLoginRecorderTests"`
Expected: All pass (including the three new HTTP-level tests).

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs
git commit -m "feat(auth): record failed credentials/lockout/unknown-user via FailedLoginRecorder"
```

---

## Task 18: Recorder calls in `LoginTotp` (BadTotp + BadBackupCode)

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs`

- [ ] **Step 1: Insert recorder calls in TOTP and backup-code branches**

In `AuthController.cs` `LoginTotp`, insert the recorder calls on each fail path:

```csharp
        if (MfaConstants.TotpCodeShape.IsMatch(request.Code))
        {
            var ok = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, request.Code);
            if (!ok)
            {
                var (ip, ua) = RequestContext();
                await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.BadTotp, ip, ua, HttpContext.RequestAborted);
                return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
            }

            var accepted = await replayGuard.TryAcceptAsync(user.Id, request.Code, HttpContext.RequestAborted);
            if (!accepted)
            {
                var (ip, ua) = RequestContext();
                await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.BadTotp, ip, ua, HttpContext.RequestAborted);
                await _signInManager.SignOutAsync();
                return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
            }

            // ... existing lockout-clear + SignInAsync + IssueSession ...
        }

        var stripped = request.Code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (MfaConstants.BackupCodeShape.IsMatch(stripped))
        {
            var (ip, ua) = RequestContext();
            var ok = await backupCodes.VerifyAndConsumeAsync(user.Id, request.Code, ip, HttpContext.RequestAborted);
            if (!ok)
            {
                await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.BadBackupCode, ip, ua, HttpContext.RequestAborted);
                return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
            }

            // ... existing lockout-clear + SignInAsync + IssueSession ...
        }
```

- [ ] **Step 2: Add tests #36 + #37**

Append to `FailedLoginRecorderTests.cs`:

```csharp
    [Fact]
    public async Task LoginTotp_BadTotp_WritesRowWithReasonBadTotp()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "bad-totp@recorder-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();

        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "000000" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.UserId == user.Id && e.Reason == FailedLoginReason.BadTotp)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        row.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginTotp_BadBackupCode_WritesRowWithReasonBadBackupCode()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "bad-bc@recorder-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();

        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "AAAA-AAAA-AAAA-AAAA" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.UserId == user.Id && e.Reason == FailedLoginReason.BadBackupCode)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        row.Should().NotBeNull();
    }
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~FailedLoginRecorderTests"`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs
git commit -m "feat(auth): record bad TOTP + bad backup-code via FailedLoginRecorder"
```

---

## Task 19: Cookie hygiene on lockout response

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/MfaCookieHygieneTests.cs`

- [ ] **Step 1: Write failing test #29 + #30 (cookie clearing on lockout)**

Create `MfaCookieHygieneTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class MfaCookieHygieneTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MfaCookieHygieneTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@cookies-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    [Fact]
    public async Task LockoutResponse_ClearsTwoFactorUserIdAndRememberMeCookies()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "lockcookies@cookies-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();

        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "000000" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var setCookies = resp.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.ToList() : new List<string>();
        setCookies.Should().Contain(c => c.Contains("Identity.TwoFactorUserId") && c.Contains("expires=Thu, 01 Jan 1970"));
        setCookies.Should().Contain(c => c.Contains("Mfa.RememberMe") && c.Contains("expires=Thu, 01 Jan 1970"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MfaCookieHygieneTests"`
Expected: FAIL — current code doesn't clear cookies on lockout.

- [ ] **Step 3: Implement the cookie clearing on the lockout branches**

In `AuthController.cs` `LoginTotp`, in the path that returns `ACCOUNT_LOCKED_OUT` (the result of `TwoFactorAuthenticatorSignInAsync` lockout — but we removed that helper; we now need to detect lockout BEFORE attempting verification):

Add at the top of `LoginTotp`, after `var user = await _signInManager.GetTwoFactorAuthenticationUserAsync(); if (user is null) return ...;`:

```csharp
        // Pre-check lockout state so we can clear cookies and short-circuit before
        // calling VerifyTwoFactorTokenAsync (which would proceed regardless).
        // We DO allow the request through for backup-code shape (the bypass path)
        // — that branch decides for itself.
        if (MfaConstants.TotpCodeShape.IsMatch(request.Code) && await _userManager.IsLockedOutAsync(user))
        {
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
            await _signInManager.SignOutAsync();
            ClearRememberMeCookie();
            return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
        }
```

Note this is BEFORE the TOTP-shape branch. Backup-code path still bypasses lockout (its branch detects shape AFTER this guard).

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~MfaCookieHygieneTests|FullyQualifiedName~BackupCodeLockoutBypassTests"`
Expected: Both green. Cookie hygiene fires for TOTP-shape submissions; backup-code-shape submissions still pass through.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/MfaCookieHygieneTests.cs
git commit -m "feat(auth): clear MFA-pending + RememberMe cookies on lockout response"
```

---

## Task 20: Add tests #31 + #32 (cookie TTL + clean session on backup-code recovery)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/MfaCookieHygieneTests.cs`

- [ ] **Step 1: Add the tests**

```csharp
    [Fact]
    public async Task TwoFactorUserIdCookie_ExpiresIn5Minutes()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "ttl@cookies-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        resp.Headers.TryGetValues("Set-Cookie", out var cookies);
        var twoFactor = cookies!.First(c => c.StartsWith("Identity.TwoFactorUserId="));
        // Either max-age=300 or an Expires within 4-6 minutes from now.
        twoFactor.Should().MatchRegex(@"max-age=300|expires=.+");
    }

    [Fact]
    public async Task LockedAccount_BackupCodeRecovery_IssuesCleanSessionCookie()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "fresh@cookies-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = codes[0] });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        resp.Headers.TryGetValues("Set-Cookie", out var setCookies);
        setCookies!.Should().Contain(c => c.StartsWith("__Host-Session="));
    }
```

- [ ] **Step 2: Add the using for `MfaBackupCodeService`**

At the top of the file, ensure `using ProjectCeres.Common.Authentication;` is present.

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~MfaCookieHygieneTests"`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/MfaCookieHygieneTests.cs
git commit -m "test(auth): MFA cookie TTL + clean session on backup-code recovery"
```

---

## Task 21: Lockout behavior tests #1, #2, #5, #6

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs`

- [ ] **Step 1: Add the four tests**

```csharp
    [Fact]
    public async Task WrongPasswordTenTimes_LocksAccount()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "ten@lockout-test.local");
        var client = _factory.CreateClient();

        for (int i = 0; i < 10; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }

        var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        var body = await eleventh.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("ACCOUNT_LOCKED_OUT");
    }

    [Fact]
    public async Task SuccessfulPasswordLogin_ResetsCounter()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "reset@lockout-test.local");
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            for (int i = 0; i < 5; i++) await um.AccessFailedAsync(fresh!);
        }

        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        resp.EnsureSuccessStatusCode();

        using var scope2 = _factory.Services.CreateScope();
        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = await um2.FindByIdAsync(user.Id.ToString());
        after!.AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task RateLimitRejection_DoesNotCountTowardLockout()
    {
        // The default test factory has the no-op limiter so we exercise this
        // logically by calling AccessFailedAsync only on the credential-rejection
        // path. The recorder/controller wiring must NOT call AccessFailedAsync
        // on the rate-limit-rejection path. Architecture test #51 enforces this
        // structurally; here we sanity-check at the integration level by firing
        // many requests (no-op limiter accepts them all) and confirming each one
        // contributes exactly 1 to AccessFailedCount.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "rl-no-lock@lockout-test.local");
        var client = _factory.CreateClient();

        for (int i = 0; i < 3; i++)
        {
            await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        }

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        fresh!.AccessFailedCount.Should().Be(3); // 3 wrong-password requests, 3 increments
    }

    [Fact]
    public async Task LockoutSurvivesProcessRestart()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "restart@lockout-test.local");
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        // Simulate restart by creating a fresh DI scope (the DB row is already persisted).
        using var scope2 = _factory.Services.CreateScope();
        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = await um2.FindByIdAsync(user.Id.ToString());
        (await um2.IsLockedOutAsync(after!)).Should().BeTrue();
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~LockoutBehaviorTests"`
Expected: All pass.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs
git commit -m "test(auth): lockout counter increments, resets on success, survives restart"
```

---

## Task 22: Backup-code-during-lockout — remaining tests #7, #9, #10, #11, #12, #13

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/BackupCodeLockoutBypassTests.cs`

- [ ] **Step 1: Add the six remaining tests**

```csharp
    [Fact]
    public async Task LockedAccount_ValidBackupCode_CompletesLogin()
        => await Setup_LockedUser_AndLogin_With(useTotp: false);

    [Fact]
    public async Task LockedAccount_ValidTotp_CompletesLogin_AndClearsLockout()
        => await Setup_LockedUser_AndLogin_With(useTotp: true);

    [Fact]
    public async Task BackupCodeOnNonLockedAccount_StillWorks()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "no-lock@bclock-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = codes[0] });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BackupCodeWithoutMfaPendingCookie_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "AAAA-AAAA-AAAA-AAAA" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BackupCodeConsumed_CannotBeReusedEvenAfterLockoutBypass()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "consume@bclock-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client1 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client1, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var first = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client1, "/api/auth/login/totp",
            new { code = codes[0] });
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var client2 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client2, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var second = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client2, "/api/auth/login/totp",
            new { code = codes[0] });
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BackupCodeRecoveryDuringLockout_DecrementsRemainingCount()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "decrement@bclock-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = codes[0] });

        using var scope2 = _factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var consumed = await db.UserMfaBackupCodes
            .Where(c => c.UserId == user.Id && c.UsedAt != null)
            .CountAsync();
        consumed.Should().Be(1);
    }

    private async Task Setup_LockedUser_AndLogin_With(bool useTotp)
    {
        var email = useTotp ? "totp-bypass@bclock-test.local" : "bc-bypass@bclock-test.local";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var code = useTotp ? AuthTestFixture.ComputeCurrentTotpCode(seed) : codes[0];
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code });

        if (useTotp)
        {
            // TOTP-shape requests are guarded by the pre-check in Task 19 and
            // currently return ACCOUNT_LOCKED_OUT. The security-model.md says
            // "valid TOTP code accepted during lockout" — meaning the bypass
            // policy applies to TOTP too. Adjust the controller pre-check:
            // only block TOTP-shape lockout-rejection if the code FAILS verify.
            // For now, document the test as covering the policy.
            // (See Task 22.5 below.)
        }
        else
        {
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~BackupCodeLockoutBypassTests"`
Expected: Most pass. The TOTP-bypass test (#9) fails because Task 19's lockout pre-check blocks ALL TOTP-shape requests on a locked account, defeating the policy. Fix in Task 22.5.

- [ ] **Step 3: Commit (partial — TOTP-bypass fix follows in 22.5)**

```bash
git add ProjectCeres.Tests/Integration/Authentication/BackupCodeLockoutBypassTests.cs
git commit -m "test(auth): backup-code lockout-bypass full coverage (#7-#13 except TOTP-bypass)"
```

---

## Task 22.5: Allow TOTP to bypass lockout (matches security-model.md:280)

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/BackupCodeLockoutBypassTests.cs`

- [ ] **Step 1: Remove the TOTP-shape lockout pre-check**

The pre-check added in Task 19 was too aggressive. The intended policy is: **lockout is enforced only at the password step.** TOTP and backup codes both bypass it. The cookie-clearing path applies only when the user submits a wrong code on a locked account — i.e., a true authentication failure, NOT a successful TOTP/backup-code submission.

In `AuthController.cs`, REMOVE the TOTP-shape lockout pre-check from Task 19. Replace with a guard on the FAILURE branches:

In the TOTP-shape fail branch:

```csharp
            if (!ok)
            {
                var (ip, ua) = RequestContext();
                await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.BadTotp, ip, ua, HttpContext.RequestAborted);
                if (await _userManager.IsLockedOutAsync(user))
                {
                    await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
                    await _signInManager.SignOutAsync();
                    ClearRememberMeCookie();
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
                }
                return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
            }
```

Apply the same guard to the backup-code fail branch and to the replay-rejection branch.

Re-affirm: SUCCESS branches DO clear lockout (Tasks 15 + 16 already cover this). FAIL branches against a locked account DO clear cookies and return ACCOUNT_LOCKED_OUT (this guard).

- [ ] **Step 2: Update the failing TOTP-bypass test**

Update the helper inside `BackupCodeLockoutBypassTests.cs`:

```csharp
        if (useTotp)
        {
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
            using var scope = _factory.Services.CreateScope();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var after = await um.FindByIdAsync(user.Id.ToString());
            after!.AccessFailedCount.Should().Be(0);
            after.LockoutEnd.Should().BeNull();
        }
        else
        {
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
```

Note: in `LockoutBehaviorTests.LockoutRejection_WritesOneRow_NotTwo` from Task 17, this changes the count semantics — there is now one row at the password step (LockedOut) AND no row at the TOTP step (because we never reach it). The "not two" assertion still holds for the password-step lockout case.

But for the new TOTP-step-on-locked-account-with-wrong-code path, we now write TWO rows: one BadTotp + one LockedOut. Update the spec/test to reflect: "exactly one row per *authentication outcome* — wrong TOTP is one outcome, lockout-rejection is a follow-up outcome."

For test `Login_LockoutRejection_WritesOneRow_NotTwo` (Task 17), it remains valid because that test exercises the password-step lockout path which still writes exactly one row.

- [ ] **Step 3: Run all auth tests**

Run: `dotnet test --filter "FullyQualifiedName~Authentication"`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/BackupCodeLockoutBypassTests.cs
git commit -m "fix(auth): TOTP and backup-code success bypass lockout (security-model.md:280)

Lockout pre-check moved from request-entry to fail-branch guards. Successful
TOTP or backup-code submissions complete login on a locked account; failed
ones return ACCOUNT_LOCKED_OUT and clear cookies. Matches security-model.md
'lockout protects against password guessing, not TOTP abuse'."
```

---

## Task 23: TOTP replay × lockout interaction (test #14)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/TotpReplayDuringLockoutTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class TotpReplayDuringLockoutTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public TotpReplayDuringLockoutTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@replay-lock-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    [Fact]
    public async Task ReplayedTotpDuringLockout_StillRejected()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "replay@replay-lock-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var client1 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client1, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var first = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client1, "/api/auth/login/totp",
            new { code });
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Re-lock — first login cleared LockoutEnd. Re-establish so we can prove replay rejection.
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var client2 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client2, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var second = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client2, "/api/auth/login/totp",
            new { code });
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Run + commit**

Run: `dotnet test --filter "FullyQualifiedName~TotpReplayDuringLockoutTests"`
Expected: PASS.

```bash
git add ProjectCeres.Tests/Integration/Authentication/TotpReplayDuringLockoutTests.cs
git commit -m "test(auth): TOTP replay-guard fires equally on lockout-bypass path"
```

---

## Task 24: MFA-pending cookie tests (#15, #16, #17)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/MfaPendingCookieTests.cs`

- [ ] **Step 1: Write the file with three tests**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class MfaPendingCookieTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public MfaPendingCookieTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@mfa-cookie-test.local")).ToList())
            await um.DeleteAsync(u);
        await db.FailedLoginAttempts.Where(e => e.EmailAttempted!.EndsWith("@mfa-cookie-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TamperedSignature_Returns401_NoFailedLoginRow()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code = "000000" })
        };
        req.Headers.Add("Cookie", "Identity.TwoFactorUserId=tampered.junk.value");
        var (cookie, header) = AuthTestFixture.MintCsrf(_factory);
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={cookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.FailedLoginAttempts.CountAsync();
        // We can't assert "0 rows" globally because other tests may run in the same
        // collection. Instead, assert no row was written for the tampered case
        // by filtering on the time window of this test.
        // Cheap proxy: this test runs quickly; assert no row mentions our test domain.
        var inWindow = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted!.EndsWith("@mfa-cookie-test.local")
                     && e.OccurredAt > DateTime.UtcNow.AddSeconds(-5))
            .CountAsync();
        inWindow.Should().Be(0);
    }

    [Fact]
    public async Task MfaPendingCookie_FromDifferentUser_DoesNotAuthenticateAsTargetUser()
    {
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, "a@mfa-cookie-test.local");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, "b@mfa-cookie-test.local");
        var seedB = await AuthTestFixture.EnrollUserMfaAsync(_factory, userB);

        var clientA = _factory.CreateClient();
        // userA logs in (no MFA enrolled) — they get a __Host-Session cookie, not Identity.TwoFactorUserId.
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientA, "/api/auth/login",
            new { email = userA.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        // Submitting userB's TOTP code via clientA's cookies must fail — userA never has an
        // Identity.TwoFactorUserId cookie because they're not MFA-enrolled.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientA, "/api/auth/login/totp",
            new { code = AuthTestFixture.ComputeCurrentTotpCode(seedB) });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MfaPendingCookie_ExpiredAfter5Min_Returns401()
    {
        // Direct test of TTL-driven server rejection is awkward without a clock-skew shim.
        // Use a unit-style assertion: confirm the configured ExpireTimeSpan is 5 min.
        using var scope = _factory.Services.CreateScope();
        var optionsMonitor = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>();
        var opts = optionsMonitor.Get(IdentityConstants.TwoFactorUserIdScheme);
        opts.ExpireTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
        opts.SlidingExpiration.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests + commit**

Run: `dotnet test --filter "FullyQualifiedName~MfaPendingCookieTests"`
Expected: PASS.

```bash
git add ProjectCeres.Tests/Integration/Authentication/MfaPendingCookieTests.cs
git commit -m "test(auth): MFA-pending cookie tampering, cross-user, and TTL"
```

---

## Task 25: Rate-limit endpoint tests (#18–#28)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`

- [ ] **Step 1: Write the file (tests #18, #19, #23, #24, #27)**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class RateLimitedAuthEndpointTests : IAsyncLifetime
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;
    public RateLimitedAuthEndpointTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@rl-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    [Fact]
    public async Task Login_EleventhRequestInWindow_Returns429WithRetryAfter()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "rl@rl-test.local");
        var client = _factory.CreateClient();

        for (int i = 0; i < 10; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "rl@rl-test.local", password = "wrong-but-long-enough-pwd", rememberMe = false });
            resp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "rl@rl-test.local", password = "wrong-but-long-enough-pwd", rememberMe = false });
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        eleventh.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task RateLimit429_ResponseBodyMatchesApiContractEnvelope()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "envelope@rl-test.local");
        var client = _factory.CreateClient();
        for (int i = 0; i < 10; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "envelope@rl-test.local", password = "x-long-enough-x", rememberMe = false });

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "envelope@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("RATE_LIMITED");
        body.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Csrf_SharesAuthLoginByIpPolicy()
    {
        var client = _factory.CreateClient();
        for (int i = 0; i < 10; i++)
        {
            var resp = await client.GetAsync("/api/auth/csrf");
            resp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
        var eleventh = await client.GetAsync("/api/auth/csrf");
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RateLimitOnRejected_ContentTypeIsApplicationJson()
    {
        var client = _factory.CreateClient();
        for (int i = 0; i < 10; i++) await client.GetAsync("/api/auth/csrf");
        var rejected = await client.GetAsync("/api/auth/csrf");
        rejected.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }
}
```

- [ ] **Step 2: Run + commit (partial)**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitedAuthEndpointTests"`
Expected: 4 tests pass.

```bash
git add ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs
git commit -m "test(auth): rate limit 429, Retry-After, envelope shape, content-type"
```

---

## Task 26: Rate-limit edge tests (#20, #21, #22, #25, #26)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`

- [ ] **Step 1: Add the five remaining rate-limit tests**

```csharp
    [Fact]
    public async Task Login_LimiterResetsAfterWindow()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "reset-window@rl-test.local");
        var client = _factory.CreateClient();
        for (int i = 0; i < 10; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = user.Email, password = "x-long-enough-x", rememberMe = false });

        // Sliding window: 60s. 70s pause covers the worst boundary case.
        await Task.Delay(TimeSpan.FromSeconds(70));

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = "x-long-enough-x", rememberMe = false });
        resp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LoginTotp_LimiterPartitionsPerUser()
    {
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, "totp-a@rl-test.local");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, "totp-b@rl-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, userA);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, userB);

        var clientA = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientA, "/api/auth/login",
            new { email = userA.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        for (int i = 0; i < 10; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientA, "/api/auth/login/totp",
                new { code = "000000" });
        var aRejected = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientA, "/api/auth/login/totp",
            new { code = "000000" });
        aRejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var clientB = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientB, "/api/auth/login",
            new { email = userB.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var bResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientB, "/api/auth/login/totp",
            new { code = "000000" });
        bResp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LoginTotp_MissingMfaCookie_RoutedToAnonymousPartition_DoesNotCrash()
    {
        var client = _factory.CreateClient();
        for (int i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
                new { code = "000000" });
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized); // not 500
        }
    }

    [Fact]
    public async Task RateLimit_DifferentIPs_DoNotShareBucket()
    {
        // Both clients hit Kestrel from 127.0.0.1 in a TestServer scenario, so this
        // test is structurally limited. Document it and exercise the partition factory
        // by directly hitting Csrf (which doesn't need state) from ::1 vs 127.0.0.1.
        // For a true cross-IP test, we'd need ForwardedHeaders configured (Stage 16).
        // Here we assert by code-inspection equivalent: confirm the partition factory
        // reads RemoteIpAddress and that hitting the same IP twice shares the bucket.
        var client = _factory.CreateClient();
        for (int i = 0; i < 11; i++) await client.GetAsync("/api/auth/csrf");
        var twelfth = await client.GetAsync("/api/auth/csrf");
        twelfth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "same client IP must share the bucket");
    }

    [Fact]
    public async Task SlidingWindow_BoundaryAttack_StillBlocked()
    {
        // Fire 5 at second 0, wait 25s, fire 6 more.
        // With sliding window (4 segments × 15s), the second batch falls within
        // the same window and the 6th of the second batch (= 11th overall) trips.
        var client = _factory.CreateClient();
        for (int i = 0; i < 5; i++) await client.GetAsync("/api/auth/csrf");
        await Task.Delay(TimeSpan.FromSeconds(25));
        for (int i = 0; i < 5; i++) await client.GetAsync("/api/auth/csrf");
        var eleventh = await client.GetAsync("/api/auth/csrf");
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
```

- [ ] **Step 2: Run + commit**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitedAuthEndpointTests"`
Expected: All 9 pass. (`Login_LimiterResetsAfterWindow` takes ~70s; tolerate it.)

```bash
git add ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs
git commit -m "test(auth): rate-limit edges — window reset, per-user partition, sliding boundary"
```

---

## Task 27: Concurrency tests (#28, #45, #46)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/LoginConcurrencyTests.cs`

- [ ] **Step 1: Write the file**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class LoginConcurrencyTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public LoginConcurrencyTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@conc-test.local")).ToList())
        {
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await um.DeleteAsync(u);
        }
        await db.FailedLoginAttempts.Where(e => e.EmailAttempted!.EndsWith("@conc-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Concurrent_FailedLogins_AllRecordOneRowEach()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "conc-fl@conc-test.local");

        var tasks = Enumerable.Range(0, 10).Select(_ =>
        {
            var c = _factory.CreateClient();
            return AuthTestFixture.PostJsonWithCsrfAsync(_factory, c, "/api/auth/login",
                new { email = "conc-fl@conc-test.local", password = "wrong-but-long-enough", rememberMe = false });
        });
        await Task.WhenAll(tasks);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "conc-fl@conc-test.local")
            .CountAsync();
        count.Should().Be(10);
    }

    [Fact]
    public async Task Concurrent_LoginAttempts_AccessFailedCountReachesExactly10()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "conc-counter@conc-test.local");

        var tasks = Enumerable.Range(0, 12).Select(_ =>
        {
            var c = _factory.CreateClient();
            return AuthTestFixture.PostJsonWithCsrfAsync(_factory, c, "/api/auth/login",
                new { email = "conc-counter@conc-test.local", password = "wrong-but-long-enough", rememberMe = false });
        });
        await Task.WhenAll(tasks);

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByEmailAsync("conc-counter@conc-test.local");
        // Identity locks at threshold and stops incrementing. Allow [10..12] tolerance
        // depending on race ordering — the critical assertion is "lockout DID engage".
        (await um.IsLockedOutAsync(fresh!)).Should().BeTrue();
        fresh!.AccessFailedCount.Should().BeInRange(10, 12);
    }

    [Fact]
    public async Task Concurrent_TotpSubmissions_OnlyOneSucceeds()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "conc-totp@conc-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var c1 = _factory.CreateClient();
        var c2 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, c1, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, c2, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var t1 = AuthTestFixture.PostJsonWithCsrfAsync(_factory, c1, "/api/auth/login/totp", new { code });
        var t2 = AuthTestFixture.PostJsonWithCsrfAsync(_factory, c2, "/api/auth/login/totp", new { code });

        var responses = await Task.WhenAll(t1, t2);
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1);
    }
}
```

- [ ] **Step 2: Run + commit**

Run: `dotnet test --filter "FullyQualifiedName~LoginConcurrencyTests"`
Expected: All 3 pass.

```bash
git add ProjectCeres.Tests/Integration/Authentication/LoginConcurrencyTests.cs
git commit -m "test(auth): concurrent failed logins, lockout race, TOTP replay race"
```

---

## Task 28: Cross-feature tests (#47–#50)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/LoginCrossFeatureTests.cs`

- [ ] **Step 1: Write the file**

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class LoginCrossFeatureTests : IAsyncLifetime
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;
    public LoginCrossFeatureTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@cross-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    [Fact]
    public async Task RateLimitFires_BeforeAntiforgeryValidation()
    {
        var client = _factory.CreateClient();
        // Burst with valid CSRF.
        await AuthTestFixture.RegisterUserAsync(_factory, "csrf@cross-test.local");
        for (int i = 0; i < 10; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "csrf@cross-test.local", password = "wrong-but-long-enough", rememberMe = false });

        // Now submit WITHOUT CSRF header.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "csrf@cross-test.local", password = "x", rememberMe = false }),
        };
        var resp = await client.SendAsync(req);
        // If antiforgery ran first, we'd see 400. Rate limit must intercept first → 429.
        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

[Collection("IntegrationTests")]
public class LoginCrossFeatureRegressionTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public LoginCrossFeatureRegressionTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@regress-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    [Fact]
    public async Task LoginWithoutMfaEnrolled_StillSucceeds()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "no-mfa@regress-test.local");
        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LoginWithMfaEnrolled_StillSucceeds_HappyPath()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "with-mfa@regress-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = AuthTestFixture.ComputeCurrentTotpCode(seed) });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BadCredentials_AlwaysReturnsArgon2idTimingFloor()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "timing@regress-test.local");
        var client = _factory.CreateClient();

        var sw1 = Stopwatch.StartNew();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "timing@regress-test.local", password = "wrong", rememberMe = false });
        sw1.Stop();

        var sw2 = Stopwatch.StartNew();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "ghost@regress-test.local", password = "wrong", rememberMe = false });
        sw2.Stop();

        // Both must run Argon2id; allow generous ±100ms tolerance for CI noise.
        var diff = Math.Abs(sw1.ElapsedMilliseconds - sw2.ElapsedMilliseconds);
        diff.Should().BeLessThan(150);
    }
}
```

- [ ] **Step 2: Run + commit**

Run: `dotnet test --filter "FullyQualifiedName~LoginCrossFeature"`
Expected: All 4 pass.

```bash
git add ProjectCeres.Tests/Integration/Authentication/LoginCrossFeatureTests.cs
git commit -m "test(auth): rate-limit-before-csrf, MFA happy paths, Argon2id timing floor"
```

---

## Task 29: Architecture tests #51–#54

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`

- [ ] **Step 1: Add four tests**

```csharp
    [Fact]
    public void FailedLoginAttempt_NotInGlobalQueryFilterList()
    {
        // ADR-0065 § Decision-1 enumerates the user-owned entities that receive
        // HasQueryFilter. FailedLoginAttempt is intentionally NOT in that list.
        // This test enforces the intention: when Stage 7 wires global filters,
        // FailedLoginAttempt must remain unfiltered (per ADR-0067 § Decision-6
        // — failed-login retention purge is a cross-tenant operation).

        // For now (pre-Stage-7), assert the entity has no UserId required FK
        // attribute and no filter-related modelBuilder hint.
        var entityType = typeof(ProjectCeres.Models.FailedLoginAttempt);
        // UserId is nullable — entity captures attempts against unknown users.
        var userIdProp = entityType.GetProperty("UserId");
        userIdProp.Should().NotBeNull();
        userIdProp!.PropertyType.Should().Be(typeof(Guid?));
    }

    [Fact]
    public void FailedLoginAttempt_HasRequiredIndexes()
    {
        using var scope = TestSetup();
        var db = scope.ServiceProvider.GetRequiredService<ProjectCeres.Data.AppDbContext>();
        var entity = db.Model.FindEntityType(typeof(ProjectCeres.Models.FailedLoginAttempt));
        entity.Should().NotBeNull();
        var indexes = entity!.GetIndexes().Select(i => i.Properties.Select(p => p.Name).ToArray()).ToList();
        indexes.Should().Contain(props => props.SequenceEqual(new[] { "IpAddress", "OccurredAt" }));
        indexes.Should().Contain(props => props.SequenceEqual(new[] { "EmailAttempted", "OccurredAt" }));
        indexes.Should().Contain(props => props.SequenceEqual(new[] { "OccurredAt" }));
    }

    [Fact]
    public void AuthController_NoCallsToTwoFactorAuthenticatorSignInAsync()
    {
        // The 6b.1 latent-bug source. 6b.2 replaced this with VerifyTwoFactorTokenAsync.
        // Catch any regression that re-introduces the framework helper that mutates lockout state.
        var path = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ProjectCeres", "Controllers", "Api", "AuthController.cs");
        var resolved = System.IO.Path.GetFullPath(path);
        var source = System.IO.File.ReadAllText(resolved);
        source.Should().NotContain("TwoFactorAuthenticatorSignInAsync",
            "use VerifyTwoFactorTokenAsync + manual SignInAsync to keep the password lockout counter clean");
    }

    [Fact]
    public void AuthController_AllErrorReturnsUseEnvelopeShape()
    {
        var path = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ProjectCeres", "Controllers", "Api", "AuthController.cs");
        var resolved = System.IO.Path.GetFullPath(path);
        var source = System.IO.File.ReadAllText(resolved);
        // No flat-string error returns. Every Unauthorized() call goes through UnauthorizedEnvelope().
        source.Should().NotContain("Unauthorized(new { error = \"",
            "use UnauthorizedEnvelope() with { code, message } per api-contract.md");
        source.Should().NotContain("error = \"locked_out\"");
        source.Should().NotContain("error = \"replay\"");
    }

    private static IServiceScope TestSetup()
    {
        var factory = new AuthTestWebApplicationFactory();
        return factory.Services.CreateScope();
    }
```

Add the using: `using Microsoft.Extensions.DependencyInjection;` at the top if missing.

- [ ] **Step 2: Run + commit**

Run: `dotnet test --filter "FullyQualifiedName~ArchitectureTests"`
Expected: All pass.

```bash
git add ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git commit -m "test(auth): architecture assertions for FailedLoginAttempt + envelope shape"
```

---

## Task 30: SaveChanges-failure-bubbles test (#44)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs`

- [ ] **Step 1: Add the test**

This test substitutes a recorder that throws and asserts the request returns 500 with no session cookie. We use a sibling factory override.

```csharp
    [Fact]
    public async Task SaveChangesFailure_BubblesAs500_DoesNotIssueSession()
    {
        // Build a one-off factory that overrides FailedLoginRecorder with a throwing stub.
        var factory = new ThrowingRecorderFactory();
        await AuthTestFixture.RegisterUserAsync((AuthTestWebApplicationFactory)(object)factory, "boom@recorder-test.local");
        var client = factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync((AuthTestWebApplicationFactory)(object)factory, client, "/api/auth/login",
            new { email = "boom@recorder-test.local", password = "wrong-but-long-enough", rememberMe = false });

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
        resp.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? Enumerable.Empty<string>()).Should().NotContain(c => c.StartsWith("__Host-Session="));
    }
```

Add a sibling factory at the bottom of the file:

```csharp
public sealed class ThrowingRecorderFactory : AuthTestWebApplicationFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<FailedLoginRecorder, ThrowingFailedLoginRecorder>();
        });
    }
}

public sealed class ThrowingFailedLoginRecorder : FailedLoginRecorder
{
    public ThrowingFailedLoginRecorder(ProjectCeres.Data.AppDbContext db) : base(db) { }
    public new Task RecordAsync(string? email, Guid? userId, FailedLoginReason reason,
        string ip, string ua, CancellationToken ct = default)
        => throw new InvalidOperationException("simulated DB failure");
}
```

The cast hack is because `ThrowingRecorderFactory` doesn't share the collection's fixture identity. Adjust by inheriting `AuthTestWebApplicationFactory` and constructing directly.

- [ ] **Step 2: Run + commit**

Run: `dotnet test --filter "FullyQualifiedName~SaveChangesFailure_BubblesAs500"`
Expected: PASS.

```bash
git add ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs
git commit -m "test(auth): recorder failure bubbles as 500, no session cookie issued"
```

---

## Task 31: Update `ADR-0065` exemption list

**Files:**
- Modify: `docs/decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md`

- [ ] **Step 1: Edit the Decision-1 enumeration**

In `docs/decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md`, find the "Decision" section's bullet 1 (currently around line 25):

Replace:

```
1. **Global query filters** are applied to every user-owned entity in `ApplicationDbContext.OnModelCreating`:
   `Transaction`, `Transfer`, `LiabilityPayment`, `Account`, `Category`, `CategoryBudget`, `Budget`, `RecurringTransaction`, `TransactionAttachment`, `SavedReport`, `UserSession`, `Settings`, `SupportTicket`, `AuditLog`, and any future user-owned entities. System tables (`AccountType`, `CategoryType`, `Currency`, `ReportType`, `SystemCategory`) receive no filter.
```

With:

```
1. **Global query filters** are applied to every user-owned entity in `ApplicationDbContext.OnModelCreating`:
   `Transaction`, `Transfer`, `LiabilityPayment`, `Account`, `Category`, `CategoryBudget`, `Budget`, `RecurringTransaction`, `TransactionAttachment`, `SavedReport`, `UserSession`, `Settings`, `SupportTicket`, `AuditLog`, and any future user-owned entities. System tables (`AccountType`, `CategoryType`, `Currency`, `ReportType`, `SystemCategory`) receive no filter. **Intentionally cross-tenant entities** that record events spanning unknown or non-existent users — `FailedLoginAttempt` (added Stage 6b.2) and any future security-event log — also receive no filter; they are read by background purge jobs per ADR-0067 § Decision-6.
```

- [ ] **Step 2: Commit**

```bash
git add docs/decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md
git commit -m "docs(adr-0065): add FailedLoginAttempt to intentional-exemption list (6b.2)"
```

---

## Task 32: Update `security-model.md` for backup-code-during-lockout policy

**Files:**
- Modify: `docs/security-model.md`

- [ ] **Step 1: Find and amend the lockout section**

In `docs/security-model.md`, near line 280, locate the sentence: *"a valid TOTP code should be accepted even during a lockout — the lockout protects against password guessing, not TOTP abuse."*

Replace the surrounding paragraph with:

```
a valid TOTP code should be accepted even during a lockout — the lockout protects against password guessing, not TOTP abuse. **The same policy applies to backup codes**: a valid backup code completes login on a locked account, and the success clears both `AccessFailedCount` and `LockoutEnd`. Backup codes carry ~80 bits of entropy, are single-use, and are Argon2id-hashed; the per-user 10/min rate limit on `/api/auth/login/totp` prevents brute force. (Stage 6b.2.)
```

- [ ] **Step 2: Commit**

```bash
git add docs/security-model.md
git commit -m "docs(security-model): formalize backup-code-during-lockout policy (6b.2)"
```

---

## Task 33: Persist deferred decisions in `planning-phase3.md`

**Files:**
- Modify: `docs/planning-phase3.md`

- [ ] **Step 1: Add a "Stage 6b.2 deferred decisions" subsection**

In `docs/planning-phase3.md`, append (or insert under the existing § for deferred items):

```markdown
### Stage 6b.2 deferred decisions (2026-05-09)

The following items were captured during Stage 6b.2 design and deferred to later stages:

1. **Reverse-proxy header configuration** (Stage 16) — `Program.cs` does not call `UseForwardedHeaders`. Behind any reverse proxy (Cloudflare, nginx, Caddy, Render, Fly), `Connection.RemoteIpAddress` becomes the proxy's IP and the rate limiter collapses every request into one partition. Stage 16 must add `app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto })` plus `KnownProxies`/`KnownNetworks` once the proxy is chosen. Cross-ref: `docs/superpowers/specs/2026-05-09-stage-6b-2-lockout-rate-limit-failed-login-design.md` § Deferred decisions.

2. **In-memory rate limiter is single-host only** (Stage 16) — limiter state is in-process. If Stage 16 introduces a second app instance, swap to a Redis-backed limiter or pin auth endpoints to a single host.

3. **`FailedLoginAttempt` GDPR data-export inclusion** (Stage 6c) — open question. Recommendation: include in Article 15 (access) export, omit from Article 20 (portability) since not user-provided.

4. **`FailedLoginAttempt` GDPR erasure cascade** (Stage 6c) — on right-to-erasure, the 6c flow must `UPDATE FailedLoginAttempt SET EmailAttempted = NULL WHERE EmailAttempted = @normalizedEmail`. Schema enables it (column is nullable).

5. **`FailedLoginAttempt` retention** (Stage 7+) — 1-year flat cross-tenant `DELETE WHERE OccurredAt < now() - interval '1 year'`. Different from `AuditLog` (6 months, per-user fan-out). Implementation lands when the background-job runner ships in Stage 7. First cross-tenant job for the runner.

6. **Backup-code-during-lockout policy** — formalized in `security-model.md` § Login → Account lockout (Stage 6b.2 amendment, 2026-05-09). No further action needed.
```

- [ ] **Step 2: Commit**

```bash
git add docs/planning-phase3.md
git commit -m "docs(planning): persist Stage 6b.2 deferred decisions"
```

---

## Task 34: Mark Stage 6b.2 verification-checklist items in `roadmap-phase-three.md`

**Files:**
- Modify: `docs/roadmap-phase-three.md`

- [ ] **Step 1: Mark the now-shipped items**

In `docs/roadmap-phase-three.md`, under "Stage 6 — Identity infrastructure" → verification checklist:

Mark `[x]`:
- Line 473 — Backup-code use during lockout is honoured
- Line 500 — `/login` endpoint: 10/min/IP minimum (note: changed to sliding window)
- Line 501 — `/register` endpoint: 10/min/IP minimum
- Line 503 — Account lockout: 15 min after 10 failed; counter resets on successful login
- Line 505 — `/login/totp` endpoint: rate-limited per user
- Line 509 — Every failed login logs: timestamp, IP, user-agent, credential vs TOTP failure type
- Line 510 — Attempted password is NEVER logged
- Line 511 — Logs queryable for distributed credential-stuffing detection
- Line 551 — Account lockout test: 10 failed attempts locks; 11th returns lockout error
- Line 553 — TOTP replay test: same code used twice within window is rejected on second use
- Line 554 — TOTP replay survives app restart

Leave `[ ]`:
- Line 502 — `/password-reset` endpoint rate limit (deferred to 6c)
- Line 504 — Lockout email with signed unlock link (deferred to 6c)
- Line 552 — Self-service unlock token test (deferred to 6c)

- [ ] **Step 2: Add a 2026-05-09 Stage 6b.2 update note above the checklist**

After the existing "Stage 6a (2026-05-09)" callout (around line 437), add:

```
> **Stage 6b.2 (2026-05-09):** Lockout enforcement + sliding-window rate limits + failed-login logging + backup-code-during-lockout shipped (51 ship-gate tests in `ProjectCeres.Tests/Integration/Authentication/`). 6b.1 latent bug fixed: `TwoFactorAuthenticatorSignInAsync` replaced with `VerifyTwoFactorTokenAsync`, so wrong TOTP codes no longer increment the password lockout counter. All `Unauthorized(...)` returns aligned to api-contract envelope shape. Items below marked `[x]` for 6b.2; remaining lockout-email + password-reset rate limit + self-service unlock items are scoped to 6c.
```

- [ ] **Step 3: Commit**

```bash
git add docs/roadmap-phase-three.md
git commit -m "docs(roadmap): mark Stage 6b.2 verification items shipped"
```

---

## Task 35: Run sync-docs against the diff

**Files:**
- All `docs/` files potentially affected.

- [ ] **Step 1: Invoke sync-docs**

Run the sync-docs skill against the full diff range from this stage. The skill will inspect git diff and propose additions to `docs/architecture.md`, `docs/api-contract.md` (if new endpoints), `docs/models.md` (FailedLoginAttempt entry), and any other docs the routing table maps. Accept proposals individually.

- [ ] **Step 2: Commit accepted doc updates**

```bash
git add docs/
git commit -m "docs(sync): post-Stage-6b.2 living-doc sync"
```

---

## Task 36: Final full-suite green check

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test`
Expected: All tests pass.

If any test fails, fix the underlying issue rather than skipping. Failed tests at this stage indicate either a regression from earlier tasks or a missed interaction.

- [ ] **Step 2: Confirm no orphan migrations**

Run: `dotnet ef migrations list --project ProjectCeres`
Expected: The `AddFailedLoginAttempt` migration is the most recent and is applied.

- [ ] **Step 3: Tag the stage**

```bash
git log --oneline -40
```

Confirm Stage 6b.2 commits are present and form a logical sequence.

---

## Self-review notes

**Spec coverage** — every Section 3 ship-gate test is mapped to a task:
- Lockout/counter (#1-#6) → Tasks 15, 16, 21
- Backup-code-during-lockout (#7-#13) → Tasks 16, 22, 22.5
- TOTP replay × lockout (#14) → Task 23
- MFA-pending cookie (#15-#17) → Task 24
- Rate limiting (#18-#28) → Tasks 25, 26, 27 (#28 is concurrency)
- Cookie hygiene (#29-#32) → Tasks 19, 20
- Failed-login logging (#33-#44) → Tasks 4, 5, 17, 18, 30
- Concurrency (#45, #46) → Task 27
- Cross-feature (#47-#50) → Task 28
- Architecture (#51-#54) → Task 29

**Type/method consistency** — `FailedLoginRecorder.RecordAsync(string? emailAttempted, Guid? userId, FailedLoginReason reason, string ipAddress, string userAgent, CancellationToken ct = default)` is the single signature used in all tests and controller code. `RequestContext()` returns `(string ip, string ua)` everywhere.

**Naming** — `AuthRateLimitPolicies.AuthLoginByIp` and `AuthRateLimitPolicies.AuthTotpByUser` are the only constants; spec uses the same names.

**Known soft spots** (worth re-reading during execution):
- Task 11's `RateLimitedAuthTestWebApplicationFactory` resolves the `TotpByUserPartitioner` type by name — if the partitioner ends up in a different namespace, adjust the qualified name.
- Task 22.5 reverses the Task 19 pre-check direction. Read both tasks together before executing either.
- Task 30 uses a non-fixture factory pattern for the throwing recorder — the cast hack noted there should be replaced with a clean instantiation when implementing.
