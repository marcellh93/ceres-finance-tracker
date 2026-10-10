# Stage 13.b — Inactive-user Anonymisation Sweep Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically warn (12 months) then anonymise (18 months) accounts that have gone inactive, as a standing GDPR storage-limitation duty, reusing the existing statutory-scrub lanes.

**Architecture:** Add a last-login timestamp to the user record, stamped at the single login convergence point. Extract the four data-scrub lanes out of `ErasureExecutor` into a shared `UserAnonymiser` service; the erasure flow and a new inactivity flow each call it with their own identity-tail. A new flat cross-tenant tool (`InactivitySweep`) finds inactive users and drives both the warning email and the anonymisation.

**Tech Stack:** .NET 10, EF Core + Npgsql, ASP.NET Core Identity, xUnit + Moq + FluentAssertions. Cross-tenant work via `AdminDbContext` (BYPASSRLS). Scrutor assembly-scanning DI.

**Spec:** `docs/superpowers/specs/2026-10-10-stage-13b-inactive-user-anonymisation-design.md`

## Global Constraints

- **Clock:** never `DateTime.UtcNow` in production paths — inject `TimeProvider` (CER004). EF property initialisers that read the clock need `[AllowsWallClock("...")]` with a justification string.
- **Cross-tenant reads:** go through `AdminDbContext` + `[RequiresAdminContext]`; every `IgnoreQueryFilters()` call site must be added to the allow-list `HashSet` in `ArchitectureTests.IgnoreQueryFilters_only_appears_in_documented_exception_paths` (~line 784) with a trailing justification comment, or the architecture test fails.
- **DI:** register services via the Scrutor scan convention (interface + impl in a scanned namespace: `ProjectCeres.Services`, `ProjectCeres.Common.*`); no manual `Program.cs` `AddScoped` line. The DI-completeness test must stay green.
- **New `AuditLogAction` values** must be added to the `expected` array in `ArchitectureTests.AuditLogAction_enum_values_match_documented_set` (~line 617).
- **New `EmailTemplateKey`** needs the 3-key contract (`Subject`/`BodyText`/`BodyHtml`) in BOTH `EmailsResource.en.resx` and `EmailsResource.es.resx`.
- **Deletion rules:** statutory financial records are anonymise-retained, never hard-deleted (already honoured by the reused lanes).
- **No new IUserOwned entity** — the five-registry chain (DbSet/OnModelCreating/RLS migration) does NOT fire. Two columns on the non-IUserOwned `ApplicationUser`.
- **Commits:** straight to `main`, no branches, no `Co-Authored-By` trailer.
- **Tests:** every DB-read assertion filters by a unique-to-this-test marker (shared sequential test DB). Use a throwaway `@…-test.invalid` user, never the `@example.*` sentinel.

---

## Design note — the identity-tail split (read before Task 3)

`ErasureExecutor.AnonymiseAccountAsync` does two things the inactivity flow treats differently:
- Sets `ErasedAt` + scrubs the Identity PII columns → **both flows want this** (an anonymised inactive account must also be refused login, same seal check).
- `RecordEmailHoldAsync` creates a 30-day `ErasedEmailHold` → **erasure only** (Decision 6: inactivity frees the email, no hold).

Therefore the extraction splits as:
- **Shared (`UserAnonymiser`):** the four data lanes — `AnonymiseStatutoryAsync`, `RedactSupportAsync`, `PurgeAsync`, `DeleteExportJobAsync` — plus the Identity-column scrub (`AnonymiseAccountAsync`, which sets `ErasedAt`). These are identical for both flows.
- **Per-caller tail:** erasure adds `RecordEmailHoldAsync` + `GdprErasureCompleted` audit + request-status flip; inactivity adds the `InactivityAnonymised` audit row and NO hold.

The `GdprErasureCompleted` write stays exclusively in the erasure caller.

---

## Task 1: Add the login-timestamp columns + migration

**Files:**
- Modify: `ProjectCeres/Models/ApplicationUser.cs`
- Create: `ProjectCeres/Migrations/<timestamp>_AddInactivityTrackingToApplicationUser.cs` (via `dotnet ef`)
- Test: `ProjectCeres.Tests/Integration/Gdpr/InactivityColumnsTests.cs`

**Interfaces:**
- Produces: `ApplicationUser.LastLoginAt` (`DateTime`, non-null), `ApplicationUser.InactivityWarningSentAt` (`DateTime?`).

- [ ] **Step 1: Write the failing test**

```csharp
// ProjectCeres.Tests/Integration/Gdpr/InactivityColumnsTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;

public class InactivityColumnsTests(Bucket1Factory factory) : IClassFixture<Bucket1Factory>
{
    [Fact]
    public async Task ApplicationUser_exposes_LastLoginAt_and_InactivityWarningSentAt()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var marker = $"col-test-{Guid.NewGuid():N}@col-test.invalid";
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = marker, UserName = marker,
            LastLoginAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var read = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(u => u.Email == marker);
        read.LastLoginAt.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        read.InactivityWarningSentAt.Should().BeNull();
    }
}
```

(Match the bucket-fixture pattern of a sibling test in `ProjectCeres.Tests/Integration/Gdpr/`; if the folder uses a different factory name, copy it exactly from an existing file there.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter InactivityColumnsTests`
Expected: FAIL — compile error, `ApplicationUser` has no `LastLoginAt`.

- [ ] **Step 3: Add the columns**

```csharp
// ProjectCeres/Models/ApplicationUser.cs — add alongside CreatedAt/SealedAt/ErasedAt
/// <summary>Last successful login (password, TOTP, or backup code). Measures inactivity for Stage 13.b.</summary>
public DateTime LastLoginAt { get; set; }

/// <summary>Set when the 12-month inactivity warning is sent; cleared to null on every login.</summary>
public DateTime? InactivityWarningSentAt { get; set; }
```

No C# default on `LastLoginAt` (the migration's DB default seeds existing rows; the login path and registration set it going forward), so no `[AllowsWallClock]` is needed here.

- [ ] **Step 4: Generate the migration with a backfill default**

Run: `dotnet ef migrations add AddInactivityTrackingToApplicationUser --project ProjectCeres`

Then edit the generated migration so `LastLoginAt` seeds existing rows to "now" (mirrors `20260509125640_AddCreatedAtToApplicationUser`):

```csharp
migrationBuilder.AddColumn<DateTime>(
    name: "LastLoginAt",
    table: "AspNetUsers",
    type: "timestamp with time zone",
    nullable: false,
    defaultValueSql: "current_timestamp");

migrationBuilder.AddColumn<DateTime>(
    name: "InactivityWarningSentAt",
    table: "AspNetUsers",
    type: "timestamp with time zone",
    nullable: true);
```

- [ ] **Step 5: Apply + run test to verify it passes**

Run: `dotnet ef database update --project ProjectCeres && dotnet test ProjectCeres.Tests --filter InactivityColumnsTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Models/ApplicationUser.cs ProjectCeres/Migrations/ ProjectCeres.Tests/Integration/Gdpr/InactivityColumnsTests.cs
git commit -m "feat(13b): add LastLoginAt + InactivityWarningSentAt to ApplicationUser"
```

---

## Task 2: Stamp LastLoginAt at the login convergence point

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs` — `IssueSessionAndCookiesAsync` (~line 513); registration path (the `Registered` audit site)
- Test: `ProjectCeres.Tests/Integration/Authentication/LoginStampsLastLoginTests.cs`

**Interfaces:**
- Consumes: `ApplicationUser.LastLoginAt`, `InactivityWarningSentAt` (Task 1).

**Why here:** all three login-success sites (`Login` ~376, `LoginTotp` ~469, backup-code ~506) route through `IssueSessionAndCookiesAsync`, which already holds `_timeProvider`, `_db`, and `user`. One stamp covers every path.

- [ ] **Step 1: Write the failing test**

```csharp
// Logs in a fresh user whose LastLoginAt is backdated + InactivityWarningSentAt set,
// then asserts login stamped now and cleared the warning. Use the project's auth
// integration-test helper (copy the login-flow setup from an existing AuthController
// test in ProjectCeres.Tests/Integration/Authentication/). Assert against the user's
// own unique email marker.
[Fact]
public async Task Successful_login_stamps_LastLoginAt_and_clears_warning()
{
    // arrange: register + confirm a user via the existing helper; backdate the row:
    //   LastLoginAt = utcNow - 400 days, InactivityWarningSentAt = utcNow - 100 days
    // act: perform a password login through the real endpoint
    // assert (AsNoTracking, filtered by the test's unique email):
    read.LastLoginAt.Should().BeCloseTo(fakeClock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(1));
    read.InactivityWarningSentAt.Should().BeNull();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter LoginStampsLastLoginTests`
Expected: FAIL — `LastLoginAt` unchanged (still backdated).

- [ ] **Step 3: Stamp inside `IssueSessionAndCookiesAsync`**

Add, near the top of the method (reuse the `now` already computed at ~line 541, or compute once):

```csharp
// Stage 13.b: every successful login (password/TOTP/backup-code) converges here.
// Reset the inactivity clock and clear any pending warning so a returning user is
// never swept. ExecuteUpdate keeps this a single UPDATE, independent of the tracked
// session insert below.
await _db.Users
    .Where(u => u.Id == user.Id)
    .ExecuteUpdateAsync(s => s
        .SetProperty(u => u.LastLoginAt, now)
        .SetProperty(u => u.InactivityWarningSentAt, (DateTime?)null));
```

Then in the registration path, set `LastLoginAt = now` on the new `ApplicationUser` before save (a brand-new account must not read as instantly inactive).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ProjectCeres.Tests --filter LoginStampsLastLoginTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/LoginStampsLastLoginTests.cs
git commit -m "feat(13b): stamp LastLoginAt + clear warning on every login"
```

---

## Task 3: Extract the shared `UserAnonymiser` service

**Files:**
- Create: `ProjectCeres/Services/UserAnonymiser.cs`
- Modify: `ProjectCeres/Services/ErasureExecutor.cs` (delegate the lanes to `UserAnonymiser`; keep its own tail)
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — allow-list entry for `UserAnonymiser.cs`
- Test: `ProjectCeres.Tests/Integration/Gdpr/ErasureExecutorTests.cs` (existing — must stay green = regression guard)

**Interfaces:**
- Produces: `UserAnonymiser.AnonymiseAsync(Guid userId, string token, CancellationToken ct)` — runs the four data lanes + the Identity-column scrub (sets `ErasedAt`), all within the caller's ambient transaction. Does NOT write any audit row, does NOT create an `ErasedEmailHold`, does NOT flip any request status.
- Consumes (by `ErasureExecutor`): the above.

- [ ] **Step 1: Confirm the regression baseline is green**

Run: `dotnet test ProjectCeres.Tests --filter ErasureExecutorTests`
Expected: PASS (before any change). This is the net the refactor must not break.

- [ ] **Step 2: Create `UserAnonymiser` by moving the lane methods verbatim**

```csharp
// ProjectCeres/Services/UserAnonymiser.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;

namespace ProjectCeres.Services;

/// <summary>
/// Shared user-data anonymisation: the four statutory/support/purge/export-ZIP lanes
/// plus the Identity-column scrub (sets ErasedAt). Erasure (Stage 13.9) and the
/// inactivity sweep (Stage 13.b) both call this, each adding its own audit/hold tail.
/// Runs inside the CALLER's ambient transaction — opens none of its own, writes no
/// audit row. Cross-tenant by design (AdminDbContext, BYPASSRLS).
/// </summary>
[RequiresAdminContext]
public class UserAnonymiser(
    AdminDbContext db,
    IWebHostEnvironment env,
    TimeProvider timeProvider,
    IOptions<FileAttachmentOptions>? options = null)
{
    private readonly string _root = options?.Value.RootPath ?? env.ContentRootPath;

    /// <summary>Runs all five anonymise steps for one user. Idempotent: deterministic
    /// token reproduces the same values; File.Exists guards each unlink.</summary>
    public async Task AnonymiseAsync(Guid userId, string token, CancellationToken ct)
    {
        var lanes = ErasureLanes.Classify(db.Model);
        await AnonymiseStatutoryAsync(userId, token, ct);
        await RedactSupportAsync(userId, ct);
        await PurgeAsync(userId, lanes.Purge, ct);
        await DeleteExportJobAsync(userId, ct);
        await AnonymiseAccountAsync(userId, token, ct);
    }

    // MOVE these five private methods VERBATIM from ErasureExecutor (lines 144-170,
    // 178-230, 234-247, 255-273, 324-339): AnonymiseStatutoryAsync, RedactSupportAsync,
    // PurgeAsync, DeleteExportJobAsync, AnonymiseAccountAsync. They already take only
    // (userId[, lanes], ct) and read nothing erasure-specific.
}
```

- [ ] **Step 3: Rewire `ErasureExecutor` to call `UserAnonymiser`**

Inject `UserAnonymiser` into `ErasureExecutor`'s primary constructor. Replace the five lane calls in `ExecuteAsync` (lines 72-77) with:

```csharp
await RecordEmailHoldAsync(userId, ct); // reads the REAL email — must run BEFORE anonymiser scrubs identity
await anonymiser.AnonymiseAsync(userId, token, ct);
```

Note the ordering: `RecordEmailHoldAsync` reads the real email before `AnonymiseAccountAsync` overwrites it (same invariant as the original, which ran `RecordEmailHoldAsync` before `AnonymiseAccountAsync`). Delete the five moved private methods from `ErasureExecutor`; keep `RecordEmailHoldAsync`, the transaction/IP-rewrite/status-flip/`GdprErasureCompleted` tail unchanged. `ErasureExecutor` keeps `pseudonym`, `normalizer`, `lookupHasher` (still used by the tail + `RecordEmailHoldAsync`); it may drop `env`/`options` if no longer referenced — verify by compile.

- [ ] **Step 4: Add the allow-list entry**

In `ArchitectureTests.cs` allow-list `HashSet` (~line 784):

```csharp
"ProjectCeres/Services/UserAnonymiser.cs",  // Stage 13.b: shared anonymise lanes extracted from ErasureExecutor; cross-tenant by design (acts for one target user with no HTTP principal) — SweepSessions/ErasureExecutor pattern.
```

(The `ErasureExecutor.cs` entry stays — its tail still calls `IgnoreQueryFilters` for the IP rewrite / status flip / email-hold.)

- [ ] **Step 5: Run the regression + build**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.Tests --filter "ErasureExecutorTests|IgnoreQueryFilters"`
Expected: PASS — erasure behaviour unchanged, allow-list test green.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/UserAnonymiser.cs ProjectCeres/Services/ErasureExecutor.cs ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git commit -m "refactor(13b): extract shared UserAnonymiser from ErasureExecutor"
```

---

## Task 4: Add the two AuditLogActions + the warning EmailTemplateKey + resx

**Files:**
- Modify: `ProjectCeres/Models/AuditLog.cs` (enum)
- Modify: `ProjectCeres/Common/Email/EmailTemplateKey.cs`
- Modify: `ProjectCeres/Common/Email/EmailComposer.cs` (`KeysFor` switch)
- Modify: `ProjectCeres/Resources/EmailsResource.en.resx`, `EmailsResource.es.resx`
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` (`expected` array ~line 617)
- Test: `ProjectCeres.Tests/.../EmailComposerTests.cs` (existing pattern)

**Interfaces:**
- Produces: `AuditLogAction.InactivityWarningSent`, `AuditLogAction.InactivityAnonymised`; `EmailTemplateKey.InactivityWarning`.

- [ ] **Step 1: Write the failing composer test**

```csharp
[Theory]
[InlineData("en-US")]
[InlineData("es-ES")]
public void Composes_inactivity_warning_in_both_cultures(string culture)
{
    var msg = _composer.Compose(EmailTemplateKey.InactivityWarning,
        CultureInfo.GetCultureInfo(culture), "18"); // arg: months-until-anonymise
    msg.Subject.Should().NotBeNullOrWhiteSpace();
    msg.BodyText.Should().NotBeNullOrWhiteSpace();
    msg.BodyHtml.Should().NotBeNullOrWhiteSpace();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter EmailComposerTests`
Expected: FAIL — `EmailTemplateKey.InactivityWarning` undefined.

- [ ] **Step 3: Add the enum values + resx keys**

```csharp
// AuditLog.cs — append (follow the `// wired in Stage N` comment convention)
InactivityWarningSent,   // wired in Stage 13.b — 12-month inactivity warning email sent
InactivityAnonymised,    // wired in Stage 13.b — 18-month inactivity anonymisation executed
```

```csharp
// EmailTemplateKey.cs — append
InactivityWarning,       // wired in Stage 13.b
```

Add to `EmailComposer.KeysFor` switch the `InactivityWarning => (...)` resx-key triple. Add the 3 keys to BOTH resx files (EN + ES). Suggested EN copy (ES mirrors):
- `InactivityWarning.Subject`: `Your Project Ceres account has been inactive`
- `InactivityWarning.BodyText` / `.BodyHtml`: explain long inactivity, that the account will be anonymised after {0} months total of no sign-in, and that logging in now stops it. (One `{0}` arg = months-until-anonymise.)

- [ ] **Step 4: Add to the documented-set test**

In `ArchitectureTests.cs` `expected` array (~line 617), add `AuditLogAction.InactivityWarningSent` and `AuditLogAction.InactivityAnonymised`.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test ProjectCeres.Tests --filter "EmailComposerTests|AuditLogAction_enum_values_match_documented_set"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Models/AuditLog.cs ProjectCeres/Common/Email/ ProjectCeres/Resources/ ProjectCeres.Tests/
git commit -m "feat(13b): add inactivity audit actions + warning email template (EN/ES)"
```

---

## Task 5: The warning notification service

**Files:**
- Create: `ProjectCeres/Common/Email/InactivityWarningNotificationService.cs`
- Test: `ProjectCeres.Tests/.../InactivityWarningNotificationServiceTests.cs`

**Interfaces:**
- Produces: `IInactivityWarningNotificationService.NotifyAsync(Guid userId, int monthsUntilAnonymise, CancellationToken ct) → Task<bool>` — returns `true` only on a successful send (so the sweep stamps `InactivityWarningSentAt` only on success).

**Why `Task<bool>`:** unlike `NewSessionNotificationService` (fire-and-forget after a login that already succeeded), a swallowed failure here must leave the user eligible to retry next sweep — so the caller needs to know whether the send succeeded before stamping.

- [ ] **Step 1: Write the failing test**

```csharp
// Mock IEmailComposer + IEmailService + IEmailRecipientResolver.
[Fact]
public async Task Returns_true_and_sends_on_success()
{
    emailService.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
    var ok = await sut.NotifyAsync(userId, 18, CancellationToken.None);
    ok.Should().BeTrue();
    emailService.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task Returns_false_and_swallows_on_send_failure()
{
    emailService.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("smtp down"));
    var ok = await sut.NotifyAsync(userId, 18, CancellationToken.None); // must not throw
    ok.Should().BeFalse();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter InactivityWarningNotificationServiceTests`
Expected: FAIL — type undefined.

- [ ] **Step 3: Implement (shape copied from `NewSessionNotificationService`)**

```csharp
using System.Globalization;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Sends the Stage 13.b "your account has been inactive" warning (12-month horizon).
/// Recipient resolves server-side via IEmailRecipientResolver (never a payload). Returns
/// false on a swallowed send failure so the sweep does NOT stamp InactivityWarningSentAt
/// and the user stays eligible to retry. No opt-out consulted — a legal-basis storage-
/// limitation notice preventing silent data loss, same posture as NewSessionNotification;
/// if a notification-preferences surface is ever built, this notice stays exempt.
/// </summary>
public interface IInactivityWarningNotificationService
{
    Task<bool> NotifyAsync(Guid userId, int monthsUntilAnonymise, CancellationToken ct);
}

public sealed class InactivityWarningNotificationService(
    IEmailComposer composer,
    IEmailService email,
    IEmailRecipientResolver userRecipient,
    ILogger<InactivityWarningNotificationService> logger) : IInactivityWarningNotificationService
{
    public async Task<bool> NotifyAsync(Guid userId, int monthsUntilAnonymise, CancellationToken ct)
    {
        try
        {
            var recipient = await userRecipient.ResolveAsync(userId, ct);
            var message = composer.Compose(
                EmailTemplateKey.InactivityWarning,
                CultureInfo.CurrentUICulture,
                monthsUntilAnonymise.ToString(CultureInfo.InvariantCulture)) with { To = recipient };
            await email.SendAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Inactivity warning send failed for user {UserId}", userId);
            return false;
        }
    }
}
```

(Culture resolution: if `NewSessionNotificationService` or its caller resolves the user's language via `LanguageResolver` rather than `CurrentUICulture`, mirror that — check the sibling before finalizing.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ProjectCeres.Tests --filter InactivityWarningNotificationServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Common/Email/InactivityWarningNotificationService.cs ProjectCeres.Tests/
git commit -m "feat(13b): inactivity-warning notification service (retry-safe)"
```

---

## Task 6: The `InactivitySweep` tool

**Files:**
- Create: `ProjectCeres/Tools/InactivitySweep.cs`
- Modify: `ProjectCeres/Program.cs` (CLI dispatch)
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` (allow-list entry)
- Test: `ProjectCeres.Tests/Integration/Gdpr/InactivitySweepTests.cs`

**Interfaces:**
- Consumes: `UserAnonymiser.AnonymiseAsync` (Task 3), `IInactivityWarningNotificationService.NotifyAsync` (Task 5), `ErasurePseudonym.Compute` (for the anonymise token), `AuditLogAction.InactivityWarningSent`/`InactivityAnonymised` (Task 4).
- Produces: `InactivitySweep.ProcessAsync(AdminDbContext db, UserAnonymiser anonymiser, IInactivityWarningNotificationService warner, ErasurePseudonym pseudonym, TimeProvider clock, ILogger logger, CancellationToken ct) → Task<(int warned, int anonymised)>`; `InactivitySweep.RunAsync(WebApplicationBuilder) → Task<int>`.

**Horizons:** `WarningHorizon = TimeSpan.FromDays(365)` (12mo), `AnonymiseHorizon = TimeSpan.FromDays(548)` (18mo) — named `static readonly` fields mirroring `RetentionPurge`.

- [ ] **Step 1: Write the failing tests** (the § 6 ship-gate set)

```csharp
// InactivitySweepTests.cs — IClassFixture on the Gdpr-folder bucket factory.
// Seed throwaway @inactivity-test.invalid users with an injected FakeTimeProvider.
// Each test asserts against its own unique-marker users.

// Boundary — warning:
[Fact] public async Task Warns_user_just_past_12_months_not_one_just_inside() { /* 365+1d warned; 365-1d untouched */ }

// Boundary — anonymise:
[Fact] public async Task Anonymises_user_just_past_18_months_not_one_just_inside() { /* 548+1d scrubbed; 548-1d untouched */ }

// Warn-once:
[Fact] public async Task Second_sweep_does_not_re_send_warning() { /* run twice; warner invoked once; InactivityWarningSentAt stays stamped */ }

// Skip guard (negative) — two users, SealedAt set / ErasedAt set:
[Fact] public async Task Skips_sealed_or_erased_accounts() { /* neither warned nor anonymised */ }

// Legal line (negative):
[Fact] public async Task Inactivity_anonymise_writes_no_GdprErasureCompleted_and_no_hold() {
    // after anonymising an 18mo user:
    //   AuditLogs: exactly one InactivityAnonymised for this user, zero GdprErasureCompleted
    //   ErasedEmailHolds: none for this user
    //   user row: ErasedAt set, identity scrubbed, financial rows anonymise-retained
}

// Warning send-failure (negative) — warner returns false:
[Fact] public async Task Failed_send_does_not_stamp_warning_sent() { /* InactivityWarningSentAt stays null */ }

// Idempotency:
[Fact] public async Task Second_anonymise_pass_is_a_no_op() { /* re-run over scrubbed user doesn't throw, no second audit row */ }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test ProjectCeres.Tests --filter InactivitySweepTests`
Expected: FAIL — `InactivitySweep` undefined.

- [ ] **Step 3: Implement the sweep**

```csharp
// ProjectCeres/Tools/InactivitySweep.cs
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common.Authentication; // ErasurePseudonym
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tools;

/// <summary>
/// Stage 13.b inactivity sweep. 12mo no login → one warning email; 18mo → anonymise.
/// Flat cross-tenant find (AdminDbContext, BYPASSRLS) + per-user anonymise loop, the
/// RetentionPurge/ErasureWorker pattern. Skips Sealed/Erased accounts (never collides
/// with an in-flight erasure). Invoked via `dotnet run -- --run-inactivity-sweep`.
/// </summary>
[RequiresAdminContext]
public static class InactivitySweep
{
    public static readonly TimeSpan WarningHorizon = TimeSpan.FromDays(365);     // 12 months
    public static readonly TimeSpan AnonymiseHorizon = TimeSpan.FromDays(548);   // 18 months
    private const int MonthsUntilAnonymise = 18;

    public static async Task<(int Warned, int Anonymised)> ProcessAsync(
        AdminDbContext db,
        UserAnonymiser anonymiser,
        IInactivityWarningNotificationService warner,
        ErasurePseudonym pseudonym,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var warnCutoff = now - WarningHorizon;
        var anonCutoff = now - AnonymiseHorizon;

        // Anonymise group first (older horizon). Cross-tenant by design — allow-listed.
        var toAnonymise = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SealedAt == null && u.ErasedAt == null && u.LastLoginAt <= anonCutoff)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var anonymised = 0;
        foreach (var userId in toAnonymise)
        {
            // Re-check immediately before acting (a user could log in between the query
            // and this iteration — mirrors ErasureWorker's defense-in-depth re-read).
            var current = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.SealedAt, u.ErasedAt, u.LastLoginAt })
                .FirstOrDefaultAsync(ct);
            if (current is null || current.SealedAt != null || current.ErasedAt != null
                || current.LastLoginAt > anonCutoff) continue;

            try
            {
                var token = pseudonym.Compute(userId);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await anonymiser.AnonymiseAsync(userId, token, ct);
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = AuditLogAction.InactivityAnonymised,
                    EntityType = token,
                    OccurredAt = clock.GetUtcNow().UtcDateTime,
                    IpAddress = "unknown",
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                anonymised++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inactivity anonymise failed for {UserId}; continuing.", userId);
            }
        }

        // Warning group: inactive >= 12mo and < 18mo and not yet warned.
        var toWarn = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SealedAt == null && u.ErasedAt == null
                && u.LastLoginAt <= warnCutoff && u.LastLoginAt > anonCutoff
                && u.InactivityWarningSentAt == null)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var warned = 0;
        foreach (var userId in toWarn)
        {
            var sent = await warner.NotifyAsync(userId, MonthsUntilAnonymise, ct);
            if (!sent) continue; // stays eligible next run
            await db.Users.IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.InactivityWarningSentAt, now), ct);
            await db.AuditLogs.IgnoreQueryFilters().AddAsync(new AuditLog
            {
                Id = Guid.NewGuid(), UserId = userId, Action = AuditLogAction.InactivityWarningSent,
                OccurredAt = clock.GetUtcNow().UtcDateTime, IpAddress = "unknown",
            }, ct);
            await db.SaveChangesAsync(ct);
            warned++;
        }

        logger.LogInformation("Inactivity sweep: {Warned} warned, {Anonymised} anonymised.", warned, anonymised);
        return (warned, anonymised);
    }

    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await ProcessAsync(
            sp.GetRequiredService<AdminDbContext>(),
            sp.GetRequiredService<UserAnonymiser>(),
            sp.GetRequiredService<IInactivityWarningNotificationService>(),
            sp.GetRequiredService<ErasurePseudonym>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<Program>>(),
            CancellationToken.None);
        return 0;
    }
}
```

(Verify `ErasurePseudonym`'s namespace + `Compute` signature against `ErasureExecutor`'s usage; adjust the using/args if it differs. Confirm `AuditLog` requires no `EntityId` — the erasure row sets one but the warning context has none; if `EntityId` is non-nullable `Guid`, pass `Guid.Empty` or match the column's nullability.)

- [ ] **Step 4: Wire the CLI dispatch**

In `Program.cs`, after the `--run-retention-purge` block (~line 1022):

```csharp
// Cron-invokable inactivity sweep: 12-month warning + 18-month anonymisation.
// Invocation: dotnet run --project ProjectCeres -- --run-inactivity-sweep
if (args.Length > 0 && args[0] == "--run-inactivity-sweep")
{
    Environment.Exit(await ProjectCeres.Tools.InactivitySweep.RunAsync(builder));
}
```

- [ ] **Step 5: Add the allow-list entry**

In `ArchitectureTests.cs` allow-list `HashSet`:

```csharp
"ProjectCeres/Tools/InactivitySweep.cs",  // Stage 13.b: cross-tenant by design — the inactivity sweep spans all users. RetentionPurge/ErasureWorker pattern.
```

- [ ] **Step 6: Run the tests + build**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.Tests --filter "InactivitySweepTests|IgnoreQueryFilters"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Tools/InactivitySweep.cs ProjectCeres/Program.cs ProjectCeres.Tests/
git commit -m "feat(13b): inactivity sweep tool (12mo warn / 18mo anonymise)"
```

---

## Task 7: Full-suite green + docs + roadmap close-out

**Files:**
- Modify: `docs/security-model.md` (mark the inactivity flow built; keep the reconciliation note)
- Modify: `docs/roadmap-phase-three.md` (Stage 13 retention line "Inactive user records…" → point at delivered 13.b; Stage 13.b sub-items → `[x]`)
- Modify: `docs/planning-resolved.md` (note the reuse-vs-extract + no-hold decisions)
- Create: `docs/decisions/ADR-00NN-inactivity-anonymisation-reuse-and-no-hold.md` (next free ADR number — grep `docs/decisions/` for the highest)
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Run the full suite once (background), confirm green**

Run: `dotnet test ProjectCeres.sln` (foreground, once — the Stop hook also runs it; do not run concurrently).
Expected: all green. Fix any red at root cause before proceeding (no skips).

- [ ] **Step 2: Run `sync-docs` against the diff**

Invoke the `sync-docs` skill; it routes the model/security/ADR updates. Write the ADR for the two decisions (shared-extract; inactivity sets `ErasedAt` but NO re-registration hold, distinct from Art. 17 erasure).

- [ ] **Step 3: Roadmap close-out**

In `docs/roadmap-phase-three.md`: flip the Stage 13.b `[x]` blocker line's siblings, mark the open-questions resolved, and update the Stage 13 retention line ("Inactive user records: archived after defined period") to point at delivered 13.b. Confirm zero unchecked `[ ]` remain under the Stage 13.b heading (Phase E gate).

- [ ] **Step 4: Run `changelog-sync`**

Invoke the `changelog-sync` skill; add the 13.b entry under `[Unreleased]`.

- [ ] **Step 5: Commit**

```bash
git add docs/ CHANGELOG.md
git commit -m "docs(13b): close out inactivity anonymisation sweep (ADR + roadmap + changelog)"
```

---

## Self-review

**Spec coverage:**
- §2 login timestamp → Tasks 1, 2. ✓
- §3 shared scrubber + two tails + legal rule → Task 3 (+ Task 6 inactivity tail). ✓
- §4 sweep, two groups, skip guard, warn-once, per-user loop, continue-on-error → Task 6. ✓
- §5 warning email, opt-out-exempt, stamp-only-on-success, every registry → Tasks 4, 5, 6. ✓
- §6 all ten ship-gate tests → mapped across Tasks 1, 2, 3 (regression), 5, 6. ✓
- §7 file list → Tasks 1-7 cover every file. ✓
- §8 decision rationale → captured in Task 7 ADR. ✓

**Placeholder scan:** no TBD/TODO. The two "verify against the sibling" notes (bucket-factory name in Task 1; culture-resolution in Task 5; `ErasurePseudonym`/`AuditLog.EntityId` shape in Task 6) are explicit confirm-at-implementation checks against named files, not deferred work — each names the file to copy from.

**Type consistency:** `UserAnonymiser.AnonymiseAsync(Guid, string, CancellationToken)` is produced in Task 3 and consumed identically in Tasks 3 (erasure) and 6 (sweep). `IInactivityWarningNotificationService.NotifyAsync(Guid, int, CancellationToken) → Task<bool>` is produced in Task 5, consumed in Task 6. `AuditLogAction.InactivityWarningSent`/`InactivityAnonymised` and `EmailTemplateKey.InactivityWarning` are produced in Task 4, consumed in Task 6. Horizons (`365`/`548` days) consistent between §4 and Task 6. ✓
