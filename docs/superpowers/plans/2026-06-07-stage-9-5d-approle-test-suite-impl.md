# Stage 9.5d — AppRole Test Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an `AppRoleTests` xUnit collection of curated auth write-path tests that run under the restricted `ceres_app` Postgres role (RLS active), proving the RLS wall holds for auth writes — and fixing the one latent pre-auth-scope gap (login session insert) that surfaces when the writes first run under RLS.

**Architecture:** A new `Integration/AppRole/` folder in the existing `ProjectCeres.Tests` project, a `[CollectionDefinition("AppRoleTests")]` collection backed by a dedicated `IAsyncLifetime` fixture (the D5 `rolbypassrls` fail-fast guard) composed alongside the existing `DualContextWebApplicationFactory` (9.5b). Tests seed/clean via `NewAdminContext()` (BYPASSRLS), act over HTTP under `ceres_app`, and assert positive + negative isolation controls via `NewAppContext(actingAs)`.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, Npgsql, EF Core, ASP.NET Core `WebApplicationFactory<Program>`, PostgreSQL Row-Level Security.

---

## Context the implementer must hold

**The reuse surface (verified file:line):**
- `ProjectCeres.Tests/Integration/DualContextWebApplicationFactory.cs:28-44` — `NewAppContext(Guid actingAs)` returns `ActingAppContext` (`.Context` → RLS-bound `AppDbContext`, `IAsyncDisposable`); `NewAdminContext()` returns `AdminContextHandle` (`.Context` → BYPASSRLS `AdminDbContext`, `IAsyncDisposable`). Both are `await using`.
- `ProjectCeres.Tests/Integration/Rls/RlsTestFixture.cs:24-45` — the `IAsyncLifetime` seed/cleanup precedent; `:88-96` — `OpenAppConnectionAsync` raw-Npgsql precedent; `:100-101` — `[CollectionDefinition]` + `ICollectionFixture<>` wiring.
- `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs` — `RegisterUserAsync` (`:26`, creates + confirms a user + seeds categories), `MintCsrf` (`:140-151`, returns `(cookie, header)`), `PostJsonWithCsrfAsync`, `LoginViaHttpAsync`, `MintAuthCookieWithLastReauthAt`. **Reuse these — do not hand-roll HTTP/CSRF.**
- `ProjectCeres/Common/Authentication/SessionConstants.cs:7-10` — `CsrfCookieName`, `CsrfHeaderName`.
- `ProjectCeres.Tests/Common/TestDbFixture.cs:33-39` — `internal const AppConnectionString` / `AdminConnectionString` / `MigratorConnectionString`.

**The cleanup discipline (D4, non-negotiable):** test bodies write/assert under `ceres_app`; **seed + cleanup ALWAYS via `NewAdminContext()`**. The existing auth tests' `IgnoreQueryFilters().ExecuteDeleteAsync()` cross-user cleanup runs on an admin context there too — replicate that, never on the app context (it 42501s).

**The 42501 rule (D-§6, non-negotiable):** if any flow's HTTP act returns 500 / the test sees `42501 new row violates row-level security policy` on a user-owned write, that is a **real production gap** (a write missing its `BeginPreAuthUserScopeAsync`). Fix it in production with the proven envelope (see Task 4), never weaken the test, never wrap the test in try/catch to swallow it.

**Per-test data isolation (the real safety net, not collection serialization):** every test uses a unique marker (per-test email suffix / GUID userId) so parallel collections don't collide. Pattern: `$"…-{Guid.NewGuid():N}@approle-test.local"`.

**Flow risk map (from research — which flows may surface a 42501):**
| Flow | Write path | 42501 risk under ceres_app |
|---|---|---|
| Register | `BeginPreAuthUserScopeAsync` (AuthController:101) | none — already scoped |
| **Login** | `IssueSessionAndCookiesAsync` **unscoped** (:465-466) | **YES — real gap, fixed in Task 4** |
| Logout | `AuditLogWriter` own scope (:61) | none |
| MFA enroll | authenticated (cookie in request → GUC set) | none |
| MFA backup-code consume | `MfaBackupCodeService:85` own scope | none |
| Password-reset confirm | `PasswordResetService:299` own scope | none |
| Email-change confirm/revoke | `IgnoreQueryFilters` + lookup-via-Admin; writes after lookup | verify in Task 9 — treat any 42501 as a gap |
| Email-confirmation verify | `BeginPreAuthUserScopeAsync` (EmailConfirmationService:260) | none |
| Lockout-unlock | `IgnoreQueryFilters` + `[RlsBypassJustified]`; AuditLog self-scopes | verify in Task 10 — treat any 42501 as a gap |
| Audit-log write | `AuditLogWriter` own scope | none |

---

## File Structure

- **Create** `ProjectCeres.Tests/Integration/AppRole/AppRoleCollection.cs` — `[CollectionDefinition("AppRoleTests")]` + `AppRoleFixture : IAsyncLifetime` (the D5 guard).
- **Create** `ProjectCeres.Tests/Integration/AppRole/RegisterWritesUnderRlsTests.cs`
- **Create** `ProjectCeres.Tests/Integration/AppRole/LoginSessionWriteUnderRlsTests.cs`
- **Modify** `ProjectCeres/Controllers/Api/AuthController.cs:428-466` — wrap the session insert in `BeginPreAuthUserScopeAsync` (Task 4 fix).
- **Create** `ProjectCeres.Tests/Integration/AppRole/LogoutWritesUnderRlsTests.cs`
- **Create** `ProjectCeres.Tests/Integration/AppRole/MfaWritesUnderRlsTests.cs`
- **Create** `ProjectCeres.Tests/Integration/AppRole/PasswordResetConfirmUnderRlsTests.cs`
- **Create** `ProjectCeres.Tests/Integration/AppRole/EmailChangeUnderRlsTests.cs`
- **Create** `ProjectCeres.Tests/Integration/AppRole/EmailConfirmationUnderRlsTests.cs`
- **Create** `ProjectCeres.Tests/Integration/AppRole/LockoutUnlockUnderRlsTests.cs`
- **Create** `ProjectCeres.Tests/Integration/AppRole/AuditLogWriteUnderRlsTests.cs`
- **Modify** `docs/testing.md`, `docs/roadmap-phase-three.md`, `docs/security-model.md` (Task 12, close-out).

---

## Task 1: The AppRole collection + D5 fail-fast guard fixture

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/AppRoleCollection.cs`

- [ ] **Step 1: Write the fixture + collection + a self-test for the guard.**

```csharp
using Npgsql;
using ProjectCeres.Tests.Integration; // DualContextWebApplicationFactory
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 9.5d. Boots the ceres_app-wired DualContextWebApplicationFactory for the AppRole
/// suite AND fails the whole collection fast (condition D1) if ceres_app secretly holds
/// BYPASSRLS — otherwise every AppRole test would pass falsely against a misconfigured role.
/// </summary>
public sealed class AppRoleFixture : IAsyncLifetime
{
    public DualContextWebApplicationFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(TestDbFixture.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rolbypassrls FROM pg_roles WHERE rolname = 'ceres_app'";
        var result = await cmd.ExecuteScalarAsync();
        var bypass = result is bool b && b;
        if (bypass)
            throw new InvalidOperationException(
                "ceres_app has BYPASSRLS — the AppRole suite would pass falsely. " +
                "Fix scripts/setup-postgres-roles.sql (ceres_app must be NOBYPASSRLS).");
    }

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("AppRoleTests")]
public class AppRoleTestsCollection : ICollectionFixture<AppRoleFixture> { }
```

- [ ] **Step 2: Verify `DualContextWebApplicationFactory` is constructable parameterless + `TestDbFixture.AppConnectionString` is accessible.**

Run: `grep -n "public DualContextWebApplicationFactory\|public sealed class DualContextWebApplicationFactory" ProjectCeres.Tests/Integration/DualContextWebApplicationFactory.cs && grep -n "AppConnectionString" ProjectCeres.Tests/Common/TestDbFixture.cs`
Expected: the factory has an implicit/public parameterless ctor (it's a `WebApplicationFactory<Program>` subclass — no declared ctor needed) and `AppConnectionString` is `internal const` (same assembly, accessible).

- [ ] **Step 3: Write a guard self-test (proves the fixture's check runs and the suite boots).**

Create `ProjectCeres.Tests/Integration/AppRole/AppRoleFixtureGuardTests.cs`:

```csharp
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class AppRoleFixtureGuardTests
{
    private readonly AppRoleFixture _fixture;
    public AppRoleFixtureGuardTests(AppRoleFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Ceres_app_role_does_not_have_BYPASSRLS()
    {
        // The fixture InitializeAsync already fails the collection if this is false; this
        // test makes the invariant visible in the run output and double-asserts live.
        await using var conn = new NpgsqlConnection(TestDbFixture.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rolbypassrls FROM pg_roles WHERE rolname = 'ceres_app'";
        var bypass = (bool)(await cmd.ExecuteScalarAsync() ?? true);
        bypass.Should().BeFalse("ceres_app must be NOBYPASSRLS for the AppRole suite to mean anything");
    }
}
```

- [ ] **Step 4: Build + run the guard test.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AppRoleFixtureGuardTests"`
Expected: PASS (ceres_app is NOBYPASSRLS in the dev/test DB per setup-postgres-roles.sql).

- [ ] **Step 5: Commit.**

```bash
git add ProjectCeres.Tests/Integration/AppRole/AppRoleCollection.cs ProjectCeres.Tests/Integration/AppRole/AppRoleFixtureGuardTests.cs
git commit -m "feat(9.5d): AppRoleTests collection + D5 fail-fast rolbypassrls guard fixture"
```

---

## Task 2: A shared AppRole test base (DRY the seed/cleanup/act boilerplate)

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/AppRoleTestBase.cs`

Rationale: every AppRole test repeats the same shape (unique marker, seed via admin, act over HTTP, assert via app contexts, cleanup via admin). Factor it once so the per-flow classes stay small and the cleanup discipline can't be forgotten.

- [ ] **Step 1: Write the base class.**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 9.5d. Shared shape for AppRole tests: a unique per-test marker, admin-context
/// seeding/cleanup (D4), and the positive+negative RLS visibility assertion helper.
/// </summary>
public abstract class AppRoleTestBase : IAsyncLifetime
{
    protected readonly AppRoleFixture Fixture;
    protected DualContextWebApplicationFactory Factory => Fixture.Factory;
    protected readonly string Marker = $"approle-{Guid.NewGuid():N}";

    protected AppRoleTestBase(AppRoleFixture fixture) => Fixture = fixture;

    public virtual Task InitializeAsync() => Task.CompletedTask;

    // Subclasses override to delete their seeded rows via the ADMIN context (BYPASSRLS).
    public abstract Task DisposeAsync();

    /// <summary>
    /// Positive + negative RLS control: assert the owner's app-context sees exactly the
    /// expected count of their rows, and a different user's app-context sees zero.
    /// </summary>
    protected async Task AssertRlsVisibility<TEntity>(
        Guid owner, Guid otherUser, System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        int expectedOwnerCount) where TEntity : class
    {
        await using (var appOwner = Factory.NewAppContext(owner))
        {
            (await appOwner.Context.Set<TEntity>().CountAsync(predicate))
                .Should().Be(expectedOwnerCount, "the owner's ceres_app context must see its own row(s)");
        }
        await using (var appOther = Factory.NewAppContext(otherUser))
        {
            (await appOther.Context.Set<TEntity>().CountAsync(predicate))
                .Should().Be(0, "a different user's ceres_app context must NOT see the owner's row(s)");
        }
    }
}
```

- [ ] **Step 2: Build to confirm it compiles against `DualContextWebApplicationFactory` + `AppRoleFixture`.**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit.**

```bash
git add ProjectCeres.Tests/Integration/AppRole/AppRoleTestBase.cs
git commit -m "feat(9.5d): AppRoleTestBase — marker + admin cleanup + positive/negative RLS assertion helper"
```

---

## Task 3: Register flow under ceres_app (the no-fix-expected baseline)

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/RegisterWritesUnderRlsTests.cs`

Register already wraps its writes in `BeginPreAuthUserScopeAsync` (AuthController:101), so this test should pass without any production change — it's the baseline proving the harness works end-to-end.

- [ ] **Step 1: Write the failing test.**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class RegisterWritesUnderRlsTests : AppRoleTestBase
{
    private readonly System.Collections.Generic.List<Guid> _seededUsers = new();

    public RegisterWritesUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Register_under_ceres_app_seeds_categories_visible_only_to_the_new_user()
    {
        var email = $"reg-{Marker}@approle-test.local";
        var client = Factory.CreateClient();

        // ACT: register over HTTP — the pipeline writes AspNetUsers + Categories + AuditLog
        // under ceres_app, inside the register action's PreAuthUserScope.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/register", new { email, password = "Sup3rSecret!pw" });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Resolve the new user's id via the admin context (BYPASSRLS).
        Guid newUserId;
        await using (var admin = Factory.NewAdminContext())
        {
            var user = await admin.Context.Users.IgnoreQueryFilters()
                .SingleAsync(u => u.Email == email);
            newUserId = user.Id;
            _seededUsers.Add(newUserId);
        }

        // ASSERT (positive + negative): the new user's categories are visible to them under
        // ceres_app, and invisible to a different user.
        var otherUser = Guid.NewGuid();
        await AssertRlsVisibility<Category>(
            owner: newUserId, otherUser: otherUser,
            predicate: c => c.UserId == newUserId, expectedOwnerCount: 26);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        foreach (var uid in _seededUsers)
        {
            await admin.Context.Categories.IgnoreQueryFilters().Where(c => c.UserId == uid).ExecuteDeleteAsync();
            await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == uid).ExecuteDeleteAsync();
            await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == uid).ExecuteDeleteAsync();
        }
    }
}
```

- [ ] **Step 2: Run — expected PASS (register is already scoped).**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~RegisterWritesUnderRlsTests"`
Expected: PASS. If it returns 500 / 42501, register's PreAuthUserScope is broken — that is a real gap; STOP and report (do not weaken the test). The 26 count comes from `CategoriesDefaultsTests` canonical list; if the count drifts, read the current default-category count and update the expected value (case 3: expected value changed).

- [ ] **Step 3: Commit.**

```bash
git add ProjectCeres.Tests/Integration/AppRole/RegisterWritesUnderRlsTests.cs
git commit -m "feat(9.5d): register-under-ceres_app test — baseline RLS write-path proof"
```

---

## Task 4: Login session-write — THE production fix (TDD: red → fix → green)

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/LoginSessionWriteUnderRlsTests.cs`
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs:428-466` (`IssueSessionAndCookiesAsync`)

This is the confirmed latent gap (security review 2026-06-07): login's `UserSession` insert runs unscoped, so under `ceres_app` the GUC is reset (login is `[PreAuthCallSite]`, cookie is in the response not the request) and the insert is rejected `42501`. The fix is the proven envelope from `MfaBackupCodeService.cs:85` / `AuditLogWriter.cs:61`.

- [ ] **Step 1: Write the test that asserts login persists a session row visible to its owner under ceres_app.**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class LoginSessionWriteUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public LoginSessionWriteUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Login_under_ceres_app_persists_a_UserSession_visible_only_to_its_owner()
    {
        var email = $"login-{Marker}@approle-test.local";
        const string password = "Sup3rSecret!pw";

        // SEED via admin: a confirmed, ready-to-login user.
        _userId = await AuthTestFixture.RegisterUserAsync(Factory, email, password);

        // ACT: log in over HTTP — IssueSessionAndCookiesAsync writes UserSession under ceres_app.
        var client = Factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/login", new { email, password, rememberMe = false });

        // Before the production fix this is 500 (42501 on the session insert); after, 204.
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent,
            "login must persist the session row under ceres_app — a 500 here means the insert hit RLS 42501 (missing PreAuthUserScope)");

        // ASSERT positive + negative: the session row is visible to its owner, not to others.
        await AssertRlsVisibility<UserSession>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: s => s.UserId == _userId && s.RevokedAt == null, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
```

- [ ] **Step 2: Run — expected RED (42501).**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~LoginSessionWriteUnderRlsTests"`
Expected: FAIL — `resp.StatusCode` is 500 (the server log shows `42501 new row violates row-level security policy for table "UserSessions"`). This confirms the gap. If it unexpectedly PASSES, the gap was already fixed elsewhere — verify by reading `IssueSessionAndCookiesAsync` and adjust this task to test-only.

- [ ] **Step 3: Apply the production fix in `IssueSessionAndCookiesAsync`.**

Read `ProjectCeres/Controllers/Api/AuthController.cs` lines 428-469 first. The current body ends with:

```csharp
        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync();
        // ... antiforgery GetAndStoreTokens ...
```

Wrap the insert in the pre-auth scope (mirrors `MfaBackupCodeService.cs:85` + `AuditLogWriter.cs:61-64`):

```csharp
        await using (var rlsScope = await _db.BeginPreAuthUserScopeAsync(user.Id, HttpContext.RequestAborted))
        {
            _db.UserSessions.Add(session);
            await _db.SaveChangesAsync(HttpContext.RequestAborted);
            await rlsScope.CommitAsync(HttpContext.RequestAborted);
        }
```

Keep the persistent-cookie hashing/append block (lines ~445-463) and the `_antiforgery.GetAndStoreTokens` call exactly where they are — only the `Add` + `SaveChangesAsync` pair moves inside the scope. Do NOT change the three call sites (303 login / 382 totp / 419 backup-code) — the scope is keyed to `user.Id` which all three already pass, and `MfaBackupCodeService` commits its own scope before returning so there is no ambient transaction at any call site.

- [ ] **Step 4: Run — expected GREEN.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~LoginSessionWriteUnderRlsTests"`
Expected: PASS (204 + session visible to owner, not to others).

- [ ] **Step 5: Run the existing login/logout admin-context tests to confirm no regression.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~LoginEndpointTests|FullyQualifiedName~LogoutEndpointTests|FullyQualifiedName~LoginTotp|FullyQualifiedName~BackupCodeLogin"`
Expected: PASS — the added scope is transparent under the admin (BYPASSRLS) fixture.

- [ ] **Step 6: Commit (production fix + test together).**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/AppRole/LoginSessionWriteUnderRlsTests.cs
git commit -m "fix(9.5d): wrap login session insert in PreAuthUserScope — closes 42501 under ceres_app (the gap 9.5d caught)"
```

---

## Task 5: Logout audit-write under ceres_app

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/LogoutWritesUnderRlsTests.cs`

Logout's audit write self-scopes via `AuditLogWriter` (own DI scope + own PreAuthUserScope), and the session-revoke runs authenticated (cookie in request). Expected PASS without production change — but it depends on Task 4's login fix to get a session to log out of.

- [ ] **Step 1: Write the test.**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class LogoutWritesUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public LogoutWritesUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Logout_under_ceres_app_revokes_session_and_writes_audit_visible_to_owner()
    {
        var email = $"logout-{Marker}@approle-test.local";
        const string password = "Sup3rSecret!pw";
        _userId = await AuthTestFixture.RegisterUserAsync(Factory, email, password);

        // LoginViaHttpAsync returns an authenticated client (session + CSRF cookies set).
        var client = await AuthTestFixture.LoginViaHttpAsync(Factory, email, password);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/logout", new { });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Session revoked (owner sees a revoked row); audit Logout row visible to owner only.
        await using (var appOwner = Factory.NewAppContext(_userId))
        {
            (await appOwner.Context.UserSessions.CountAsync(s => s.UserId == _userId && s.RevokedAt != null))
                .Should().BeGreaterThanOrEqualTo(1);
        }
        await AssertRlsVisibility<AuditLog>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: a => a.UserId == _userId && a.Action == AuditLogAction.Logout, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
```

Note: `AuditLogAction.Logout` — confirm the enum member name via `grep -n "Logout" ProjectCeres/Models/AuditLogAction.cs` before running; adjust if the member differs.

- [ ] **Step 2: Run — expected PASS.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~LogoutWritesUnderRlsTests"`
Expected: PASS. Any 42501 → real gap, fix in production per Task 4's pattern, do not weaken.

- [ ] **Step 3: Commit.**

```bash
git add ProjectCeres.Tests/Integration/AppRole/LogoutWritesUnderRlsTests.cs
git commit -m "feat(9.5d): logout-under-ceres_app test — session revoke + audit write"
```

---

## Task 6: MFA enroll + backup-code writes under ceres_app

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/MfaWritesUnderRlsTests.cs`

MFA enroll is authenticated (cookie in request → GUC set); backup-code consume self-scopes (`MfaBackupCodeService:85`). Expected PASS. This task needs a TOTP code computed from the enroll secret — reuse the existing MFA test helper.

- [ ] **Step 1: Find the existing TOTP-compute + enroll helper to reuse.**

Run: `grep -rn "ComputeTotp\|GenerateTotp\|enroll/verify\|otpAuth\|EnrollUserMfaAsync" ProjectCeres.Tests/Integration/Authentication/Mfa/MfaEnrollmentTests.cs ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs`
Expected: a helper that posts `/api/auth/mfa/enroll`, computes a TOTP from the returned secret, and posts `/api/auth/mfa/enroll/verify`. Reuse it; do not reimplement TOTP.

- [ ] **Step 2: Write the test (using the helper found in Step 1 — fill the exact call once known).**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class MfaWritesUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public MfaWritesUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task MfaEnroll_under_ceres_app_persists_10_backup_codes_visible_only_to_owner()
    {
        var email = $"mfa-{Marker}@approle-test.local";
        const string password = "Sup3rSecret!pw";
        _userId = await AuthTestFixture.RegisterUserAsync(Factory, email, password);
        var client = await AuthTestFixture.LoginViaHttpAsync(Factory, email, password);

        // Reuse the enroll+verify helper from Step 1 (posts enroll, computes TOTP, posts verify).
        // It returns the backup codes; we assert the persisted rows under ceres_app.
        await AuthTestFixture.EnrollMfaViaHttpAsync(Factory, client); // exact name per Step 1

        await AssertRlsVisibility<UserMfaBackupCode>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: c => c.UserId == _userId && c.UsedAt == null, expectedOwnerCount: 10);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
```

If no reusable enroll-over-HTTP helper exists (Step 1 finds only in-class private methods), add one to `AuthTestFixture` (`EnrollMfaViaHttpAsync(factory, client)`) lifted from `MfaEnrollmentTests` — a shared helper, not a copy. Keep it minimal.

- [ ] **Step 3: Run — expected PASS.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~MfaWritesUnderRlsTests"`
Expected: PASS. Any 42501 → real gap, fix in production, don't weaken.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Tests/Integration/AppRole/MfaWritesUnderRlsTests.cs ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs
git commit -m "feat(9.5d): MFA enroll backup-code writes under ceres_app"
```

---

## Task 7: Password-reset confirm under ceres_app (pre-auth scoped)

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/PasswordResetConfirmUnderRlsTests.cs`

`PasswordResetService:299` opens its own PreAuthUserScope. Expected PASS. Seed: a user + an active `PasswordResetToken` (issued via the request endpoint, or seeded via admin with a known raw token).

- [ ] **Step 1: Find the token-seeding pattern (how existing tests create a usable reset token).**

Run: `grep -n "PasswordResetToken\|password-reset/request\|TokenLookupHasher\|Generate(" ProjectCeres.Tests/Integration/Authentication/PasswordResetConfirmNoMfaTests.cs`
Expected: either (a) POST `/api/auth/password-reset/request` then extract the raw token from the captured email, or (b) seed a token row via admin with `TokenLookupHasher.ComputeLookup` + the hashed secret. Use the same approach the existing no-MFA test uses.

- [ ] **Step 2: Write the test.**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class PasswordResetConfirmUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public PasswordResetConfirmUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task PasswordReset_confirm_under_ceres_app_consumes_token_and_revokes_sessions()
    {
        var email = $"pwreset-{Marker}@approle-test.local";
        _userId = await AuthTestFixture.RegisterUserAsync(Factory, email, "Old!Passw0rd");

        // Obtain a raw reset token per Step 1 (request endpoint + captured email is preferred).
        var rawToken = await AuthTestFixture.RequestPasswordResetAndCaptureTokenAsync(Factory, email); // per Step 1

        var client = Factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/password-reset/confirm",
            new { token = rawToken, newPassword = "New!Passw0rd123" });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // The consumed token row + its visibility under ceres_app (positive/negative).
        await using (var appOwner = Factory.NewAppContext(_userId))
        {
            (await appOwner.Context.PasswordResetTokens.CountAsync(t => t.UserId == _userId && t.ConsumedAt != null))
                .Should().BeGreaterThanOrEqualTo(1);
        }
        await AssertRlsVisibility<PasswordResetToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.PasswordResetTokens.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
```

If Step 1 shows the existing test seeds the token via admin rather than the request endpoint, replace the `RequestPasswordResetAndCaptureTokenAsync` call with that admin-seed block inline.

- [ ] **Step 3: Run — expected PASS.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~PasswordResetConfirmUnderRlsTests"`
Expected: PASS. Any 42501 → real gap, fix in production, don't weaken.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Tests/Integration/AppRole/PasswordResetConfirmUnderRlsTests.cs ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs
git commit -m "feat(9.5d): password-reset confirm under ceres_app"
```

---

## Task 8: Email-confirmation verify under ceres_app (the table that shipped the 9.3 gap)

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/EmailConfirmationUnderRlsTests.cs`

`EmailConfirmationService:260` opens its own PreAuthUserScope. Expected PASS. This is the highest-symbolic-value test: `EmailConfirmationTokens` is the table whose missing RLS policy shipped in 9.3.

- [ ] **Step 1: Find the verify-token-capture pattern.**

Run: `grep -n "email/verify\|EmailConfirmationToken\|captured\|rawToken" ProjectCeres.Tests/Integration/EmailConfirmationTests.cs`
Expected: register issues a token; the test extracts the raw token from the captured email; POSTs `/api/auth/email/verify` with `{ token = rawToken }`. Reuse that capture mechanism.

- [ ] **Step 2: Write the test.**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class EmailConfirmationUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public EmailConfirmationUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task EmailConfirmation_verify_under_ceres_app_consumes_token_visible_only_to_owner()
    {
        var email = $"emailconfirm-{Marker}@approle-test.local";
        // Register WITHOUT auto-confirm so a real EmailConfirmationToken is issued + capturable.
        var (userId, rawToken) = await AuthTestFixture.RegisterAndCaptureConfirmTokenAsync(Factory, email, "Sup3rSecret!pw"); // per Step 1
        _userId = userId;

        var client = Factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/email/verify", new { token = rawToken });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        await using (var appOwner = Factory.NewAppContext(_userId))
        {
            (await appOwner.Context.EmailConfirmationTokens.CountAsync(t => t.UserId == _userId && t.ConsumedAt != null))
                .Should().BeGreaterThanOrEqualTo(1);
            (await appOwner.Context.Users.CountAsync(u => u.Id == _userId && u.EmailConfirmed)).Should().Be(1);
        }
        await AssertRlsVisibility<EmailConfirmationToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.EmailConfirmationTokens.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Categories.IgnoreQueryFilters().Where(c => c.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
```

- [ ] **Step 3: Run — expected PASS.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~EmailConfirmationUnderRlsTests"`
Expected: PASS — and this is the test that, had it existed in 9.3, would have caught the original gap.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Tests/Integration/AppRole/EmailConfirmationUnderRlsTests.cs ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs
git commit -m "feat(9.5d): email-confirmation verify under ceres_app — the table that shipped the 9.3 gap"
```

---

## Task 9: Email-change confirm + revoke under ceres_app (RLS-bypass write path — verify)

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/EmailChangeUnderRlsTests.cs`

`EmailChangeService` looks the token up via AdminDbContext, then writes via `IgnoreQueryFilters` + `[RlsBypassJustified]` — under ceres_app `IgnoreQueryFilters` does NOT bypass the DB policy. **This task must verify whether the writes succeed; if they 42501, that is a real gap to fix in production (the writes need a PreAuthUserScope keyed to the resolved match.UserId).**

- [ ] **Step 1: Find the email-change token-seed pattern.**

Run: `grep -n "EmailChangeToken\|MintAuthCookieWithLastReauthAt\|Purpose.VerifyNew\|EmailChangeTokenGenerator\|email-change/request" ProjectCeres.Tests/Integration/Authentication/EmailChangeConfirmTests.cs`
Expected: the confirm test arranges a user, requests an email change (authenticated, `[RequireRecentAuth]`), captures the VerifyNew token from the captured email. Reuse that arrange.

- [ ] **Step 2: Write the confirm test.**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class EmailChangeUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public EmailChangeUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task EmailChange_confirm_under_ceres_app_consumes_tokens_and_updates_email()
    {
        var email = $"emailchange-{Marker}@approle-test.local";
        var newEmail = $"emailchange-new-{Marker}@approle-test.local";
        _userId = await AuthTestFixture.RegisterUserAsync(Factory, email, "Sup3rSecret!pw");

        // Arrange the pending VerifyNew/RevokeOld token pair + capture the VerifyNew raw token (Step 1).
        var rawVerifyToken = await AuthTestFixture.RequestEmailChangeAndCaptureVerifyTokenAsync(Factory, email, "Sup3rSecret!pw", newEmail); // per Step 1

        var client = Factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/email-change/confirm", new { token = rawVerifyToken });

        // If this is 500/42501, the EmailChangeService writes need a PreAuthUserScope keyed to
        // match.UserId — fix in production (Task 4 pattern), DO NOT weaken this assertion.
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent,
            "email-change confirm must persist under ceres_app");

        await using (var appOwner = Factory.NewAppContext(_userId))
        {
            (await appOwner.Context.EmailChangeTokens.CountAsync(t => t.UserId == _userId && t.ConsumedAt != null))
                .Should().BeGreaterThanOrEqualTo(1);
            (await appOwner.Context.Users.CountAsync(u => u.Id == _userId && u.Email == newEmail)).Should().Be(1);
        }
        await AssertRlsVisibility<EmailChangeToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 2); // VerifyNew + RevokeOld
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.EmailChangeTokens.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
```

- [ ] **Step 3: Run — observe pass-or-42501.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~EmailChangeUnderRlsTests"`
Expected: PASS if `EmailChangeService`'s writes already set the GUC correctly (it may, via its own scope on the resolved userId). If FAIL with 42501: this is a discovered production gap — wrap the `EmailChangeService` confirm/revoke writes in `BeginPreAuthUserScopeAsync(match.UserId, ct)` (mirror `PasswordResetService:299`), re-run to green, and note the production fix in the commit. STOP and report which path it took before continuing.

- [ ] **Step 4: Commit (note in the message whether a production fix was needed).**

```bash
git add ProjectCeres.Tests/Integration/AppRole/EmailChangeUnderRlsTests.cs ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs
# include ProjectCeres/Common/Authentication/EmailChangeService.cs if a fix was applied
git commit -m "feat(9.5d): email-change confirm under ceres_app [+ PreAuthUserScope fix if 42501 surfaced]"
```

---

## Task 10: Lockout-unlock under ceres_app (RLS-bypass write path — verify)

**Files:**
- Create: `ProjectCeres.Tests/Integration/AppRole/LockoutUnlockUnderRlsTests.cs`

`LockoutUnlockService` writes the token-consume via `IgnoreQueryFilters` + `[RlsBypassJustified("CER-1007")]`; the AuditLog write self-scopes. Under ceres_app the token-consume update may 42501 — **verify; treat a 42501 as a real gap to fix.**

- [ ] **Step 1: Find the lockout-token seed pattern.**

Run: `grep -n "LockoutUnlockToken\|LockoutEnd\|lockout-unlock\|EnsureLockedOut\|Generate(" ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs`
Expected: arrange a user with `LockoutEnd` in the future + a `LockoutUnlockToken` row (seeded via admin); capture/know the raw token. Reuse.

- [ ] **Step 2: Write the test.**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class LockoutUnlockUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public LockoutUnlockUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task LockoutUnlock_confirm_under_ceres_app_consumes_token_and_clears_lockout()
    {
        var email = $"lockout-{Marker}@approle-test.local";
        _userId = await AuthTestFixture.RegisterUserAsync(Factory, email, "Sup3rSecret!pw");

        // Lock the user + seed a LockoutUnlockToken via admin; capture the raw token (Step 1).
        var rawToken = await AuthTestFixture.LockUserAndSeedUnlockTokenAsync(Factory, _userId); // per Step 1

        var client = Factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/lockout-unlock", new { token = rawToken });

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent,
            "lockout-unlock confirm must persist under ceres_app");

        await using (var appOwner = Factory.NewAppContext(_userId))
        {
            (await appOwner.Context.LockoutUnlockTokens.CountAsync(t => t.UserId == _userId && t.ConsumedAt != null))
                .Should().BeGreaterThanOrEqualTo(1);
        }
        await AssertRlsVisibility<LockoutUnlockToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.LockoutUnlockTokens.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
```

- [ ] **Step 3: Run — observe pass-or-42501.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~LockoutUnlockUnderRlsTests"`
Expected: PASS or a discovered 42501. If 42501 on the token-consume: wrap that write in `BeginPreAuthUserScopeAsync(match.UserId, ct)` in `LockoutUnlockService` and re-run to green. STOP and report which path it took.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Tests/Integration/AppRole/LockoutUnlockUnderRlsTests.cs ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs
# include ProjectCeres/Common/Authentication/LockoutUnlockService.cs if a fix was applied
git commit -m "feat(9.5d): lockout-unlock under ceres_app [+ PreAuthUserScope fix if 42501 surfaced]"
```

---

## Task 11: Full-suite green + the AppRole filter

**Files:** none (verification task)

- [ ] **Step 1: Run the whole AppRole collection.**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ProjectCeres.Tests.Integration.AppRole"`
Expected: all AppRole tests PASS. Count ≈ 10 test classes (guard + register + login + logout + mfa + password-reset + email-confirm + email-change + lockout + any add-ons).

- [ ] **Step 2: Run the full suite — no regression anywhere.**

Run: `dotnet test`
Expected: full suite green (the existing ~60 admin auth tests + ~1142 prior + the new AppRole tests). The Task 4 production fix must not have regressed any admin-context test.

- [ ] **Step 3: Confirm the AppRole collection runs without cross-collection DB races.**

Run the full suite a second time: `dotnet test`
Expected: green again (proves the marker-based isolation holds under parallel collection execution; if flaky, a test is missing its per-test marker — fix the marker, never add a Skip).

- [ ] **Step 4: Commit (if any marker/flake fix was needed; otherwise skip).**

```bash
git add -A && git commit -m "test(9.5d): AppRole suite green end-to-end under ceres_app"
```

---

## Task 12: Docs sync + roadmap close-out

**Files:**
- Modify: `docs/testing.md`, `docs/roadmap-phase-three.md`, `docs/security-model.md`

- [ ] **Step 1: Add the AppRole subsection to `docs/testing.md`** after the "Test-infrastructure RLS parity (Stage 9.5b)" subsection:

Document: the `AppRoleTests` collection; the canonical pattern (seed via `NewAdminContext`, act over HTTP under ceres_app, assert positive+negative via `NewAppContext`, cleanup via `NewAdminContext`); the D5 fail-fast guard; that login required a production fix (the session-insert PreAuthUserScope); and that the suite rides Tier 2.

- [ ] **Step 2: Correct the stale "no second collection" line** in `docs/testing.md` (§ Test Collection Serialization):

Replace "Do not create a second collection — a second collection runs in parallel with the first and reintroduces the race condition." with a note that the project intentionally runs sibling collections (`RlsTests`, `RateLimitTests`, `MfaRateLimitTests`, `AppRoleTests`); `RateLimitTests`/`MfaRateLimitTests` carry `DisableParallelization = true`; the real cross-collection safety net is per-test data isolation by marker (`feedback_filter_test_queries_by_test_data`), not single-collection serialization.

- [ ] **Step 3: Add a `security-model.md` note** in the RLS section: auth write-flows are now verified under `ceres_app` by the AppRole suite (closing the test-blindness that let the 9.3 `EmailConfirmationTokens` gap ship); note the login session-insert PreAuthUserScope fix.

- [ ] **Step 4: Tick 9.5d in `docs/roadmap-phase-three.md`** (line ~1255): `[ ]` → `[x]` with a close-out note recording: the collection (not a separate csproj — D1); the curated write-path set; the login PreAuthUserScope production fix; any email-change/lockout fixes that surfaced; D1-guard; the corrected testing.md line.

- [ ] **Step 5: Run sync-docs + changelog-sync, then commit.**

```bash
git add docs/testing.md docs/roadmap-phase-three.md docs/security-model.md
git commit -m "docs(9.5d): AppRole test-infra subsection; correct stale 'no second collection' line; tick 9.5d"
```

---

## Final verification (before stage close-out)

- [ ] `dotnet build` — 0 errors, 0 CER errors (the Task 4 `BeginPreAuthUserScopeAsync` satisfies CER001 on `[PreAuthScope]` AuthController).
- [ ] `dotnet test` — full suite green twice in a row (flake check).
- [ ] AppRole collection: every test has a per-test marker; every `DisposeAsync` cleans via `NewAdminContext` (not the app context); no `[Fact(Skip=...)]`, no swallowed 42501.
- [ ] Any discovered production gap (login confirmed; email-change/lockout possible) was FIXED in production with `BeginPreAuthUserScopeAsync`, not worked around in the test.
- [ ] Evidence bundle: `build-matrix.sh 9.5d` + turn-shape generator; `registry-sweep.json` NOT required (no new IUserOwned entity / Models change); `rls-audit.psql` refreshed (the diff touches auth write paths).
- [ ] Stage close-out via playbook Phase E (sync-docs + changelog-sync fired; zero unchecked `[ ]` under the 9.5d line).
