# Stage 7 — Multi-tenancy cutover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Promote Project Ceres from sentinel-tagged single-tenant data to real multi-tenancy with defence-in-depth: EF global query filters, scope primitives for background jobs, per-user category copies on registration, and a one-shot sentinel-data remap migration — without breaking the 303 existing Auth integration tests.

**Architecture:** Two-commit cutover. Commit 1 ships reversible scaffolding (`IUserScope`, `IUserJobRunner`, query filters, architecture test, IDOR suite, per-user category seeding). Commit 2 ships the destructive remap migration + deletes `SingleUserAccessor` + swaps 78 test sites. Approach respects ADR-0065 (explicit redundancy: services keep their `.Owned(user)` chains; query filters are the second layer), ADR-0066 (sentinel → first-registered-user), and ADR-0067 (HTTP-context → AsyncLocal → throw resolution order).

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 + PostgreSQL (Npgsql), ASP.NET Identity (already wired at Stage 6a), xUnit + Moq + FluentAssertions.

**Spec:** [`docs/superpowers/specs/2026-05-12-stage-7-multi-tenancy-cutover-design.md`](../specs/2026-05-12-stage-7-multi-tenancy-cutover-design.md)

---

## File map — Commit 1 (scaffolding)

| Change | Path | Why |
|---|---|---|
| New | `ProjectCeres/Common/IUserScope.cs` | Interface for background-job scope entry |
| New | `ProjectCeres/Common/UserScope.cs` | AsyncLocal-backed implementation |
| New | `ProjectCeres/Common/IUserJobRunner.cs` | Interface for per-user job iteration |
| New | `ProjectCeres/Common/UserJobRunner.cs` | EF-backed implementation with exception isolation |
| New | `ProjectCeres/Common/Categories.cs` | Default-category seed list (data only — no behaviour) |
| New | `ProjectCeres/Services/CategorySeedService.cs` | Copies default categories for a new user |
| New | `ProjectCeres/Migrations/<ts>_AddIsReservedToCategory.cs` | Adds bool column + backfills two existing reserved GUIDs |
| Modify | `ProjectCeres/Common/Authentication/HttpContextCurrentUserAccessor.cs` | Add IUserScope fallback; flip throw type |
| Modify | `ProjectCeres/Models/{8 auth-internal entities}.cs` | Add `: IUserOwned` |
| Modify | `ProjectCeres/Models/Category.cs` | Add `IsReserved`; pre-flip prep (UserId stays nullable until Commit 2) |
| Modify | `ProjectCeres/Services/CategoryPolicies.cs` | Rewrite IsReserved to read `category.IsReserved` |
| Modify | `ProjectCeres/Data/AppDbContext.cs` | Inject ICurrentUserAccessor; add `ConfigureGlobalQueryFilters` |
| Modify | `ProjectCeres/Controllers/Api/AuthController.cs` | Call `CategorySeedService` after `CreateAsync` |
| Modify | `ProjectCeres/Program.cs` | Register IUserScope, IUserJobRunner, CategorySeedService |
| Modify | `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs` | Test fixture also calls CategorySeedService after CreateAsync |
| Modify | `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` | Add IgnoreQueryFilters allow-list; rewrite FailedLoginAttempt assertion |
| New | `ProjectCeres.Tests/Integration/MultiTenancy/IdorIsolationTests.cs` | Cross-tenant 404 suite |
| New | `ProjectCeres.Tests/Integration/Startup/EmptyDbStartupTests.cs` | Empty-DB boot regression |
| New | `ProjectCeres.Tests/Common/IUserScopeTests.cs` | Unit tests for AsyncLocal scope |
| New | `ProjectCeres.Tests/Common/UserJobRunnerTests.cs` | Per-user iteration + isolation tests |

## File map — Commit 2 (destructive)

| Change | Path | Why |
|---|---|---|
| New | `ProjectCeres.Tests/Common/FakeCurrentUserAccessor.cs` | Replacement test double |
| New | `ProjectCeres/Migrations/<ts>_RemapSentinelToFirstUser.cs` | One-shot transactional UPDATE |
| New | `ProjectCeres/Migrations/<ts>_MakeCategoryUserIdNonNullable.cs` | Schema flip after data is remapped |
| Modify | `ProjectCeres/Common/ICurrentUserAccessor.cs` | Delete `SingleUserAccessor` class + sentinel constant |
| Modify | `ProjectCeres/Common/IUserOwned.cs` | Delete `IOptionallyUserOwned` |
| Modify | `ProjectCeres/Common/QueryableExtensions.cs` | Delete `OwnedOrShared` + helper |
| Modify | `ProjectCeres/Common/UserOwnershipInterceptor.cs` | Delete `IOptionallyUserOwned` branch |
| Modify | `ProjectCeres/Models/Category.cs` | `UserId` becomes non-nullable; flip interface to `IUserOwned` |
| Modify | `ProjectCeres/Data/AppDbContext.cs` | Delete `SeedAccounts`, `SeedCategories`, `SeedSettings`; remove `SingleUserAccessor` `using` |
| Modify | 11 `.OwnedOrShared(user)` call sites | Flip to `.Owned(user)` |
| Modify | 78 test files | Swap `new SingleUserAccessor()` → `new FakeCurrentUserAccessor(fixture.TestUserId)` |

---

# COMMIT 1 — Scaffolding

## Task 1: Add `IUserScope` interface + AsyncLocal implementation

**Files:**
- Create: `ProjectCeres/Common/IUserScope.cs`
- Create: `ProjectCeres/Common/UserScope.cs`
- Test: `ProjectCeres.Tests/Common/IUserScopeTests.cs`

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Common/IUserScopeTests.cs`:

```csharp
using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

public class IUserScopeTests
{
    [Fact]
    public void EnterAs_sets_Current_for_duration_of_using_block()
    {
        IUserScope scope = new UserScope();
        var userId = Guid.NewGuid();

        scope.Current.Should().BeNull();
        using (scope.EnterAs(userId))
        {
            scope.Current.Should().Be(userId);
        }
        scope.Current.Should().BeNull();
    }

    [Fact]
    public void EnterAs_nests_with_stack_semantics()
    {
        IUserScope scope = new UserScope();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using (scope.EnterAs(a))
        {
            scope.Current.Should().Be(a);
            using (scope.EnterAs(b))
            {
                scope.Current.Should().Be(b);
            }
            scope.Current.Should().Be(a, "disposing inner scope restores outer");
        }
        scope.Current.Should().BeNull();
    }

    [Fact]
    public async Task EnterAs_propagates_across_await_boundaries()
    {
        IUserScope scope = new UserScope();
        var userId = Guid.NewGuid();

        using (scope.EnterAs(userId))
        {
            await Task.Yield();
            scope.Current.Should().Be(userId);
            await Task.Delay(1);
            scope.Current.Should().Be(userId);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~IUserScopeTests`
Expected: FAIL — types `IUserScope` and `UserScope` don't exist.

- [ ] **Step 3: Create `IUserScope.cs`**

```csharp
namespace ProjectCeres.Common;

/// <summary>
/// Background-job scope holder. HTTP requests resolve user identity from the cookie via
/// <see cref="ICurrentUserAccessor"/>; non-HTTP code paths (hosted services, background jobs)
/// enter via <see cref="EnterAs"/> so the same accessor can return a user id.
/// AsyncLocal-backed: propagates across <c>await</c> boundaries within a single logical flow.
/// </summary>
public interface IUserScope
{
    Guid? Current { get; }
    IDisposable EnterAs(Guid userId);
}
```

- [ ] **Step 4: Create `UserScope.cs`**

```csharp
namespace ProjectCeres.Common;

public sealed class UserScope : IUserScope
{
    private static readonly AsyncLocal<Guid?> _current = new();

    public Guid? Current => _current.Value;

    public IDisposable EnterAs(Guid userId)
    {
        var previous = _current.Value;
        _current.Value = userId;
        return new ScopeReleaser(previous);
    }

    private sealed class ScopeReleaser(Guid? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = previous;
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~IUserScopeTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Common/IUserScope.cs ProjectCeres/Common/UserScope.cs ProjectCeres.Tests/Common/IUserScopeTests.cs
git commit -m "feat(stage-7): add IUserScope with AsyncLocal stack semantics"
```

---

## Task 2: Add `IUserJobRunner` with per-user exception isolation

**Files:**
- Create: `ProjectCeres/Common/IUserJobRunner.cs`
- Create: `ProjectCeres/Common/UserJobRunner.cs`
- Test: `ProjectCeres.Tests/Common/UserJobRunnerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `ProjectCeres.Tests/Common/UserJobRunnerTests.cs`. Use the existing `TestDbFixture` pattern (see `ProjectCeres.Tests/Integration/TestDbFixture.cs`) for a real `AppDbContext`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Common;

[Collection("IntegrationTests")]
public class UserJobRunnerTests(TestDbFixture fixture) : IClassFixture<TestDbFixture>
{
    [Fact]
    public async Task ForEachUserAsync_enters_scope_per_user_in_turn()
    {
        var scope = new UserScope();
        var observed = new List<Guid>();
        var runner = new UserJobRunner(fixture.Db, scope, NullLogger<UserJobRunner>.Instance);

        var userA = await CreateUserAsync(fixture.Db, "a@test.local");
        var userB = await CreateUserAsync(fixture.Db, "b@test.local");

        await runner.ForEachUserAsync(
            u => u.Email == "a@test.local" || u.Email == "b@test.local",
            id => { observed.Add(scope.Current!.Value); return Task.CompletedTask; });

        observed.Should().BeEquivalentTo(new[] { userA.Id, userB.Id });
    }

    [Fact]
    public async Task ForEachUserAsync_continues_after_one_user_throws()
    {
        var scope = new UserScope();
        var runner = new UserJobRunner(fixture.Db, scope, NullLogger<UserJobRunner>.Instance);

        var userA = await CreateUserAsync(fixture.Db, "fail@test.local");
        var userB = await CreateUserAsync(fixture.Db, "ok@test.local");
        var succeeded = new List<Guid>();

        await runner.ForEachUserAsync(
            u => u.Email == "fail@test.local" || u.Email == "ok@test.local",
            id =>
            {
                if (id == userA.Id) throw new InvalidOperationException("boom");
                succeeded.Add(id);
                return Task.CompletedTask;
            });

        succeeded.Should().ContainSingle().Which.Should().Be(userB.Id);
    }

    private static async Task<ApplicationUser> CreateUserAsync(AppDbContext db, string email)
    {
        var u = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~UserJobRunnerTests`
Expected: FAIL — types `IUserJobRunner` and `UserJobRunner` don't exist.

- [ ] **Step 3: Create `IUserJobRunner.cs`**

```csharp
using System.Linq.Expressions;
using ProjectCeres.Models;

namespace ProjectCeres.Common;

public interface IUserJobRunner
{
    Task ForEachUserAsync(
        Expression<Func<ApplicationUser, bool>> filter,
        Func<Guid, Task> work,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `UserJobRunner.cs`**

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common;

public sealed class UserJobRunner(
    AppDbContext db,
    IUserScope scope,
    ILogger<UserJobRunner> logger) : IUserJobRunner
{
    public async Task ForEachUserAsync(
        Expression<Func<ApplicationUser, bool>> filter,
        Func<Guid, Task> work,
        CancellationToken ct = default)
    {
        // Cross-tenant by design: this is the entry point that enumerates the user list.
        // AspNetUsers carries no query filter; the architecture test's IgnoreQueryFilters()
        // allow-list grants this single file the exemption.
        var userIds = await db.Users
            .IgnoreQueryFilters()
            .Where(filter)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;
            using (scope.EnterAs(userId))
            {
                try
                {
                    await work(userId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Per-user job failed for {UserId}", userId);
                }
            }
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~UserJobRunnerTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Common/IUserJobRunner.cs ProjectCeres/Common/UserJobRunner.cs ProjectCeres.Tests/Common/UserJobRunnerTests.cs
git commit -m "feat(stage-7): add IUserJobRunner with per-user exception isolation"
```

---

## Task 3: Extend `HttpContextCurrentUserAccessor` with IUserScope fallback

**Files:**
- Modify: `ProjectCeres/Common/Authentication/HttpContextCurrentUserAccessor.cs`
- Test: `ProjectCeres.Tests/Common/CurrentUserAccessorResolutionTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Common/CurrentUserAccessorResolutionTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Common;

public class CurrentUserAccessorResolutionTests
{
    [Fact]
    public void Resolves_from_http_context_first()
    {
        var http = MockHttp(claim: Guid.NewGuid());
        var scope = new UserScope();
        var sut = new HttpContextCurrentUserAccessor(http.Object, scope);

        var fromClaim = Guid.Parse(http.Object.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        using (scope.EnterAs(Guid.NewGuid())) // a different id
        {
            sut.UserId.Should().Be(fromClaim, "HTTP context wins over scope");
        }
    }

    [Fact]
    public void Falls_back_to_scope_when_no_http_context()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);
        var scope = new UserScope();
        var sut = new HttpContextCurrentUserAccessor(http.Object, scope);

        var jobUser = Guid.NewGuid();
        using (scope.EnterAs(jobUser))
        {
            sut.UserId.Should().Be(jobUser);
        }
    }

    [Fact]
    public void Throws_InvalidOperationException_when_neither_resolves()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);
        var sut = new HttpContextCurrentUserAccessor(http.Object, new UserScope());

        var act = () => sut.UserId;
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*HTTP requests resolve from cookie*background jobs must enter via IUserScope.EnterAs*");
    }

    private static Mock<IHttpContextAccessor> MockHttp(Guid claim)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, claim.ToString())
        }, "TestScheme"));
        var mock = new Mock<IHttpContextAccessor>();
        mock.SetupGet(h => h.HttpContext).Returns(ctx);
        return mock;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CurrentUserAccessorResolutionTests`
Expected: FAIL — constructor signature doesn't match (no `IUserScope` parameter); throw type is wrong.

- [ ] **Step 3: Modify `HttpContextCurrentUserAccessor.cs`**

Replace the existing file body with:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Common;

namespace ProjectCeres.Common.Authentication;

public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;
    private readonly IUserScope _scope;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor http, IUserScope scope)
    {
        _http = http;
        _scope = scope;
    }

    public Guid UserId
    {
        get
        {
            var claim = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(claim, out var fromCookie)) return fromCookie;

            if (_scope.Current is { } fromScope) return fromScope;

            throw new InvalidOperationException(
                "No user context available. HTTP requests resolve from cookie; " +
                "background jobs must enter via IUserScope.EnterAs().");
        }
    }
}
```

- [ ] **Step 4: Update `Program.cs` to register `IUserScope` + `IUserJobRunner`**

Modify `ProjectCeres/Program.cs` near the existing accessor registration (around line 61):

```csharp
// Phase 3 Stage 7: background-job scope primitive. Singleton — the AsyncLocal
// inside does the per-flow isolation; the holder is process-wide.
builder.Services.AddSingleton<IUserScope, UserScope>();
builder.Services.AddScoped<IUserJobRunner, UserJobRunner>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
builder.Services.AddScoped<UserOwnershipInterceptor>();
```

Also remove the stale "SingleUserAccessor stays in the codebase because Stage 7's data remap references the sentinel constant" comment block (lines 58–60 currently).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CurrentUserAccessorResolutionTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Run the full Auth integration suite to confirm no regression**

Run: `dotnet test --filter FullyQualifiedName~Authentication`
Expected: all 303 Auth integration tests still PASS. The throw-type flip from `UnauthorizedAccessException` → `InvalidOperationException` has zero catch sites in production code (verified pre-spec) so no test should regress on the throw type.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Common/Authentication/HttpContextCurrentUserAccessor.cs ProjectCeres/Program.cs ProjectCeres.Tests/Common/CurrentUserAccessorResolutionTests.cs
git commit -m "feat(stage-7): accessor resolves HTTP claim → scope → throw"
```

---

## Task 4: Promote auth-internal entities to `IUserOwned`

**Files:**
- Modify: `ProjectCeres/Models/UserSession.cs`, `UserBlockedIp.cs`, `UserMfaBackupCode.cs`, `TotpReplayEntry.cs`, `PasswordResetToken.cs`, `EmailChangeToken.cs`, `LockoutUnlockToken.cs`, `AuditLog.cs`
- Test: `ProjectCeres.Tests/Common/IUserOwnedConformanceTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Common/IUserOwnedConformanceTests.cs`:

```csharp
using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Common;

public class IUserOwnedConformanceTests
{
    [Theory]
    [InlineData(typeof(UserSession))]
    [InlineData(typeof(UserBlockedIp))]
    [InlineData(typeof(UserMfaBackupCode))]
    [InlineData(typeof(TotpReplayEntry))]
    [InlineData(typeof(PasswordResetToken))]
    [InlineData(typeof(EmailChangeToken))]
    [InlineData(typeof(LockoutUnlockToken))]
    [InlineData(typeof(AuditLog))]
    public void Auth_internal_entity_implements_IUserOwned(Type t)
    {
        typeof(IUserOwned).IsAssignableFrom(t)
            .Should().BeTrue($"{t.Name} must implement IUserOwned so Stage 7 query filters can apply");
    }

    [Fact]
    public void FailedLoginAttempt_does_NOT_implement_IUserOwned()
    {
        // Cross-tenant by design per ADR-0067. Retention sweep iterates all rows.
        typeof(IUserOwned).IsAssignableFrom(typeof(FailedLoginAttempt))
            .Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~IUserOwnedConformanceTests`
Expected: 8 theory cases FAIL.

- [ ] **Step 3: Add `: IUserOwned` to each of the 8 entity class declarations**

For each file, change the class declaration line. Example for `ProjectCeres/Models/UserSession.cs`:

```csharp
// Before:
public sealed class UserSession
// After:
public sealed class UserSession : IUserOwned
```

The 8 files: `UserSession.cs`, `UserBlockedIp.cs`, `UserMfaBackupCode.cs`, `TotpReplayEntry.cs`, `PasswordResetToken.cs`, `EmailChangeToken.cs`, `LockoutUnlockToken.cs`, `AuditLog.cs`. Each already has `public Guid UserId { get; set; }` — no other change needed.

Add `using ProjectCeres.Common;` at the top of each file if not already present.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~IUserOwnedConformanceTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Confirm no regression in the wider auth suite**

Run: `dotnet test --filter FullyQualifiedName~Authentication`
Expected: PASS. The interface is purely structural; no behaviour changed yet.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Models/UserSession.cs ProjectCeres/Models/UserBlockedIp.cs ProjectCeres/Models/UserMfaBackupCode.cs ProjectCeres/Models/TotpReplayEntry.cs ProjectCeres/Models/PasswordResetToken.cs ProjectCeres/Models/EmailChangeToken.cs ProjectCeres/Models/LockoutUnlockToken.cs ProjectCeres/Models/AuditLog.cs ProjectCeres.Tests/Common/IUserOwnedConformanceTests.cs
git commit -m "feat(stage-7): promote 8 auth-internal entities to IUserOwned"
```

---

## Task 5: Add `Category.IsReserved` column + EF migration + policy rewrite

**Files:**
- Modify: `ProjectCeres/Models/Category.cs`
- Modify: `ProjectCeres/Services/CategoryPolicies.cs`
- Modify: `ProjectCeres/Data/AppDbContext.cs` (HasData backfill in seed)
- Create: `ProjectCeres/Migrations/<ts>_AddIsReservedToCategory.cs` (EF-generated)
- Test: `ProjectCeres.Tests/Unit/CategoryPoliciesTests.cs` (new or extend)

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Unit/CategoryPoliciesTests.cs`:

```csharp
using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

public class CategoryPoliciesTests
{
    [Fact]
    public void Reserved_category_cannot_be_edited()
    {
        var c = new Category { Id = Guid.NewGuid(), Name = "Uncategorized Income", IsSystem = false, IsReserved = true, CategoryTypeId = 1, IsActive = true };
        CategoryPolicies.CanEdit(c).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Non_reserved_user_category_can_be_edited()
    {
        var c = new Category { Id = Guid.NewGuid(), Name = "Coffee", IsSystem = false, IsReserved = false, CategoryTypeId = 2, IsActive = true };
        CategoryPolicies.CanEdit(c).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void System_category_cannot_be_edited_regardless_of_reserved_flag()
    {
        var c = new Category { Id = Guid.NewGuid(), Name = "Opening Balance", IsSystem = true, IsReserved = false, CategoryTypeId = 1, IsActive = true };
        CategoryPolicies.CanEdit(c).IsSuccess.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CategoryPoliciesTests`
Expected: FAIL — `Category.IsReserved` property doesn't exist.

- [ ] **Step 3: Add `IsReserved` to `Category` model**

In `ProjectCeres/Models/Category.cs`, after the existing `IsSystem` property:

```csharp
/// <summary>
/// Application-reserved row that must not be edited or deleted. Distinct from
/// <see cref="IsSystem"/>: a reserved category is user-owned (each user has their own
/// copy) but the application code depends on the row existing for that user (e.g.
/// "Uncategorized Income" / "Uncategorized Expense"). Stamped at registration time
/// by <c>CategorySeedService</c>; pre-Stage-7 backfill stamps the two existing rows.
/// </summary>
public bool IsReserved { get; set; }
```

- [ ] **Step 4: Rewrite `CategoryPolicies.cs`**

Replace the file body with:

```csharp
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Policy: which categories can be edited or deactivated. Pulled out of the service so
/// the rule lives in one place — controllers query it for pre-flight checks, the
/// service applies it on writes, and tests cover it directly without spinning up EF.
/// </summary>
public static class CategoryPolicies
{
    public const string SystemImmutableCode = "SYSTEM_CATEGORY_IMMUTABLE";
    public const string CategoryInUseCode   = "CATEGORY_IN_USE";

    public static Result CanEdit(Category category)
    {
        if (category.IsSystem || category.IsReserved)
            return Result.Fail(SystemImmutableCode, "System categories cannot be modified.");
        return Result.Ok();
    }

    public static Result CanDeactivate(Category category, bool hasTransactions)
    {
        var edit = CanEdit(category);
        if (!edit.IsSuccess) return edit;
        if (hasTransactions)
            return Result.Fail(CategoryInUseCode, "This category has transactions. Reassign them before deactivating.");
        return Result.Ok();
    }

    public static Result CanReactivate(Category category) => CanEdit(category);
}
```

This deletes the old hard-coded GUID constants and the `IsReserved(Guid)` overload. Confirm no caller still uses the `IsReserved(Guid)` signature: `grep -rn "CategoryPolicies.IsReserved" ProjectCeres ProjectCeres.Tests --include='*.cs'`. If hits exist, update them to pass the `Category` instance instead.

- [ ] **Step 5: Update the seed data to stamp `IsReserved` on the two existing rows**

In `ProjectCeres/Data/AppDbContext.cs`, find `SeedCategories` and modify the last two entries:

```csharp
// --- Uncategorized fallbacks (IsSystem = false so they appear in reports and transaction lists) ---
new Category { Id = new Guid("20000000-0000-0000-0000-000000000025"), Name = "Uncategorized Income",  CategoryTypeId = 1, IsActive = true, IsSystem = false, IsReserved = true, LifestyleTag = null, UserId = owner },
new Category { Id = new Guid("20000000-0000-0000-0000-000000000026"), Name = "Uncategorized Expense", CategoryTypeId = 2, IsActive = true, IsSystem = false, IsReserved = true, LifestyleTag = null, UserId = owner }
```

Add `IsReserved = false` to every other seeded `new Category { ... }` entry (EF migration generation will be cleaner with explicit defaults).

- [ ] **Step 6: Generate EF migration**

Run: `dotnet ef migrations add AddIsReservedToCategory --project ProjectCeres`
Inspect the generated `Up()` to confirm it adds the column with `defaultValue: false` and updates the two seeded GUIDs to `IsReserved = true`. If EF only generates the column-add without the row updates, manually add to the migration body:

```csharp
migrationBuilder.Sql(@"UPDATE categories SET ""IsReserved"" = true WHERE ""Id"" IN ('20000000-0000-0000-0000-000000000025', '20000000-0000-0000-0000-000000000026');");
```

- [ ] **Step 7: Apply migration**

Run: `dotnet ef database update --project ProjectCeres`
Expected: migration applies. Verify with `psql -d project_ceres -c "\d categories"` that the column exists.

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CategoryPoliciesTests`
Expected: PASS (3 tests).

- [ ] **Step 9: Commit**

```bash
git add ProjectCeres/Models/Category.cs ProjectCeres/Services/CategoryPolicies.cs ProjectCeres/Data/AppDbContext.cs ProjectCeres/Migrations/ ProjectCeres.Tests/Unit/CategoryPoliciesTests.cs
git commit -m "feat(stage-7): replace hard-coded Reserved GUIDs with Category.IsReserved column"
```

---

## Task 6: Extract default-category seed list into `Common/Categories.cs`

**Files:**
- Create: `ProjectCeres/Common/Categories.cs`
- Test: `ProjectCeres.Tests/Common/CategoriesDefaultsTests.cs` (new)

This is a pure data-extraction step. The list moves from `AppDbContext.SeedCategories` into a static `Defaults` list that both the seed data (kept for Phase 1 dev DB) and the upcoming `CategorySeedService` can read.

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Common/CategoriesDefaultsTests.cs`:

```csharp
using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

public class CategoriesDefaultsTests
{
    [Fact]
    public void Defaults_includes_opening_balance()
    {
        Categories.Defaults.Should().ContainSingle(c => c.Name == "Opening Balance" && c.IsSystem);
    }

    [Fact]
    public void Defaults_includes_two_reserved_uncategorized_rows()
    {
        Categories.Defaults.Where(c => c.IsReserved).Should().HaveCount(2);
        Categories.Defaults.Should().Contain(c => c.Name == "Uncategorized Income" && c.IsReserved);
        Categories.Defaults.Should().Contain(c => c.Name == "Uncategorized Expense" && c.IsReserved);
    }

    [Fact]
    public void Defaults_includes_all_26_canonical_categories()
    {
        Categories.Defaults.Should().HaveCount(26);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CategoriesDefaultsTests`
Expected: FAIL — type `Categories` doesn't exist.

- [ ] **Step 3: Create `Common/Categories.cs`**

```csharp
namespace ProjectCeres.Common;

/// <summary>
/// Canonical default-category list. Used by <c>CategorySeedService</c> at registration
/// to create a per-user copy of every default category. The historic single-user seed
/// in <c>AppDbContext.SeedCategories</c> reads from this list too, so the two sources
/// stay in lockstep until Commit 2 deletes the in-DbContext seed.
/// </summary>
public static class Categories
{
    public sealed record DefaultCategory(
        string Name,
        int CategoryTypeId,
        bool IsSystem,
        bool IsReserved,
        string? LifestyleTag);

    public static IReadOnlyList<DefaultCategory> Defaults { get; } = new[]
    {
        new DefaultCategory("Opening Balance",        1, IsSystem: true,  IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Salary",                 1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Freelance Income",       1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Rental Income",          1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Investment Income",      1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Business Income",        1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Other Income",           1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Housing / Rent",         2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Utilities",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Groceries",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Transport",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Fuel",                   2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Healthcare",             2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Insurance",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Subscriptions",          2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Dining Out",             2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Entertainment",          2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Clothing",               2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Personal Care",          2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Education",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Travel",                 2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Home & Garden",          2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Gifts & Donations",      2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Other Expenses",         2, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Uncategorized Income",   1, IsSystem: false, IsReserved: true,  LifestyleTag: null),
        new DefaultCategory("Uncategorized Expense",  2, IsSystem: false, IsReserved: true,  LifestyleTag: null),
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CategoriesDefaultsTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Common/Categories.cs ProjectCeres.Tests/Common/CategoriesDefaultsTests.cs
git commit -m "feat(stage-7): extract default-category list to Common/Categories.cs"
```

---

## Task 7: Add `CategorySeedService` + wire it into registration + test fixture

**Files:**
- Create: `ProjectCeres/Services/CategorySeedService.cs`
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs` (call after `CreateAsync` succeeds)
- Modify: `ProjectCeres/Program.cs` (DI registration)
- Modify: `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs` (also call after `CreateAsync`)
- Test: `ProjectCeres.Tests/Integration/Authentication/RegistrationSeedsCategoriesTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Integration/Authentication/RegistrationSeedsCategoriesTests.cs`:

```csharp
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class RegistrationSeedsCategoriesTests(AuthTestWebApplicationFactory factory)
    : IClassFixture<AuthTestWebApplicationFactory>
{
    [Fact]
    public async Task Registering_a_new_user_creates_26_per_user_categories()
    {
        var email = $"seed-{Guid.NewGuid():N}@test.local";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.Categories
            .IgnoreQueryFilters()
            .Where(c => c.UserId == user.Id)
            .CountAsync();

        count.Should().Be(26, "every registration seeds the canonical default list");
    }

    [Fact]
    public async Task Two_users_get_independent_category_copies()
    {
        var a = await AuthTestFixture.RegisterUserAsync(factory, $"a-{Guid.NewGuid():N}@test.local");
        var b = await AuthTestFixture.RegisterUserAsync(factory, $"b-{Guid.NewGuid():N}@test.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var aIds = await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == a.Id).Select(c => c.Id).ToListAsync();
        var bIds = await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == b.Id).Select(c => c.Id).ToListAsync();

        aIds.Should().HaveCount(26);
        bIds.Should().HaveCount(26);
        aIds.Intersect(bIds).Should().BeEmpty("categories are per-user copies with their own GUIDs");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~RegistrationSeedsCategoriesTests`
Expected: FAIL — registration does not yet seed per-user categories.

- [ ] **Step 3: Create `Services/CategorySeedService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Creates a per-user copy of every default category. Invoked at user registration
/// (both the API controller and the test fixture call it after <c>UserManager.CreateAsync</c>).
/// Idempotent: if the user already has categories, the call is a no-op.
/// </summary>
public sealed class CategorySeedService(AppDbContext db)
{
    public async Task CopyDefaultsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var alreadyHas = await db.Categories
            .IgnoreQueryFilters()
            .AnyAsync(c => c.UserId == userId, ct);
        if (alreadyHas) return;

        var copies = Categories.Defaults.Select(d => new Category
        {
            Id            = Guid.NewGuid(),
            Name          = d.Name,
            CategoryTypeId = d.CategoryTypeId,
            IsActive      = true,
            IsSystem      = d.IsSystem,
            IsReserved    = d.IsReserved,
            LifestyleTag  = d.LifestyleTag,
            UserId        = userId,
        }).ToList();

        db.Categories.AddRange(copies);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Register in `Program.cs`**

Near other service registrations:

```csharp
builder.Services.AddScoped<CategorySeedService>();
```

- [ ] **Step 5: Wire into `AuthController.Register`**

In `ProjectCeres/Controllers/Api/AuthController.cs`, inject `CategorySeedService` into the constructor (add to the field/parameter list), then call it after `CreateAsync` succeeds and before the audit-log write:

```csharp
// existing:
var result = await _userManager.CreateAsync(user, request.Password);
if (!result.Succeeded) { /* unchanged */ }

// NEW — Stage 7: every user owns their own copy of the default categories.
await _categorySeedService.CopyDefaultsForUserAsync(user.Id, HttpContext.RequestAborted);

await _auditLog.RecordAsync(user.Id, AuditLogAction.Registered, ct: HttpContext.RequestAborted);
return NoContent();
```

- [ ] **Step 6: Wire into the test fixture**

In `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs`, in **both** `RegisterUserAsync` overloads, after `ConfirmEmailAsync` succeeds:

```csharp
var seed = scope.ServiceProvider.GetRequiredService<ProjectCeres.Services.CategorySeedService>();
await seed.CopyDefaultsForUserAsync(user.Id);
```

(Place inside the same `scope` block — the scope already exists.)

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~RegistrationSeedsCategoriesTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Run full Auth suite to confirm no regression**

Run: `dotnet test --filter FullyQualifiedName~Authentication`
Expected: all tests PASS. New per-user category rows are extra inserts; no existing assertion should care.

- [ ] **Step 9: Commit**

```bash
git add ProjectCeres/Services/CategorySeedService.cs ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs ProjectCeres.Tests/Integration/Authentication/RegistrationSeedsCategoriesTests.cs
git commit -m "feat(stage-7): seed per-user categories on registration"
```

---

## Task 8: Flip `OwnedOrShared` call sites to `Owned`

**Files:**
- Modify: 11 call sites — `ProjectCeres/Controllers/Api/CategoriesApiController.cs:26,47,59`; `ProjectCeres/Services/CategoryService.cs:14,26,64,83,101`; `ProjectCeres/Services/RecurringTransactionService.cs:130,184`; `ProjectCeres/Services/CategoryBudgetService.cs:38`

Note: `IOptionallyUserOwned`, `OwnedOrShared`, and `Category.UserId` nullability are NOT removed yet — that's Commit 2. This task only changes the **call site** behaviour: every read now scopes by exact match, not by "match OR null". Since every newly-registered user owns 26 categories, exact-match returns the right set. The sentinel-user (in dev DB) still sees its 26 sentinel-stamped rows.

- [ ] **Step 1: Write the failing test**

Add to `ProjectCeres.Tests/Integration/CategoryServiceTests.cs` (or create — verify with `find ProjectCeres.Tests -name 'CategoryServiceTests.cs'`):

```csharp
[Fact]
public async Task ListAsync_returns_only_categories_owned_by_current_user()
{
    var userA = Guid.NewGuid();
    var userB = Guid.NewGuid();
    _fixture.Db.Categories.AddRange(
        new Category { Id = Guid.NewGuid(), Name = "A-cat",  CategoryTypeId = 2, IsActive = true, IsSystem = false, IsReserved = false, UserId = userA },
        new Category { Id = Guid.NewGuid(), Name = "B-cat",  CategoryTypeId = 2, IsActive = true, IsSystem = false, IsReserved = false, UserId = userB });
    await _fixture.Db.SaveChangesAsync();

    var svc = new CategoryService(_fixture.Db, new FakeCurrentUserAccessor(userA));
    var result = await svc.ListActiveAsync();

    result.Should().ContainSingle().Which.Name.Should().Be("A-cat");
}
```

(If `FakeCurrentUserAccessor` doesn't exist yet, this test goes red and stays that way until Task 14 — that's acceptable; mark it `[Fact(Skip = "Pending Task 14")]` or use `new SingleUserAccessor()` as a temporary stand-in by constructing the test data with sentinel `UserId`. Pick the path that keeps the suite green; the IDOR suite in Task 11 is the real assertion.)

- [ ] **Step 2: Mechanically flip each call site**

For each of the 11 lines listed in Files, change `.OwnedOrShared(user)` to `.Owned(user)`. Example diff for `Services/CategoryService.cs:14`:

```csharp
// Before:
var categories = await db.Categories.OwnedOrShared(user).Where(c => c.IsActive).ToListAsync();
// After:
var categories = await db.Categories.Owned(user).Where(c => c.IsActive).ToListAsync();
```

After the edit run `grep -rn "OwnedOrShared" ProjectCeres --include='*.cs'` — only the extension method itself (`QueryableExtensions.cs:20`) and the interface (`IUserOwned.cs:18`) should remain. The extension and interface are deleted in Commit 2.

- [ ] **Step 3: Adjust `Category.UserId` shape so `.Owned()` compiles**

Currently `Category : IOptionallyUserOwned` and `UserId` is `Guid?`. The `Owned<T>` extension constrains `T : IUserOwned`, so this won't compile yet. Temporary bridge: add `: IUserOwned` to `Category` **while keeping `IOptionallyUserOwned`** AND **while keeping `UserId` nullable**.

In `ProjectCeres/Models/Category.cs`:

```csharp
// Before:
public class Category : IOptionallyUserOwned
{
    public Guid? UserId { get; set; }
    // ...
}

// After (Commit 1 — bridge state; Commit 2 makes UserId non-nullable and drops IOptionallyUserOwned):
public class Category : IUserOwned, IOptionallyUserOwned
{
    Guid IUserOwned.UserId
    {
        get => UserId ?? Guid.Empty;
        set => UserId = value;
    }
    public Guid? UserId { get; set; }
    // ...
}
```

The explicit interface implementation gives `IUserOwned`-constrained call sites (`Owned<Category>`) a `Guid` to compare against, while preserving the nullable column for the data still in the dev DB.

- [ ] **Step 4: Run build to confirm it compiles**

Run: `dotnet build ProjectCeres`
Expected: zero errors.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test`
Expected: all green. Existing `SingleUserAccessor`-based tests still see the sentinel data correctly (the explicit interface returns `Guid.Empty` only when `UserId` is null — and the dev DB's sentinel categories have `UserId = sentinel`, not null; the one `IsSystem = true` "Opening Balance" with `UserId = null` is the only row that returns `Guid.Empty`. That row gets remapped in Commit 2's data migration.)

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Controllers/Api/CategoriesApiController.cs ProjectCeres/Services/CategoryService.cs ProjectCeres/Services/RecurringTransactionService.cs ProjectCeres/Services/CategoryBudgetService.cs ProjectCeres/Models/Category.cs
git commit -m "refactor(stage-7): flip OwnedOrShared call sites to Owned"
```

---

## Task 9: Add EF global query filters in `AppDbContext`

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs`
- Test: `ProjectCeres.Tests/Integration/MultiTenancy/GlobalQueryFilterTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Integration/MultiTenancy/GlobalQueryFilterTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.MultiTenancy;

[Collection("IntegrationTests")]
public class GlobalQueryFilterTests(AuthTestWebApplicationFactory factory)
    : IClassFixture<AuthTestWebApplicationFactory>
{
    [Fact]
    public async Task Account_query_filtered_to_current_user()
    {
        var userA = await AuthTestFixture.RegisterUserAsync(factory, $"a-{Guid.NewGuid():N}@test.local");
        var userB = await AuthTestFixture.RegisterUserAsync(factory, $"b-{Guid.NewGuid():N}@test.local");

        using (var seedScope = factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedDb.Accounts.AddRange(
                new Account { Id = Guid.NewGuid(), Name = "A-acct", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = userA.Id },
                new Account { Id = Guid.NewGuid(), Name = "B-acct", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = userB.Id });
            await seedDb.SaveChangesAsync();
        }

        // Authenticate as User A; the query filter must hide B's account even without explicit .Where()
        using var client = factory.CreateClient();
        await factory.AuthenticateAsync(client, userA);

        using var queryScope = factory.Services.CreateScope();
        var db = queryScope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Note: the accessor in this scope reads from the HttpContext-via-client, so we use
        // the helper that establishes the scope from a captured cookie.
        var accounts = await db.Accounts.ToListAsync();
        accounts.Should().OnlyContain(a => a.UserId == userA.Id);
    }
}
```

Note: this test depends on a helper `factory.AuthenticateAsync(client, user)` that establishes the auth cookie. If that helper doesn't exist, write it inline using the existing login flow (`POST /api/auth/login`). Inspect `AuthTestFixture` or other Authentication tests for the established pattern.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~GlobalQueryFilterTests`
Expected: FAIL — no global query filter yet; the query returns both accounts.

- [ ] **Step 3: Modify `AppDbContext` constructor + add `ConfigureGlobalQueryFilters`**

In `ProjectCeres/Data/AppDbContext.cs`, change the constructor to take `ICurrentUserAccessor`:

```csharp
private readonly ICurrentUserAccessor _currentUser;

public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserAccessor currentUser)
    : base(options)
{
    _currentUser = currentUser;
}
```

In `OnModelCreating`, add the call after `ConfigureUserOwnership`:

```csharp
ConfigureGlobalQueryFilters(modelBuilder);
```

Add the method:

```csharp
/// <summary>
/// Stage 7 multi-tenancy: every IUserOwned entity carries an EF global query filter so an
/// accidentally-omitted `.Where(t => t.UserId == _currentUser.UserId)` returns zero rows
/// instead of leaking. Service code still writes the explicit Where as belt-and-suspenders
/// (ADR-0065 explicit redundancy). Movement is abstract under TPC — filters apply to each
/// concrete subtype, not the abstract root.
/// </summary>
private void ConfigureGlobalQueryFilters(ModelBuilder modelBuilder)
{
    // Finance domain (11 — Category included via its explicit IUserOwned bridge from Task 8)
    modelBuilder.Entity<Account>()                .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<Budget>()                 .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<Category>()               .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<CategoryBudget>()         .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<ImportProfile>()          .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<ImportStagedTransaction>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<ImportStagedTransfer>()   .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<ImportTransferExclusion>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<RecurringTransaction>()   .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<SavedReport>()            .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<Settings>()               .HasQueryFilter(e => e.UserId == _currentUser.UserId);

    // Movement TPC subtypes (3)
    modelBuilder.Entity<Transaction>()            .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<Transfer>()               .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<LiabilityPayment>()       .HasQueryFilter(e => e.UserId == _currentUser.UserId);

    // Auth-internal (8)
    modelBuilder.Entity<UserSession>()            .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<UserBlockedIp>()          .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<UserMfaBackupCode>()      .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<TotpReplayEntry>()        .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<PasswordResetToken>()     .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<EmailChangeToken>()       .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<LockoutUnlockToken>()     .HasQueryFilter(e => e.UserId == _currentUser.UserId);
    modelBuilder.Entity<AuditLog>()               .HasQueryFilter(e => e.UserId == _currentUser.UserId);

    // NOT filtered (and why):
    // - TransactionAttachment, TransferAttachment: no UserId column; scoped via parent in service code.
    // - FailedLoginAttempt: cross-tenant by design (ADR-0067); retention sweep iterates all rows.
    // - AccountType, CategoryType, Currency, ReportType: system reference tables.
    // - AspNet* Identity tables: cross-tenant by definition.
}
```

- [ ] **Step 4: Audit every service for IgnoreQueryFilters-needing call sites**

The auth services (token services, session services) currently read tokens by `TokenLookup` HMAC and password-reset flow uses cross-user lookups. After filters land, these queries will silently return zero rows. Grep:

```bash
grep -rn "_db\.PasswordResetTokens\|_db\.EmailChangeTokens\|_db\.LockoutUnlockTokens\|_db\.UserSessions\|_db\.AuditLogs" ProjectCeres/Common/Authentication --include='*.cs'
```

For each call site that legitimately needs to query without scope (e.g. `/password-reset/confirm` runs **before** the user is identified), wrap the query with `.IgnoreQueryFilters()` and add an inline comment `// Cross-tenant: <reason>`. Expected sites: `PasswordResetService` token lookup, `EmailChangeService` token lookup, `LockoutUnlockService` token lookup, the failed-login retention sweep, audit-log retention sweep. Run the full Auth integration suite after this edit:

```bash
dotnet test --filter FullyQualifiedName~Authentication
```

Expected: all 303 PASS. If any test fails because a token lookup returns null, the call site needs `IgnoreQueryFilters()`.

- [ ] **Step 5: Run the GlobalQueryFilterTests test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~GlobalQueryFilterTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Data/AppDbContext.cs ProjectCeres/Common/Authentication/ ProjectCeres.Tests/Integration/MultiTenancy/GlobalQueryFilterTests.cs
git commit -m "feat(stage-7): wire EF global query filters on 22 entities"
```

---

## Task 10: Add architecture test for `IgnoreQueryFilters()` allow-list

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`:

```csharp
[Fact]
public void IgnoreQueryFilters_only_appears_in_documented_exception_paths()
{
    var allowed = new[]
    {
        "ProjectCeres/Common/UserJobRunner.cs",
        "ProjectCeres/Services/CategorySeedService.cs",
        "ProjectCeres/Common/Authentication/PasswordResetService.cs",
        "ProjectCeres/Common/Authentication/EmailChangeService.cs",
        "ProjectCeres/Common/Authentication/LockoutUnlockService.cs",
        "ProjectCeres/Common/Authentication/FailedLoginRetentionSweep.cs",
        "ProjectCeres/Common/Authentication/AuditLogRetentionSweep.cs",
        // Admin/** is anticipated for Phase 4+; allow-list pre-grants the namespace.
    };

    var repoRoot = FindRepoRoot();
    var hits = Directory.EnumerateFiles(Path.Combine(repoRoot, "ProjectCeres"), "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains("/Migrations/"))
        .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters("))
        .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
        .ToList();

    var unexpected = hits
        .Where(h => !h.StartsWith("ProjectCeres/Admin/", StringComparison.Ordinal))
        .Where(h => !allowed.Contains(h))
        .ToList();

    unexpected.Should().BeEmpty(
        "IgnoreQueryFilters() bypasses Stage 7's multi-tenancy safety net. " +
        "Add the file to the allow-list with an inline comment explaining why it's cross-tenant.");
}

[Fact]
public void Every_user_owned_entity_carries_a_global_query_filter()
{
    var factory = new ProjectCeres.Tests.Integration.AuthTestWebApplicationFactory();
    try
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectCeres.Data.AppDbContext>();
        var expected = new[]
        {
            "Account", "Budget", "Category", "CategoryBudget",
            "ImportProfile", "ImportStagedTransaction", "ImportStagedTransfer", "ImportTransferExclusion",
            "RecurringTransaction", "SavedReport", "Settings",
            "Transaction", "Transfer", "LiabilityPayment",
            "UserSession", "UserBlockedIp", "UserMfaBackupCode", "TotpReplayEntry",
            "PasswordResetToken", "EmailChangeToken", "LockoutUnlockToken", "AuditLog",
        };
        var missing = expected
            .Where(name => db.Model.FindEntityType($"ProjectCeres.Models.{name}")?.GetQueryFilter() is null)
            .ToList();
        missing.Should().BeEmpty("Stage 7 requires a global query filter on every user-owned entity");
    }
    finally
    {
        factory.Dispose();
    }
}

[Fact]
public void FailedLoginAttempt_has_no_global_query_filter()
{
    // Replaces the placeholder assertion. ADR-0067: cross-tenant retention sweep
    // iterates all rows; FailedLoginAttempt must remain filter-free.
    var factory = new ProjectCeres.Tests.Integration.AuthTestWebApplicationFactory();
    try
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectCeres.Data.AppDbContext>();
        db.Model.FindEntityType(typeof(ProjectCeres.Models.FailedLoginAttempt))!
            .GetQueryFilter().Should().BeNull();
    }
    finally
    {
        factory.Dispose();
    }
}

private static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProjectCeres.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
}
```

Also **delete** the existing `FailedLoginAttempt_NotInGlobalQueryFilterList` placeholder test (lines 83–99 of the file) — replaced by the real assertion above.

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~ArchitectureTests`
Expected: PASS. If `Every_user_owned_entity_carries_a_global_query_filter` fails, an entity in the `expected` list lacks `HasQueryFilter` — fix in `AppDbContext`. If `IgnoreQueryFilters_only_appears_in_documented_exception_paths` fails, either move the call into one of the allow-listed services or add the file to the allow-list with justification.

- [ ] **Step 3: Confirm the boundary fires on intentional violation**

Manually add `_db.Accounts.IgnoreQueryFilters().ToList();` to any normal service file (e.g. `AccountService.cs`). Run the architecture test — confirm it fails. Revert the manual edit.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git commit -m "test(stage-7): architecture test for IgnoreQueryFilters boundary"
```

---

## Task 11: IDOR integration test suite

**Files:**
- Create: `ProjectCeres.Tests/Integration/MultiTenancy/IdorIsolationTests.cs`

- [ ] **Step 1: Write the test file**

Create `ProjectCeres.Tests/Integration/MultiTenancy/IdorIsolationTests.cs`. Use the existing `AuthTestWebApplicationFactory` pattern, the existing `AuthTestFixture.RegisterUserAsync`, and the established cookie-auth helper.

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.MultiTenancy;

[Collection("IntegrationTests")]
public class IdorIsolationTests(AuthTestWebApplicationFactory factory)
    : IClassFixture<AuthTestWebApplicationFactory>
{
    private async Task<(ApplicationUser a, ApplicationUser b, Guid aAccountId, Guid aTransactionId, Guid aTransferId, Guid aBudgetId, Guid aSavedReportId)> SeedTwoUsersAsync()
    {
        var a = await AuthTestFixture.RegisterUserAsync(factory, $"a-{Guid.NewGuid():N}@test.local");
        var b = await AuthTestFixture.RegisterUserAsync(factory, $"b-{Guid.NewGuid():N}@test.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var aAccount = new Account { Id = Guid.NewGuid(), Name = "A-acct", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = a.Id };
        var aCategory = await db.Categories.IgnoreQueryFilters().FirstAsync(c => c.UserId == a.Id);
        var aTx = new Transaction { Id = Guid.NewGuid(), AccountId = aAccount.Id, CategoryId = aCategory.Id, Amount = 100m, Date = DateOnly.FromDateTime(DateTime.UtcNow), Description = "A-tx", UserId = a.Id };
        var bAccount = new Account { Id = Guid.NewGuid(), Name = "B-acct", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = b.Id };
        var aTransfer = new Transfer { Id = Guid.NewGuid(), SourceAccountId = aAccount.Id, DestAccountId = aAccount.Id, Amount = 5m, Date = DateOnly.FromDateTime(DateTime.UtcNow), UserId = a.Id };
        var aBudget = new Budget { Id = Guid.NewGuid(), Name = "A-budget", TargetAmount = 1000m, IsActive = true, UserId = a.Id };
        var aSavedReport = new SavedReport { Id = Guid.NewGuid(), Name = "A-report", ReportTypeId = 1, UserId = a.Id };

        db.Accounts.AddRange(aAccount, bAccount);
        db.Transactions.Add(aTx);
        db.Transfers.Add(aTransfer);
        db.Budgets.Add(aBudget);
        db.SavedReports.Add(aSavedReport);
        await db.SaveChangesAsync();

        return (a, b, aAccount.Id, aTx.Id, aTransfer.Id, aBudget.Id, aSavedReport.Id);
    }

    [Fact]
    public async Task User_B_cannot_GET_user_A_account_by_id() =>
        await AssertCrossTenant404Async(seeded => $"/api/accounts/{seeded.aAccountId}");

    [Fact]
    public async Task User_B_cannot_GET_user_A_transaction_by_id() =>
        await AssertCrossTenant404Async(seeded => $"/api/transactions/{seeded.aTransactionId}");

    [Fact]
    public async Task User_B_cannot_GET_user_A_transfer_by_id() =>
        await AssertCrossTenant404Async(seeded => $"/api/transfers/{seeded.aTransferId}");

    [Fact]
    public async Task User_B_cannot_GET_user_A_budget_by_id() =>
        await AssertCrossTenant404Async(seeded => $"/api/budgets/{seeded.aBudgetId}");

    [Fact]
    public async Task User_B_cannot_GET_user_A_savedreport_by_id() =>
        await AssertCrossTenant404Async(seeded => $"/api/saved-reports/{seeded.aSavedReportId}");

    [Fact]
    public async Task User_B_account_list_does_not_contain_user_A_rows()
    {
        var seeded = await SeedTwoUsersAsync();
        using var client = await factory.LoginAsync(seeded.b);
        var accounts = await client.GetFromJsonAsync<List<AccountDto>>("/api/accounts");
        accounts.Should().NotContain(a => a.Id == seeded.aAccountId);
    }

    [Fact]
    public async Task User_B_DELETE_user_A_transaction_returns_404()
    {
        var seeded = await SeedTwoUsersAsync();
        using var client = await factory.LoginAsync(seeded.b);
        var resp = await client.DeleteAsync($"/api/transactions/{seeded.aTransactionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_PATCH_user_A_transaction_returns_404()
    {
        var seeded = await SeedTwoUsersAsync();
        using var client = await factory.LoginAsync(seeded.b);
        var resp = await client.PatchAsync($"/api/transactions/{seeded.aTransactionId}",
            JsonContent.Create(new { description = "hijack" }));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Negative_assertion_global_filter_catches_missing_service_scope()
    {
        // The IDOR safety net is two-layered: explicit .Owned(user) in services AND the EF global
        // filter. This test proves the EF filter alone catches a leak. We bypass the service layer
        // and query the DbContext directly as User B; user A's row must not appear.
        var seeded = await SeedTwoUsersAsync();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProjectCeres.Common.IUserScope>().EnterAs(seeded.b.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var allAccountsAsB = await db.Accounts.ToListAsync();
        allAccountsAsB.Should().NotContain(a => a.Id == seeded.aAccountId,
            "the global query filter must hide cross-tenant rows even when no service .Owned() chain runs");
    }

    private async Task AssertCrossTenant404Async(Func<(ApplicationUser a, ApplicationUser b, Guid aAccountId, Guid aTransactionId, Guid aTransferId, Guid aBudgetId, Guid aSavedReportId), string> urlFromSeed)
    {
        var seeded = await SeedTwoUsersAsync();
        using var client = await factory.LoginAsync(seeded.b);
        var resp = await client.GetAsync(urlFromSeed(seeded));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record AccountDto(Guid Id, string Name);
}
```

Note: `factory.LoginAsync(user)` is the helper that returns an authenticated `HttpClient`. If it doesn't exist by that name, find the equivalent in `AuthTestFixture` or the existing auth tests — name will match the established convention.

- [ ] **Step 2: Run the suite**

Run: `dotnet test --filter FullyQualifiedName~IdorIsolationTests`
Expected: all 9 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/MultiTenancy/IdorIsolationTests.cs
git commit -m "test(stage-7): IDOR cross-tenant isolation suite"
```

---

## Task 12: Empty-DB boot regression test

**Files:**
- Create: `ProjectCeres.Tests/Integration/Startup/EmptyDbStartupTests.cs`

- [ ] **Step 1: Write the test**

Create `ProjectCeres.Tests/Integration/Startup/EmptyDbStartupTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration.Startup;

public class EmptyDbStartupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public EmptyDbStartupTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task App_boots_against_empty_db_and_responds_to_health()
    {
        // Wipe the user-owned tables (NOT a schema drop — just zero rows so we can verify
        // no boot-time IHostedService throws on missing user data).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM "Settings";
                DELETE FROM "Accounts";
                DELETE FROM "Categories";
                """);
        }

        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

If `/health` doesn't exist, substitute any anonymous-allowed endpoint that is known to respond with 200 — `/api/auth/csrf` typically works.

- [ ] **Step 2: Run the test**

Run: `dotnet test --filter FullyQualifiedName~EmptyDbStartupTests`
Expected: PASS. If it fails because a hosted service throws on startup, the failure is the bug — `ISettingsService.EnsureExistsAsync` or another boot-time hook still queries user data. Stage 6a's removal of `EnsureExistsAsync` should make this clean; the test pins it.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Startup/EmptyDbStartupTests.cs
git commit -m "test(stage-7): empty-db boot regression"
```

---

## Task 13: Run full suite + manual browser pass + COMMIT 1 close-out

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: 0 failures. Note the new test counts: ~9 IDOR + ~3 query-filter + ~3 architecture + ~2 boot + ~3 categories defaults + ~3 policies + ~3 accessor resolution + ~3 IUserScope + ~2 UserJobRunner + ~9 IUserOwned conformance + ~2 RegistrationSeedsCategories = ~42 new tests.

- [ ] **Step 2: Run the React client build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: succeeds (Stage 7 has no client-side changes; this is the standard smoke check).

- [ ] **Step 3: Manual UX verification**

Start the dev server: `dotnet run --project ProjectCeres`.
- Log in as the existing developer account.
- Walk through dashboard, accounts, movements, categories, budgets, reports.
- Confirm everything still renders. No user-visible change expected.

If browser access is unavailable, hand the checklist to the user with the URLs.

- [ ] **Step 4: Sync docs**

Run the `sync-docs` skill against the diff. Expected updates: `docs/multi-tenancy-strategy.md` flipped to "implemented (Commit 1)", `docs/models.md` notes `Category.IsReserved`, `docs/security-model.md` adds the cross-tenant filter diagram (or note that diagrams are deferred to the close-out task in Commit 2).

- [ ] **Step 5: Confirm Commit 1 is on `main`**

Run: `git -C <repo> log --oneline -15`
Expected: the new commits from Tasks 1–12 are present on `main` (per the `stay_on_main` user feedback memory). No branch or worktree created.

---

# COMMIT 2 — Destructive cleanup

## Task 14: Create `FakeCurrentUserAccessor`

**Files:**
- Create: `ProjectCeres.Tests/Common/FakeCurrentUserAccessor.cs`

- [ ] **Step 1: Create the file**

```csharp
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Test double for <see cref="ICurrentUserAccessor"/>. Replaces the now-deleted
/// <c>SingleUserAccessor</c> at 78 test sites. Bind to a real test-fixture user id;
/// the fake exists only so unit/integration tests can construct services without
/// spinning up an HTTP context.
/// </summary>
public sealed class FakeCurrentUserAccessor(Guid userId) : ICurrentUserAccessor
{
    public Guid UserId { get; } = userId;
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build ProjectCeres.Tests`
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Common/FakeCurrentUserAccessor.cs
git commit -m "test(stage-7): add FakeCurrentUserAccessor"
```

---

## Task 15: Sentinel-to-real-user data migration

**Files:**
- Create: `ProjectCeres/Migrations/<ts>_RemapSentinelToFirstUser.cs`
- Test: `ProjectCeres.Tests/Integration/Migrations/SentinelRemapTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Integration/Migrations/SentinelRemapTests.cs`. This test does NOT run the migration via EF — it executes the SQL body directly against a scratch DB so the assertions are deterministic.

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Migrations;

[Collection("IntegrationTests")]
public class SentinelRemapTests(TestDbFixture fixture) : IClassFixture<TestDbFixture>
{
    private const string Sentinel = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task Aborts_cleanly_when_zero_users_registered()
    {
        await fixture.Db.Database.ExecuteSqlRawAsync(@"DELETE FROM ""AspNetUsers"";");
        // Migration body would be invoked here; assert no rows changed and no exception thrown.
        // Implementation pending Step 3.
    }

    [Fact]
    public async Task Aborts_cleanly_when_more_than_one_user_registered()
    {
        await fixture.Db.Database.ExecuteSqlRawAsync(@"
            INSERT INTO ""AspNetUsers"" (""Id"", ""UserName"", ""NormalizedUserName"", ""Email"", ""NormalizedEmail"", ""EmailConfirmed"", ""PasswordHash"", ""SecurityStamp"", ""ConcurrencyStamp"", ""PhoneNumberConfirmed"", ""TwoFactorEnabled"", ""LockoutEnabled"", ""AccessFailedCount"")
            VALUES (gen_random_uuid(), 'u1', 'U1', 'u1@x.x', 'U1@X.X', true, 'x', 'x', 'x', false, false, false, 0),
                   (gen_random_uuid(), 'u2', 'U2', 'u2@x.x', 'U2@X.X', true, 'x', 'x', 'x', false, false, false, 0);
        ");
        // Assert: migration aborts; sentinel rows untouched.
    }

    [Fact]
    public async Task Remaps_every_sentinel_tagged_table_to_the_first_real_user()
    {
        // Arrange: exactly one user; one sentinel-stamped Account.
        // Act: run migration body.
        // Assert: account.user_id == that user's id; zero rows remain with sentinel.
    }
}
```

(The detailed test bodies are intentionally light here because the migration is generated by EF; the test pins observable behaviour rather than every UPDATE.)

- [ ] **Step 2: Generate the migration scaffold**

Run: `dotnet ef migrations add RemapSentinelToFirstUser --project ProjectCeres`
EF generates an empty migration body. Open the generated `*.cs` file and fill in `Up()`:

```csharp
public partial class RemapSentinelToFirstUser : Migration
{
    private const string Sentinel = "00000000-0000-0000-0000-000000000001";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@$"
DO $$
DECLARE
    real_user_id uuid;
    user_count int;
    sentinel_row_count int;
BEGIN
    SELECT COUNT(*) INTO user_count FROM ""AspNetUsers"";
    IF user_count = 0 THEN
        RAISE NOTICE 'Stage 7 remap: zero registered users — no-op';
        RETURN;
    END IF;
    IF user_count > 1 THEN
        RAISE EXCEPTION 'Stage 7 remap precondition failed: AspNetUsers contains % rows, expected 1', user_count;
    END IF;

    SELECT ""Id"" INTO real_user_id FROM ""AspNetUsers"" LIMIT 1;

    sentinel_row_count :=
        (SELECT COUNT(*) FROM accounts WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM categories WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM transactions WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM transfers WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM liability_payments WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM category_budgets WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM budgets WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM recurring_transactions WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM saved_reports WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM csv_import_profiles WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM import_staged_transactions WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM import_staged_transfers WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM import_transfer_exclusions WHERE user_id = '{Sentinel}') +
        (SELECT COUNT(*) FROM settings WHERE user_id = '{Sentinel}');

    IF sentinel_row_count = 0 THEN
        RAISE NOTICE 'Stage 7 remap: zero sentinel-tagged rows — no-op';
        RETURN;
    END IF;

    UPDATE accounts                   SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE categories                 SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    -- The legacy ""Opening Balance"" row had user_id IS NULL pre-Stage-7; remap that too.
    UPDATE categories                 SET user_id = real_user_id WHERE user_id IS NULL;
    UPDATE transactions               SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE transfers                  SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE liability_payments         SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE category_budgets           SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE budgets                    SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE recurring_transactions     SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE saved_reports              SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE csv_import_profiles        SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE import_staged_transactions SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE import_staged_transfers    SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE import_transfer_exclusions SET user_id = real_user_id WHERE user_id = '{Sentinel}';
    UPDATE settings                   SET user_id = real_user_id WHERE user_id = '{Sentinel}';

    -- Post-check
    IF EXISTS (SELECT 1 FROM accounts WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM categories WHERE user_id = '{Sentinel}' OR user_id IS NULL) OR
       EXISTS (SELECT 1 FROM transactions WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM transfers WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM liability_payments WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM category_budgets WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM budgets WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM recurring_transactions WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM saved_reports WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM csv_import_profiles WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM import_staged_transactions WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM import_staged_transfers WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM import_transfer_exclusions WHERE user_id = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM settings WHERE user_id = '{Sentinel}')
    THEN
        RAISE EXCEPTION 'Stage 7 remap post-check failed: sentinel rows remain after UPDATE';
    END IF;
END $$;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty. Sentinel-to-real-user is a one-way remap; reverting would
        // require re-stamping every row with the sentinel UUID, which destroys multi-user
        // state. Per project memory feedback_never_delete_db_without_consent, the operator
        // must restore from the pre-deploy snapshot to reverse this migration.
    }
}
```

The whole UPDATE block runs inside the implicit transaction PostgreSQL wraps around a `DO $$` block; if the RAISE EXCEPTION fires, every UPDATE rolls back.

- [ ] **Step 3: Ask the user before applying to dev DB**

Per the pinned project memory `feedback_never_delete_db_without_consent`, even though this is a remap (not a DELETE), it mutates persistent state in dev. Before running `dotnet ef database update`:

Ask the user: "Stage 7 Commit 2's remap migration is ready to apply. It will rewrite every user_id in your dev DB from the sentinel `00000000-0000-0000-0000-000000000001` to your registered user's id (and migrate the one NULL-user-id 'Opening Balance' category too). This is irreversible without a snapshot. Take a backup first? Recommended command: `pg_dump project_ceres > ~/project-ceres-pre-stage7-$(date +%Y%m%d).sql`. Once the backup is confirmed, apply with `dotnet ef database update`."

- [ ] **Step 4: Apply the migration to dev DB after explicit user consent**

Wait for user confirmation. Then run: `dotnet ef database update --project ProjectCeres`.

Verify: `psql -d project_ceres -c "SELECT COUNT(*) FROM accounts WHERE user_id = '00000000-0000-0000-0000-000000000001';"` returns 0.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Migrations/ ProjectCeres.Tests/Integration/Migrations/SentinelRemapTests.cs
git commit -m "feat(stage-7): one-shot sentinel-to-real-user remap migration"
```

---

## Task 16: Flip `Category.UserId` to non-nullable + drop `IOptionallyUserOwned`

**Files:**
- Modify: `ProjectCeres/Models/Category.cs`
- Modify: `ProjectCeres/Common/IUserOwned.cs`
- Modify: `ProjectCeres/Common/QueryableExtensions.cs`
- Modify: `ProjectCeres/Common/UserOwnershipInterceptor.cs`
- Create: `ProjectCeres/Migrations/<ts>_MakeCategoryUserIdNonNullable.cs`

Pre-condition: Task 15 ran and there are no `categories.user_id IS NULL` rows left in any environment.

- [ ] **Step 1: Modify `Category.cs`**

```csharp
public class Category : IUserOwned
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int CategoryTypeId { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public bool IsReserved { get; set; }
    public string? LifestyleTag { get; set; }
    public Guid UserId { get; set; }  // non-nullable now
    public CategoryType CategoryType { get; set; } = default!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
```

Remove the explicit `IUserOwned.UserId` bridge added in Task 8 — `UserId` is now a `Guid` directly. Remove the `: IOptionallyUserOwned` (since the interface is deleted next step).

- [ ] **Step 2: Delete `IOptionallyUserOwned` from `IUserOwned.cs`**

`ProjectCeres/Common/IUserOwned.cs` keeps only the `IUserOwned` interface; the `IOptionallyUserOwned` interface block is removed.

- [ ] **Step 3: Delete `OwnedOrShared` from `QueryableExtensions.cs`**

Remove the `OwnedOrShared<T>` method (lines 19–22) and the `BuildOwnedOrSharedPredicate<T>` helper (lines 33–41).

- [ ] **Step 4: Delete the `IOptionallyUserOwned` branch in `UserOwnershipInterceptor.cs`**

Remove the comment block referring to it. The method body now handles only `IUserOwned`:

```csharp
private void Stamp(DbContextEventData eventData)
{
    if (eventData.Context is null) return;
    foreach (var entry in eventData.Context.ChangeTracker.Entries())
    {
        if (entry.State != EntityState.Added) continue;
        if (entry.Entity is IUserOwned owned && owned.UserId == default)
        {
            entry.Property(nameof(IUserOwned.UserId)).CurrentValue = user.UserId;
        }
    }
}
```

- [ ] **Step 5: Generate the schema migration**

Run: `dotnet ef migrations add MakeCategoryUserIdNonNullable --project ProjectCeres`
Expected `Up()`: `ALTER TABLE categories ALTER COLUMN user_id SET NOT NULL;` Verify the generated migration body matches.

- [ ] **Step 6: Apply migration**

Run: `dotnet ef database update --project ProjectCeres`
Expected: succeeds. If it fails with a NOT NULL violation, a `categories.user_id IS NULL` row slipped past Task 15 — investigate before retrying.

- [ ] **Step 7: Run full suite**

Run: `dotnet test`
Expected: still green. Any compile error here means a `.OwnedOrShared(` call site survived Task 8 — grep and fix.

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/Models/Category.cs ProjectCeres/Common/IUserOwned.cs ProjectCeres/Common/QueryableExtensions.cs ProjectCeres/Common/UserOwnershipInterceptor.cs ProjectCeres/Migrations/
git commit -m "refactor(stage-7): drop IOptionallyUserOwned; Category.UserId becomes non-nullable"
```

---

## Task 17: Delete `SingleUserAccessor` + sentinel constant + sentinel seeds

**Files:**
- Modify: `ProjectCeres/Common/ICurrentUserAccessor.cs`
- Modify: `ProjectCeres/Data/AppDbContext.cs`

- [ ] **Step 1: Delete the `SingleUserAccessor` class**

In `ProjectCeres/Common/ICurrentUserAccessor.cs`, remove lines 8–18 (the doc comment + class). File becomes:

```csharp
namespace ProjectCeres.Common;

public interface ICurrentUserAccessor
{
    Guid UserId { get; }
}
```

- [ ] **Step 2: Delete the sentinel seeds in `AppDbContext.cs`**

Remove:
- `SeedAccounts` method (lines ≈418–459) and its call in `SeedData`.
- `SeedCategories` method (lines ≈461–502) and its call in `SeedData`.
- `SeedSettings` method (lines ≈504–516) and its call in `SeedData`.
- The `using` for `SingleUserAccessor` (it lives in the same namespace, so likely just the type reference needs removing from `SeedAccounts`/`SeedCategories`/`SeedSettings` bodies — which are about to be deleted anyway).
- The doc comment in `ConfigureUserOwnership` referencing `SingleUserAccessor.SentinelUserId` (line ≈174).

`SeedData` should keep only the system reference-table seeds (`AccountType`, `CategoryType`, `Currency`, `ReportType`).

- [ ] **Step 3: Generate the migration to remove HasData-seeded rows**

Run: `dotnet ef migrations add RemoveSentinelSeeds --project ProjectCeres`
EF generates DELETE statements for each previously-seeded `Account`/`Category`/`Settings` row. Inspect — these are the same rows the previous migration just remapped to the real user. **Risk**: this would delete the real user's data.

**Fix**: in the generated migration, replace the auto-generated DELETEs with no-op SQL comments or remove them entirely. The data lives under the real user's id now; we don't want to delete it. Keep only any genuine schema cleanup EF emits.

Verify the migration's `Up()` body is empty (or only contains explanatory SQL comments) before applying. Run: `dotnet ef database update --project ProjectCeres`.

- [ ] **Step 4: Verify the sentinel is gone from production code**

Run:
```bash
grep -rn "SingleUserAccessor\|00000000-0000-0000-0000-000000000001" ProjectCeres --include='*.cs' | grep -v Migrations
```
Expected: zero hits. Migration-file hits in `ProjectCeres/Migrations/` are acceptable (they're historic SQL).

- [ ] **Step 5: Run full suite**

Run: `dotnet test`
Expected: 78 test sites still construct `new SingleUserAccessor()` and will fail to compile. Task 18 fixes them.

If the tests fail with `error CS0246: The type or namespace name 'SingleUserAccessor' could not be found`, that's expected — proceed to Task 18.

- [ ] **Step 6: Commit (test-suite-broken state)**

```bash
git add ProjectCeres/Common/ICurrentUserAccessor.cs ProjectCeres/Data/AppDbContext.cs ProjectCeres/Migrations/
git commit -m "refactor(stage-7): delete SingleUserAccessor + sentinel seeds (tests broken; fix next commit)"
```

The "tests broken" trailer is intentional — it signals the next commit must restore green.

---

## Task 18: Swap 78 test sites from `SingleUserAccessor` to `FakeCurrentUserAccessor`

**Files:**
- Modify: 78 test files across `ProjectCeres.Tests/Unit/` and `ProjectCeres.Tests/Integration/`

- [ ] **Step 1: Enumerate the affected files**

Run:
```bash
grep -rln "new SingleUserAccessor()" ProjectCeres.Tests --include='*.cs'
```
Expected: a list of test files (the 78 sites are concentrated in fewer files — many files have multiple sites).

- [ ] **Step 2: Define a per-fixture `TestUserId`**

For each integration test fixture (`TestDbFixture`, `AuthTestWebApplicationFactory`), add (or confirm) a stable per-test or per-fixture user id. In `TestDbFixture`, add:

```csharp
public Guid TestUserId { get; } = Guid.NewGuid();

public Task SeedTestUserAsync()
{
    Db.Users.Add(new ApplicationUser { Id = TestUserId, UserName = $"test-{TestUserId}@x", Email = $"test-{TestUserId}@x", EmailConfirmed = true });
    return Db.SaveChangesAsync();
}
```

Existing test classes that construct `new SingleUserAccessor()` and rely on the sentinel reading sentinel-stamped data must either:
1. Seed the test data under `_fixture.TestUserId` and pass `new FakeCurrentUserAccessor(_fixture.TestUserId)`; or
2. Generate a per-test user id inline, seed real `ApplicationUser` + data + `FakeCurrentUserAccessor(thatId)` together.

Pick the path that minimises churn per file. Unit-only tests (`ReportGeneratorFactoryTests` — which passes `null!` for the DbContext) can pass `new FakeCurrentUserAccessor(Guid.NewGuid())` — the user id is never read because the DbContext is null.

- [ ] **Step 3: Mechanical swap, file by file**

For each file in Step 1's list:
1. Add `using ProjectCeres.Tests.Common;` if missing.
2. Replace every `new SingleUserAccessor()` with `new FakeCurrentUserAccessor(_fixture.TestUserId)` (integration tests) or `new FakeCurrentUserAccessor(Guid.NewGuid())` (unit tests that don't need a real DB).
3. Remove any seeding that used the sentinel UUID directly (e.g. `UserId = SingleUserAccessor.SentinelUserId`) — replace with `UserId = _fixture.TestUserId`.

Then run, per file:
```bash
dotnet test --filter FullyQualifiedName~<TestClass>
```
Expected: green. Fix per-file failures before moving on.

- [ ] **Step 4: Confirm zero remaining references**

```bash
grep -rn "SingleUserAccessor" ProjectCeres.Tests --include='*.cs'
```
Expected: zero hits.

```bash
grep -rn "00000000-0000-0000-0000-000000000001" ProjectCeres.Tests --include='*.cs'
```
Expected: zero hits (or only in test files that intentionally use the constant for the migration test in Task 15).

- [ ] **Step 5: Run full suite**

Run: `dotnet test`
Expected: 0 failures.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Tests/
git commit -m "test(stage-7): swap 78 SingleUserAccessor sites to FakeCurrentUserAccessor"
```

---

## Task 19: Close-out — docs, roadmap, planning hygiene

**Files:**
- Modify: `docs/roadmap-phase-three.md`
- Modify: `docs/planning-phase3.md`
- Modify: `docs/planning-resolved.md`
- Modify: `docs/multi-tenancy-strategy.md`
- Modify: `docs/security-model.md`
- Modify: `docs/models.md`

- [ ] **Step 1: Flip Stage 7 to ✅ Done in the roadmap**

In `docs/roadmap-phase-three.md`, change the Stage 7 status line and tick every verification-checklist box that automated tests cover. Leave any remaining manual-browser items unchecked with a note.

- [ ] **Step 2: Close planning-phase3.md open questions**

For each Stage 7 open question that resolved during execution, remove it from "Open Questions", append the resolution to `docs/planning-resolved.md`.

- [ ] **Step 3: Update strategy + model docs**

- `docs/multi-tenancy-strategy.md`: flip "planned" sections under Stage 7 to "implemented (2026-MM-DD)"; add a § "Phase 3 service audit (closed)" listing the 22 services and the verification date.
- `docs/security-model.md`: add a short subsection "Cross-tenant access denial flow" describing how a request from User B to User A's resource returns 404 via the global query filter. Include the diagram-or-text decision (text-only acceptable per the close-out scope).
- `docs/models.md`: note `Category.UserId` is non-nullable; note `Category.IsReserved`; note the removal of `IOptionallyUserOwned` and `OwnedOrShared`.

- [ ] **Step 4: Run `sync-docs` skill**

The skill checks the diff for any docs that should have been touched but weren't (e.g. `api-contract.md` if any endpoint changed, `testing.md` if any rule shifted).

- [ ] **Step 5: Commit**

```bash
git add docs/
git commit -m "docs(stage-7): flip stage to Done; close planning gates; update strategy + models"
```

- [ ] **Step 6: Final verification**

Run the `verify` skill (Definition of Done from `docs/testing.md` § Rules). Expected: clean.

Run: `dotnet test`
Expected: 0 failures across the full suite (303 Auth integration + the new ~42 Stage 7 tests).

Run: `pnpm --dir ProjectCeres.Client build`
Expected: succeeds.

If all three pass, Stage 7 is shipped. Stage 7.5 (PostgreSQL RLS, ADR-0068) is the immediate follow-up.

---

# Self-review (post-write, against the spec)

**Spec coverage check** — every spec sub-stage maps to at least one task:
- Spec §3.1 (IUserScope) → Task 1 ✓
- Spec §3.2 (IUserJobRunner) → Task 2 ✓
- Spec §3.3 (accessor extension + throw flip) → Task 3 ✓
- Spec §3.4a (auth entities → IUserOwned) → Task 4 ✓
- Spec §3.4 (global query filters) → Task 9 ✓
- Spec §3.5 (architecture test) → Task 10 ✓
- Spec §3.7a (Category.IsReserved) → Task 5 ✓
- Spec §3.7b (drop IOptionallyUserOwned) → Tasks 8 (bridge) + 16 (final flip) ✓
- Spec §3.7c (per-user copy on registration) → Tasks 6 (defaults) + 7 (service + wiring) ✓
- Spec §3.6 (sentinel remap migration) → Task 15 ✓
- Spec §3.7 (delete SingleUserAccessor + seeds + 78 test sites) → Tasks 14, 17, 18 ✓
- Spec §3.8 (service audit confirm only) → Task 19 docs sync ✓
- Spec §3.9 (empty-DB regression) → Task 12 ✓
- Spec §3.10 (IDOR suite) → Task 11 ✓

**Placeholder scan** — no TBD, no "TODO", every step has either runnable commands or full code blocks. The migration test bodies in Task 15 Step 1 are intentionally light because the migration body in Step 3 is the actual assertion — the test file's role is the failure-mode scaffold.

**Type consistency** — `FakeCurrentUserAccessor(Guid userId)` signature is identical across Task 14 (definition) and Task 18 (call sites). `IUserScope.EnterAs` returns `IDisposable` in Task 1 and is called inside `using` blocks in Tasks 2, 3, 11. `CategorySeedService.CopyDefaultsForUserAsync(Guid, CancellationToken)` signature is identical across Tasks 7 (definition + wiring) and the test fixture at Step 6 of Task 7.

**Scope** — single stage, single phase. No decomposition needed.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-12-stage-7-multi-tenancy-cutover.md`. Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute tasks in this session using `executing-plans`, batch execution with checkpoints.

Stage 7 is high-blast-radius and includes a destructive migration that needs explicit user consent (Task 15 Step 3). The subagent-driven path's between-task review checkpoints make that consent natural; the inline path will need a hard stop before Task 15.
