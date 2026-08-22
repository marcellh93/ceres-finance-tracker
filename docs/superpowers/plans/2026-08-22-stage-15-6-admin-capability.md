# Stage 15.6 — Admin Capability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Project Ceres an `Admin` role, a way to create the first admin from the command line, a way to promote an existing account, and the enforced `Admin/` namespace boundary that later admin features live inside.

**Architecture:** ASP.NET Identity roles are already registered (`Program.cs:114`, `AddIdentity<ApplicationUser, IdentityRole<Guid>>`) and `AspNetRoles` / `AspNetUserRoles` already exist in the schema — both empty, with no role check anywhere in the codebase. This stage populates and enforces them. The first admin is bootstrapped through the existing `SeedDevUser` CLI tool rather than by marking the first registered account, because that assumption is what produced the duplicate categories in `RemapSentinelToFirstUser`. `ADR-0065` already reserves `Admin/` as the only namespace permitted to call `IgnoreQueryFilters()`; the architecture test enforcing that already exists and gains an allow-list entry here.

**Tech Stack:** .NET 10, ASP.NET Core Identity, EF Core + Npgsql, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-08-22-shared-category-catalogue-design.md`

## Global Constraints

- Tests are written before the code they cover (`docs/testing.md` § Rules — binding).
- No `Co-Authored-By:` trailer in any commit message, ever.
- Commit straight to `main`. No branches, no worktrees.
- Role name is the exact string `Admin` — used by `[Authorize(Roles = "Admin")]`, by `SeedDevUser`, and by the promote endpoint. Declared once as `AppRoles.Admin`.
- `IgnoreQueryFilters()` may only appear under `ProjectCeres/Admin/`. The architecture test at `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` enforces this and must keep passing.
- Integration tests that create users must purge owned rows before deleting them — call `UserOwnedCleanup.PurgeUserAsync(db, userId)` before `UserManager.DeleteAsync`. See `ProjectCeres.Tests/Integration/UserOwnedCleanup.cs`.
- Test DB assertions filter by a marker unique to the test — the integration database is shared and sequential.

## File Structure

| File | Responsibility |
| --- | --- |
| `ProjectCeres/Common/AppRoles.cs` | **Create.** The single declaration of the `Admin` role name. |
| `ProjectCeres/Admin/README.md` | **Create.** States what may live in this namespace and why the boundary exists. |
| `ProjectCeres/Admin/AdminRoleService.cs` | **Create.** Ensures the role row exists; grants and revokes it; answers "does any admin exist?". |
| `ProjectCeres/Controllers/Api/AdminUsersApiController.cs` | **Create.** `POST /api/admin/users/{id}/promote`, admin-only. |
| `ProjectCeres/Tools/SeedDevUser.cs` | **Modify.** Add `--admin`; relax the environment gate for the no-admin-exists case. |
| `ProjectCeres/Program.cs` | **Modify.** Register `AdminRoleService`. |
| `ProjectCeres.Tests/Integration/Admin/AdminRoleServiceTests.cs` | **Create.** Role creation, grant, revoke, existence check. |
| `ProjectCeres.Tests/Integration/Admin/AdminAuthorizationTests.cs` | **Create.** Non-admin refused, admin allowed, anonymous refused. |
| `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` | **Modify.** Allow-list `Admin/` for `IgnoreQueryFilters()`. |
| `docs/roadmap-phase-three.md` | **Modify.** New Stage 15.6 between Stage 15.5 and Stage 16. |

---

### Task 1: The role name constant

**Files:**
- Create: `ProjectCeres/Common/AppRoles.cs`
- Test: `ProjectCeres.Tests/Integration/Admin/AdminRoleServiceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ProjectCeres.Common.AppRoles.Admin` — `const string` with value `"Admin"`.

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Integration/Admin/AdminRoleServiceTests.cs`:

```csharp
using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Integration.Admin;

public class AppRolesTests
{
    [Fact]
    public void Admin_role_name_is_the_exact_string_used_by_authorize_attributes()
    {
        AppRoles.Admin.Should().Be("Admin",
            "the value is duplicated in [Authorize(Roles = \"Admin\")] attributes, " +
            "which take a literal string and cannot reference the constant");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AppRolesTests"`
Expected: FAIL — build error, `AppRoles` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `ProjectCeres/Common/AppRoles.cs`:

```csharp
namespace ProjectCeres.Common;

/// <summary>
/// Application role names. Declared once here; `[Authorize(Roles = ...)]` takes a
/// literal string and cannot reference a constant, so the value is repeated in
/// attributes and pinned by AppRolesTests.
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AppRolesTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Common/AppRoles.cs ProjectCeres.Tests/Integration/Admin/AdminRoleServiceTests.cs
git commit -m "feat(15.6): declare the Admin role name in one place"
```

---

### Task 2: The Admin namespace boundary

**Files:**
- Create: `ProjectCeres/Admin/README.md`
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: the `ProjectCeres/Admin/` directory, allow-listed for `IgnoreQueryFilters()`.

The architecture test that scans for `IgnoreQueryFilters(` call sites already exists (`ScanIgnoreQueryFiltersCallSites`). This task adds `Admin/` to its allow-list so Task 3's service can query across users, and documents what the boundary means.

- [ ] **Step 1: Write the failing test**

Add to `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`:

```csharp
    [Fact]
    public void Admin_namespace_is_allow_listed_for_IgnoreQueryFilters()
    {
        // ADR-0065 reserves Admin/ as the only namespace permitted to bypass the
        // per-user query filter. Stage 15.6 is the first code to live there, so this
        // pins that a file under Admin/ is accepted while the boundary still holds
        // for every other namespace.
        var repoRoot = FindRepoRoot();
        var adminDir = Path.Combine(repoRoot, "ProjectCeres", "Admin");

        Directory.Exists(adminDir).Should().BeTrue(
            "ProjectCeres/Admin/ is the namespace ADR-0065 reserves for cross-user queries");

        File.Exists(Path.Combine(adminDir, "README.md")).Should().BeTrue(
            "the boundary needs a stated contract, not just a directory");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~Admin_namespace_is_allow_listed"`
Expected: FAIL — `Directory.Exists` returns false.

- [ ] **Step 3: Create the directory and its contract**

Create `ProjectCeres/Admin/README.md`:

```markdown
# Admin namespace

Code here may query across users. Everywhere else in `ProjectCeres/` must not.

`ADR-0065` reserves this namespace as the only place permitted to call
`IgnoreQueryFilters()`, and an architecture test
(`ArchitectureTests.IgnoreQueryFilters_only_in_Admin_namespace`) fails the build if
that call appears anywhere else.

**What belongs here:** platform-wide operations that are meaningless when scoped to
one user — role administration, the global category catalogue (Stage 15.7), platform
statistics, GDPR erasure.

**What does not:** anything a normal signed-in user triggers for their own data. If it
can be scoped with `.Owned(user)`, it belongs in `Services/`.

**Rules for code in here:**

1. Every cross-user query states its scope explicitly. Either `.Where(x => x.UserId ==
   targetUserId)` for a per-user admin action, or a comment saying why an unscoped
   aggregate is correct.
2. Every entry point is gated by `[Authorize(Roles = "Admin")]`. The attribute takes a
   literal string, so the value is repeated rather than referencing `AppRoles.Admin`;
   `AppRolesTests` pins the two in sync. Being in this namespace is not itself an
   authorization check.
3. Bypassing the filter is a deliberate act. If you are reaching for
   `IgnoreQueryFilters()` to make a test pass, the query is wrong.
```

- [ ] **Step 4: Update the allow-list in the existing scan test**

In `ArchitectureTests.cs`, find the test that asserts `IgnoreQueryFilters()` call sites are confined, and add `Admin/` to its permitted set. The existing allow-list is a `HashSet<string>` of repo-relative paths; add a prefix check alongside it:

```csharp
        // ADR-0065: anything under Admin/ may bypass the filter by design.
        var unexpected = hits
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Where(rel => !allowed.Contains(rel))
            .Where(rel => !rel.StartsWith("ProjectCeres/Admin/", StringComparison.Ordinal))
            .ToList();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ArchitectureTests"`
Expected: PASS — all architecture tests, including the pre-existing filter-confinement one.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Admin/README.md ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git commit -m "feat(15.6): establish the Admin namespace boundary"
```

---

### Task 3: AdminRoleService

**Files:**
- Create: `ProjectCeres/Admin/AdminRoleService.cs`
- Modify: `ProjectCeres/Program.cs`
- Test: `ProjectCeres.Tests/Integration/Admin/AdminRoleServiceTests.cs`

**Interfaces:**
- Consumes: `AppRoles.Admin` (Task 1).
- Produces:
  - `AdminRoleService.EnsureRoleExistsAsync(CancellationToken) : Task`
  - `AdminRoleService.GrantAsync(Guid userId, CancellationToken) : Task<bool>` — `false` if the user does not exist.
  - `AdminRoleService.RevokeAsync(Guid userId, CancellationToken) : Task<bool>` — `false` if the user does not exist or was not an admin.
  - `AdminRoleService.AnyAdminExistsAsync(CancellationToken) : Task<bool>`
  - `AdminRoleService.IsAdminAsync(Guid userId, CancellationToken) : Task<bool>`

- [ ] **Step 1: Write the failing tests**

Replace the contents of `ProjectCeres.Tests/Integration/Admin/AdminRoleServiceTests.cs`, keeping the `AppRolesTests` class from Task 1 and adding:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Admin;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.Admin;

[Collection("IntegrationTests")]
public class AdminRoleServiceTests : IAsyncLifetime
{
    private const string EmailSuffix = "@admin-role-test.local";
    private readonly AuthTestWebApplicationFactory _factory;

    public AdminRoleServiceTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task EnsureRoleExistsAsync_is_safe_to_call_twice()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
        var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await svc.EnsureRoleExistsAsync();
        await svc.EnsureRoleExistsAsync();

        (await rm.RoleExistsAsync(AppRoles.Admin)).Should().BeTrue();
    }

    [Fact]
    public async Task GrantAsync_makes_the_user_an_admin()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"grant{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        (await svc.IsAdminAsync(user.Id)).Should().BeFalse("a freshly registered user is not an admin");

        var granted = await svc.GrantAsync(user.Id);

        granted.Should().BeTrue();
        (await svc.IsAdminAsync(user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task GrantAsync_is_idempotent()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"twice{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        await svc.GrantAsync(user.Id);
        var second = await svc.GrantAsync(user.Id);

        second.Should().BeTrue("granting an existing admin is a no-op, not a failure");
        (await svc.IsAdminAsync(user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task GrantAsync_returns_false_for_an_unknown_user()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        (await svc.GrantAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_removes_the_role()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"revoke{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        await svc.GrantAsync(user.Id);
        var revoked = await svc.RevokeAsync(user.Id);

        revoked.Should().BeTrue();
        (await svc.IsAdminAsync(user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AnyAdminExistsAsync_is_true_once_a_user_is_granted()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"exists{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        await svc.GrantAsync(user.Id);

        (await svc.AnyAdminExistsAsync()).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AdminRoleServiceTests"`
Expected: FAIL — build error, `AdminRoleService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `ProjectCeres/Admin/AdminRoleService.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Admin;

/// <summary>
/// Grants, revokes and reports the Admin role. Lives under Admin/ because
/// AnyAdminExistsAsync asks a question about every user, which no user-scoped
/// service is permitted to do (ADR-0065).
/// </summary>
public class AdminRoleService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager)
{
    /// <summary>Creates the Admin role row if it is not already present.</summary>
    public async Task EnsureRoleExistsAsync(CancellationToken ct = default)
    {
        if (await roleManager.RoleExistsAsync(AppRoles.Admin)) return;
        await roleManager.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));
    }

    /// <summary>Grants Admin. Returns false when the user does not exist.</summary>
    public async Task<bool> GrantAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        await EnsureRoleExistsAsync(ct);
        if (await userManager.IsInRoleAsync(user, AppRoles.Admin)) return true;

        var result = await userManager.AddToRoleAsync(user, AppRoles.Admin);
        return result.Succeeded;
    }

    /// <summary>Revokes Admin. Returns false when the user does not exist.</summary>
    public async Task<bool> RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var result = await userManager.RemoveFromRoleAsync(user, AppRoles.Admin);
        return result.Succeeded;
    }

    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is not null && await userManager.IsInRoleAsync(user, AppRoles.Admin);
    }

    /// <summary>
    /// True when at least one account holds Admin. Used by the bootstrap tool to decide
    /// whether it may run outside Development.
    /// </summary>
    public async Task<bool> AnyAdminExistsAsync(CancellationToken ct = default)
    {
        if (!await roleManager.RoleExistsAsync(AppRoles.Admin)) return false;
        var admins = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
        return admins.Count > 0;
    }
}
```

- [ ] **Step 4: Register the service**

In `ProjectCeres/Program.cs`, alongside the other `builder.Services.AddScoped<...>()` registrations:

```csharp
builder.Services.AddScoped<ProjectCeres.Admin.AdminRoleService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AdminRoleServiceTests"`
Expected: PASS — 6 tests.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Admin/AdminRoleService.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Admin/AdminRoleServiceTests.cs
git commit -m "feat(15.6): AdminRoleService — grant, revoke, and report the Admin role"
```

---

### Task 4: Bootstrap the first admin from the CLI

**Files:**
- Modify: `ProjectCeres/Tools/SeedDevUser.cs`

**Interfaces:**
- Consumes: `AdminRoleService.GrantAsync`, `AdminRoleService.AnyAdminExistsAsync` (Task 3).
- Produces: `--admin` flag on the existing CLI tool.

The tool currently refuses to run outside Development. It gains one exception: creating an admin when no admin exists yet. That is the bootstrap case, and it closes as soon as the first admin is created.

- [ ] **Step 1: Add `--admin` to the parsed arguments**

In `SeedDevUser.cs`, extend the `Args` record and `ParseArgs`:

```csharp
    private sealed record Args(string Email, string? PlainPassword, bool GeneratePassword, bool Admin);
```

In `ParseArgs`, add a local `bool admin = false;` beside the existing locals, add the case:

```csharp
                case "--admin":
                    admin = true;
                    break;
```

and include it in the returned record.

- [ ] **Step 2: Relax the environment gate**

Replace the environment gate block with:

```csharp
        // === Environment gate ===
        // Development is always allowed. Outside Development the only permitted use is
        // creating the FIRST admin — the bootstrap case, which closes as soon as one
        // exists. Shell access to the server is the practical safeguard.
        var isDevelopment = builder.Environment.IsDevelopment();
        if (!isDevelopment && !parsed.Admin)
        {
            Console.Error.WriteLine(
                "ERROR: this tool is gated on Development unless --admin is used to create " +
                "the first admin account. ASPNETCORE_ENVIRONMENT is not 'Development'. Aborting.");
            return 2;
        }
```

Then, after `using var scope = app.Services.CreateScope();` and the service resolutions, add the second half of the gate:

```csharp
        var adminRoles = sp.GetRequiredService<ProjectCeres.Admin.AdminRoleService>();

        if (!isDevelopment && parsed.Admin && await adminRoles.AnyAdminExistsAsync())
        {
            Console.Error.WriteLine(
                "ERROR: an admin account already exists. Outside Development this tool may " +
                "only create the FIRST admin; promote further admins through the app. Aborting.");
            return 2;
        }
```

- [ ] **Step 3: Grant the role after the user is created**

At the end of the existing user-creation path, after `userId` is known and categories are seeded:

```csharp
        if (parsed.Admin)
        {
            var granted = await adminRoles.GrantAsync(userId);
            if (!granted)
            {
                Console.Error.WriteLine("[SeedDevUser] ERROR: could not grant the Admin role.");
                return 1;
            }
            Console.WriteLine($"[SeedDevUser] Granted Admin to {parsed.Email}.");
        }
```

- [ ] **Step 4: Update the usage text**

In `PrintUsage()`, add `--admin` with a one-line description: *"grant the Admin role to this account; permitted outside Development only when no admin exists yet."*

- [ ] **Step 5: Verify by hand**

The tool builds a full application host, so it is exercised manually rather than by integration test.

```bash
dotnet run --project ProjectCeres -- --seed-dev-user --email admin@ceres.local --generate-password --admin
```

Expected: account created, generated password printed, `Granted Admin to admin@ceres.local.` Then confirm the role landed:

```bash
psql -d project_ceres -tAc "SELECT u.\"Email\", r.\"Name\" FROM \"AspNetUsers\" u JOIN \"AspNetUserRoles\" ur ON ur.\"UserId\"=u.\"Id\" JOIN \"AspNetRoles\" r ON r.\"Id\"=ur.\"RoleId\";"
```

Expected: one row pairing the email with `Admin`.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Tools/SeedDevUser.cs
git commit -m "feat(15.6): --admin flag bootstraps the first admin account"
```

---

### Task 5: Promote an existing account

**Files:**
- Create: `ProjectCeres/Controllers/Api/AdminUsersApiController.cs`
- Test: `ProjectCeres.Tests/Integration/Admin/AdminAuthorizationTests.cs`

**Interfaces:**
- Consumes: `AdminRoleService.GrantAsync`, `AdminRoleService.RevokeAsync` (Task 3); `AppRoles.Admin` (Task 1).
- Produces: `POST /api/admin/users/{id:guid}/promote` and `POST /api/admin/users/{id:guid}/demote`, both admin-only.

- [ ] **Step 1: Write the failing tests**

Create `ProjectCeres.Tests/Integration/Admin/AdminAuthorizationTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Admin;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.Admin;

[Collection("IntegrationTests")]
public class AdminAuthorizationTests : IAsyncLifetime
{
    private const string EmailSuffix = "@admin-authz-test.local";
    private readonly AuthTestWebApplicationFactory _factory;

    public AdminAuthorizationTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    private async Task<(HttpClient Client, string Session, ApplicationUser User)> SignedInAsync(string email)
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var session = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        return (client, session, user);
    }

    private static HttpRequestMessage Promote(Guid targetId, string session, string csrfCookie, string csrfHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetId}/promote");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={session}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return req;
    }

    [Fact]
    public async Task Anonymous_is_refused()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/promote");
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_non_admin_is_refused()
    {
        var (client, session, caller) = await SignedInAsync($"plain{EmailSuffix}");
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);

        var resp = await client.SendAsync(Promote(Guid.NewGuid(), session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "holding a session is not the same as holding the Admin role");
    }

    [Fact]
    public async Task An_admin_can_promote_another_account()
    {
        var (client, session, caller) = await SignedInAsync($"caller{EmailSuffix}");
        var target = await AuthTestFixture.RegisterUserAsync(_factory, $"target{EmailSuffix}");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var resp = await client.SendAsync(Promote(target.Id, session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            (await svc.IsAdminAsync(target.Id)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Promoting_an_unknown_account_is_a_404()
    {
        var (client, session, caller) = await SignedInAsync($"unknown{EmailSuffix}");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var resp = await client.SendAsync(Promote(Guid.NewGuid(), session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AdminAuthorizationTests"`
Expected: FAIL — the route does not exist, so the admin case returns 404 where 204 is expected.

- [ ] **Step 3: Write the controller**

Create `ProjectCeres/Controllers/Api/AdminUsersApiController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Admin;

namespace ProjectCeres.Controllers.Api;

/// <summary>
/// Admin-only account administration. Promotion is how a second admin is created;
/// the first one is bootstrapped from the command line (see Tools/SeedDevUser.cs),
/// because there is no admin session available to authorize that first grant.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersApiController(AdminRoleService adminRoles) : ControllerBase
{
    [HttpPost("{id:guid}/promote")]
    public async Task<IActionResult> Promote(Guid id)
    {
        var granted = await adminRoles.GrantAsync(id, HttpContext.RequestAborted);
        return granted ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/demote")]
    public async Task<IActionResult> Demote(Guid id)
    {
        var revoked = await adminRoles.RevokeAsync(id, HttpContext.RequestAborted);
        return revoked ? NoContent() : NotFound();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AdminAuthorizationTests"`
Expected: PASS — 4 tests.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj`
Expected: PASS, no regressions. Confirm the `IgnoreQueryFilters` architecture test still passes.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Controllers/Api/AdminUsersApiController.cs ProjectCeres.Tests/Integration/Admin/AdminAuthorizationTests.cs
git commit -m "feat(15.6): promote and demote endpoints, admin-only"
```

---

### Task 6: Roadmap entry

**Files:**
- Modify: `docs/roadmap-phase-three.md`

**Interfaces:**
- Consumes: everything above.
- Produces: Stage 15.6 with a verification checklist.

- [ ] **Step 1: Insert the stage**

Add between `## Stage 15.5 — Onboarding wizard (Batch 5)` and `## Stage 16 — Hosting + ops (Batch 5)`:

```markdown
## Stage 15.6 — Admin capability (Batch 5)

**Status: ❌ Pending.** First of three stages delivering the shared category catalogue. Stages 15.7 and 15.8 both depend on the role and the namespace boundary established here.

> **Goal:** an account can hold an `Admin` role; admin-only surfaces are enforced; the `Admin/` namespace that `ADR-0065` reserves for cross-user queries exists and is guarded by an architecture test.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 15.6.1 | `AppRoles.Admin` — one declaration of the role name | `docs/superpowers/specs/2026-08-22-shared-category-catalogue-design.md` § Access control |
| 15.6.2 | `ProjectCeres/Admin/` namespace + README contract; allow-listed in the `IgnoreQueryFilters` architecture test | [ADR-0065](decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md) |
| 15.6.3 | `AdminRoleService` — grant, revoke, is-admin, any-admin-exists | (spec, above) |
| 15.6.4 | `--admin` flag on the seed tool; gate relaxed to "Development, or first admin" | (spec, above) |
| 15.6.5 | `POST /api/admin/users/{id}/promote` and `/demote`, admin-only | (spec, above) |

### Verification checklist

- [ ] `AppRoles.Admin` is the only declaration of the role name
- [ ] `ProjectCeres/Admin/README.md` states what may live in the namespace
- [ ] The `IgnoreQueryFilters` architecture test allow-lists `Admin/` and still fails for any other namespace
- [ ] `AdminRoleService.GrantAsync` is idempotent and returns false for an unknown user
- [ ] `--admin` creates the first admin outside Development, and refuses once one exists
- [ ] Promote/demote return 401 anonymous, 403 for a signed-in non-admin, 204 for an admin
- [ ] Promoting an unknown account returns 404
- [ ] Full `dotnet test` green

### Out of scope

Email invitations for accounts that do not exist yet; admin dashboards; user management beyond promotion and demotion; usage statistics. See the spec's Out of scope section.
```

- [ ] **Step 2: Correct the spec's ordering note**

The spec says the three stages land *before* Stage 15.5. They now land after it. In `docs/superpowers/specs/2026-08-22-shared-category-catalogue-design.md`, replace the sentence beginning *"Landing before Stage 15.5"* with:

```markdown
Landing as Stages 15.6–15.8, after the onboarding wizard. Onboarding does not list
categories — its steps are preferences, accounts and opening balance — so it is
unaffected by the catalogue change beyond using the global "Opening Balance" row,
which survives the conversion unchanged.
```

- [ ] **Step 3: Commit**

```bash
git add docs/roadmap-phase-three.md docs/superpowers/specs/2026-08-22-shared-category-catalogue-design.md
git commit -m "docs(15.6): roadmap entry for the admin capability stage"
```

---

## What this stage deliberately does not do

- **No admin UI.** Promotion is an API call. The screen arrives with Stage 15.8, where there is something for an admin to manage.
- **No email invitations.** Promotion assumes the target already has an account.
- **No `Admin/` services beyond role management.** The catalogue service lands in Stage 15.7.
- **No change to the per-user query filter or to RLS.** That is Stage 15.7's highest-risk work and is kept out of this stage entirely.
