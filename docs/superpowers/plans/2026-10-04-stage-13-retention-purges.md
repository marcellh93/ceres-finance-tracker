# Stage 13 Retention Purge Jobs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce the decided GDPR retention periods with four flat cross-tenant purge jobs (audit-log 12mo, failed-login 1yr, soft-deleted SavedReport 90d, soft-deleted ImportProfile 90d), bundled under one `--run-retention-purge` entrypoint, built on the `SweepSessions` pattern. Logic + entrypoint + tests now; the real daily cron schedule is wired at Stage 16.

**Architecture:** A new static class `ProjectCeres/Tools/RetentionPurge.cs` (`[RequiresAdminContext]`), four independently-testable `static Task<int>` purge methods each doing a flat `AdminDbContext.<Set>.IgnoreQueryFilters().Where(time-keyed).ExecuteDeleteAsync()`, and a `RunAsync(WebApplicationBuilder)` entrypoint dispatched from `Program.cs` on `--run-retention-purge`. Clock via injected `TimeProvider` (CER004). Mirrors `SweepSessions` exactly.

**Tech Stack:** .NET 10, EF Core (`ExecuteDeleteAsync`), xUnit integration tests via `AppRoleTestBase` / `AppRoleFixture` (RLS-active fixture, seed/assert via `Factory.NewAdminContext()`).

**Spec:** `docs/superpowers/specs/2026-10-03-stage-13-retention-purges-design.md`

## Global Constraints

- **SDK pinned 10.0.401** via `global.json`; `dotnet` resolves to it (Homebrew).
- **`AdminDbContext`** (role `ceres_admin`, BYPASSRLS) for all purges — cross-tenant time-keyed deletes; `AppDbContext`/`ceres_app` would RLS-scope to nobody and delete nothing.
- **`[RequiresAdminContext]`** marker required on `RetentionPurge` (enforced by `AdminContextDisciplineTests`).
- **`IgnoreQueryFilters()` allow-list:** `ProjectCeres/Tools/RetentionPurge.cs` MUST be added to the HashSet in `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` (~line 783) with a justifying comment, or `IgnoreQueryFilters_only_appears_in_documented_exception_paths` fails.
- **Clock:** inject `TimeProvider`; never `DateTime.UtcNow` in production code (CER004).
- **Retention periods are fixed** (do not change): audit 12 months, failed-login 1 year, SavedReport 90 days, ImportProfile 90 days.
- **Entity facts (verified):** `AuditLog.OccurredAt` (DbSet `AuditLogs`, IUserOwned); `FailedLoginAttempt.OccurredAt` (DbSet `FailedLoginAttempts`, no RLS filter); `SavedReport.DeletedAt?` (DbSet `SavedReports`, IUserOwned); `ImportProfile.DeletedAt?` (DbSet `ImportProfiles`, IUserOwned — class `ImportProfile` lives in `Models/CsvImportProfile.cs`, format-agnostic so this covers Excel too).
- **Test discipline:** shared sequential DB — every seeded row carries a unique per-test marker; assertions filter by that marker only. Seed/assert via `Factory.NewAdminContext()`; use the real `TimeProvider` from `Factory.Services`.
- **No `Co-Authored-By` trailer. Stay on `main`.** Don't run `dotnet test` concurrently/background.

## File Structure

- Create: `ProjectCeres/Tools/RetentionPurge.cs` — the four purge methods + `RunAsync` entrypoint.
- Modify: `ProjectCeres/Program.cs` — add the `--run-retention-purge` dispatch line.
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — add `RetentionPurge.cs` to the IgnoreQueryFilters allow-list.
- Create: `ProjectCeres.Tests/Integration/Retention/RetentionPurgeTests.cs` — the four fixture-then-purge tests.
- Modify: `docs/roadmap-phase-three.md` — correct the wrong "already implemented" line; tick covered items; add the Stage 16 cron-registration `[ ]`.

---

## Task 1: RetentionPurge class with the four purge methods

**Files:**
- Create: `ProjectCeres/Tools/RetentionPurge.cs`
- Test: deferred to Task 3 (the integration tests exercise these methods).

**Interfaces:**
- Produces: `RetentionPurge.PurgeAuditLogAsync(AdminDbContext, TimeProvider, CancellationToken) : Task<int>`, `PurgeFailedLoginsAsync(...)`, `PurgeSavedReportsAsync(...)`, `PurgeImportProfilesAsync(...)` — each returns rows deleted. Consumed by Task 2's `RunAsync` and Task 3's tests.

- [ ] **Step 1: Write the class** (no test-first here — these are thin EF deletes exercised by Task 3's integration tests, which cannot run until the methods exist; the TDD cycle is Task 3 writing a failing test against these then this already satisfying it. Build the methods, then Task 3 proves them.)

Create `ProjectCeres/Tools/RetentionPurge.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Data;

namespace ProjectCeres.Tools;

/// <summary>
/// GDPR retention purges — four FLAT cross-tenant DELETEs through AdminDbContext
/// (ceres_admin, BYPASSRLS), mirroring SweepSessions. NOT per-user fan-outs, so no
/// IUserJobRunner/BackgroundJobScope. AppDbContext/ceres_app would RLS-scope the
/// DELETE to nobody. Invoked by cron via `dotnet run -- --run-retention-purge`.
/// Retention periods are fixed by Stage 13 decision (planning-resolved 2026-09-19).
/// </summary>
[RequiresAdminContext]
public static class RetentionPurge
{
    public static readonly TimeSpan AuditLogHorizon = TimeSpan.FromDays(365);      // 12 months
    public static readonly TimeSpan FailedLoginHorizon = TimeSpan.FromDays(365);   // 1 year
    public static readonly TimeSpan SoftDeleteHorizon = TimeSpan.FromDays(90);     // 90 days

    /// <summary>Deletes AuditLog rows older than 12 months. Returns rows deleted.</summary>
    public static async Task<int> PurgeAuditLogAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - AuditLogHorizon;
        // IgnoreQueryFilters: cross-tenant by design — spans all users (allow-listed).
        return await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.OccurredAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Deletes FailedLoginAttempt rows older than 1 year. Returns rows deleted.</summary>
    public static async Task<int> PurgeFailedLoginsAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - FailedLoginHorizon;
        // FailedLoginAttempt carries no RLS filter, but IgnoreQueryFilters is harmless
        // and keeps the pattern uniform with the IUserOwned purges.
        return await db.FailedLoginAttempts
            .IgnoreQueryFilters()
            .Where(f => f.OccurredAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Hard-deletes SavedReport rows soft-deleted more than 90 days ago. Returns rows deleted.</summary>
    public static async Task<int> PurgeSavedReportsAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - SoftDeleteHorizon;
        return await db.SavedReports
            .IgnoreQueryFilters()
            .Where(r => r.DeletedAt != null && r.DeletedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Hard-deletes ImportProfile rows soft-deleted more than 90 days ago (all formats). Returns rows deleted.</summary>
    public static async Task<int> PurgeImportProfilesAsync(AdminDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - SoftDeleteHorizon;
        return await db.ImportProfiles
            .IgnoreQueryFilters()
            .Where(p => p.DeletedAt != null && p.DeletedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
```
(Confirm the `[RequiresAdminContext]` attribute namespace matches `SweepSessions.cs`'s using — `ProjectCeres.Analyzers.Annotations`. If the analyzer annotation is elsewhere, match SweepSessions exactly.)

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build ProjectCeres/ProjectCeres.csproj -c Debug`
Expected: `Build succeeded.` (The `AdminContextDisciplineTests`/CER analyzers may warn if `[RequiresAdminContext]` is missing — it is present.)

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Tools/RetentionPurge.cs
git commit -m "feat(13): RetentionPurge — four flat retention purge methods"
```

---

## Task 2: --run-retention-purge entrypoint + Program.cs dispatch

**Files:**
- Modify: `ProjectCeres/Tools/RetentionPurge.cs` (add `RunAsync`)
- Modify: `ProjectCeres/Program.cs` (dispatch)

**Interfaces:**
- Consumes: the four purge methods from Task 1.
- Produces: `RetentionPurge.RunAsync(WebApplicationBuilder) : Task<int>`; the `--run-retention-purge` CLI flag.

- [ ] **Step 1: Add the entrypoint to RetentionPurge.cs**

Append to the `RetentionPurge` class (mirrors `SweepSessions.RunAsync`):
```csharp
    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AdminDbContext>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var logger = sp.GetRequiredService<ILogger<Program>>();
        var ct = CancellationToken.None;

        var audit = await PurgeAuditLogAsync(db, clock, ct);
        var failed = await PurgeFailedLoginsAsync(db, clock, ct);
        var reports = await PurgeSavedReportsAsync(db, clock, ct);
        var profiles = await PurgeImportProfilesAsync(db, clock, ct);

        logger.LogInformation(
            "Retention purge: deleted {Audit} audit-log, {Failed} failed-login, {Reports} saved-report, {Profiles} import-profile rows.",
            audit, failed, reports, profiles);
        return 0;
    }
```

- [ ] **Step 2: Add the dispatch line in Program.cs**

In the arg-dispatch block (alongside `--sweep-sessions` / `--run-export-jobs` / `--run-erasure-jobs`, after service registration, before the final `builder.Build()` — around `Program.cs:996-1015`), add:
```csharp
// Invocation: dotnet run --project ProjectCeres -- --run-retention-purge
if (args.Length > 0 && args[0] == "--run-retention-purge")
    Environment.Exit(await ProjectCeres.Tools.RetentionPurge.RunAsync(builder));
```
(Match the exact surrounding style — read the `--sweep-sessions` block and mirror its spacing/comment form.)

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet build ProjectCeres/ProjectCeres.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 4: Smoke-run the entrypoint** (proves it wires + runs end-to-end against the dev DB; deletes only genuinely-aged rows, likely 0 in dev)

Run: `~/.dotnet/dotnet run --project ProjectCeres -- --run-retention-purge`
Expected: process exits 0; logs the "Retention purge: deleted N … rows." line.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Tools/RetentionPurge.cs ProjectCeres/Program.cs
git commit -m "feat(13): --run-retention-purge entrypoint + dispatch"
```

---

## Task 3: Integration tests (fixture-then-purge, boundary + negative)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Retention/RetentionPurgeTests.cs`

**Interfaces:**
- Consumes: the four `RetentionPurge.Purge*Async` methods; `AppRoleTestBase`/`AppRoleFixture`; `Factory.NewAdminContext()`; `Factory.Services.GetRequiredService<TimeProvider>()`.

- [ ] **Step 1: Write the failing tests**

Create `ProjectCeres.Tests/Integration/Retention/RetentionPurgeTests.cs`. Mirror `SessionRetentionSweepTests` exactly (collection, base class, admin-context seed/assert, per-test marker). One test per purge; each seeds an aged row (past the horizon) + a fresh row (inside the horizon), runs the purge with the real `TimeProvider`, asserts only the aged one is gone — filtered by the marker. For the two soft-delete purges, also seed a never-soft-deleted row (`DeletedAt == null`) and assert it survives.

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.AppRole;
using ProjectCeres.Tools;

namespace ProjectCeres.Tests.Integration.Retention;

[Collection("AppRoleTests")]
public class RetentionPurgeTests : AppRoleTestBase
{
    public RetentionPurgeTests(AppRoleFixture fixture) : base(fixture) { }

    private async Task<Guid> RegisterUserAsync(string marker)
    {
        var email = $"{marker}@approle-test.local";
        var client = Factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        await using var admin = Factory.NewAdminContext();
        var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
        return u.Id;
    }

    [Fact]
    public async Task PurgeAuditLogAsync_deletes_only_rows_past_12_months()
    {
        var marker = $"rp-audit-{Guid.NewGuid():N}";
        var userId = await RegisterUserAsync(marker);
        var now = DateTime.UtcNow;
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.AuditLogs.AddRange(
                new AuditLog { Id = Guid.NewGuid(), UserId = userId, Action = AuditLogAction.LoginSucceeded, EntityType = marker, OccurredAt = now.AddDays(-366) }, // past 12mo → deleted
                new AuditLog { Id = Guid.NewGuid(), UserId = userId, Action = AuditLogAction.LoginSucceeded, EntityType = marker, OccurredAt = now.AddDays(-364) }  // inside → kept
            );
            await admin.Context.SaveChangesAsync();
        }
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await using (var run = Factory.NewAdminContext())
            await RetentionPurge.PurgeAuditLogAsync(run.Context, clock, default);

        await using var verify = Factory.NewAdminContext();
        var remaining = await verify.Context.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.EntityType == marker).ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].OccurredAt.Should().BeAfter(now.AddDays(-365));
    }

    [Fact]
    public async Task PurgeFailedLoginsAsync_deletes_only_rows_past_1_year()
    {
        var marker = $"rp-fail-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.FailedLoginAttempts.AddRange(
                new FailedLoginAttempt { Id = Guid.NewGuid(), EmailAttempted = $"{marker}-old", IpAddress = "127.0.0.1", OccurredAt = now.AddDays(-366) },
                new FailedLoginAttempt { Id = Guid.NewGuid(), EmailAttempted = $"{marker}-new", IpAddress = "127.0.0.1", OccurredAt = now.AddDays(-364) }
            );
            await admin.Context.SaveChangesAsync();
        }
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await using (var run = Factory.NewAdminContext())
            await RetentionPurge.PurgeFailedLoginsAsync(run.Context, clock, default);

        await using var verify = Factory.NewAdminContext();
        var remaining = await verify.Context.FailedLoginAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.EmailAttempted!.StartsWith(marker)).Select(f => f.EmailAttempted).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { $"{marker}-new" });
    }

    [Fact]
    public async Task PurgeSavedReportsAsync_deletes_only_soft_deleted_past_90_days()
    {
        var marker = $"rp-rep-{Guid.NewGuid():N}";
        var userId = await RegisterUserAsync(marker);
        var now = DateTime.UtcNow;
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.SavedReports.AddRange(
                NewReport(userId, $"{marker}-old",  deletedAt: now.AddDays(-91)),  // soft-deleted past 90d → deleted
                NewReport(userId, $"{marker}-recent", deletedAt: now.AddDays(-89)), // soft-deleted inside → kept
                NewReport(userId, $"{marker}-live",  deletedAt: null)               // never deleted → kept regardless
            );
            await admin.Context.SaveChangesAsync();
        }
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await using (var run = Factory.NewAdminContext())
            await RetentionPurge.PurgeSavedReportsAsync(run.Context, clock, default);

        await using var verify = Factory.NewAdminContext();
        var remaining = await verify.Context.SavedReports.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Name.StartsWith(marker)).Select(r => r.Name).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { $"{marker}-recent", $"{marker}-live" });
    }

    [Fact]
    public async Task PurgeImportProfilesAsync_deletes_only_soft_deleted_past_90_days()
    {
        var marker = $"rp-prof-{Guid.NewGuid():N}";
        var userId = await RegisterUserAsync(marker);
        var now = DateTime.UtcNow;
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.ImportProfiles.AddRange(
                NewProfile(userId, $"{marker}-old",  deletedAt: now.AddDays(-91)),
                NewProfile(userId, $"{marker}-recent", deletedAt: now.AddDays(-89)),
                NewProfile(userId, $"{marker}-live",  deletedAt: null)
            );
            await admin.Context.SaveChangesAsync();
        }
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await using (var run = Factory.NewAdminContext())
            await RetentionPurge.PurgeImportProfilesAsync(run.Context, clock, default);

        await using var verify = Factory.NewAdminContext();
        var remaining = await verify.Context.ImportProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Name.StartsWith(marker)).Select(p => p.Name).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { $"{marker}-recent", $"{marker}-live" });
    }

    private static SavedReport NewReport(Guid userId, string name, DateTime? deletedAt) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Name = name, DeletedAt = deletedAt
        // set any other REQUIRED SavedReport fields here — read Models/SavedReport.cs
    };

    private static ImportProfile NewProfile(Guid userId, string name, DateTime? deletedAt) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Name = name, CreatedAt = DateTime.UtcNow, DeletedAt = deletedAt
    };
}
```
**Implementer MUST read `Models/SavedReport.cs`, `Models/AuditLog.cs` (+ `AuditLogAction` enum), `Models/FailedLoginAttempt.cs`, and `Models/CsvImportProfile.cs` before finalizing** — fill every REQUIRED (non-nullable, no-default) property in the `New*` helpers and the `AuditLog`/`FailedLoginAttempt` seeds, and use a REAL `AuditLogAction` enum value (the example `LoginSucceeded` must be verified to exist; substitute a real one if not). Match the exact required-field set or the seed `SaveChangesAsync` throws.

- [ ] **Step 2: Run to verify they pass** (the Task-1/2 methods already exist, so these should pass once the seeds are correct)

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~RetentionPurgeTests"`
Expected: 4 passed. (If a seed throws on a missing required field, fix the seed — not the production code.)

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Retention/RetentionPurgeTests.cs
git commit -m "test(13): RetentionPurge fixture-then-purge tests (boundary + soft-delete-null)"
```

---

## Task 4: IgnoreQueryFilters allow-list entry

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`

- [ ] **Step 1: Add the allow-list entry**

In the `allowed` HashSet (~line 783, the block that already lists `SweepSessions.cs`, `ExportJobWorker.cs`, etc.), add:
```csharp
            "ProjectCeres/Tools/RetentionPurge.cs",                                   // Stage 13: cross-tenant by design — the four retention purges span all users. SweepSessions pattern.
```

- [ ] **Step 2: Run the architecture test**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~IgnoreQueryFilters_only_appears_in_documented_exception_paths"`
Expected: PASS (fails before the entry, passes after).

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git commit -m "test(13): allow-list RetentionPurge for IgnoreQueryFilters"
```

---

## Task 5: Roadmap updates (correct the wrong line, tick covered items, add Stage 16 cron row)

**Files:**
- Modify: `docs/roadmap-phase-three.md`

- [ ] **Step 1: Correct the wrong "already implemented" line**

In Stage 13's retention checklist, the line `Soft-deleted CsvImportProfiles: hard-deleted after 90 days (already implemented; verify)` — change the parenthetical: it was NOT implemented; this stage builds it. Reword to reflect it's now shipped by `RetentionPurge.PurgeImportProfilesAsync`.

- [ ] **Step 2: Tick the retention items now covered**

Tick (`[x]`) the four retention `[ ]` items covered by `RetentionPurge` + tests: audit-log 12mo purge, failed-login 1yr purge, SavedReport 90d, ImportProfile 90d, and "Each retention rule … verifiable in code (test …)". Leave the inactive-user line pointing at Stage 13.b (already done). Note in each ticked line that the *cron schedule* is wired at Stage 16 (logic+test shipped here).

- [ ] **Step 3: Add the Stage 16 cron-registration row**

In Stage 16 § Scheduled jobs (where the `--sweep-sessions` cron row lives), add a `[ ]` row: register the daily `--run-retention-purge` cron, same mechanism as `--sweep-sessions`.

- [ ] **Step 4: Commit**

```bash
git add docs/roadmap-phase-three.md
git commit -m "docs(13): correct import-profile purge line; tick retention items; add Stage 16 cron row"
```

---

## Self-Review

**Spec coverage:** four purges → Task 1; one bundled entrypoint + dispatch → Task 2; fixture-then-purge tests with boundary + soft-delete-null negatives → Task 3; allow-list obligation → Task 4; roadmap correction + tick + Stage 16 cron row → Task 5; no-audit-record-in-v1 (honored — no AuditLog write, no enum change); AdminDbContext + `[RequiresAdminContext]` + TimeProvider (CER004) → Task 1 constraints. Inactive-user archival already split to Stage 13.b (prior commit). All covered.

**Placeholder scan:** the `New*` seed helpers leave the exact required-field list for the implementer to fill by reading the model files at build time — this is a "read the environment fact" instruction, not an invented placeholder or deferred work; the test shape (markered rows, boundary ages, soft-delete-null negative) is fixed. The `AuditLogAction.LoginSucceeded` value is explicitly flagged to verify-or-substitute against the real enum.

**Type consistency:** `PurgeAuditLogAsync`/`PurgeFailedLoginsAsync`/`PurgeSavedReportsAsync`/`PurgeImportProfilesAsync` defined Task 1, consumed Task 2's `RunAsync` + Task 3's tests with identical signatures `(AdminDbContext, TimeProvider, CancellationToken) → Task<int>`. DbSet names (`AuditLogs`/`FailedLoginAttempts`/`SavedReports`/`ImportProfiles`) and columns (`OccurredAt`/`DeletedAt`) match the verified spec. No drift.
