# Stage 9.5b — DualContext + RLS-parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a user-owned table impossible to ship without its RLS policy — derive the user-owned set from the EF model (deleting the hand-typed `UserOwnedTables.All`), verify it fail-closed at startup and in tests, and give tests a real-account (`ceres_app`, RLS-active) fixture that can actually observe a missing policy.

**Architecture:** Four pieces — (1) a `UserOwnedModel` reflection helper that is the single source of the user-owned set; (2) an `RlsParityStartupCheck` beside the existing `PrivilegeLeakStartupCheck` (boot compares model vs declared migrations; tests compare model vs live `pg_class`); (3) a `DualContextWebApplicationFactory` that boots wired to `ceres_app`; (4) an architecture test pinning `Controllers`/`Services` to `AppDbContext` unless `[RequiresAdminContext]` (exact-type match). All five readers of `UserOwnedTables.All` plus two extra consumers migrate to the helper in the same change; the historical RLS migration inlines a frozen 24-name list.

**Tech Stack:** .NET 10, EF Core (Npgsql), xUnit + FluentAssertions, PostgreSQL RLS, `ProjectCeres.Analyzers.Annotations.RequiresAdminContextAttribute` (exists since 9.5c).

**Source spec:** `docs/superpowers/specs/2026-05-31-stage-9-5b-dualcontext-rls-parity-design.md` (decisions D1–D7).

---

## File structure

| File | Responsibility | Task |
|---|---|---|
| `ProjectCeres/Common/UserOwnedModel.cs` | NEW — single source: model-derived user-owned table set (`RlsTables`, `FinanceTables`) | 1 |
| `ProjectCeres.Tests/Unit/UserOwnedModelTests.cs` | NEW — TPC/attachment/false-friend trap tests | 1 |
| `ProjectCeres/Data/AppDbContext.cs` | MODIFY — query-filter loop reads helper, not `.All` | 2 |
| `ProjectCeres/Common/RlsExceptionTranslator.cs` | MODIFY — reads helper | 2 |
| `ProjectCeres/Common/Exceptions/RlsPolicyViolationException.cs` | MODIFY — doc-comment ref | 2 |
| `ProjectCeres/Tools/SeedDevUser.cs` | MODIFY — own list → `UserOwnedModel.FinanceTables` | 3 |
| `ProjectCeres/Migrations/20260514011916_AddRowLevelSecurityPolicies.cs` | MODIFY — inline frozen 24-name `string[]` | 4 |
| `ProjectCeres.Tests/Integration/Rls/ParityTests.cs` | MODIFY — expected set from helper | 5 |
| `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` | MODIFY — repoint `UserOwnedTables_All_*` test | 5 |
| `ProjectCeres/Common/UserOwnedTables.cs` | DELETE — last, after all readers migrated | 6 |
| `ProjectCeres/Common/RlsParityStartupCheck.cs` | NEW — fail-closed boot check (model vs declared migrations) | 7 |
| `ProjectCeres/Program.cs` | MODIFY — invoke the check beside `PrivilegeLeakStartupCheck` | 7 |
| `ProjectCeres.Tests/Integration/DualContextWebApplicationFactory.cs` | NEW — `ceres_app`-wired factory | 8 |
| `ProjectCeres.Tests/Integration/WafCollection.cs` | MODIFY — `UseAppRoleConnection` virtual | 8 |
| `ProjectCeres.Tests/Integration/Rls/RlsParityMetaTests.cs` | NEW — meta-test + live-DB strictness | 9 |
| `ProjectCeres.Tests/Integration/Rls/AdminContextDisciplineTests.cs` | NEW — architecture test + exact-type meta-test | 10 |
| `docs/roadmap-phase-three.md`, `docs/multi-tenancy-strategy.md`, `docs/security-model.md` | MODIFY — discharge line 1094, de-stale, note | 11 |

> **Sequencing rule:** `UserOwnedTables.cs` is deleted in Task 6 — AFTER every reader (Tasks 2–5) compiles against the helper. Do not delete earlier or the build breaks mid-plan.

---

## Task 1: `UserOwnedModel` reflection helper

**Files:**
- Create: `ProjectCeres/Common/UserOwnedModel.cs`
- Test: `ProjectCeres.Tests/Unit/UserOwnedModelTests.cs`

- [ ] **Step 1: Write the failing tests** (the three traps from spec §3)

```csharp
using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Data;
using Xunit;

namespace ProjectCeres.Tests.Unit;

public class UserOwnedModelTests
{
    private static AppDbContext Model() => DesignTimeModel.Create(); // see Step 3 note

    [Fact]
    public void RlsTables_includes_the_three_concrete_Movement_tables_and_excludes_the_abstract_root()
    {
        var names = UserOwnedModel.RlsTables(Model().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().Contain(new[] { "Transactions", "Transfers", "LiabilityPayments" });
        names.Should().NotContain("Movements");
        names.Should().NotContain("Movement");
    }

    [Fact]
    public void RlsTables_includes_both_attachment_tables()
    {
        var names = UserOwnedModel.RlsTables(Model().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().Contain(new[] { "TransactionAttachments", "TransferAttachments" });
    }

    [Fact]
    public void RlsTables_excludes_entities_that_have_a_UserId_but_do_not_implement_IUserOwned()
    {
        var names = UserOwnedModel.RlsTables(Model().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().NotContain("FailedLoginAttempts");
        names.Should().NotContain("EmailDeliveryEvents");
    }

    [Fact]
    public void RlsTables_has_exactly_25_entries()
    {
        UserOwnedModel.RlsTables(Model().Model).Should().HaveCount(25);
    }

    [Fact]
    public void FinanceTables_excludes_auth_internal_tables()
    {
        var names = UserOwnedModel.FinanceTables(Model().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().NotContain(new[] { "UserSessions", "AuditLogs", "EmailConfirmationTokens" });
        names.Should().Contain(new[] { "Accounts", "TransactionAttachments" });
    }
}
```

> Note on `DesignTimeModel.Create()`: the model is needed without a DB connection. If a design-time model factory does not already exist, the simplest in-test construction is `new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options, new FakeCurrentUserAccessor(Guid.Empty))` — building options does not open a connection, and `.Model` triggers `OnModelCreating` only. Confirm `FakeCurrentUserAccessor` is accessible from `ProjectCeres.Tests.Common`; if `OnModelCreating` throws without a live connection, fall back to `IntegrationTests` collection and resolve `AppDbContext` from the WAF instead (move this file under `Integration/Rls/`).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UserOwnedModelTests"`
Expected: FAIL — `UserOwnedModel` does not exist.

- [ ] **Step 3: Write the helper**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProjectCeres.Common;

/// <summary>
/// Single source of truth for the set of user-owned tables, derived from the EF
/// model — replaces the hand-typed UserOwnedTables.All (Stage 9.5b / D4). A table
/// is user-owned iff its entity type implements <see cref="IUserOwned"/>, is
/// concrete, and maps to a physical table. The abstract TPC root Movement is
/// excluded; its three concrete subtypes are included via their own table names.
/// </summary>
public readonly record struct UserOwnedTable(string PostgresTableName, Type EntityType);

public static class UserOwnedModel
{
    // Auth-internal tables excluded from the dev-seed finance subset (D6).
    private static readonly HashSet<string> AuthInternalTables = new(StringComparer.Ordinal)
    {
        "UserSessions", "UserBlockedIps", "UserMfaBackupCodes", "TotpReplayEntries",
        "PasswordResetTokens", "EmailChangeTokens", "LockoutUnlockTokens",
        "AuditLogs", "EmailConfirmationTokens",
    };

    /// <summary>Every user-owned table that must carry an RLS policy.</summary>
    public static IReadOnlyList<UserOwnedTable> RlsTables(IModel model) =>
        model.GetEntityTypes()
            .Where(e => !e.ClrType.IsAbstract
                        && typeof(IUserOwned).IsAssignableFrom(e.ClrType)
                        && e.GetTableName() is not null)
            .Select(e => new UserOwnedTable(e.GetTableName()!, e.ClrType))
            .GroupBy(t => t.PostgresTableName)   // collapse any owned-type duplicates
            .Select(g => g.First())
            .OrderBy(t => t.PostgresTableName, StringComparer.Ordinal)
            .ToList();

    /// <summary>The finance+attachment subset SeedDevUser remaps (D6) — no auth-internal tables.</summary>
    public static IReadOnlyList<UserOwnedTable> FinanceTables(IModel model) =>
        RlsTables(model).Where(t => !AuthInternalTables.Contains(t.PostgresTableName)).ToList();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UserOwnedModelTests"`
Expected: PASS (5 tests). If the `HaveCount(25)` test fails with 26+, an abstract/owned type leaked — re-check the `!IsAbstract` + `GetTableName() is not null` predicate against the failing name.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Common/UserOwnedModel.cs ProjectCeres.Tests/Unit/UserOwnedModelTests.cs
git commit -m "feat(9.5b): UserOwnedModel — model-derived user-owned table set (TPC-aware)"
```

---

## Task 2: Repoint the query-filter loop + exception consumers

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs` (the `foreach (var table in UserOwnedTables.All)` filter loop, ~line 326)
- Modify: `ProjectCeres/Common/RlsExceptionTranslator.cs` (the `UserOwnedTables.All` lookup)
- Modify: `ProjectCeres/Common/Exceptions/RlsPolicyViolationException.cs` (doc-comment `<see cref>` only)

- [ ] **Step 1: Repoint the filter loop.** In `AppDbContext.cs`, the loop currently iterates `UserOwnedTables.All` and skips TPC subtypes (filter goes on the `Movement` root). The query-filter set = RLS set MINUS the two attachment tables (spec §4.1). Replace the loop source:

```csharp
// Query-filter set: model-derived RLS set minus attachment tables (scoped via parent
// at the EF layer; RLS-protected at the DB layer). See UserOwnedModel + Stage 9.5b §4.1.
var filterTables = UserOwnedModel.RlsTables(this.Model)   // 'this.Model' inside OnModelCreating: use the modelBuilder's metadata
    .Where(t => t.PostgresTableName is not "TransactionAttachments" and not "TransferAttachments");
foreach (var table in filterTables)
{
    if (typeof(Movement).IsAssignableFrom(table.EntityType)) continue; // filter on root, not subtypes
    RegisterUserOwnedFilter(modelBuilder, table.EntityType);
}
```

> **Caveat — model availability inside `OnModelCreating`:** `this.Model` is not yet finalized during `OnModelCreating`. Use `modelBuilder.Model` (the mutable metadata) as the `IModel` argument instead: `UserOwnedModel.RlsTables(modelBuilder.Model)`. Verify `GetTableName()` is resolvable at this point — table names are set early via `ToTable`/conventions, so this holds; if a name comes back null mid-build, fall back to keeping the existing hard-coded `typeof(...)` list for the *filter loop only* and note it, since the filter loop is not the load-bearing RLS oracle (the startup check is).

- [ ] **Step 2: Repoint `RlsExceptionTranslator.cs`.** Replace its `UserOwnedTables.All` reference with `UserOwnedModel.RlsTables(<model>)`. It needs an `IModel` — inject `AppDbContext` (or `IModel`) if not already available, or accept the model where the translator is constructed. Match the existing DI shape; if the translator is static, pass the model from the caller that already has the `DbContext`.

- [ ] **Step 3: Fix the doc-comment in `RlsPolicyViolationException.cs`** — change `<see cref="UserOwnedTables.All"/>` to `<see cref="UserOwnedModel.RlsTables"/>`. No logic.

- [ ] **Step 4: Build (do not delete `UserOwnedTables.cs` yet)**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded. 0 Error(s)`. (`UserOwnedTables.All` still exists — only its readers changed.)

- [ ] **Step 5: Run the query-filter regression**

Run: `dotnet test --filter "FullyQualifiedName~GlobalQueryFilterTests|FullyQualifiedName~IdorIsolationTests"`
Expected: PASS — the per-user filter still applies on every entity it did before.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Data/AppDbContext.cs ProjectCeres/Common/RlsExceptionTranslator.cs ProjectCeres/Common/Exceptions/RlsPolicyViolationException.cs
git commit -m "feat(9.5b): query-filter loop + RLS exception consumers read UserOwnedModel"
```

---

## Task 3: SeedDevUser → FinanceTables

**Files:**
- Modify: `ProjectCeres/Tools/SeedDevUser.cs:60-78` (its own `string[] UserOwnedTables`)

- [ ] **Step 1: Replace the hand-typed array with a model-derived call.** SeedDevUser resolves `AdminDbContext` from DI; use its `.Model`. Delete lines 60-78 (`private static readonly string[] UserOwnedTables = [ ... ];`) and replace each `foreach (var table in UserOwnedTables)` (lines ~218, ~377) with:

```csharp
foreach (var table in UserOwnedModel.FinanceTables(db.Model).Select(t => t.PostgresTableName))
```

where `db` is the `AdminDbContext` already in scope at each call site (confirm the local variable name; it is resolved earlier in `RunAsync`).

- [ ] **Step 2: Build**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Verify the seed tool still runs end-to-end**

Run: `dotnet run --project ProjectCeres -- --seed-dev-user --email dev@example.com --generate-password`
Expected: completes; prints a usable login; remaps the same 16 finance/attachment tables as before (auth-internal tables still NOT remapped — `FinanceTables` excludes them per D6).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Tools/SeedDevUser.cs
git commit -m "feat(9.5b): SeedDevUser derives its finance table set from UserOwnedModel"
```

---

## Task 4: Inline the frozen list into the historical RLS migration (D5)

**Files:**
- Modify: `ProjectCeres/Migrations/20260514011916_AddRowLevelSecurityPolicies.cs` (two `foreach (var table in UserOwnedTables.All)` blocks, ~lines 80 and 122)

> **Why frozen, not derived:** a migration must emit byte-identical SQL forever and must not depend on mutable app types. This migration already ran against dev + test. It gets its OWN private array, copied from the 24 names `UserOwnedTables.All` held *at this migration's authoring time* (the original 24 — NOT including `EmailConfirmationTokens`, which got its policy in the later `20260526054514_EnableRlsOnEmailConfirmationTokens` migration). Reproducing the original output is the whole point.

- [ ] **Step 1: Add a private frozen array to the migration class.** Inside the `AddRowLevelSecurityPolicies` class, above `Up()`:

```csharp
// Frozen at authoring time (Stage 7.5). A migration must emit identical SQL forever,
// so it does NOT read the live model. The 24 original tables (EmailConfirmationTokens
// got its policy in 20260526054514, intentionally absent here). Stage 9.5b / D5.
private static readonly (string PostgresTableName, bool IsAttachment)[] FrozenUserOwnedTables =
{
    ("Accounts", false), ("Budgets", false), ("Categories", false), ("CategoryBudgets", false),
    ("ImportProfiles", false), ("ImportStagedTransactions", false), ("ImportStagedTransfers", false),
    ("ImportTransferExclusions", false), ("RecurringTransactions", false), ("SavedReports", false),
    ("Settings", false), ("Transactions", false), ("Transfers", false), ("LiabilityPayments", false),
    ("TransactionAttachments", true), ("TransferAttachments", true),
    ("UserSessions", false), ("UserBlockedIps", false), ("UserMfaBackupCodes", false),
    ("TotpReplayEntries", false), ("PasswordResetTokens", false), ("EmailChangeTokens", false),
    ("LockoutUnlockTokens", false), ("AuditLogs", false),
};
```

> Verify the `IsAttachment` flag is only needed if Phase A's foreach distinguished attachment tables. If the original loop used `table.PostgresTableName` only (no `EntityType` / no attachment branch), drop the tuple and use a plain `string[]` of the 24 names. Read the two foreach bodies (lines 80, 122) and match exactly what fields they consumed off `table`.

- [ ] **Step 2: Repoint both foreach blocks** from `UserOwnedTables.All` to `FrozenUserOwnedTables`, adjusting field access (`table.PostgresTableName`) to match the tuple/array shape chosen in Step 1.

- [ ] **Step 3: Verify the migration produces identical SQL.** Generate an idempotent script and diff against a pre-change baseline:

```bash
git stash   # baseline = current (pre-Task-4) migration
dotnet ef migrations script --idempotent --project ProjectCeres -o /tmp/rls-baseline.sql 2>/dev/null
git stash pop
dotnet ef migrations script --idempotent --project ProjectCeres -o /tmp/rls-after.sql 2>/dev/null
diff /tmp/rls-baseline.sql /tmp/rls-after.sql && echo "IDENTICAL — frozen list reproduces output"
```
Expected: `IDENTICAL`. Any diff means the frozen list drifted from the original 24 — fix the array, do not accept the diff.

- [ ] **Step 4: Build**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded.` (migration no longer references `UserOwnedTables.All`).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Migrations/20260514011916_AddRowLevelSecurityPolicies.cs
git commit -m "feat(9.5b): inline frozen 24-table list into historical RLS migration (D5)"
```

---

## Task 5: Repoint the parity test + the architecture query-filter test

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Rls/ParityTests.cs` (`expected` built from `UserOwnedTables.All`, ~lines 40, 74)
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` (`UserOwnedTables_All_matches_HasQueryFilter_registrations`, ~line 761)

- [ ] **Step 1: Repoint `ParityTests` expected sets.** Both `expected` builders change from `UserOwnedTables.All.Select(t => t.PostgresTableName)` to derive from the model. ParityTests is in the `IntegrationTests` collection and can resolve `AppDbContext`; use its `.Model`:

```csharp
var expected = UserOwnedModel.RlsTables(db.Model)
    .Select(t => t.PostgresTableName).OrderBy(n => n).ToList();
```
where `db` is the context the test already resolves. Now the expected set is model-derived (would have *included* a missing entity), closing Hole B (spec §1).

- [ ] **Step 2: Repoint the ArchitectureTests query-filter test.** `UserOwnedTables_All_matches_HasQueryFilter_registrations` references `ProjectCeres.Common.UserOwnedTables.All`. Change its source to `UserOwnedModel.RlsTables(<model>)`, keeping the assertion (every entry has a `HasQueryFilter`, attachments reconciled out). If a second hard-coded ~20-type list exists in `Every_user_owned_entity_carries_a_global_query_filter`, leave it as a deliberate hand-cross-check and add a comment: `// Independent hand-maintained backstop vs UserOwnedModel — flags silent reflection drift (9.5b).`

- [ ] **Step 3: Run both**

Run: `dotnet test --filter "FullyQualifiedName~ParityTests|FullyQualifiedName~UserOwnedTables_All_matches_HasQueryFilter"`
Expected: PASS. (DB already has all 25 policies; model now yields 25; they match.)

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Integration/Rls/ParityTests.cs ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs
git commit -m "feat(9.5b): parity + arch tests derive expected set from UserOwnedModel (closes Hole B)"
```

---

## Task 6: Delete `UserOwnedTables.cs`

**Files:**
- Delete: `ProjectCeres/Common/UserOwnedTables.cs`

- [ ] **Step 1: Confirm zero remaining references**

Run: `grep -rn "UserOwnedTables\.All\|UserOwnedTables\b" --include=*.cs ProjectCeres ProjectCeres.Tests | grep -vE "UserOwnedModel|/obj/|/bin/|FrozenUserOwnedTables|SeedDevUser.cs"`
Expected: no output. Any hit is an unmigrated reader — migrate it before deleting.

- [ ] **Step 2: Delete the file**

```bash
git rm ProjectCeres/Common/UserOwnedTables.cs
```

- [ ] **Step 3: Build the solution**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(9.5b): delete UserOwnedTables.All hand-list — model is the single source (D4)"
```

---

## Task 7: `RlsParityStartupCheck` (fail-closed boot check, D3)

**Files:**
- Create: `ProjectCeres/Common/RlsParityStartupCheck.cs`
- Modify: `ProjectCeres/Program.cs` (beside `PrivilegeLeakStartupCheck`, ~line 540)
- Test: covered by Task 9's meta-test

- [ ] **Step 1: Write the boot check.** Boot oracle = model-derived RLS set vs the policies **declared in this assembly's migrations** (NOT live `pg_class`), per D3.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProjectCeres.Common;

/// <summary>
/// Refuses to start if any user-owned table (UserOwnedModel.RlsTables) lacks an RLS
/// policy DECLARED in the assembly's migrations. Deploy-order-independent: it does not
/// query live pg_class, so a rolling deploy (binary up, migration not yet applied)
/// does not crash. The live-pg_class comparison runs in the test suite. Stage 9.5b / D3.
/// </summary>
public static class RlsParityStartupCheck
{
    public static void EnsureEveryUserOwnedTableHasADeclaredPolicy(IModel model, IReadOnlySet<string> tablesWithDeclaredPolicies)
    {
        var required = UserOwnedModel.RlsTables(model).Select(t => t.PostgresTableName);
        var missing = required.Where(t => !tablesWithDeclaredPolicies.Contains(t)).OrderBy(t => t).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "User-owned tables have no RLS policy declared in any migration — refusing to start: "
                + string.Join(", ", missing)
                + ". Add an ENABLE ROW LEVEL SECURITY + user_isolation policy migration for each, "
                + "or re-check IUserOwned. Stage 9.5b RLS-parity check.");
        }
    }
}
```

> **Sourcing `tablesWithDeclaredPolicies`:** derive from the migrations assembly's recorded operations. The most robust approach: scan `IMigrationsAssembly.Migrations` for `migrationBuilder.Sql` text containing `ENABLE ROW LEVEL SECURITY` per table is brittle; instead, maintain a small generated/curated set is what we're deleting. **Recommended:** read the set from `pg_class` of the connection the app would use for migrations IS the live-DB form (rejected by D3). The deploy-order-independent source is the migration history *in the assembly*. If extracting policy declarations from `MigrationBuilder` operations proves impractical at impl time, the fallback that still honors D3 is: assert against the live DB but ONLY for tables whose creating-migration is already in `__EFMigrationsHistory` (i.e. skip tables whose migration is pending) — surface this decision to the user before coding, as it changes the check's shape. Pin the chosen mechanism in the test (Task 9).

- [ ] **Step 2: Wire it into `Program.cs`** beside the existing check (after `var app = builder.Build();`, ~line 540), under the same gate:

```csharp
if (!app.Configuration.GetValue<bool>("Stage75:SkipPrivilegeLeakCheck"))
{
    await PrivilegeLeakStartupCheck.EnsureApplicationConnectionLacksDdlAsync(
        app.Configuration.GetConnectionString("ApplicationConnection")!);

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    RlsParityStartupCheck.EnsureEveryUserOwnedTableHasADeclaredPolicy(
        db.Model, ResolveDeclaredPolicyTables(scope)); // per Step 1 mechanism
}
```

- [ ] **Step 3: Build + boot the app**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj && dotnet run --project ProjectCeres &` then `curl -sf http://localhost:5xxx/health || true`; stop the app.
Expected: boots cleanly (all 25 tables have declared policies today).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Common/RlsParityStartupCheck.cs ProjectCeres/Program.cs
git commit -m "feat(9.5b): fail-closed RLS-parity startup check (model vs declared migrations, D3)"
```

---

## Task 8: `DualContextWebApplicationFactory` (ceres_app-wired)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/WafCollection.cs` (add `UseAppRoleConnection` virtual; gate lines 75-76)
- Create: `ProjectCeres.Tests/Integration/DualContextWebApplicationFactory.cs`

- [ ] **Step 1: Add the virtual to the base factory.** In `TestWebApplicationFactory.ConfigureWebHost`, replace the unconditional admin override:

```csharp
protected virtual bool UseAppRoleConnection => false;  // default: legacy admin routing
// ...
var appConn = UseAppRoleConnection ? AppConnectionString : AdminConnectionString;
builder.UseSetting("ConnectionStrings:ApplicationConnection", appConn);
builder.UseSetting("ConnectionStrings:AdminConnection", AdminConnectionString);
```

(`AppConnectionString` already exists as a constant in `WafCollection.cs:36`.)

- [ ] **Step 2: Create the dual-context factory**

```csharp
namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Boots the app wired to ceres_app (RLS-active) instead of ceres_admin. Exposes both
/// AppDbContext (rules enforced) and AdminDbContext (rules bypassed) so a test can seed
/// as admin and assert through the RLS-active context. Stage 9.5b.
/// SETUP DISCIPLINE: seed via AdminContext, assert via AppContext — under ceres_app,
/// inserts without an established user context are rejected (42501).
/// </summary>
public sealed class DualContextWebApplicationFactory : AuthTestWebApplicationFactory
{
    protected override bool UseAppRoleConnection => true;

    public AppDbContext NewAppContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    public AdminDbContext NewAdminContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    }
}
```

- [ ] **Step 3: Build the test project**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj`
Expected: `Build succeeded.` Existing tests untouched (they don't override `UseAppRoleConnection`, so they keep admin routing — D7).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Integration/WafCollection.cs ProjectCeres.Tests/Integration/DualContextWebApplicationFactory.cs
git commit -m "feat(9.5b): DualContextWebApplicationFactory — ceres_app-wired test fixture"
```

---

## Task 9: Meta-test + live-DB strictness test (the ship-gate)

**Files:**
- Create: `ProjectCeres.Tests/Integration/Rls/RlsParityMetaTests.cs`

- [ ] **Step 1: Write the meta-test (proves the check catches a missing rule — the input the old test got wrong) + the live strictness test + the isolation test.**

```csharp
using FluentAssertions;
using ProjectCeres.Common;
using Xunit;

namespace ProjectCeres.Tests.Integration.Rls;

[Collection("IntegrationTests")]
public class RlsParityMetaTests
{
    [Fact]
    public void StartupCheck_throws_when_a_user_owned_table_has_no_declared_policy()
    {
        // The declared-policy set deliberately OMITS one required table.
        var model = /* resolve AppDbContext.Model from the fixture */ null!;
        var declared = UserOwnedModel.RlsTables(model)
            .Select(t => t.PostgresTableName).Skip(1)            // drop one → simulate a forgotten policy
            .ToHashSet(StringComparer.Ordinal);

        var act = () => RlsParityStartupCheck
            .EnsureEveryUserOwnedTableHasADeclaredPolicy(model, declared);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*refusing to start*");
    }

    [Fact]
    public async Task Every_user_owned_table_has_rowsecurity_AND_forcerowsecurity_in_the_live_db()
    {
        // D1: both flags. Query pg_class for each UserOwnedModel.RlsTables entry.
        // (reuse ParityTests' existing pg_class query shape)
        var model = /* fixture model */ null!;
        foreach (var t in UserOwnedModel.RlsTables(model))
        {
            var (rls, force) = await QueryPgClassFlags(t.PostgresTableName); // helper per ParityTests
            rls.Should().BeTrue($"{t.PostgresTableName} must have rowsecurity");
            force.Should().BeTrue($"{t.PostgresTableName} must have forcerowsecurity");
        }
    }

    [Fact]
    public async Task App_context_cannot_see_a_row_another_user_owns()
    {
        using var factory = new DualContextWebApplicationFactory();
        var userA = Guid.NewGuid();
        // seed via ADMIN (bypasses RLS); assert via APP (RLS-active) as a DIFFERENT user.
        await using (var admin = factory.NewAdminContext())
        {
            admin.Accounts.Add(new Account { /* minimal valid, UserId = userA */ });
            await admin.SaveChangesAsync();
        }
        await using var app = factory.NewAppContext(); // bound to ceres_app; current user != userA
        (await app.Accounts.CountAsync(a => a.UserId == userA)).Should().Be(0);
    }
}
```

> Fill the `model`/`QueryPgClassFlags`/account-construction blanks against the existing `ParityTests` + `RlsTestFixture` helpers (they already do the `pg_class` query and minimal-`Account` construction). Do not invent new helpers if equivalents exist.

- [ ] **Step 2: Run — meta-test must pass (check throws), strictness must pass (DB is correct today), isolation must pass**

Run: `dotnet test --filter "FullyQualifiedName~RlsParityMetaTests"`
Expected: PASS (3 tests). If the isolation test fails on *setup* (42501), the seed went through the app context — move it to admin (the discipline note).

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Rls/RlsParityMetaTests.cs
git commit -m "feat(9.5b): meta-test proves RLS-parity check throws on a missing policy + live strictness + isolation"
```

---

## Task 10: Architecture test — AppDbContext vs AdminDbContext discipline

**Files:**
- Create: `ProjectCeres.Tests/Integration/Rls/AdminContextDisciplineTests.cs`

- [ ] **Step 1: Write the test + its exact-type meta-test.** Scan `ProjectCeres.Controllers`, `ProjectCeres.Controllers.Api`, `ProjectCeres.Services`. Any type with a constructor parameter or field of EXACT type `AdminDbContext` must carry `[RequiresAdminContext]` (class or method). Exact-type, not `IsAssignableFrom` (the subclass trap).

```csharp
using System.Reflection;
using FluentAssertions;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Data;
using Xunit;

namespace ProjectCeres.Tests.Integration.Rls;

public class AdminContextDisciplineTests
{
    private static readonly string[] ScannedNamespaces =
        { "ProjectCeres.Controllers", "ProjectCeres.Controllers.Api", "ProjectCeres.Services" };

    private static bool InjectsAdminContext(Type t) =>
        t.GetConstructors().SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(AdminDbContext))      // EXACT type
        || t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Any(f => f.FieldType == typeof(AdminDbContext));

    private static bool HasMarker(Type t) =>
        t.GetCustomAttribute<RequiresAdminContextAttribute>() is not null
        || t.GetMethods().Any(m => m.GetCustomAttribute<RequiresAdminContextAttribute>() is not null);

    [Fact]
    public void Controllers_and_services_using_AdminDbContext_must_carry_RequiresAdminContext()
    {
        var offenders = typeof(AppDbContext).Assembly.GetTypes()
            .Where(t => t.Namespace is not null && ScannedNamespaces.Any(ns => t.Namespace.StartsWith(ns, StringComparison.Ordinal)))
            .Where(InjectsAdminContext)
            .Where(t => !HasMarker(t))
            .Select(t => t.FullName)
            .ToList();
        offenders.Should().BeEmpty("admin (BYPASSRLS) context use in Controllers/Services must be declared with [RequiresAdminContext]");
    }

    [Fact]
    public void Meta_an_undecorated_AdminDbContext_field_is_detected()
    {
        InjectsAdminContext(typeof(FixtureWithUndecoratedAdmin)).Should().BeTrue();
        HasMarker(typeof(FixtureWithUndecoratedAdmin)).Should().BeFalse();
    }

    private sealed class FixtureWithUndecoratedAdmin { private readonly AdminDbContext _db = null!; }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test --filter "FullyQualifiedName~AdminContextDisciplineTests"`
Expected: PASS. If `Controllers_and_services...` fails, it found a real undeclared admin user in the scanned namespaces — add `[RequiresAdminContext]` to that type (it's a legitimate pre-auth case) rather than weakening the test. Record which types got marked in the commit message.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Rls/AdminContextDisciplineTests.cs
git commit -m "feat(9.5b): architecture test pins Controllers/Services to AppDbContext unless [RequiresAdminContext]"
```

---

## Task 11: Docs — discharge line 1094, de-stale, note

**Files:**
- Modify: `docs/roadmap-phase-three.md` (line 1094 discharge + tick the 9.5b checklist line 1254)
- Modify: `docs/multi-tenancy-strategy.md` (stale 8-entity list → model-derived note)
- Modify: `docs/security-model.md` (RLS section note)

- [ ] **Step 1: Discharge roadmap line 1094.** Mark the "Decide test-infrastructure parity strategy" `[ ]` as `[x]`, recording option (b): "9.5b ships `DualContextWebApplicationFactory` (the `ceres_app` capability + new RLS tests use it); the full auth-suite switch is Stage 9.5d. Documented in `docs/testing.md`."

- [ ] **Step 2: Tick the 9.5b checklist line** (1254): `[ ]` → `[x]` with a close-out note (B1 = fail-closed startup check shipped; B2 = `UserOwnedTables.All` deleted, `UserOwnedModel` is the source; commit range).

- [ ] **Step 3: De-stale `docs/multi-tenancy-strategy.md`.** Replace the 8-entity list + the "attachments/auth tables don't get UserId" claim with the 25-table model-derived reality. This overturns a documented decision → run the sync-docs supersession sweep (`rg` for the old claim across `docs/`).

- [ ] **Step 4: Add the `docs/security-model.md` note** — the user-owned set is now model-derived and boot-verified by `RlsParityStartupCheck`.

- [ ] **Step 5: Commit**

```bash
git add docs/roadmap-phase-three.md docs/multi-tenancy-strategy.md docs/security-model.md docs/testing.md
git commit -m "docs(9.5b): discharge roadmap line 1094; de-stale multi-tenancy-strategy; security-model note"
```

---

## Final verification (before stage close-out)

- [ ] `dotnet build` — 0 errors, 0 CER errors
- [ ] `dotnet test` — full suite green (no count regression vs pre-9.5b baseline)
- [ ] `grep -rn "UserOwnedTables" --include=*.cs ProjectCeres ProjectCeres.Tests | grep -vE "UserOwnedModel|FrozenUserOwnedTables"` → only the migration's frozen-list comment + SeedDevUser if any residual
- [ ] Evidence bundle (`.cs` writes touch `UserOwnedTables.cs`/`Models` paths → `registry-sweep.json` required): run `build-matrix.sh 9.5b` + turn-shape generator
- [ ] `verify-stage-completeness` skill: new `RlsParityStartupCheck` is a new service — confirm it needs no IUserOwned registries (it doesn't; it's a static check)
- [ ] Stage close-out via playbook Phase E (sync-docs + changelog-sync fired; zero unchecked `[ ]` under the 9.5b section)
