# Stage 12.10 — Session Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the active-sessions list from showing dead/duplicate sessions and stop `UserSession` rows piling up — via a read-time expiry filter, same-device login dedup, and a cron-invoked retention sweep.

**Architecture:** Three independent parts sharing one set of lifetime constants. (A) `SessionService.GetActiveAsync` gains an expiry predicate. (B) `AuthController.IssueSessionAndCookiesAsync` revokes any live same-device (UserAgent+IP) ephemeral session before inserting the new row. (C) A `--sweep-sessions` CLI one-shot flat-DELETEs rows past the 90-day retention horizon through `AdminDbContext` (BYPASSRLS), mirroring `--seed-dev-user`.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core + PostgreSQL (RLS), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-08-29-stage-12-10-session-lifecycle-design.md` — read it alongside this plan.

## Global Constraints

- **Commit straight to `main`.** No branches, no worktrees. **No `Co-Authored-By` trailer**, ever.
- **No schema migration.** `IsPersistent`, `LastUsedAt`, `RevokedAt` all already exist on `UserSession`. Nothing in this plan adds a column.
- **Lifetime values are the single source of truth in `SessionConstants`** (Task 0): `EphemeralSlidingWindow = 30 min`, `PersistentLifetime = 30 days`, `RetentionHorizon = 90 days`. The cookie config, the filter, the dedup, and the sweep all consume these — no inline literals.
- **The sweep is a FLAT cross-tenant DELETE via `AdminDbContext` (ceres_admin, BYPASSRLS)** — never `AppDbContext`/`ceres_app` (RLS would scope it to one user and delete nothing), and never `IUserJobRunner`/`BackgroundJobScope` (those are per-user fan-out). The sweep type carries `[RequiresAdminContext]`; any `IgnoreQueryFilters()` call site lands on the `ArchitectureTests` allow-list in the same commit.
- **Every DB read assertion in a test filters by a unique-to-this-test marker** (the test DB is shared and sequential — `feedback_filter_test_queries_by_test_data`). Session tests key on a distinctive `IpCreatedAt` suffix (see `SessionsApiTests`, `.EndsWith(".sessions-api-test")`).
- **The three test classes derive from `AppRoleTestBase`** (`ProjectCeres.Tests/Integration/AppRole/`), which exposes `Factory` (a `DualContextWebApplicationFactory`). `Factory.NewAdminContext()` returns an `AdminContextHandle` with a `.Context` (BYPASSRLS `AdminDbContext`) — the seam for seeding `UserSession` rows and reading them back cross-tenant. `FakeCurrentUserAccessor(Guid userId)` (`ProjectCeres.Tests/Common/`) binds a `SessionService` to a user. Register via the HTTP endpoint (`AuthTestFixture.PostJsonWithCsrfAsync`, `AuthTestFixture.ValidPassword`), never a direct app-context insert (it 42501s with no user scope).
- **Never skip/weaken a test to make it pass.** A failing test → fix production, or name the case (`docs/testing.md` § Rules).

---

## File structure

**New files:**
- `ProjectCeres/Tools/SweepSessions.cs` — the retention-sweep tool (`RunAsync(WebApplicationBuilder) → Task<int>`), mirrors `Tools/SeedDevUser.cs`.
- `ProjectCeres.Tests/Integration/Authentication/SessionExpiryFilterTests.cs` — Part A.
- `ProjectCeres.Tests/Integration/Authentication/SessionLoginDedupTests.cs` — Part B.
- `ProjectCeres.Tests/Integration/Authentication/SessionRetentionSweepTests.cs` — Part C.

**Modified files:**
- `ProjectCeres/Common/Authentication/SessionConstants.cs` — add the three `TimeSpan` constants.
- `ProjectCeres/Program.cs` — cookie `ExpireTimeSpan` consumes the constant; add the `--sweep-sessions` dispatch branch.
- `ProjectCeres/Controllers/Api/AuthController.cs` — persistent-cookie `Expires` consumes the constant; `IssueSessionAndCookiesAsync` revokes the same-device duplicate.
- `ProjectCeres/Services/SessionService.cs` — `GetActiveAsync` expiry predicate.
- `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — IgnoreQueryFilters allow-list (only if the sweep uses `IgnoreQueryFilters()`).

---

### Task 0: Shared lifetime constants in `SessionConstants`

**Files:**
- Modify: `ProjectCeres/Common/Authentication/SessionConstants.cs`
- Modify: `ProjectCeres/Program.cs` (cookie `ExpireTimeSpan`), `ProjectCeres/Controllers/Api/AuthController.cs` (persistent `Expires`)
- Test: none of its own (a pure refactor; existing session tests are the safety net — the values are unchanged)

**Interfaces:**
- Produces: `SessionConstants.EphemeralSlidingWindow` (`TimeSpan`, 30 min), `SessionConstants.PersistentLifetime` (`TimeSpan`, 30 days), `SessionConstants.RetentionHorizon` (`TimeSpan`, 90 days).

- [ ] **Step 1: Add the constants** to `SessionConstants` (after the existing string consts):

```csharp
/// <summary>Ephemeral (non-"remember me") sliding-cookie window. Mirrors
/// CookieAuthenticationOptions.ExpireTimeSpan; a session with no activity past
/// this is effectively dead. Single source of truth for cookie config + the
/// active-sessions expiry filter.</summary>
public static readonly TimeSpan EphemeralSlidingWindow = TimeSpan.FromMinutes(30);

/// <summary>Persistent "remember me" cookie lifetime. Mirrors the persistent
/// cookie's Expires (+30 days).</summary>
public static readonly TimeSpan PersistentLifetime = TimeSpan.FromDays(30);

/// <summary>How long a dead session row is retained before the sweep deletes it.
/// Fixed at 90 days by security-model.md § Retention Policy (revoked UserSession
/// rows + User-Agent strings).</summary>
public static readonly TimeSpan RetentionHorizon = TimeSpan.FromDays(90);
```

- [ ] **Step 2: Point the cookie config at the constant.** In `Program.cs`, replace `options.ExpireTimeSpan = TimeSpan.FromMinutes(30);` with `options.ExpireTimeSpan = SessionConstants.EphemeralSlidingWindow;` (add the `using ProjectCeres.Common.Authentication;` if not present).

- [ ] **Step 3: Point the persistent-cookie `Expires` at the constant.** In `AuthController.IssueSessionAndCookiesAsync`, replace `Expires = DateTimeOffset.UtcNow.AddDays(30),` with `Expires = DateTimeOffset.UtcNow.Add(SessionConstants.PersistentLifetime),`.

- [ ] **Step 4: Build.** Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -c "error CS"` → expect 0.

- [ ] **Step 5: Commit.**

```bash
git add ProjectCeres/Common/Authentication/SessionConstants.cs ProjectCeres/Program.cs ProjectCeres/Controllers/Api/AuthController.cs
git commit -m "refactor(12.10): session lifetime durations into SessionConstants"
```

---

### Task A: `GetActiveAsync` expiry filter

**Files:**
- Modify: `ProjectCeres/Services/SessionService.cs`
- Test: `ProjectCeres.Tests/Integration/Authentication/SessionExpiryFilterTests.cs` (create)

**Interfaces:**
- Consumes: `SessionConstants.EphemeralSlidingWindow`, `SessionConstants.PersistentLifetime` (Task 0).
- Produces: `GetActiveAsync` returns only rows that are `RevokedAt == null` AND live-by-expiry (ephemeral within 30 min of `LastUsedAt`, persistent within 30 days).

- [ ] **Step 1: Write the failing test.** Seeds four sessions for one user via an admin-connected `AppDbContext` scope (the WAF rewires `AppDbContext` to the admin connection for cross-user setup), each keyed on a unique `IpCreatedAt` marker, then drives `GetActiveAsync` and asserts which appear. Mirror the seeding shape in `SessionsApiTests` (scope → `GetRequiredService<AppDbContext>()` → add rows with backdated timestamps → `SaveChangesAsync`).

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("Waf")]
public class SessionExpiryFilterTests
{
    private readonly WafFixture _factory;
    public SessionExpiryFilterTests(WafFixture factory) => _factory = factory;

    [Fact]
    public async Task GetActiveAsync_hides_expired_ephemeral_and_stale_persistent_sessions()
    {
        var marker = $"expiry-{Guid.NewGuid():N}";
        var email = $"{marker}@approle-test.local";
        var client = Factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        Guid userId;
        await using (var admin = Factory.NewAdminContext())
        {
            var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
            userId = u.Id;
        }
        var now = DateTime.UtcNow;

        UserSession Row(string tag, bool persistent, DateTime lastUsed, DateTime? revoked = null) => new()
        {
            Id = Guid.NewGuid(), UserId = userId, IpCreatedAt = $"{tag}.{marker}",
            UserAgent = "test", IsPersistent = persistent,
            CreatedAt = now.AddDays(-40), LastUsedAt = lastUsed, RevokedAt = revoked,
        };

        // Seed via the BYPASSRLS admin context (the app-role context would 42501 on
        // these inserts because no user scope is active in a bare DI scope).
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.UserSessions.AddRange(
                Row("live-eph", persistent: false, lastUsed: now.AddMinutes(-5)),        // shown
                Row("dead-eph", persistent: false, lastUsed: now.AddMinutes(-31)),       // hidden (expired)
                Row("live-persist", persistent: true, lastUsed: now.AddDays(-3)),        // shown
                Row("stale-persist", persistent: true, lastUsed: now.AddDays(-31)),      // hidden (past 30d)
                Row("revoked", persistent: false, lastUsed: now.AddMinutes(-1), revoked: now)); // hidden
            await admin.Context.SaveChangesAsync();
        }

        // SessionService bound to this user via FakeCurrentUserAccessor (the Stage 7 seam).
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var sessions = new SessionService(db, new FakeCurrentUserAccessor(userId), clock);
        var result = await sessions.GetActiveAsync(Guid.NewGuid());

        var shownTags = result.Select(r => r.IpCreatedAt).Where(ip => ip.EndsWith(marker)).ToList();
        shownTags.Should().BeEquivalentTo(new[] { $"live-eph.{marker}", $"live-persist.{marker}" });
    }
}
```

> **Fixture seam (real, verified — do NOT invent helpers):** the AppRole suite registers through the HTTP endpoint and reads the id from the admin context — mirror `LoginSessionWriteUnderRlsTests`:
> ```csharp
> var email = $"{marker}@approle-test.local";
> var client = Factory.CreateClient();
> await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
>     new { email, password = AuthTestFixture.ValidPassword });
> Guid userId;
> await using (var admin = Factory.NewAdminContext()) {
>     var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
>     userId = u.Id; u.EmailConfirmed = true; await admin.Context.SaveChangesAsync();
> }
> ```
> Seed the `UserSession` rows through `Factory.NewAdminContext()` too (BYPASSRLS — the app-role context would 42501 on a foreign insert). Resolve `SessionService` bound to that user via `new FakeCurrentUserAccessor(userId)` (the pattern 29 test files use since the Stage 7 cutover). Keep the assertion filtered by `marker`. Adjust the `[Collection(...)]` / base-class to whatever the AppRole tests use (they derive from an RLS-aware base, not a bare `[Collection("Waf")]`).

- [ ] **Step 2: Run, verify fail.** Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SessionExpiryFilterTests"` → FAIL (expired/stale rows still returned).

- [ ] **Step 3: Implement the filter.** In `SessionService.GetActiveAsync`, replace the `.Where(s => s.UserId == userId && s.RevokedAt == null)` clause with an expiry-aware predicate:

```csharp
var now = timeProvider.GetUtcNow().UtcDateTime;
var ephemeralCutoff = now - SessionConstants.EphemeralSlidingWindow;
var persistentCutoff = now - SessionConstants.PersistentLifetime;

return await db.UserSessions
    .Where(s => s.UserId == userId
        && s.RevokedAt == null
        && (s.IsPersistent
            ? s.LastUsedAt > persistentCutoff
            : s.LastUsedAt > ephemeralCutoff))
    .OrderByDescending(s => s.LastUsedAt)
    .Select(/* unchanged SessionDto projection */)
    .ToArrayAsync();
```

(Add `using ProjectCeres.Common.Authentication;` if needed. `timeProvider` is already injected into `SessionService`.)

- [ ] **Step 4: Run, verify pass.** Same filter → PASS.

- [ ] **Step 5: Run the existing sessions suite** to confirm no regression: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SessionsApiTests"` → PASS.

- [ ] **Step 6: Commit.**

```bash
git add ProjectCeres/Services/SessionService.cs ProjectCeres.Tests/Integration/Authentication/SessionExpiryFilterTests.cs
git commit -m "feat(12.10): active-sessions list hides expired sessions"
```

---

### Task B: Login dedup — revoke the same-device duplicate

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs` (`IssueSessionAndCookiesAsync`)
- Test: `ProjectCeres.Tests/Integration/Authentication/SessionLoginDedupTests.cs` (create)

**Interfaces:**
- Consumes: the `AppDbContext _db` already in `AuthController`.
- Produces: after login, at most one live (`RevokedAt == null`) ephemeral `UserSession` per (`UserId`, `UserAgent`, `IpCreatedAt`).

- [ ] **Step 1: Write the failing test.** Register a user, log in twice from the SAME simulated UA+IP, assert exactly one live ephemeral row remains and the older one is now `RevokedAt != null` (audit intact). Then log in from a different IP and assert two live rows. Use the real login endpoint (mirror `SessionsApiTests` login helpers); the test host's `RemoteIpAddress` is null (IP `""`) and `UserAgent` is set via the request header, so vary the UA to simulate "different device" and use a seeded row with a distinct `IpCreatedAt` to simulate "different IP".

```csharp
[Fact]
public async Task Second_login_from_the_same_device_revokes_the_first_session()
{
    var marker = $"dedup-{Guid.NewGuid():N}";
    var email = $"{marker}@approle-test.local";
    var client = Factory.CreateClient();

    // Register via the HTTP endpoint (owns its PreAuthUserScope), confirm the email
    // via admin context (login requires a confirmed account) — the exact seam
    // LoginSessionWriteUnderRlsTests uses.
    await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
        new { email, password = AuthTestFixture.ValidPassword });
    Guid userId;
    await using (var admin = Factory.NewAdminContext())
    {
        var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
        userId = u.Id; u.EmailConfirmed = true; await admin.Context.SaveChangesAsync();
    }

    // Two logins, identical UA + IP (both "" on the TestServer host).
    async Task Login() => (await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/login",
        new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }))
        .StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    await Login();
    await Login();

    await using var verify = Factory.NewAdminContext();
    var rows = await verify.Context.UserSessions.IgnoreQueryFilters().AsNoTracking()
        .Where(s => s.UserId == userId).ToListAsync();

    rows.Count(s => s.RevokedAt == null && !s.IsPersistent).Should().Be(1,
        "the same device keeps exactly one live ephemeral session");
    rows.Count(s => s.RevokedAt != null).Should().BeGreaterThanOrEqualTo(1,
        "the superseded session survives as a revoked row for audit");
}
```

> Same fixture seam as Task A (register-via-HTTP, confirm + read id via `Factory.NewAdminContext()`). A fresh `HttpClient` shares the cookie container, so the second login reuses the same UA + IP — exactly the dedup case. Derive the test class from the AppRole RLS base the sibling tests use, not a bare `[Collection("Waf")]`.

- [ ] **Step 2: Run, verify fail.** Filter `~SessionLoginDedupTests` → FAIL (two live ephemeral rows).

- [ ] **Step 3: Implement the dedup.** In `IssueSessionAndCookiesAsync`, immediately before `_db.UserSessions.Add(session);`, revoke live same-device ephemeral duplicates:

```csharp
// Dedup: one live ephemeral session per device. Revoke any existing live
// ephemeral session from the same UA + IP before adding the new row, so the
// list shows one row per device instead of one per login. Guarded by
// Id != sessionId so we never revoke the row we are about to create; the new
// cookie already carries the new sid, so SessionRevocationValidator still
// accepts this request. Persistent sessions rotate via their own middleware.
var now = _timeProvider.GetUtcNow().UtcDateTime;
await _db.UserSessions
    .Where(s => s.UserId == user.Id
        && !s.IsPersistent
        && s.RevokedAt == null
        && s.UserAgent == ua
        && s.IpCreatedAt == ip
        && s.Id != sessionId)
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
```

(`ua`, `ip`, and `sessionId` are already in scope in this method. `ExecuteUpdateAsync` issues its own statement; it runs before the `Add`+`SaveChangesAsync`, which is the intended order.)

- [ ] **Step 4: Run, verify pass.** Filter → PASS.

- [ ] **Step 5: Run the login + session-revocation suites** for regressions: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SessionRevocation|FullyQualifiedName~SessionsApiTests|FullyQualifiedName~LoginSessionWriteUnderRls"` → PASS.

- [ ] **Step 6: Commit.**

```bash
git add ProjectCeres/Controllers/Api/AuthController.cs ProjectCeres.Tests/Integration/Authentication/SessionLoginDedupTests.cs
git commit -m "feat(12.10): login revokes the same-device duplicate session"
```

---

### Task C: Retention sweep — `--sweep-sessions` CLI one-shot

**Files:**
- Create: `ProjectCeres/Tools/SweepSessions.cs`
- Modify: `ProjectCeres/Program.cs` (dispatch branch)
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` (IgnoreQueryFilters allow-list — only if the tool calls `IgnoreQueryFilters()`)
- Test: `ProjectCeres.Tests/Integration/Authentication/SessionRetentionSweepTests.cs` (create)

**Interfaces:**
- Consumes: `AdminDbContext`, `SessionConstants.RetentionHorizon`, `TimeProvider`.
- Produces: `SweepSessions.RunAsync(WebApplicationBuilder builder) → Task<int>` (0 on success). A static `SweepSessions.DeleteExpiredAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct) → Task<int>` (rows deleted) so the test can drive the DELETE without the CLI wrapper.

- [ ] **Step 1: Write the failing test.** Seed, via an admin-connected `AppDbContext` scope, rows straddling the 90-day horizon; call `SweepSessions.DeleteExpiredAsync` against a resolved `AdminDbContext`; assert only the >90-day rows are gone and in-window rows survive. Key each row on a unique `IpCreatedAt` marker.

```csharp
[Fact]
public async Task DeleteExpiredAsync_removes_only_rows_past_the_90_day_horizon()
{
    var marker = $"sweep-{Guid.NewGuid():N}";
    var email = $"{marker}@approle-test.local";
    var client = Factory.CreateClient();
    await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
        new { email, password = AuthTestFixture.ValidPassword });
    Guid userId;
    await using (var admin = Factory.NewAdminContext())
    {
        var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
        userId = u.Id;
    }
    var now = DateTime.UtcNow;

    UserSession Row(string tag, bool persistent, DateTime lastUsed, DateTime? revoked) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, IpCreatedAt = $"{tag}.{marker}",
        UserAgent = "test", IsPersistent = persistent,
        CreatedAt = now.AddDays(-200), LastUsedAt = lastUsed, RevokedAt = revoked,
    };

    await using (var admin = Factory.NewAdminContext())
    {
        admin.Context.UserSessions.AddRange(
            Row("old-revoked", false, now.AddDays(-200), revoked: now.AddDays(-91)),  // deleted
            Row("new-revoked", false, now.AddDays(-200), revoked: now.AddDays(-89)),  // kept
            Row("old-dead-eph", false, now.AddDays(-91), revoked: null),              // deleted
            Row("live-persist", true, now.AddDays(-2), revoked: null));               // kept
        await admin.Context.SaveChangesAsync();
    }

    await using var verify = Factory.NewAdminContext();
    var clock = Factory.Services.GetRequiredService<TimeProvider>();
    await SweepSessions.DeleteExpiredAsync(verify.Context, clock, default);

    var remaining = await verify.Context.UserSessions.IgnoreQueryFilters().AsNoTracking()
        .Where(x => x.IpCreatedAt.EndsWith(marker)).Select(x => x.IpCreatedAt).ToListAsync();
    remaining.Should().BeEquivalentTo(new[] { $"new-revoked.{marker}", $"live-persist.{marker}" });
}
```

- [ ] **Step 2: Run, verify fail.** Filter `~SessionRetentionSweepTests` → FAIL (`SweepSessions` not found).

- [ ] **Step 3: Implement `SweepSessions.cs`.**

```csharp
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;

namespace ProjectCeres.Tools;

/// <summary>
/// Retention sweep for UserSession rows. A FLAT cross-tenant DELETE through
/// AdminDbContext (ceres_admin, BYPASSRLS) — not a per-user fan-out, so it does
/// NOT use IUserJobRunner/BackgroundJobScope. AppDbContext/ceres_app would scope
/// the DELETE to one user via RLS and delete nothing. Invoked by cron via
/// `dotnet run -- --sweep-sessions`, mirroring SeedDevUser.
/// </summary>
[RequiresAdminContext]
public static class SweepSessions
{
    /// <summary>Deletes rows past the 90-day retention horizon. Returns rows deleted.</summary>
    public static async Task<int> DeleteExpiredAsync(
        AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now - SessionConstants.RetentionHorizon;

        // IgnoreQueryFilters: cross-tenant by design — the sweep spans all users.
        // Stage 10 architecture test allow-lists this file.
        return await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => (s.RevokedAt != null && s.RevokedAt < cutoff)
                     || (s.RevokedAt == null && !s.IsPersistent && s.LastUsedAt < cutoff))
            .ExecuteDeleteAsync(ct);
    }

    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AdminDbContext>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var logger = sp.GetRequiredService<ILogger<Program>>();

        var deleted = await DeleteExpiredAsync(db, clock, CancellationToken.None);
        logger.LogInformation("Session sweep deleted {Count} expired UserSession rows.", deleted);
        return 0;
    }
}
```

- [ ] **Step 4: Add the CLI dispatch** in `Program.cs`, next to the `--seed-dev-user` branch (before `builder.Build()` in the app path):

```csharp
if (args.Length > 0 && args[0] == "--sweep-sessions")
{
    Environment.Exit(await ProjectCeres.Tools.SweepSessions.RunAsync(builder));
}
```

- [ ] **Step 5: Add the IgnoreQueryFilters allow-list entry.** In `ArchitectureTests` (the `IgnoreQueryFilters` allow-list — the same list the RLS work maintains), add `ProjectCeres/Tools/SweepSessions.cs`. Run the arch test to confirm: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ArchitectureTests"` → PASS. (If it was already green because the allow-list matches by directory, note that and skip.)

- [ ] **Step 6: Run, verify pass.** Filter `~SessionRetentionSweepTests` → PASS.

- [ ] **Step 7: Commit.**

```bash
git add ProjectCeres/Tools/SweepSessions.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Authentication/SessionRetentionSweepTests.cs ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git commit -m "feat(12.10): --sweep-sessions retention sweep of expired UserSession rows"
```

---

### Task D: Docs sync + roadmap + close-out

**Files:**
- Modify: `docs/models.md`, `docs/security-model.md`, `docs/roadmap-phase-three.md`

- [ ] **Step 1: Run the `sync-docs` skill** against the full 12.10 diff — models.md § UserSession (expiry semantics + sweep), security-model.md § Sessions ("one row per device" now enforced by dedup) + § Retention (the sweep is the mechanism behind the 90-day line).
- [ ] **Step 2: Add a Stage 12.10 roadmap section** (✅ Done) under Stage 12; add the cron registration to the Stage 16 hosting checklist (`* * cron → dotnet run -- --sweep-sessions`, daily); note the audit-log purge (13.6) can reuse the `--sweep-*` cron-command pattern.
- [ ] **Step 3: Run `changelog-sync`** — Fixed: the active-sessions list no longer shows expired sessions; Changed: repeated logins from one browser no longer stack up sessions.
- [ ] **Step 4: Final verification** — `dotnet build`, `dotnet test` (background, once), all exit 0.
- [ ] **Step 5: Commit the close-out.**

---

## Self-review

**Spec coverage:** Part A filter (Task A) ✓; Part B dedup with the `Id != sessionId` guard + SessionRevocationValidator safety (Task B) ✓; Part C flat cross-tenant DELETE via AdminDbContext + `[RequiresAdminContext]` + IgnoreQueryFilters allow-list + `--sweep-sessions` CLI (Task C) ✓; shared SessionConstants (Task 0) ✓; the three verify-against-codebase corrections all land (constants added not reused — Task 0; `Id != sessionId` guard — Task B Step 3; AdminDbContext-not-AppDbContext + not-a-fan-out — Task C) ✓; testing per part ✓; docs sync ✓.

**Placeholder scan:** every code step carries real code; the one soft spot — the fixture helper names (`RegisterUserReturningIdAsync`, `SessionServiceFor`, `LoginAsync`, `UserId`) — is called out with the instruction to reuse the existing AppRole/SessionsApi seam or add a thin helper, not invent a pattern.

**Type consistency:** `SessionConstants.EphemeralSlidingWindow` / `PersistentLifetime` / `RetentionHorizon` used consistently across Tasks 0/A/C; `SweepSessions.DeleteExpiredAsync(AdminDbContext, TimeProvider, CancellationToken) → Task<int>` defined in Task C Step 3 and consumed by the Task C test; `IssueSessionAndCookiesAsync`'s in-scope `ua`/`ip`/`sessionId` used by the Task B dedup.

**One risk noted:** the fixture helper names are indicative — the implementer must bind them to whatever the `Waf`/AppRole fixtures actually expose (register + login + resolve a user-scoped `SessionService`). This is a naming bind, not a design gap; the seeding/assertion shape is fully specified.
