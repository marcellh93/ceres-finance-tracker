# Stage 6b.1 — TOTP MFA (opt-in) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire ASP.NET Core Identity's built-in TOTP for opt-in MFA. Add a `UserMfaBackupCode` table (Argon2id-hashed, single-use) + `TotpReplayEntry` table (Argon2id-hashed, 2-min sliding window). Branch the Stage 6a login flow on the framework's `RequiresTwoFactor` result. Add three enrollment endpoints under `/api/auth/mfa/`. JSON only — no UI; UI ships in Stage 9.

**Architecture:** Use ASP.NET Identity's canonical two-step flow: `SignInManager.PasswordSignInAsync` returns `RequiresTwoFactor=true` and auto-sets the scoped `Identity.TwoFactorUserId` cookie when MFA is enabled; `SignInManager.TwoFactorAuthenticatorSignInAsync` consumes that cookie and issues the real session. We provide our own backup-code path (Identity stores recovery codes plaintext, which violates `security-model.md`). Replay-prevention is a per-row Argon2id-hashed `TotpReplayEntry` table with opportunistic 2-min purge.

**Tech Stack:** .NET 10, ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`), Konscious.Security.Cryptography.Argon2 (already in `csproj` from 6a), EF Core 10 + Npgsql, xUnit + Moq + FluentAssertions, `WebApplicationFactory<Program>` test factory (`AuthTestWebApplicationFactory` from 6a).

**Spec reference:** `docs/superpowers/specs/2026-05-09-stage-6b-1-totp-mfa-design.md` — read it before starting. Locked decisions live there; do not relitigate. Policy authority for opt-in MFA: `docs/decisions/ADR-0069-mfa-opt-in-for-personal-users.md`.

---

## File Structure

This plan creates the following files. Each has one clear responsibility.

### Production code (`ProjectCeres/`)

- `Models/ApplicationUser.cs` — **modified** to add `CreatedAt` (audit only; no behavioural role).
- `Models/UserMfaBackupCode.cs` — new entity for hashed backup codes, one row per code.
- `Models/TotpReplayEntry.cs` — new entity for the 2-min replay window.
- `Data/AppDbContext.cs` — **modified** to register the two new entities and their indexes.
- `Common/Authentication/MfaBackupCodeService.cs` — generate/hash/verify-and-consume/regenerate backup codes.
- `Common/Authentication/TotpReplayGuard.cs` — `TryAcceptAsync` with opportunistic purge.
- `Common/Authentication/MfaConstants.cs` — small string constants (issuer name, manual-entry-key formatter, code-shape regexes).
- `Controllers/Api/MfaController.cs` — three enrollment endpoints.
- `Controllers/Api/AuthController.cs` — **modified** to inspect `SignInResult.RequiresTwoFactor` and add `POST /login/totp`.
- `ViewModels/Auth/LoginTotpRequest.cs` — DTO for `POST /login/totp`.
- `ViewModels/Auth/EnrollVerifyRequest.cs` — DTO for `POST /mfa/enroll/verify`.
- `Migrations/<ts>_AddCreatedAtToApplicationUser.cs` — auto-generated.
- `Migrations/<ts>_AddMfaBackupCodesAndReplayPrevention.cs` — auto-generated.
- `Program.cs` — **modified** to register `MfaBackupCodeService` + `TotpReplayGuard` in DI.

### Test code (`ProjectCeres.Tests/Integration/Authentication/Mfa/`)

New sub-folder under existing `Authentication/`:

- `MfaEnrollmentTests.cs` — `/enroll` returns valid otpauth URI; `/enroll/verify` flips `TwoFactorEnabled` and returns 10 backup codes; wrong code keeps it false; second `/enroll` invalidates the first candidate.
- `MfaBackupCodeServiceTests.cs` — generate yields 10 unique codes; codes are Argon2id-hashed; verify-and-consume marks `UsedAt`; second consume of same code returns false; regenerate deletes existing rows.
- `LoginWithTotpTests.cs` — login with MFA enabled returns 200 + `requiresTotp:true` (no session cookie); valid TOTP via `/login/totp` issues session; replayed code returns 401; valid backup code issues session and marks code used; expired scoped cookie behaviour confirmed.
- `LoginWithoutMfaTests.cs` — confirms ADR-0069 opt-in: no grace cliff; login with `TwoFactorEnabled=false` returns 204 + session regardless of `CreatedAt` age.
- `TotpReplayGuardTests.cs` — same code accepted then rejected; rows older than 2min purged on next accept; per-user scoping holds.
- `MfaCacheControlTests.cs` — all three MFA endpoints return `Cache-Control: no-store, no-cache` + `Pragma: no-cache`.
- `LoginScopedCookieTests.cs` — confirms `Identity.TwoFactorUserId` cookie is set on `RequiresTwoFactor` response and that other authenticated endpoints (e.g. `/api/accounts`) still return 401 even when this scoped cookie is present.
- `AuthTestFixture.cs` — **modified** to add `EnrollUserMfaAsync(factory, user, password)` helper that calls `GenerateNewAuthenticatorKey`, computes a current TOTP code, calls `SetTwoFactorEnabledAsync(true)`, and returns the seed so subsequent tests can compute fresh codes. Also adds a small TOTP-code-from-seed helper using Identity's own algorithm.

### Files NOT touched

- All Stage 6a files except those modified above. The `AuthController.Register`, `AuthController.Logout`, `AuthController.Csrf`, the cookie config, the persistent-cookie middleware, the architecture tests, the global fallback policy — none of these change.
- `WafCollection.cs` / `TestAuthenticationHandler.cs` — the two-factory split from 6a still applies. Pre-Stage-6a CRUD tests use `TestWebApplicationFactory` (auto-auth, MFA bypassed entirely). 6b.1 tests use `AuthTestWebApplicationFactory` (real pipeline).

---

## Working agreements before starting

- **Stay on `main` per project memory.** No branches, no worktrees.
- **Foreground tests + builds per project memory.** Don't background `dotnet test` / `dotnet build`.
- **Commit cadence:** every task ends with a commit. Commit messages start with `feat(auth):`, `test(auth):`, `chore(auth):`, or `refactor(auth):`.
- **No `Co-Authored-By` trailer.**
- **Migrations:** apply to both dev DB and `project_ceres_test` (the WAF integration-test DB) immediately after generation, before running tests.
- **Test running:** `dotnet test --filter "FullyQualifiedName~Authentication"` for fast scoped runs.

---

## Task 0: Add `CreatedAt` to `ApplicationUser`

**Files:**
- Modify: `ProjectCeres/Models/ApplicationUser.cs`

- [ ] **Step 1: Add the property**

Path: `ProjectCeres/Models/ApplicationUser.cs`

```csharp
using Microsoft.AspNetCore.Identity;

namespace ProjectCeres.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Build to confirm**

Run: `dotnet build`
Expected: zero errors. The property is now part of the model.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/Models/ApplicationUser.cs
git -C <repo> commit -m "feat(auth): add ApplicationUser.CreatedAt for audit trail"
```

---

## Task 1: Migration `AddCreatedAtToApplicationUser`

**Files:**
- Create: `ProjectCeres/Migrations/<ts>_AddCreatedAtToApplicationUser.cs` (auto-generated)

- [ ] **Step 1: Generate the migration**

```bash
dotnet ef migrations add AddCreatedAtToApplicationUser --project ProjectCeres
```

- [ ] **Step 2: Verify the generated SQL**

Open the generated file. Verify it:
- Adds a `CreatedAt timestamp with time zone NOT NULL` column to `AspNetUsers`.
- Backfills existing rows with `current_timestamp` (default).

If the column is nullable or missing the default, edit the migration's `Up` method:

```csharp
migrationBuilder.AddColumn<DateTime>(
    name: "CreatedAt",
    table: "AspNetUsers",
    type: "timestamp with time zone",
    nullable: false,
    defaultValueSql: "current_timestamp");
```

- [ ] **Step 3: Apply to dev + test DBs**

```bash
dotnet ef database update --project ProjectCeres
dotnet ef database update --project ProjectCeres --connection "Host=localhost;Database=project_ceres_test;Username=postgres;Password=postgres"
```

Expected: both runs end with `Done.`

- [ ] **Step 4: Commit**

```bash
git -C <repo> add ProjectCeres/Migrations/
git -C <repo> commit -m "feat(auth): EF migration AddCreatedAtToApplicationUser"
```

---

## Task 2: `UserMfaBackupCode` + `TotpReplayEntry` entities

**Files:**
- Create: `ProjectCeres/Models/UserMfaBackupCode.cs`
- Create: `ProjectCeres/Models/TotpReplayEntry.cs`
- Modify: `ProjectCeres/Data/AppDbContext.cs`

- [ ] **Step 1: Create `UserMfaBackupCode.cs`**

Path: `ProjectCeres/Models/UserMfaBackupCode.cs`

```csharp
namespace ProjectCeres.Models;

/// <summary>
/// One row per MFA backup code. Codes are Argon2id-hashed (PHC string in
/// CodeHash). Single-use: UsedAt is set on first successful verify.
/// Regeneration deletes all existing rows for the user.
/// </summary>
public sealed class UserMfaBackupCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? UsedFromIp { get; set; }
}
```

- [ ] **Step 2: Create `TotpReplayEntry.cs`**

Path: `ProjectCeres/Models/TotpReplayEntry.cs`

```csharp
namespace ProjectCeres.Models;

/// <summary>
/// One row per TOTP code accepted within the 2-minute replay window. Argon2id-hashed
/// (per-row salt). On every accept call we (a) verify the new code does not match any
/// existing row in the window, then (b) insert the new row + opportunistic-purge old
/// rows. Purge happens inline on every call instead of as a scheduled job.
/// </summary>
public sealed class TotpReplayEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime AcceptedAt { get; set; }
}
```

- [ ] **Step 3: Register in `AppDbContext`**

Open `ProjectCeres/Data/AppDbContext.cs`. Add the `DbSet`s near the existing user-owned ones:

```csharp
public DbSet<ProjectCeres.Models.UserMfaBackupCode> UserMfaBackupCodes => Set<ProjectCeres.Models.UserMfaBackupCode>();
public DbSet<ProjectCeres.Models.TotpReplayEntry> TotpReplayEntries => Set<ProjectCeres.Models.TotpReplayEntry>();
```

In `OnModelCreating`, after the existing 6a `UserSession` / `UserBlockedIp` registration, add:

```csharp
modelBuilder.Entity<ProjectCeres.Models.UserMfaBackupCode>(b =>
{
    b.HasKey(c => c.Id);
    b.HasIndex(c => c.UserId);
    // Postgres partial index: only the unused codes (the hot path in verify).
    b.HasIndex(c => new { c.UserId, c.UsedAt })
        .HasFilter(@"""UsedAt"" IS NULL")
        .HasDatabaseName("IX_UserMfaBackupCodes_UserId_Unused");
    b.Property(c => c.CodeHash).HasMaxLength(512);
    b.Property(c => c.UsedFromIp).HasMaxLength(45);
});

modelBuilder.Entity<ProjectCeres.Models.TotpReplayEntry>(b =>
{
    b.HasKey(e => e.Id);
    b.HasIndex(e => e.UserId);
    b.HasIndex(e => e.AcceptedAt);
    b.Property(e => e.CodeHash).HasMaxLength(512);
});
```

- [ ] **Step 4: Build to confirm**

Run: `dotnet build`
Expected: zero errors.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres/Models/UserMfaBackupCode.cs ProjectCeres/Models/TotpReplayEntry.cs ProjectCeres/Data/AppDbContext.cs
git -C <repo> commit -m "feat(auth): add UserMfaBackupCode + TotpReplayEntry entities"
```

---

## Task 3: Migration `AddMfaBackupCodesAndReplayPrevention`

**Files:**
- Create: `ProjectCeres/Migrations/<ts>_AddMfaBackupCodesAndReplayPrevention.cs` (auto-generated)

- [ ] **Step 1: Generate**

```bash
dotnet ef migrations add AddMfaBackupCodesAndReplayPrevention --project ProjectCeres
```

- [ ] **Step 2: Verify generated SQL**

Open the migration. Verify:
- `UserMfaBackupCodes` table created with the columns from Task 2.
- `TotpReplayEntries` table created with the columns from Task 2.
- Three indexes on `UserMfaBackupCodes`: `UserId`, the partial unused-codes index, and the PK.
- Two indexes on `TotpReplayEntries`: `UserId`, `AcceptedAt`.
- No foreign keys to `AspNetUsers` (Stage 7's data remap adds those — same pattern as Stage 6a's `UserSession` and `UserBlockedIp`).

- [ ] **Step 3: Apply to dev + test DBs**

```bash
dotnet ef database update --project ProjectCeres
dotnet ef database update --project ProjectCeres --connection "Host=localhost;Database=project_ceres_test;Username=postgres;Password=postgres"
```

- [ ] **Step 4: Commit**

```bash
git -C <repo> add ProjectCeres/Migrations/
git -C <repo> commit -m "feat(auth): EF migration AddMfaBackupCodesAndReplayPrevention"
```

---

## Task 4: `MfaConstants`

**Files:**
- Create: `ProjectCeres/Common/Authentication/MfaConstants.cs`

- [ ] **Step 1: Create the constants file**

Path: `ProjectCeres/Common/Authentication/MfaConstants.cs`

```csharp
using System.Text.RegularExpressions;

namespace ProjectCeres.Common.Authentication;

public static class MfaConstants
{
    public const string Issuer = "Project Ceres";

    /// <summary>10 backup codes per batch.</summary>
    public const int BackupCodeBatchSize = 10;

    /// <summary>16 chars from Crockford base-32 → ~80 bits entropy.</summary>
    public const int BackupCodeLength = 16;

    /// <summary>
    /// Crockford base-32 alphabet — 32 chars, no I/L/O/U (visually ambiguous letters).
    /// All 10 digits are kept so a 16-digit string is technically a valid backup code,
    /// which is why TOTP shape detection runs first (^\d{6}$) before backup-code shape.
    /// </summary>
    public const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Replay window: any code accepted within this duration cannot be re-used.</summary>
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(2);

    /// <summary>Six-digit numeric TOTP shape. Tested first; if it matches, route to the TOTP path.</summary>
    public static readonly Regex TotpCodeShape = new(@"^\d{6}$", RegexOptions.Compiled);

    /// <summary>
    /// Backup-code shape after stripping `-` separators and uppercasing.
    /// Tests strict 16-char Crockford base-32 (excludes I/L/O/U).
    /// </summary>
    public static readonly Regex BackupCodeShape = new(
        @"^[0-9A-HJKMNP-TV-Z]{16}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/MfaConstants.cs
git -C <repo> commit -m "feat(auth): MfaConstants (Crockford alphabet, code shapes, replay window)"
```

---

## Task 5: `MfaBackupCodeService` (TDD — generate + hash)

**Files:**
- Create: `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaBackupCodeServiceTests.cs`

- [ ] **Step 1: Write the failing test for generate**

Path: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaBackupCodeServiceTests.cs`

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class MfaBackupCodeServiceTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MfaBackupCodeServiceTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserMfaBackupCodes
            .Where(c => c.UserId.ToString().StartsWith("dddddddd-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GenerateAndPersistAsync_yields_10_unique_argon2id_hashed_rows()
    {
        var userId = new Guid("dddddddd-0000-0000-0000-000000000001");
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var codes = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);

        codes.Should().HaveCount(10);
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => c.Length == 19); // 16 chars + 3 hyphens

        var rows = await db.UserMfaBackupCodes.Where(c => c.UserId == userId).ToListAsync();
        rows.Should().HaveCount(10);
        rows.Should().OnlyContain(r => r.CodeHash.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$"));
        rows.Should().OnlyContain(r => r.UsedAt == null);
    }
}
```

- [ ] **Step 2: Run; expect compile fail**

Run: `dotnet test --filter "FullyQualifiedName~MfaBackupCodeServiceTests"`
Expected: compile error — `MfaBackupCodeService` does not exist.

- [ ] **Step 3: Implement generate + hash**

Path: `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs`

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class MfaBackupCodeService
{
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _hasher;

    public MfaBackupCodeService(AppDbContext db, Argon2idPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<IReadOnlyList<string>> GenerateAndPersistAsync(Guid userId, CancellationToken ct)
    {
        var codes = new List<string>(MfaConstants.BackupCodeBatchSize);
        for (int i = 0; i < MfaConstants.BackupCodeBatchSize; i++)
        {
            var raw = GenerateOne();
            var hash = _hasher.HashPassword(new ApplicationUser(), raw);
            _db.UserMfaBackupCodes.Add(new UserMfaBackupCode
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CodeHash = hash,
                CreatedAt = DateTime.UtcNow,
            });
            codes.Add(Format(raw));
        }
        await _db.SaveChangesAsync(ct);
        return codes;
    }

    private static string GenerateOne()
    {
        var sb = new StringBuilder(MfaConstants.BackupCodeLength);
        Span<byte> bytes = stackalloc byte[MfaConstants.BackupCodeLength];
        RandomNumberGenerator.Fill(bytes);
        for (int i = 0; i < MfaConstants.BackupCodeLength; i++)
        {
            sb.Append(MfaConstants.CrockfordAlphabet[bytes[i] & 0x1F]);
        }
        return sb.ToString();
    }

    /// <summary>Inserts hyphens every 4 chars: ABCD-EFGH-JKLM-NPQR.</summary>
    private static string Format(string raw) =>
        $"{raw[..4]}-{raw[4..8]}-{raw[8..12]}-{raw[12..16]}";
}
```

- [ ] **Step 4: Register in DI**

Open `ProjectCeres/Program.cs`. Find the existing Stage 6a auth-services block (after `PersistentTokenService` registration). Add:

```csharp
builder.Services.AddScoped<MfaBackupCodeService>();
```

- [ ] **Step 5: Run the test; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~MfaBackupCodeServiceTests.GenerateAndPersistAsync_yields"`
Expected: 1 passed.

- [ ] **Step 6: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/MfaBackupCodeService.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaBackupCodeServiceTests.cs ProjectCeres/Program.cs
git -C <repo> commit -m "feat(auth): MfaBackupCodeService.GenerateAndPersistAsync (Argon2id-hashed batches of 10)"
```

---

## Task 6: `MfaBackupCodeService.VerifyAndConsumeAsync`

**Files:**
- Modify: `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaBackupCodeServiceTests.cs`

- [ ] **Step 1: Add the failing tests**

Append to `MfaBackupCodeServiceTests.cs`:

```csharp
[Fact]
public async Task VerifyAndConsumeAsync_marks_UsedAt_and_returns_true_on_match()
{
    var userId = new Guid("dddddddd-0000-0000-0000-000000000002");
    using var scope = _factory.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var codes = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);
    var first = codes.First();

    var ok = await svc.VerifyAndConsumeAsync(userId, first, "10.0.0.1", CancellationToken.None);
    ok.Should().BeTrue();

    var row = await db.UserMfaBackupCodes
        .FirstAsync(c => c.UserId == userId && c.UsedAt != null);
    row.UsedFromIp.Should().Be("10.0.0.1");
}

[Fact]
public async Task VerifyAndConsumeAsync_returns_false_on_replay_of_used_code()
{
    var userId = new Guid("dddddddd-0000-0000-0000-000000000003");
    using var scope = _factory.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();

    var codes = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);
    var first = codes.First();

    (await svc.VerifyAndConsumeAsync(userId, first, "10.0.0.1", CancellationToken.None)).Should().BeTrue();
    (await svc.VerifyAndConsumeAsync(userId, first, "10.0.0.1", CancellationToken.None)).Should().BeFalse();
}

[Fact]
public async Task VerifyAndConsumeAsync_accepts_hyphenated_or_unhyphenated_input()
{
    var userId = new Guid("dddddddd-0000-0000-0000-000000000004");
    using var scope = _factory.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();

    var codes = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);
    var first = codes.First();              // formatted: XXXX-XXXX-XXXX-XXXX
    var unhyphenated = first.Replace("-", "");

    var ok = await svc.VerifyAndConsumeAsync(userId, unhyphenated, "10.0.0.1", CancellationToken.None);
    ok.Should().BeTrue();
}
```

- [ ] **Step 2: Run; expect compile fail**

Run: `dotnet test --filter "FullyQualifiedName~MfaBackupCodeServiceTests.VerifyAndConsumeAsync"`
Expected: compile error — method does not exist.

- [ ] **Step 3: Implement verify-and-consume**

Append to `MfaBackupCodeService.cs`:

```csharp
public async Task<bool> VerifyAndConsumeAsync(
    Guid userId, string submittedCode, string clientIp, CancellationToken ct)
{
    var normalized = NormalizeForVerify(submittedCode);
    if (normalized is null) return false;

    var unused = await _db.UserMfaBackupCodes
        .Where(c => c.UserId == userId && c.UsedAt == null)
        .ToListAsync(ct);

    foreach (var row in unused)
    {
        var result = _hasher.VerifyHashedPassword(new ApplicationUser(), row.CodeHash, normalized);
        if (result is Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success
                   or Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded)
        {
            row.UsedAt = DateTime.UtcNow;
            row.UsedFromIp = clientIp;
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
    return false;
}

private static string? NormalizeForVerify(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return null;
    var stripped = input.Replace("-", "").Replace(" ", "").ToUpperInvariant();
    return MfaConstants.BackupCodeShape.IsMatch(stripped) ? stripped : null;
}
```

- [ ] **Step 4: Run the tests; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~MfaBackupCodeServiceTests.VerifyAndConsume"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/MfaBackupCodeService.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaBackupCodeServiceTests.cs
git -C <repo> commit -m "feat(auth): MfaBackupCodeService.VerifyAndConsumeAsync (single-use, hyphen-tolerant)"
```

---

## Task 7: `MfaBackupCodeService.RegenerateAsync`

**Files:**
- Modify: `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaBackupCodeServiceTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `MfaBackupCodeServiceTests.cs`:

```csharp
[Fact]
public async Task RegenerateAsync_invalidates_all_previous_codes_and_yields_10_new()
{
    var userId = new Guid("dddddddd-0000-0000-0000-000000000005");
    using var scope = _factory.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var first = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);
    await svc.RegenerateAsync(userId, CancellationToken.None);
    var second = await db.UserMfaBackupCodes
        .Where(c => c.UserId == userId)
        .ToListAsync();

    second.Should().HaveCount(10);
    // None of the new rows should validate against the old plaintext codes.
    var oldFirst = first.First();
    var ok = await svc.VerifyAndConsumeAsync(userId, oldFirst, "10.0.0.1", CancellationToken.None);
    ok.Should().BeFalse();
}
```

- [ ] **Step 2: Run; expect compile fail**

Run: `dotnet test --filter "FullyQualifiedName~MfaBackupCodeServiceTests.RegenerateAsync"`
Expected: compile error — method does not exist.

- [ ] **Step 3: Implement regenerate**

Append to `MfaBackupCodeService.cs`:

```csharp
public async Task<IReadOnlyList<string>> RegenerateAsync(Guid userId, CancellationToken ct)
{
    await _db.UserMfaBackupCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
    return await GenerateAndPersistAsync(userId, ct);
}
```

- [ ] **Step 4: Run the test; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~MfaBackupCodeServiceTests.RegenerateAsync"`
Expected: 1 passed.

- [ ] **Step 5: Run full backup-code suite to confirm no regression**

Run: `dotnet test --filter "FullyQualifiedName~MfaBackupCodeServiceTests"`
Expected: 5 passed.

- [ ] **Step 6: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/MfaBackupCodeService.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaBackupCodeServiceTests.cs
git -C <repo> commit -m "feat(auth): MfaBackupCodeService.RegenerateAsync (delete-all + generate-10)"
```

---

## Task 8: `TotpReplayGuard` (TDD)

**Files:**
- Create: `ProjectCeres/Common/Authentication/TotpReplayGuard.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/Mfa/TotpReplayGuardTests.cs`

- [ ] **Step 1: Write the failing tests**

Path: `ProjectCeres.Tests/Integration/Authentication/Mfa/TotpReplayGuardTests.cs`

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class TotpReplayGuardTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public TotpReplayGuardTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.TotpReplayEntries
            .Where(e => e.UserId.ToString().StartsWith("ddddeeee-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TryAcceptAsync_accepts_first_use_and_rejects_replay_within_window()
    {
        var userId = new Guid("ddddeeee-0000-0000-0000-000000000001");
        using var scope = _factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<TotpReplayGuard>();

        (await guard.TryAcceptAsync(userId, "123456", CancellationToken.None)).Should().BeTrue();
        (await guard.TryAcceptAsync(userId, "123456", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task TryAcceptAsync_does_not_block_a_different_user_using_the_same_numeric_code()
    {
        var userA = new Guid("ddddeeee-0000-0000-0000-000000000002");
        var userB = new Guid("ddddeeee-0000-0000-0000-000000000003");
        using var scope = _factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<TotpReplayGuard>();

        (await guard.TryAcceptAsync(userA, "654321", CancellationToken.None)).Should().BeTrue();
        (await guard.TryAcceptAsync(userB, "654321", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task TryAcceptAsync_purges_rows_older_than_replay_window()
    {
        var userId = new Guid("ddddeeee-0000-0000-0000-000000000004");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<TotpReplayGuard>();

        // Seed an "old" entry just past the replay window.
        db.TotpReplayEntries.Add(new TotpReplayEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = "$argon2id$v=19$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            AcceptedAt = DateTime.UtcNow - TimeSpan.FromMinutes(5),
        });
        await db.SaveChangesAsync();

        // Accept any new code → should trigger purge.
        await guard.TryAcceptAsync(userId, "111111", CancellationToken.None);

        var remaining = await db.TotpReplayEntries
            .Where(e => e.UserId == userId)
            .ToListAsync();
        // One row remains (the one we just accepted); the old one was purged.
        remaining.Should().HaveCount(1);
        remaining[0].AcceptedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }
}
```

- [ ] **Step 2: Run; expect compile fail**

Run: `dotnet test --filter "FullyQualifiedName~TotpReplayGuardTests"`
Expected: compile error — `TotpReplayGuard` does not exist.

- [ ] **Step 3: Implement the guard**

Path: `ProjectCeres/Common/Authentication/TotpReplayGuard.cs`

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class TotpReplayGuard
{
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _hasher;

    public TotpReplayGuard(AppDbContext db, Argon2idPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    /// <summary>
    /// Returns true if the code has not been accepted within the replay window for
    /// this user; false if it is a replay. On true, the code is recorded in the table
    /// and old rows beyond the window are purged.
    /// </summary>
    public async Task<bool> TryAcceptAsync(Guid userId, string code, CancellationToken ct)
    {
        var threshold = DateTime.UtcNow - MfaConstants.ReplayWindow;

        var candidates = await _db.TotpReplayEntries
            .Where(e => e.UserId == userId && e.AcceptedAt > threshold)
            .ToListAsync(ct);

        foreach (var row in candidates)
        {
            var result = _hasher.VerifyHashedPassword(new ApplicationUser(), row.CodeHash, code);
            if (result is PasswordVerificationResult.Success
                       or PasswordVerificationResult.SuccessRehashNeeded)
            {
                return false;   // replay
            }
        }

        // Insert + opportunistic purge of out-of-window entries.
        _db.TotpReplayEntries.Add(new TotpReplayEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = _hasher.HashPassword(new ApplicationUser(), code),
            AcceptedAt = DateTime.UtcNow,
        });
        await _db.TotpReplayEntries
            .Where(e => e.AcceptedAt < threshold)
            .ExecuteDeleteAsync(ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 4: Register in DI**

Open `ProjectCeres/Program.cs`. After the `MfaBackupCodeService` registration, add:

```csharp
builder.Services.AddScoped<TotpReplayGuard>();
```

- [ ] **Step 5: Run the tests; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~TotpReplayGuardTests"`
Expected: 3 passed.

- [ ] **Step 6: Commit**

```bash
git -C <repo> add ProjectCeres/Common/Authentication/TotpReplayGuard.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Authentication/Mfa/TotpReplayGuardTests.cs
git -C <repo> commit -m "feat(auth): TotpReplayGuard (Argon2id-hashed 2-min sliding window with opportunistic purge)"
```

---

## Task 9: `AuthTestFixture.EnrollUserMfaAsync` helper

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs`

- [ ] **Step 1: Add the helper**

Open `AuthTestFixture.cs`. Append the following after the existing methods, **inside** the class:

```csharp
/// <summary>
/// Enrolls a user's TOTP via Identity's built-in flow. Generates an authenticator
/// key, computes a current TOTP code, and flips TwoFactorEnabled to true. Returns
/// the seed (base32 string) so subsequent test code can compute fresh codes for
/// login attempts.
/// </summary>
public static async Task<string> EnrollUserMfaAsync(
    AuthTestWebApplicationFactory factory, ApplicationUser user)
{
    using var scope = factory.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var freshUser = await userManager.FindByIdAsync(user.Id.ToString())
        ?? throw new InvalidOperationException("user vanished between Register + Enroll");

    await userManager.ResetAuthenticatorKeyAsync(freshUser);
    var seed = await userManager.GetAuthenticatorKeyAsync(freshUser)
        ?? throw new InvalidOperationException("authenticator key not set after generate");

    var code = ComputeCurrentTotpCode(seed);
    var verified = await userManager.VerifyTwoFactorTokenAsync(
        freshUser, TokenOptions.DefaultAuthenticatorProvider, code);
    verified.Should().BeTrue("freshly-generated code must verify");

    await userManager.SetTwoFactorEnabledAsync(freshUser, true);
    return seed;
}

/// <summary>
/// Computes the current 6-digit TOTP code for a base32-encoded seed using the
/// standard RFC 6238 algorithm with 30-second period and SHA1. Mirrors what
/// Identity does internally; we replicate it here so tests can produce codes
/// without standing up an authenticator app.
/// </summary>
public static string ComputeCurrentTotpCode(string base32Seed)
{
    var key = DecodeBase32(base32Seed);
    var counter = (long)Math.Floor(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30.0);
    var counterBytes = BitConverter.GetBytes(counter);
    if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

    using var hmac = new System.Security.Cryptography.HMACSHA1(key);
    var hash = hmac.ComputeHash(counterBytes);
    var offset = hash[^1] & 0x0F;
    var binary = ((hash[offset] & 0x7F) << 24)
               | ((hash[offset + 1] & 0xFF) << 16)
               | ((hash[offset + 2] & 0xFF) << 8)
               | (hash[offset + 3] & 0xFF);
    return (binary % 1_000_000).ToString("D6");
}

private static byte[] DecodeBase32(string input)
{
    const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    var output = new List<byte>();
    int buffer = 0, bits = 0;
    foreach (var c in input.ToUpperInvariant())
    {
        if (c == '=') break;
        var idx = alphabet.IndexOf(c);
        if (idx < 0) continue;
        buffer = (buffer << 5) | idx;
        bits += 5;
        if (bits >= 8)
        {
            bits -= 8;
            output.Add((byte)((buffer >> bits) & 0xFF));
        }
    }
    return output.ToArray();
}
```

You'll need to add the using directives at the top of the file if not already present:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;
```

- [ ] **Step 2: Build to confirm compile**

Run: `dotnet build`
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs
git -C <repo> commit -m "test(auth): EnrollUserMfaAsync + ComputeCurrentTotpCode helpers in AuthTestFixture"
```

---

## Task 10: Auth request DTOs for MFA

**Files:**
- Create: `ProjectCeres/ViewModels/Auth/LoginTotpRequest.cs`
- Create: `ProjectCeres/ViewModels/Auth/EnrollVerifyRequest.cs`

- [ ] **Step 1: Create login-TOTP request DTO**

Path: `ProjectCeres/ViewModels/Auth/LoginTotpRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class LoginTotpRequest
{
    /// <summary>
    /// Either a 6-digit TOTP code or a 16-character Crockford backup code
    /// (with or without `-` separators). The endpoint detects shape and routes.
    /// </summary>
    [Required, StringLength(32)]
    public string Code { get; set; } = "";
}
```

- [ ] **Step 2: Create enroll-verify request DTO**

Path: `ProjectCeres/ViewModels/Auth/EnrollVerifyRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class EnrollVerifyRequest
{
    [Required, StringLength(8), RegularExpression(@"^\d{6}$")]
    public string Code { get; set; } = "";
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git -C <repo> add ProjectCeres/ViewModels/Auth/LoginTotpRequest.cs ProjectCeres/ViewModels/Auth/EnrollVerifyRequest.cs
git -C <repo> commit -m "feat(auth): LoginTotpRequest + EnrollVerifyRequest DTOs"
```

---

## Task 11: `MfaController.Enroll` (TDD)

**Files:**
- Create: `ProjectCeres/Controllers/Api/MfaController.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaEnrollmentTests.cs`

- [ ] **Step 1: Write the failing test**

Path: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaEnrollmentTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class MfaEnrollmentTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MfaEnrollmentTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@mfa-enroll-test.local")).ToList())
        {
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Enroll_returns_otpauth_uri_and_manual_entry_key()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "e@mfa-enroll-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Login first so the user has a session cookie.
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "e@mfa-enroll-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);

        // Enroll
        var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
        var resp = await client.SendAsync(enrollReq);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("otpAuthUri").GetString().Should()
            .StartWith("otpauth://totp/Project%20Ceres:e@mfa-enroll-test.local?secret=");
        body.GetProperty("manualEntryKey").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Enroll_response_carries_no_store_cache_control()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "c@mfa-enroll-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Login
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "c@mfa-enroll-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);

        // Enroll
        var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
        var resp = await client.SendAsync(enrollReq);

        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
        resp.Headers.CacheControl!.NoCache.Should().BeTrue();
    }

    private static string? ExtractSetCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var first = v.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == cookieName) return first[(eq + 1)..];
        }
        return null;
    }
}
```

- [ ] **Step 2: Run; expect FAIL**

Run: `dotnet test --filter "FullyQualifiedName~MfaEnrollmentTests"`
Expected: tests fail because `/api/auth/mfa/enroll` returns 404 (controller does not exist yet).

- [ ] **Step 3: Implement `MfaController.Enroll`**

Path: `ProjectCeres/Controllers/Api/MfaController.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/mfa")]
[Authorize]
public sealed class MfaController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public MfaController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll()
    {
        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();

        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key)) return StatusCode(500);

        var encodedIssuer = Uri.EscapeDataString(MfaConstants.Issuer);
        var encodedEmail = Uri.EscapeDataString(user.Email ?? "");
        var otpAuthUri = $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={key}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
        var manualEntryKey = FormatManualKey(key);

        ApplyNoStoreHeaders();
        return Ok(new { otpAuthUri, manualEntryKey });
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var sid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return sid is null ? null : await _userManager.FindByIdAsync(sid);
    }

    private static string FormatManualKey(string key)
    {
        // Insert a space every 4 chars: ABCD EFGH JKLM NPQR STUV
        var sb = new System.Text.StringBuilder(key.Length + key.Length / 4);
        for (int i = 0; i < key.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(key[i]);
        }
        return sb.ToString();
    }

    private void ApplyNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
    }
}
```

- [ ] **Step 4: Run the tests; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~MfaEnrollmentTests"`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres/Controllers/Api/MfaController.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaEnrollmentTests.cs
git -C <repo> commit -m "feat(auth): POST /api/auth/mfa/enroll (otpauth URI + manual entry key, no-store)"
```

---

## Task 12: `MfaController.EnrollVerify` (TDD)

**Files:**
- Modify: `ProjectCeres/Controllers/Api/MfaController.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaEnrollmentTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `MfaEnrollmentTests.cs`:

```csharp
[Fact]
public async Task EnrollVerify_with_correct_code_flips_TwoFactorEnabled_and_returns_10_backup_codes()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, "v@mfa-enroll-test.local");
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Login
    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new
        {
            email = "v@mfa-enroll-test.local",
            password = AuthTestFixture.ValidPassword,
            rememberMe = false
        }),
    };
    loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
    loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
    var loginResp = await client.SendAsync(loginReq);
    var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);

    // Enroll → get the seed by reading the otpAuthUri's secret param
    var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    enrollReq.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
    enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
    var enrollResp = await client.SendAsync(enrollReq);
    var enrollBody = await enrollResp.Content.ReadFromJsonAsync<JsonElement>();
    var otpAuthUri = enrollBody.GetProperty("otpAuthUri").GetString()!;
    var seed = ExtractSecretFromUri(otpAuthUri);

    // Compute current code; verify
    var code = AuthTestFixture.ComputeCurrentTotpCode(seed);

    var (verifyCookie, verifyHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var verifyReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll/verify")
    {
        Content = JsonContent.Create(new { code }),
    };
    verifyReq.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={verifyCookie}");
    verifyReq.Headers.Add(SessionConstants.CsrfHeaderName, verifyHeader);
    var resp = await client.SendAsync(verifyReq);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
    var codes = body.GetProperty("backupCodes").EnumerateArray().Select(e => e.GetString()!).ToList();
    codes.Should().HaveCount(10);
    codes.Should().OnlyHaveUniqueItems();

    // Confirm TwoFactorEnabled flipped
    using var scope = _factory.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var refreshed = await userManager.FindByIdAsync(user.Id.ToString());
    refreshed!.TwoFactorEnabled.Should().BeTrue();
}

[Fact]
public async Task EnrollVerify_with_wrong_code_keeps_TwoFactorEnabled_false()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, "w@mfa-enroll-test.local");
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Login
    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new
        {
            email = "w@mfa-enroll-test.local",
            password = AuthTestFixture.ValidPassword,
            rememberMe = false
        }),
    };
    loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
    loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
    var loginResp = await client.SendAsync(loginReq);
    var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);

    // Enroll
    var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
    enrollReq.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
    enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
    await client.SendAsync(enrollReq);

    // Verify with wrong code
    var (verifyCookie, verifyHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var verifyReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll/verify")
    {
        Content = JsonContent.Create(new { code = "000000" }),
    };
    verifyReq.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={verifyCookie}");
    verifyReq.Headers.Add(SessionConstants.CsrfHeaderName, verifyHeader);
    var resp = await client.SendAsync(verifyReq);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    using var scope = _factory.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var refreshed = await userManager.FindByIdAsync(user.Id.ToString());
    refreshed!.TwoFactorEnabled.Should().BeFalse();
}

private static string ExtractSecretFromUri(string otpAuthUri)
{
    var uri = new Uri(otpAuthUri);
    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
    return query["secret"]!;
}
```

- [ ] **Step 2: Run; expect FAIL**

Run: `dotnet test --filter "FullyQualifiedName~MfaEnrollmentTests.EnrollVerify"`
Expected: tests fail — endpoint does not exist.

- [ ] **Step 3: Implement `EnrollVerify`**

Append to `MfaController.cs`:

```csharp
[HttpPost("enroll/verify")]
public async Task<IActionResult> EnrollVerify(
    [FromBody] ProjectCeres.ViewModels.Auth.EnrollVerifyRequest request,
    [FromServices] MfaBackupCodeService backupCodes)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);

    var user = await GetCurrentUserAsync();
    if (user is null) return Unauthorized();

    var hasKey = await _userManager.GetAuthenticatorKeyAsync(user);
    if (string.IsNullOrEmpty(hasKey))
    {
        return BadRequest(new { error = "no_enrollment_in_progress" });
    }

    var verified = await _userManager.VerifyTwoFactorTokenAsync(
        user, TokenOptions.DefaultAuthenticatorProvider, request.Code);
    if (!verified)
    {
        return BadRequest(new { error = "code_did_not_verify" });
    }

    await _userManager.SetTwoFactorEnabledAsync(user, true);
    var codes = await backupCodes.GenerateAndPersistAsync(user.Id, HttpContext.RequestAborted);

    ApplyNoStoreHeaders();
    return Ok(new { backupCodes = codes });
}
```

- [ ] **Step 4: Run the tests; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~MfaEnrollmentTests.EnrollVerify"`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres/Controllers/Api/MfaController.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaEnrollmentTests.cs
git -C <repo> commit -m "feat(auth): POST /api/auth/mfa/enroll/verify (flip TwoFactorEnabled, return 10 backup codes)"
```

---

## Task 13: `MfaController.RegenerateBackupCodes` (TDD)

**Files:**
- Modify: `ProjectCeres/Controllers/Api/MfaController.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaEnrollmentTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `MfaEnrollmentTests.cs`:

```csharp
[Fact]
public async Task RegenerateBackupCodes_returns_10_fresh_codes_and_invalidates_previous()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, "r@mfa-enroll-test.local");
    var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Login (TOTP-required path)
    var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
    var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new
        {
            email = "r@mfa-enroll-test.local",
            password = AuthTestFixture.ValidPassword,
            rememberMe = false
        }),
    };
    loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
    loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
    var loginResp = await client.SendAsync(loginReq);
    var twoFactorCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
    twoFactorCookie.Should().NotBeNullOrEmpty();

    // Complete login with TOTP
    var totpCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
    var (totpCsrf, totpHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
    {
        Content = JsonContent.Create(new { code = totpCode }),
    };
    totpReq.Headers.Add("Cookie",
        $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={totpCsrf}");
    totpReq.Headers.Add(SessionConstants.CsrfHeaderName, totpHeader);
    var totpResp = await client.SendAsync(totpReq);
    var sessionCookie = ExtractSetCookie(totpResp, SessionConstants.SessionCookieName);

    // Now regenerate
    var (regenCookie, regenHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var regenReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/backup-codes/regenerate");
    regenReq.Headers.Add("Cookie",
        $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={regenCookie}");
    regenReq.Headers.Add(SessionConstants.CsrfHeaderName, regenHeader);
    var resp = await client.SendAsync(regenReq);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
    var codes = body.GetProperty("backupCodes").EnumerateArray().Select(e => e.GetString()!).ToList();
    codes.Should().HaveCount(10);
    codes.Should().OnlyHaveUniqueItems();
}
```

- [ ] **Step 2: Run; expect FAIL**

Run: `dotnet test --filter "FullyQualifiedName~MfaEnrollmentTests.RegenerateBackupCodes"`
Expected: 404 — endpoint does not exist.

- [ ] **Step 3: Implement `RegenerateBackupCodes`**

Append to `MfaController.cs`:

```csharp
[HttpPost("backup-codes/regenerate")]
public async Task<IActionResult> RegenerateBackupCodes(
    [FromServices] MfaBackupCodeService backupCodes)
{
    var user = await GetCurrentUserAsync();
    if (user is null) return Unauthorized();

    if (!user.TwoFactorEnabled)
    {
        return BadRequest(new { error = "mfa_not_enabled" });
    }

    var codes = await backupCodes.RegenerateAsync(user.Id, HttpContext.RequestAborted);

    ApplyNoStoreHeaders();
    return Ok(new { backupCodes = codes });
}
```

- [ ] **Step 4: Run the test; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~MfaEnrollmentTests.RegenerateBackupCodes"`
Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres/Controllers/Api/MfaController.cs ProjectCeres.Tests/Integration/Authentication/Mfa/MfaEnrollmentTests.cs
git -C <repo> commit -m "feat(auth): POST /api/auth/mfa/backup-codes/regenerate"
```

---

## Task 14: Login flow — branch on `RequiresTwoFactor` (TDD)

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithTotpTests.cs`

- [ ] **Step 1: Write the failing test for the MFA-required branch**

Path: `ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithTotpTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class LoginWithTotpTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LoginWithTotpTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@mfa-login-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Login_with_mfa_enabled_returns_200_requiresTotp_and_no_session_cookie()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "m@mfa-login-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "m@mfa-login-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("requiresTotp").GetBoolean().Should().BeTrue();

        var setCookies = resp.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
        setCookies.Should().NotContain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));
        setCookies.Should().Contain(c => c.StartsWith("Identity.TwoFactorUserId="));
    }

    private static string? ExtractSetCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var first = v.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == cookieName) return first[(eq + 1)..];
        }
        return null;
    }
}
```

- [ ] **Step 2: Run; expect FAIL**

Run: `dotnet test --filter "FullyQualifiedName~LoginWithTotpTests.Login_with_mfa_enabled"`
Expected: tests fail — login currently issues a session even when MFA is enabled.

- [ ] **Step 3: Modify `AuthController.Login` to branch on `RequiresTwoFactor`**

Open `ProjectCeres/Controllers/Api/AuthController.cs`. Find the `Login` method. Replace the body so that:

- After `PasswordSignInAsync`, check the `SignInResult` for `RequiresTwoFactor`. If set, return `200 OK { requiresTotp: true }` and **do not** insert a `UserSession` row, do not set the persistent cookie. Identity has already set `Identity.TwoFactorUserId` on the response — that's the half-auth state.
- The existing 6a code path (insert `UserSession`, optional persistent cookie, rotate CSRF, return 204) only runs when `result.Succeeded` is true.

Concretely, replace the body of `Login` with the following. Keep existing dependencies (`SignInManager`, `UserManager`, `AppDbContext`, `Argon2idPasswordHasher`, `PersistentTokenService`, `IAntiforgery`):

```csharp
[HttpPost("login"), AllowAnonymous]
public async Task<IActionResult> Login([FromBody] ProjectCeres.ViewModels.Auth.LoginRequest request)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);

    var user = await _userManager.FindByEmailAsync(request.Email);
    if (user is null)
    {
        // Constant-time enumeration prevention.
        _argon.RunDummyHash();
        return Unauthorized();
    }

    var result = await _signInManager.PasswordSignInAsync(
        user, request.Password, isPersistent: request.RememberMe, lockoutOnFailure: true);

    if (result.RequiresTwoFactor)
    {
        // Identity has already set the Identity.TwoFactorUserId scoped cookie.
        // We do NOT insert a UserSession row — that happens in /login/totp.
        // We DO rotate the CSRF cookie so the SPA's next call has a matching pair.
        _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requiresTotp = true });
    }

    if (result.IsLockedOut)
    {
        return Unauthorized(new { error = "locked_out" });
    }

    if (!result.Succeeded)
    {
        return Unauthorized();
    }

    // Existing 6a session-issue path
    var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    var ua = Request.Headers.UserAgent.ToString();

    string? persistentToken = null;
    if (request.RememberMe)
    {
        persistentToken = _tokens.Generate();
    }

    var session = new UserSession
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        IpCreatedAt = ip,
        UserAgent = ua,
        CreatedAt = DateTime.UtcNow,
        LastUsedAt = DateTime.UtcNow,
        IsPersistent = request.RememberMe,
        PersistentTokenHash = persistentToken is null ? null : _tokens.Hash(persistentToken),
    };
    _db.UserSessions.Add(session);
    await _db.SaveChangesAsync();

    if (persistentToken is not null)
    {
        Response.Cookies.Append(SessionConstants.PersistentCookieName, persistentToken, new Microsoft.AspNetCore.Http.CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });
    }

    _antiforgery.GetAndStoreTokens(HttpContext);
    return NoContent();
}
```

If your existing 6a `Login` method was structured slightly differently, preserve its existing body for the `Succeeded`-true path; only the early-return branches above need to be added.

- [ ] **Step 4: Run the test; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~LoginWithTotpTests.Login_with_mfa_enabled"`
Expected: 1 passed.

- [ ] **Step 5: Run the full Stage 6a + 6b.1 login suite to confirm no regression**

Run: `dotnet test --filter "FullyQualifiedName~LoginEndpointTests|FullyQualifiedName~LoginWithTotpTests"`
Expected: all 6a login tests still green plus the new MFA test.

- [ ] **Step 6: Commit**

```bash
git -C <repo> add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithTotpTests.cs
git -C <repo> commit -m "feat(auth): POST /api/auth/login branches on RequiresTwoFactor (200 + requiresTotp)"
```

---

## Task 15: `AuthController.LoginTotp` — TOTP path

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithTotpTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `LoginWithTotpTests.cs`:

```csharp
[Fact]
public async Task LoginTotp_with_valid_totp_issues_session_cookie_and_inserts_user_session()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, "t@mfa-login-test.local");
    var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Step A — credentials login → ticket cookie
    var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
    var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new
        {
            email = "t@mfa-login-test.local",
            password = AuthTestFixture.ValidPassword,
            rememberMe = false
        }),
    };
    loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
    loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header);
    var loginResp = await client.SendAsync(loginReq);
    var twoFactorCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
    twoFactorCookie.Should().NotBeNullOrEmpty();

    // Step B — submit TOTP code
    var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
    var (totpCsrf, totpHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
    {
        Content = JsonContent.Create(new { code }),
    };
    totpReq.Headers.Add("Cookie",
        $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={totpCsrf}");
    totpReq.Headers.Add(SessionConstants.CsrfHeaderName, totpHeader);
    var resp = await client.SendAsync(totpReq);

    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    resp.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var session = await db.UserSessions.FirstAsync(s => s.UserId == user.Id);
    session.RevokedAt.Should().BeNull();
}

[Fact]
public async Task LoginTotp_with_replayed_code_returns_401()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, "p@mfa-login-test.local");
    var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // First login completes successfully
    var twoFactorCookie1 = await DoCredentialsLoginAndGetTwoFactorCookie(_factory, client, "p@mfa-login-test.local");
    var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
    await SubmitTotpAsync(_factory, client, twoFactorCookie1!, user.Id, code);

    // Second login: same code → reject
    var twoFactorCookie2 = await DoCredentialsLoginAndGetTwoFactorCookie(_factory, client, "p@mfa-login-test.local");
    var (totpCsrf, totpHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
    var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
    {
        Content = JsonContent.Create(new { code }),
    };
    totpReq.Headers.Add("Cookie",
        $"Identity.TwoFactorUserId={twoFactorCookie2}; {SessionConstants.CsrfCookieName}={totpCsrf}");
    totpReq.Headers.Add(SessionConstants.CsrfHeaderName, totpHeader);
    var resp = await client.SendAsync(totpReq);

    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}

private static async Task<string?> DoCredentialsLoginAndGetTwoFactorCookie(
    AuthTestWebApplicationFactory factory, HttpClient client, string email)
{
    var (csrf, header) = AuthTestFixture.MintCsrf(factory);
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new
        {
            email,
            password = AuthTestFixture.ValidPassword,
            rememberMe = false
        }),
    };
    req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
    req.Headers.Add(SessionConstants.CsrfHeaderName, header);
    var resp = await client.SendAsync(req);
    return ExtractSetCookie(resp, "Identity.TwoFactorUserId");
}

private static async Task<HttpResponseMessage> SubmitTotpAsync(
    AuthTestWebApplicationFactory factory, HttpClient client, string twoFactorCookie, Guid userId, string code)
{
    var (totpCsrf, totpHeader) = AuthTestFixture.MintCsrf(factory, userId);
    var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
    {
        Content = JsonContent.Create(new { code }),
    };
    totpReq.Headers.Add("Cookie",
        $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={totpCsrf}");
    totpReq.Headers.Add(SessionConstants.CsrfHeaderName, totpHeader);
    return await client.SendAsync(totpReq);
}
```

- [ ] **Step 2: Run; expect FAIL**

Run: `dotnet test --filter "FullyQualifiedName~LoginWithTotpTests.LoginTotp"`
Expected: 404 — `/api/auth/login/totp` does not exist.

- [ ] **Step 3: Implement `LoginTotp` with TOTP path**

Append to `AuthController.cs`. Inject `TotpReplayGuard` + `MfaBackupCodeService` via constructor (add the parameters; keep the existing fields — same pattern as 6a):

```csharp
[HttpPost("login/totp"), AllowAnonymous]
public async Task<IActionResult> LoginTotp(
    [FromBody] ProjectCeres.ViewModels.Auth.LoginTotpRequest request,
    [FromServices] TotpReplayGuard replayGuard,
    [FromServices] MfaBackupCodeService backupCodes)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);

    // Identity reads Identity.TwoFactorUserId from request cookies
    // and resolves the half-authenticated user.
    var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
    if (user is null) return Unauthorized();

    if (MfaConstants.TotpCodeShape.IsMatch(request.Code))
    {
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
            request.Code, isPersistent: false, rememberClient: false);
        if (result.IsLockedOut) return Unauthorized(new { error = "locked_out" });
        if (!result.Succeeded) return Unauthorized();

        var accepted = await replayGuard.TryAcceptAsync(user.Id, request.Code, HttpContext.RequestAborted);
        if (!accepted)
        {
            // Replayed code; force sign-out (the framework just signed them in) and return 401.
            await _signInManager.SignOutAsync();
            return Unauthorized(new { error = "replay" });
        }

        await IssueSessionRowAsync(user);
        _antiforgery.GetAndStoreTokens(HttpContext);
        return NoContent();
    }

    var stripped = request.Code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
    if (MfaConstants.BackupCodeShape.IsMatch(stripped))
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ok = await backupCodes.VerifyAndConsumeAsync(user.Id, request.Code, ip, HttpContext.RequestAborted);
        if (!ok) return Unauthorized();

        await _signInManager.SignInAsync(user, isPersistent: false);
        await IssueSessionRowAsync(user);
        _antiforgery.GetAndStoreTokens(HttpContext);
        return NoContent();
    }

    return Unauthorized();
}

private async Task IssueSessionRowAsync(ApplicationUser user)
{
    var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    var ua = Request.Headers.UserAgent.ToString();
    _db.UserSessions.Add(new UserSession
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        IpCreatedAt = ip,
        UserAgent = ua,
        CreatedAt = DateTime.UtcNow,
        LastUsedAt = DateTime.UtcNow,
        IsPersistent = false,
    });
    await _db.SaveChangesAsync();
}
```

- [ ] **Step 4: Run the tests; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~LoginWithTotpTests.LoginTotp"`
Expected: 2 passed (valid TOTP + replay rejection).

- [ ] **Step 5: Commit**

```bash
git -C <repo> add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithTotpTests.cs
git -C <repo> commit -m "feat(auth): POST /api/auth/login/totp (TOTP path with replay-guard)"
```

---

## Task 16: `LoginTotp` — backup-code path test

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithTotpTests.cs`

The implementation already supports backup codes from Task 15. Add a dedicated test.

- [ ] **Step 1: Add the test**

Append to `LoginWithTotpTests.cs`:

```csharp
[Fact]
public async Task LoginTotp_with_valid_backup_code_issues_session_and_marks_used()
{
    var user = await AuthTestFixture.RegisterUserAsync(_factory, "b@mfa-login-test.local");
    await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Generate backup codes by calling enroll/verify
    using (var scope = _factory.Services.CreateScope())
    {
        var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
        var codes = await svc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        var firstCode = codes.First();

        var twoFactorCookie = await DoCredentialsLoginAndGetTwoFactorCookie(_factory, client, "b@mfa-login-test.local");
        var resp = await SubmitTotpAsync(_factory, client, twoFactorCookie!, user.Id, firstCode);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var used = await db.UserMfaBackupCodes
            .Where(c => c.UserId == user.Id && c.UsedAt != null)
            .CountAsync();
        used.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~LoginWithTotpTests.LoginTotp_with_valid_backup_code"`
Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithTotpTests.cs
git -C <repo> commit -m "test(auth): /login/totp accepts backup code + marks UsedAt"
```

---

## Task 17: Login-without-MFA regression test (ADR-0069 confirmation)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithoutMfaTests.cs`

- [ ] **Step 1: Write the test**

Path: `ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithoutMfaTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class LoginWithoutMfaTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LoginWithoutMfaTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@no-mfa-login-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Login_without_mfa_returns_204_and_session_cookie_regardless_of_account_age()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "n@no-mfa-login-test.local");

        // Backdate CreatedAt to 30 days ago to confirm there's no grace cliff.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Users.FirstAsync(u => u.Id == user.Id);
            stored.CreatedAt = DateTime.UtcNow.AddDays(-30);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "n@no-mfa-login-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        resp.Headers.GetValues("Set-Cookie").Should()
            .Contain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));
    }
}
```

- [ ] **Step 2: Run; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~LoginWithoutMfaTests"`
Expected: 1 passed. Confirms ADR-0069 opt-in: no grace cliff.

- [ ] **Step 3: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/Mfa/LoginWithoutMfaTests.cs
git -C <repo> commit -m "test(auth): login without MFA returns 204 + session cookie regardless of account age (ADR-0069)"
```

---

## Task 18: Cache-control + scoped-cookie tests

**Files:**
- Create: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaCacheControlTests.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/Mfa/LoginScopedCookieTests.cs`

- [ ] **Step 1: Cache-control across all three MFA endpoints**

Path: `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaCacheControlTests.cs`

```csharp
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class MfaCacheControlTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MfaCacheControlTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@cache-mfa-test.local")).ToList())
        {
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Enroll_verify_and_regenerate_all_set_no_store()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "h@cache-mfa-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Login (TOTP path) and capture session cookie
        var twoFactorCookie = await DoCredentialsLoginAndGetTwoFactorCookie(client, "h@cache-mfa-test.local");
        var totpCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var sessionCookie = await SubmitTotpAndGetSessionCookieAsync(client, twoFactorCookie!, user.Id, totpCode);

        // /enroll
        var enroll = await PostMfaAsync(client, sessionCookie!, user.Id, "/api/auth/mfa/enroll", body: null);
        enroll.Headers.CacheControl!.NoStore.Should().BeTrue();

        // /enroll/verify (force a fresh enrollment + verify with current code)
        var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
        var enrollResp = await client.SendAsync(enrollReq);
        var enrollBody = await enrollResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var newSeed = ExtractSecretFromUri(enrollBody.GetProperty("otpAuthUri").GetString()!);
        var newCode = AuthTestFixture.ComputeCurrentTotpCode(newSeed);

        var verify = await PostMfaAsync(client, sessionCookie!, user.Id, "/api/auth/mfa/enroll/verify",
            new { code = newCode });
        verify.Headers.CacheControl!.NoStore.Should().BeTrue();

        // /backup-codes/regenerate
        var regen = await PostMfaAsync(client, sessionCookie!, user.Id, "/api/auth/mfa/backup-codes/regenerate", body: null);
        regen.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private async Task<HttpResponseMessage> PostMfaAsync(
        HttpClient client, string sessionCookie, Guid userId, string path, object? body)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, userId);
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return await client.SendAsync(req);
    }

    private async Task<string?> DoCredentialsLoginAndGetTwoFactorCookie(HttpClient client, string email)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email,
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        return ExtractSetCookie(resp, "Identity.TwoFactorUserId");
    }

    private async Task<string?> SubmitTotpAndGetSessionCookieAsync(
        HttpClient client, string twoFactorCookie, Guid userId, string code)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, userId);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code }),
        };
        req.Headers.Add("Cookie",
            $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        return ExtractSetCookie(resp, SessionConstants.SessionCookieName);
    }

    private static string? ExtractSetCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var first = v.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == cookieName) return first[(eq + 1)..];
        }
        return null;
    }

    private static string ExtractSecretFromUri(string otpAuthUri)
    {
        var uri = new Uri(otpAuthUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["secret"]!;
    }
}
```

- [ ] **Step 2: Scoped-cookie isolation**

Path: `ProjectCeres.Tests/Integration/Authentication/Mfa/LoginScopedCookieTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class LoginScopedCookieTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LoginScopedCookieTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@scoped-cookie-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Identity_TwoFactorUserId_cookie_does_not_grant_access_to_authenticated_endpoints()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "s@scoped-cookie-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step A — credentials login → only the scoped cookie is set
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "s@scoped-cookie-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var loginResp = await client.SendAsync(loginReq);
        var twoFactorCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
        twoFactorCookie.Should().NotBeNullOrEmpty();

        // Step B — try to access an authenticated endpoint with ONLY the scoped cookie.
        var probe = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        probe.Headers.Add("Cookie", $"Identity.TwoFactorUserId={twoFactorCookie}");
        var resp = await client.SendAsync(probe);

        // The scoped cookie is NOT a session — global fallback policy returns 401.
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string? ExtractSetCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var first = v.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == cookieName) return first[(eq + 1)..];
        }
        return null;
    }
}
```

- [ ] **Step 3: Run; expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~MfaCacheControlTests|FullyQualifiedName~LoginScopedCookieTests"`
Expected: 2 passed.

- [ ] **Step 4: Commit**

```bash
git -C <repo> add ProjectCeres.Tests/Integration/Authentication/Mfa/MfaCacheControlTests.cs ProjectCeres.Tests/Integration/Authentication/Mfa/LoginScopedCookieTests.cs
git -C <repo> commit -m "test(auth): MFA cache-control + scoped-cookie isolation"
```

---

## Task 19: Mark Stage 6b.1 verification items in `roadmap-phase-three.md`

**Files:**
- Modify: `docs/roadmap-phase-three.md`

- [ ] **Step 1: Update the TOTP verification block**

Open `docs/roadmap-phase-three.md`. In the Stage 6 verification checklist's TOTP section, change the following items from `[ ]` to `[x]` (these are the 6b.1-shipped items):

- TOTP seed generated with cryptographically secure RNG
- TOTP enrollment is opt-in per [ADR-0069](decisions/ADR-0069-mfa-opt-in-for-personal-users.md). Users enable from Settings → Security; once enabled, MFA is enforced on every subsequent login. Login does not block on enrollment, no grace period, no enforcement deadline. Onboarding presents MFA as recommended-but-skippable.
- Replay-prevention table is **persistent** (database or Redis), not in-memory — survives application restart
- Replay records auto-purged after 2 minutes
- Backup codes hashed with Argon2id (not plaintext) — single-use, regeneration invalidates all previous codes
- No SMS option exposed (SIM-swap vulnerability)

Leave these items `[ ]` (deferred to 6b.2 or 6c):

- TOTP seed stored encrypted at rest via ASP.NET Core Data Protection (`IDataProtector`) — production hardening deferred to Stage 16; the wiring is correct in 6b.1
- Backup-code use during lockout is honoured — depends on lockout, ships in 6b.2

- [ ] **Step 2: Commit**

```bash
git -C <repo> add docs/roadmap-phase-three.md
git -C <repo> commit -m "docs(roadmap): mark Stage 6b.1 TOTP verification items shipped"
```

---

## Task 20: Final full-suite verification

**Files:**
- None (verification only)

- [ ] **Step 1: Run the full Authentication suite**

Run: `dotnet test --filter "FullyQualifiedName~Authentication"`
Expected: every test in `ProjectCeres.Tests/Integration/Authentication/` passes — Stage 6a's 24 tests + 6b.1's ~15 new tests.

- [ ] **Step 2: Run the entire test suite**

Run: `dotnet test`
Expected: every test passes. The `TestWebApplicationFactory` (auto-auth) bypass means pre-Stage-6a CRUD tests are unaffected by MFA. The `AuthTestWebApplicationFactory` (real pipeline) handles all 6a + 6b.1 tests.

- [ ] **Step 3: Release build**

Run: `dotnet build -c Release`
Expected: zero warnings, zero errors.

- [ ] **Step 4: Verify migrations applied to test DB**

Run:
```bash
dotnet ef migrations list --project ProjectCeres --connection "Host=localhost;Database=project_ceres_test;Username=postgres;Password=postgres"
```
Expected: includes `AddCreatedAtToApplicationUser` and `AddMfaBackupCodesAndReplayPrevention` marked applied.

- [ ] **Step 5: No final commit needed unless changes were required**

If steps 1–4 all passed, Stage 6b.1 is complete. If any test required adjustment, commit with a descriptive message before declaring done.

---

## Self-Review

Confirmed during plan authoring:

- **Spec coverage:** Tasks 0–20 cover spec § 2 (`ApplicationUser.CreatedAt`), § 3 (entities + migration), § 4 (services — backup-code + replay-guard; the `MfaTicketService` from the original draft was deleted per § Sub-decision 2), § 5 (login flow — `RequiresTwoFactor` branching + TOTP path + backup-code path), § 6 (three MFA endpoints), § 8 (no new appsettings keys). § 7 (Data Protection key storage hardening) is explicitly deferred to Stage 16 per the spec.
- **Type consistency:** `MfaConstants.BackupCodeBatchSize`, `BackupCodeLength`, `BackupCodeShape`, `TotpCodeShape`, `ReplayWindow` defined once (Task 4) and referenced consistently across Tasks 5/6/7/8/15. `MfaBackupCodeService.GenerateAndPersistAsync`/`VerifyAndConsumeAsync`/`RegenerateAsync` defined in Tasks 5/6/7 and used identically in Tasks 12/13/15/16. `TotpReplayGuard.TryAcceptAsync` defined in Task 8 and called identically in Task 15. `AuthTestFixture.EnrollUserMfaAsync` and `ComputeCurrentTotpCode` defined in Task 9 and used in Tasks 12/13/15/16/17/18.
- **Placeholder scan:** No "TBD", "TODO", or "implement later" in any task. Each test step has the full code; each implementation step has the full code; each command has expected output.
- **Spelling:** American English throughout (`enroll`, `enrollment`) per the spec's locked decision.
- **Migration safety:** Task 1 + Task 3 both apply migrations to dev AND test DBs explicitly. The existing `AppDbContextModelSnapshot` will be regenerated automatically by `dotnet ef migrations add`.
- **Architecture-test compatibility:** `MfaController` (Task 11) carries class-level `[Authorize]`, no class-level `[AllowAnonymous]` → passes the existing Stage 6a `No_api_controller_class_has_AllowAnonymous` test. All actions are `POST` → passes the `Api_HttpGet_actions_must_not_have_write_verb_names` test (no GET actions to begin with).

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-09-stage-6b-1-totp-mfa-plan.md`. Proceeding with execution per auto-mode directive.
