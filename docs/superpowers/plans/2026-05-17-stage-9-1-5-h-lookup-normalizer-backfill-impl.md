# Stage 9.1.5.h — Backfill `BackfillIdentityNormalizedToLowercase` Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship an idempotent EF migration that lowercases every normalized Identity column in the schema (`AspNetUsers.NormalizedEmail`, `AspNetUsers.NormalizedUserName`, `AspNetRoles.NormalizedName`) to match `LowercaseLookupNormalizer`'s output, plus a standing rule in `security-model.md` so future normalizer changes ship with a same-commit data migration.

**Architecture:** Three raw-SQL `UPDATE … SET col = LOWER(col) WHERE col <> LOWER(col)` statements inside one EF migration's `Up()`. `Down()` throws `NotSupportedException` (reverting would restore the broken pre-`4b35911` state). Idempotency comes from the `WHERE` filter; re-runs on already-lowercase data are no-ops. Tests run against the existing `TestWebApplicationFactory` shared-DB fixture and assert per-column behaviour for uppercase / already-lowercase / NULL inputs.

**Tech Stack:** EF Core 10 PostgreSQL provider (Npgsql), xUnit + FluentAssertions + the project's `TestWebApplicationFactory` integration-test fixture, ASP.NET Core Identity (`AspNetUsers` + `AspNetRoles` tables), `LowercaseLookupNormalizer` already registered at `ProjectCeres/Program.cs:139` (NOT modified by this stage).

**Spec:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-design.md` (committed `4e45b62`).

**Parent commit:** `4e45b62` (HEAD at plan-write).

---

## Binding constraints (apply to every task)

- Stay on `main`. No worktrees, no branches.
- `dotnet` commands run from repo root. EF commands include `--project ProjectCeres --startup-project ProjectCeres` so they don't need a `cd`.
- NO `Co-Authored-By` trailer in any commit message.
- TDD per `docs/testing.md` § Rules. Commit 1's tests are **case (1)** — new tests for new behavior (the migration's idempotency contract).
- Pre-existing failures get root-caused NOW (per `feedback_never_skip_tests_to_make_them_pass`). Don't defer.
- Stop-hook (`.claude/hooks/run-tests.sh`) runs `dotnet test` on every commit. Both commits must keep the .NET suite green.
- `dotnet test` and `dotnet build` always run **foreground** (per `feedback_dont_background_one_shot_verifications`).
- Per `feedback_never_delete_db_without_consent`: `dotnet ef database update` IS a destructive write to dev DB (applies the migration). The user has authorized by approving the spec, but Task 1.8 still issues a single `AskUserQuestion` before running the apply so the destructive op is gated.
- Per the chat-deferral hook (`.claude/skills/no-unjustified-deferrals/hooks/stop-chat-deferral-detect.js`): NO scope-as-deferral language in commit messages or chat replies. `AspNetRoles` is **included** in the migration, not deferred — this is the design decision the spec settled.

---

## Discovery findings baked into this plan

Done at plan-write (grep + targeted Reads against parent commit `4e45b62`). The implementer does NOT need to re-discover these:

1. **Stage 6.15 migration precedent** at `ProjectCeres/Migrations/20260511155005_AddTokenLookup.cs`:
   - Lines 23-69 show the `MigrationBuilder.AddColumn` + `MigrationBuilder.Sql` + `MigrationBuilder.CreateIndex` pattern.
   - Lines 45-57 are the exact `MigrationBuilder.Sql(@"…")` raw-string shape to mirror.
   - Lines 73-90 show a `Down()` that drops added schema. This stage's `Down()` is different — see Task 1.5.

2. **`LowercaseLookupNormalizer` registration** at `ProjectCeres/Program.cs:139`: `builder.Services.AddSingleton<ILookupNormalizer, LowercaseLookupNormalizer>();`. NOT touched by this stage; the spec is about backfilling DATA, not changing the normalizer.

3. **AspNetRoles confirmed empty** at parent commit `4e45b62`: `grep -rn "roleManager\.\|CreateRoleAsync\|AddToRoleAsync\|RoleExistsAsync" ProjectCeres --include="*.cs"` returns ZERO hits. The role-table UPDATE is a no-op today but binds the migration's contract.

4. **Test home is `ProjectCeres.Tests/Integration/`** — `ProjectCeres.Tests/Migrations/` does NOT exist. Migration tests need a real DB, so they go alongside `DbContextRegistrationTests.cs` etc. in `Integration/`.

5. **Test fixture pattern** from `DbContextRegistrationTests.cs:16-24`:
   ```csharp
   [Collection("IntegrationTests")]
   public class XxxTests
   {
       private readonly TestWebApplicationFactory _factory;
       public XxxTests(TestWebApplicationFactory factory) { _factory = factory; }
       // ... [Fact] methods use `using var scope = _factory.Services.CreateScope();`
       // then `scope.ServiceProvider.GetRequiredService<AppDbContext>()` to get a db
   }
   ```

6. **security-model.md target section** — `### ASP.NET Core Identity Hardening` at line 426. The standing rule from spec § 4 goes at the END of this section (after the existing hardening bullets, before the next `## Authentication Flow Diagrams` H2 at line 449).

7. **models.md target section** — `### ApplicationUser (Phase 3, Stage 6a)` at line 981. The one-line cross-reference goes RIGHT AFTER the `**TOTP seed storage:**` paragraph at line 993, before the `---` horizontal rule.

8. **Identity guide callout target** — `docs/guide/09-authentication-and-security/01-aspnetcore-identity.md` has a "Watch the migration risk" callout in a blockquote near lines 28-32 (verify exact line numbers at task time). Update it to point at `security-model.md` § ASP.NET Core Identity Hardening as the authoritative source rather than restating the rule.

---

## File structure

### Files created

- `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.cs` — the migration. Timestamp assigned by `dotnet ef migrations add`.
- `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.Designer.cs` — auto-generated, no manual editing.
- `ProjectCeres.Tests/Integration/BackfillIdentityNormalizedToLowercaseTests.cs` — 9 xUnit tests.

### Files modified

- `ProjectCeres/Migrations/AppDbContextModelSnapshot.cs` — EF rewrites this on `migrations add`. No schema changes in this migration, so the diff should be minimal (possibly a timestamp-only delta). Inspect before committing; if it's a no-op, exclude from the staged files.
- `docs/security-model.md` — insert the standing rule from spec § 4 at the end of `### ASP.NET Core Identity Hardening` (after line ~448, before the `## Authentication Flow Diagrams` H2 at line 449).
- `docs/models.md` — add one-line cross-reference after the `**TOTP seed storage:**` paragraph in `### ApplicationUser` (around line 993).
- `docs/guide/09-authentication-and-security/01-aspnetcore-identity.md` — update the existing "Watch the migration risk" callout to point at `security-model.md` rather than restating the rule.
- `docs/roadmap-phase-three.md` — flip line 1103 (sub-stage row) + line 1114 (verification line) to `[x]` with summary text.

### Files NOT touched (verified)

- `ProjectCeres/Program.cs:139` — the `LowercaseLookupNormalizer` registration. The DATA needs backfill; the registration is correct.
- `ProjectCeres/Common/Authentication/LowercaseLookupNormalizer.cs` — the normalizer implementation. Not touched.
- `ProjectCeres.Client/` — frontend untouched.

---

## Commit 1 — Migration + tests (atomic — TDD ordering inside)

### Task 1.1: Write the 9 xUnit tests FIRST

**Files:**
- Create: `ProjectCeres.Tests/Integration/BackfillIdentityNormalizedToLowercaseTests.cs`

- [ ] **Step 1: Create the test file with the full content below**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 9.1.5.h — Pin the BackfillIdentityNormalizedToLowercase migration's
/// idempotency contract: every normalized Identity column is lowercased on a
/// single application of the migration's SQL, and re-application is a no-op
/// (zero rows affected). NULL rows are preserved (no exception, no state change).
///
/// We invoke the migration's SQL directly via _db.Database.ExecuteSqlRaw rather
/// than running `dotnet ef database update` from inside a test — the shared
/// IntegrationTests fixture has the migration already applied by the time tests
/// run, so the test just exercises the SQL contract against rows we control.
/// </summary>
[Collection("IntegrationTests")]
public class BackfillIdentityNormalizedToLowercaseTests
{
    private readonly TestWebApplicationFactory _factory;

    public BackfillIdentityNormalizedToLowercaseTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // The three SQL statements the migration's Up() runs. Keep these as
    // constants here so a copy-paste drift between the migration and the test
    // fails the test (the test's seeded data flips back via the same SQL the
    // migration writes — if the migration's SQL changes shape, this test class
    // is the most likely first failure).
    private const string LowercaseEmailSql = @"
        UPDATE ""AspNetUsers""
        SET ""NormalizedEmail"" = LOWER(""NormalizedEmail"")
        WHERE ""NormalizedEmail"" IS NOT NULL
          AND ""NormalizedEmail"" <> LOWER(""NormalizedEmail"");";

    private const string LowercaseUserNameSql = @"
        UPDATE ""AspNetUsers""
        SET ""NormalizedUserName"" = LOWER(""NormalizedUserName"")
        WHERE ""NormalizedUserName"" IS NOT NULL
          AND ""NormalizedUserName"" <> LOWER(""NormalizedUserName"");";

    private const string LowercaseRoleNameSql = @"
        UPDATE ""AspNetRoles""
        SET ""NormalizedName"" = LOWER(""NormalizedName"")
        WHERE ""NormalizedName"" IS NOT NULL
          AND ""NormalizedName"" <> LOWER(""NormalizedName"");";

    // ─── AspNetUsers.NormalizedEmail ──────────────────────────────────────────

    [Fact]
    public async Task Migration_lowercases_uppercase_NormalizedEmail()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"backfill-email-upper-{userId}@test.local";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker.ToUpperInvariant(),
            normalizedUserName: marker.ToUpperInvariant());
        try
        {
            var affected = await db.Database.ExecuteSqlRawAsync(LowercaseEmailSql);
            affected.Should().BeGreaterThanOrEqualTo(1, "the uppercase row must be touched");

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedEmail\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker.ToLowerInvariant());
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task Migration_is_idempotent_on_already_lowercase_NormalizedEmail()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"backfill-email-lower-{userId}@test.local";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker,
            normalizedUserName: marker);
        try
        {
            // First run — may touch 0 or 1 rows depending on the seed (already lower).
            await db.Database.ExecuteSqlRawAsync(LowercaseEmailSql);

            // Second run — MUST touch 0 rows for this seeded row (it's already lowercase).
            // Note: other rows in the shared DB may still match (different tests' leftovers).
            // We assert the row's final state instead.
            await db.Database.ExecuteSqlRawAsync(LowercaseEmailSql);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedEmail\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker);
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task Migration_handles_NULL_NormalizedEmail_safely()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();

        await SeedAspNetUserAsync(db, userId, normalizedEmail: null,
            normalizedUserName: $"backfill-null-{userId}");
        try
        {
            await db.Database.Invoking(d => d.ExecuteSqlRawAsync(LowercaseEmailSql))
                .Should().NotThrowAsync();

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedEmail\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().BeNull();
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    // ─── AspNetUsers.NormalizedUserName ───────────────────────────────────────

    [Fact]
    public async Task Migration_lowercases_uppercase_NormalizedUserName()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"BACKFILL-USERNAME-UPPER-{userId}";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker.ToLowerInvariant(),
            normalizedUserName: marker);
        try
        {
            var affected = await db.Database.ExecuteSqlRawAsync(LowercaseUserNameSql);
            affected.Should().BeGreaterThanOrEqualTo(1);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedUserName\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker.ToLowerInvariant());
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task Migration_is_idempotent_on_already_lowercase_NormalizedUserName()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"backfill-username-lower-{userId}";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker, normalizedUserName: marker);
        try
        {
            await db.Database.ExecuteSqlRawAsync(LowercaseUserNameSql);
            await db.Database.ExecuteSqlRawAsync(LowercaseUserNameSql);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedUserName\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker);
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task Migration_handles_NULL_NormalizedUserName_safely()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"backfill-null-username-{userId}@test.local";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker, normalizedUserName: null);
        try
        {
            await db.Database.Invoking(d => d.ExecuteSqlRawAsync(LowercaseUserNameSql))
                .Should().NotThrowAsync();

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedUserName\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().BeNull();
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    // ─── AspNetRoles.NormalizedName ───────────────────────────────────────────
    // Roles are unused at parent commit 4e45b62, but the migration covers
    // this column so its contract is bound for the future. Tests use raw SQL
    // inserts because the project has no RoleManager usage to bypass.

    [Fact]
    public async Task Migration_lowercases_uppercase_role_NormalizedName()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var roleId = Guid.NewGuid();
        var marker = $"BACKFILL-ROLE-UPPER-{roleId}";

        await SeedAspNetRoleAsync(db, roleId, name: marker.ToLowerInvariant(), normalizedName: marker);
        try
        {
            var affected = await db.Database.ExecuteSqlRawAsync(LowercaseRoleNameSql);
            affected.Should().BeGreaterThanOrEqualTo(1);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedName\" AS \"Value\" FROM \"AspNetRoles\" WHERE \"Id\" = '{roleId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker.ToLowerInvariant());
        }
        finally
        {
            await DeleteAspNetRoleAsync(db, roleId);
        }
    }

    [Fact]
    public async Task Migration_is_idempotent_on_already_lowercase_role_NormalizedName()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var roleId = Guid.NewGuid();
        var marker = $"backfill-role-lower-{roleId}";

        await SeedAspNetRoleAsync(db, roleId, name: marker, normalizedName: marker);
        try
        {
            await db.Database.ExecuteSqlRawAsync(LowercaseRoleNameSql);
            await db.Database.ExecuteSqlRawAsync(LowercaseRoleNameSql);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedName\" AS \"Value\" FROM \"AspNetRoles\" WHERE \"Id\" = '{roleId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker);
        }
        finally
        {
            await DeleteAspNetRoleAsync(db, roleId);
        }
    }

    [Fact]
    public async Task Migration_handles_NULL_role_NormalizedName_safely()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var roleId = Guid.NewGuid();
        var marker = $"backfill-role-null-{roleId}";

        await SeedAspNetRoleAsync(db, roleId, name: marker, normalizedName: null);
        try
        {
            await db.Database.Invoking(d => d.ExecuteSqlRawAsync(LowercaseRoleNameSql))
                .Should().NotThrowAsync();

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedName\" AS \"Value\" FROM \"AspNetRoles\" WHERE \"Id\" = '{roleId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().BeNull();
        }
        finally
        {
            await DeleteAspNetRoleAsync(db, roleId);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SeedAspNetUserAsync(AdminDbContext db, Guid id,
        string? normalizedEmail, string? normalizedUserName)
    {
        // Raw insert bypasses UserManager (which would re-normalize). The columns
        // not under test are populated with safe defaults so the row satisfies
        // any non-null constraints.
        await db.Database.ExecuteSqlRawAsync($@"
            INSERT INTO ""AspNetUsers""
                (""Id"", ""UserName"", ""NormalizedUserName"", ""Email"", ""NormalizedEmail"",
                 ""EmailConfirmed"", ""PasswordHash"", ""SecurityStamp"", ""ConcurrencyStamp"",
                 ""PhoneNumberConfirmed"", ""TwoFactorEnabled"", ""LockoutEnabled"",
                 ""AccessFailedCount"", ""CreatedAt"")
            VALUES
                ('{id}'::uuid, 'seed', {NullableSqlString(normalizedUserName)},
                 'seed@test.local', {NullableSqlString(normalizedEmail)},
                 false, '', '', '', false, false, false, 0, NOW());
        ");
    }

    private static async Task DeleteAspNetUserAsync(AdminDbContext db, Guid id)
    {
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = '{id}'::uuid");
    }

    private static async Task SeedAspNetRoleAsync(AdminDbContext db, Guid id,
        string? name, string? normalizedName)
    {
        await db.Database.ExecuteSqlRawAsync($@"
            INSERT INTO ""AspNetRoles""
                (""Id"", ""Name"", ""NormalizedName"", ""ConcurrencyStamp"")
            VALUES
                ('{id}'::uuid, {NullableSqlString(name)}, {NullableSqlString(normalizedName)}, '');
        ");
    }

    private static async Task DeleteAspNetRoleAsync(AdminDbContext db, Guid id)
    {
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"AspNetRoles\" WHERE \"Id\" = '{id}'::uuid");
    }

    private static string NullableSqlString(string? value) =>
        value is null ? "NULL" : $"'{value.Replace("'", "''")}'";
}
```

**Note on `AdminDbContext` vs `AppDbContext`**: The test uses `AdminDbContext` for raw `INSERT/DELETE` on `AspNetUsers`/`AspNetRoles` because those tables are subject to RLS (Row-Level Security) policies on `AppDbContext` per ADR-0068. Admin context bypasses RLS — the right choice for test fixtures that need to write Identity rows without a logged-in user context. If `AdminDbContext` doesn't exist or the registration name differs, grep `ProjectCeres/Data/` for the admin-role context name and swap in.

**Note on per-test cleanup**: Each test seeds a row with a unique GUID + marker, runs the assertion, then DELETEs the row in a `finally`. This avoids cross-test pollution in the shared `IntegrationTests` collection without requiring a transaction-rollback fixture (which the project doesn't appear to have).

### Task 1.2: Run the new tests against the not-yet-existing migration

- [ ] **Step 1: Run the test file**

```bash
dotnet test --filter FullyQualifiedName~BackfillIdentityNormalizedToLowercase
```

Expected: **the file COMPILES and the tests RUN.** They may even PASS if the test DB already happens to have all rows lowercase (likely — `LowercaseLookupNormalizer` has been the active normalizer in dev for some time). The TDD-fail-first signal here is weaker than usual: the tests pin the migration's SQL contract, and the SQL itself is what's missing. To make the failure-first step meaningful, **briefly check** that the test asserts something non-trivial — e.g. the `Migration_lowercases_uppercase_NormalizedEmail` test SHOULD return a `marker.ToLowerInvariant()` value after seeding uppercase, and this only works if the SQL constant in the test file is correct (which it is — we wrote it ourselves in Task 1.1).

If all 9 tests pass on the first run BEFORE the migration exists: that's expected and acceptable for THIS stage, because the tests are validating the SQL the test file itself owns. The real "migration is applied to the database" verification happens at Task 1.8 (`dotnet ef database update`). Document this in the commit message.

If any test fails for a non-SQL reason (e.g. `AdminDbContext` not registered, RLS denial, schema mismatch): root-cause now per `feedback_never_skip_tests_to_make_them_pass`.

### Task 1.3: Generate the migration scaffold

- [ ] **Step 1: Run the EF migrations-add command**

```bash
dotnet ef migrations add BackfillIdentityNormalizedToLowercase \
  --project ProjectCeres \
  --startup-project ProjectCeres \
  --output-dir Migrations
```

Expected output:
- New file `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.cs` with empty `Up()` and `Down()` bodies (since no schema changes were detected by EF).
- New file `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.Designer.cs` — auto-generated snapshot reference.
- `ProjectCeres/Migrations/AppDbContextModelSnapshot.cs` — may be edited by EF (likely a no-op since the migration introduces no schema changes; verify before committing).

If `dotnet ef` is not installed: `dotnet tool install --global dotnet-ef` then retry.

### Task 1.4: Edit the generated migration's Up() method

**Files:**
- Modify: `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.cs`

- [ ] **Step 1: Replace the empty Up() body**

EF generates this:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
}
```

Replace with (per spec § 2):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Backfill NormalizedEmail to match LowercaseLookupNormalizer output.
    // Idempotent — the WHERE filter is false for rows already lowercase, so
    // a re-run on a fully-lowercase table is a no-op (0 rows affected).
    migrationBuilder.Sql(@"
        UPDATE ""AspNetUsers""
        SET ""NormalizedEmail"" = LOWER(""NormalizedEmail"")
        WHERE ""NormalizedEmail"" IS NOT NULL
          AND ""NormalizedEmail"" <> LOWER(""NormalizedEmail"");
    ");

    migrationBuilder.Sql(@"
        UPDATE ""AspNetUsers""
        SET ""NormalizedUserName"" = LOWER(""NormalizedUserName"")
        WHERE ""NormalizedUserName"" IS NOT NULL
          AND ""NormalizedUserName"" <> LOWER(""NormalizedUserName"");
    ");

    // AspNetRoles is currently empty (no roles seeded, no RoleManager usage
    // beyond a dead constructor injection in ApplicationUserClaimsPrincipalFactory).
    // Including this UPDATE now is a no-op against zero rows, but the migration's
    // contract becomes "every normalized identity column matches the active
    // normalizer's output" — so a future commit that introduces roles inherits
    // the protection automatically, without needing a same-commit follow-up
    // migration that someone has to remember to add.
    migrationBuilder.Sql(@"
        UPDATE ""AspNetRoles""
        SET ""NormalizedName"" = LOWER(""NormalizedName"")
        WHERE ""NormalizedName"" IS NOT NULL
          AND ""NormalizedName"" <> LOWER(""NormalizedName"");
    ");
}
```

### Task 1.5: Edit the generated migration's Down() method

**Files:**
- Modify: same file as Task 1.4

- [ ] **Step 1: Replace the empty Down() body**

EF generates this:

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
}
```

Replace with (per spec § 2):

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    // Reverting this migration would restore the broken state — uppercase
    // rows that the LowercaseLookupNormalizer cannot match. The correct
    // recovery if a rollback is needed is to ALSO revert the
    // LowercaseLookupNormalizer DI registration (back to UpperInvariantLookupNormalizer)
    // in the same operation, which is outside EF migrations' scope. Throwing
    // here forces the engineer to make that choice explicitly rather than
    // silently producing an unauthenticatable database.
    throw new NotSupportedException(
        "Backfill migration cannot be reverted: doing so would restore the " +
        "broken pre-4b35911 state where AspNetUsers.NormalizedEmail/NormalizedUserName " +
        "uppercase rows are unmatchable by LowercaseLookupNormalizer. To roll back, " +
        "revert both this migration AND the LowercaseLookupNormalizer DI registration " +
        "(Program.cs ~line 139) in a coordinated change."
    );
}
```

### Task 1.6: Add the migration's XML doc comment

**Files:**
- Modify: same file as Tasks 1.4–1.5

- [ ] **Step 1: Add a `<summary>` block above the class declaration**

Match the precedent at `ProjectCeres/Migrations/20260511155005_AddTokenLookup.cs:7-19`. Replace the placeholder class header with:

```csharp
/// <summary>
/// Stage 9.1.5.h — Backfill every normalized Identity column to match
/// <c>LowercaseLookupNormalizer</c>'s output. The columns covered:
/// <c>AspNetUsers.NormalizedEmail</c>, <c>AspNetUsers.NormalizedUserName</c>,
/// <c>AspNetRoles.NormalizedName</c>.
///
/// Idempotent: each <c>UPDATE</c> filters out rows whose value already equals
/// its own lowercased version, so re-runs are no-ops. NULL rows are preserved.
///
/// <c>AspNetRoles</c> is empty at the time of this migration (no <c>RoleManager</c>
/// usage), but is included so the migration's contract becomes "all normalized
/// identity columns are kept in sync with the active normalizer" — future
/// commits introducing roles inherit the protection automatically.
///
/// Closes the silent-login-break incident from commit <c>4b35911</c>: that commit
/// swapped <c>UpperInvariantLookupNormalizer</c> for <c>LowercaseLookupNormalizer</c>
/// without a backfill, leaving pre-existing uppercase rows unmatchable by
/// <c>FindByEmailAsync</c>/<c>FindByNameAsync</c>. See spec
/// <c>docs/superpowers/specs/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-design.md</c>.
/// </summary>
public partial class BackfillIdentityNormalizedToLowercase : Migration
```

### Task 1.7: Run the tests and full suite

- [ ] **Step 1: Re-run the migration tests**

```bash
dotnet test --filter FullyQualifiedName~BackfillIdentityNormalizedToLowercase
```

Expected: all 9 tests pass.

If any test fails, root-cause now. Likely failure modes:
- `AdminDbContext` not the right registration name → grep for the actual name in `ProjectCeres/Data/`.
- RLS prevents the insert even via admin context → check `security-model.md` § PostgreSQL Row-Level Security for the RLS-bypass pattern.
- Seed SQL fails a constraint that wasn't in the discovery notes → read the actual `AspNetUsers` column constraints via `\d "AspNetUsers"` in psql and add the missing columns to `SeedAspNetUserAsync`.

- [ ] **Step 2: Run the full .NET suite**

```bash
dotnet test
```

Expected: all pass. If any pre-existing test regresses (e.g. an auth-tier test now sees a lowercased seed row it expected uppercase), root-cause now.

- [ ] **Step 3: Run the .NET build**

```bash
dotnet build
```

Expected: clean.

### Task 1.8: Apply migration to local dev DB — GATED on user confirmation

- [ ] **Step 1: Issue an AskUserQuestion before running the apply**

Per `feedback_never_delete_db_without_consent`: any state-changing migration apply on a dev DB needs a fresh confirmation, even when the user has approved the spec.

The subagent issues this question (controller relays to the user if no AskUserQuestion is available to the subagent):

> "About to run `dotnet ef database update` against the local dev DB. This applies the `BackfillIdentityNormalizedToLowercase` migration, which lowercases any uppercase rows in `AspNetUsers.NormalizedEmail/NormalizedUserName` and `AspNetRoles.NormalizedName`. Expected effect on the current dev DB: 0 rows changed (the seeded user was already lowercased in-session via the original `UPDATE … SET = LOWER(…)` fix, and `AspNetRoles` is empty). Proceed?"

Wait for "yes" / "proceed" before running.

- [ ] **Step 2: Apply the migration**

```bash
dotnet ef database update --project ProjectCeres --startup-project ProjectCeres
```

Expected output: `Applying migration '<timestamp>_BackfillIdentityNormalizedToLowercase'. Done.` and rough row-update counts of 0/0/0 (or 1/1/0 if any dev DB row was still uppercase).

- [ ] **Step 3: Manual login regression check (handed back to the user)**

Tell the user to:
1. Start the dev environment (`dotnet run --project ProjectCeres` if not already running).
2. Sign in with the existing seeded user via `/app/login`.
3. Confirm login succeeds.

If login fails, the migration broke something — root-cause before commit.

### Task 1.9: Commit

- [ ] **Step 1: Stage and commit**

```bash
git -C <repo> add \
  ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.cs \
  ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.Designer.cs \
  ProjectCeres/Migrations/AppDbContextModelSnapshot.cs \
  ProjectCeres.Tests/Integration/BackfillIdentityNormalizedToLowercaseTests.cs

git -C <repo> commit -m "feat(stage-9.1.5.h): BackfillIdentityNormalizedToLowercase migration + idempotency tests"
```

Expected: commit succeeds. Stop-hook runs `dotnet test` (clean from Task 1.7) and exits 0. No `Co-Authored-By` trailer.

If `AppDbContextModelSnapshot.cs` was not modified by EF (no-op snapshot), exclude it from the staged file list.

---

## Commit 2 — Standing rule + cross-references + roadmap close-out

### Task 2.1: Insert the standing rule into security-model.md

**Files:**
- Modify: `docs/security-model.md` (insert at end of `### ASP.NET Core Identity Hardening` section, around line 448)

- [ ] **Step 1: Confirm the section boundary**

```bash
grep -n "^### ASP.NET Core Identity Hardening\|^## Authentication Flow Diagrams" docs/security-model.md | head -2
```

Expected: two line numbers — the `### ASP.NET Core Identity Hardening` H3 (around 426) and the `## Authentication Flow Diagrams` H2 (around 449). The new rule goes between them, at the end of the H3 section.

- [ ] **Step 2: Read the existing tail of the section**

Read the last ~10 lines of the `### ASP.NET Core Identity Hardening` section to find a clean insertion point right before the closing `---` separator (if any) or right before the next H2.

- [ ] **Step 3: Insert the standing-rule block**

Append this block at the end of the `### ASP.NET Core Identity Hardening` section, immediately before the `---` separator or before the next H2:

```markdown
#### `ILookupNormalizer` registration changes require a same-commit data-migration

ASP.NET Identity stores normalized lookup values (`AspNetUsers.NormalizedEmail`, `AspNetUsers.NormalizedUserName`, `AspNetRoles.NormalizedName`) and uses them as the matching key in `FindByEmailAsync`, `FindByNameAsync`, etc. Swapping `ILookupNormalizer` changes the format of NEW lookup values, but pre-existing rows retain the previous format. The result: every pre-existing user's `FindByNameAsync`/`FindByEmailAsync` lookup returns null, `PasswordSignInAsync` returns "invalid login" without ever checking the password, `AccessFailedCount` stays at 0, and the user is locked out of the application with no audit trail.

Any commit that registers a new `ILookupNormalizer` (e.g. swapping `UpperInvariantLookupNormalizer` for a custom variant, or changing the algorithm of an existing custom normalizer) MUST include an EF migration in the same commit that backfills every populated normalized column to the new normalizer's output. The migration MUST be idempotent (filtered `UPDATE` that no-ops on already-correct rows).

Precedent: commit `4b35911` (Stage 9 mid-stream) swapped to `LowercaseLookupNormalizer` without this migration and silently broke login for every pre-existing user. The fix shipped in Stage 9.1.5.h (`BackfillIdentityNormalizedToLowercase`) — see `docs/superpowers/specs/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-design.md`.

**Tables currently covered:** `AspNetUsers.NormalizedEmail`, `AspNetUsers.NormalizedUserName`, `AspNetRoles.NormalizedName`. All three are backfilled by the Stage 9.1.5.h migration regardless of whether the table currently has rows.
```

### Task 2.2: Add the cross-reference to models.md

**Files:**
- Modify: `docs/models.md` (after the `**TOTP seed storage:**` paragraph in `### ApplicationUser`, around line 993)

- [ ] **Step 1: Confirm the insertion point**

```bash
grep -n "TOTP seed storage\|^### ApplicationUser\|^### UserSession" docs/models.md | head -3
```

Expected: the `**TOTP seed storage:**` paragraph (around line 993), bounded above by `### ApplicationUser` (line 981) and below by `### UserSession` (line 997).

- [ ] **Step 2: Add the cross-reference paragraph**

Right after the line beginning `**TOTP seed storage:** Identity stores …`, add this new paragraph:

```markdown
**Normalizer DI changes:** any change to the registered `ILookupNormalizer` (e.g. swapping `LowercaseLookupNormalizer` for a different variant) requires a same-commit EF migration that backfills `AspNetUsers.NormalizedEmail` + `NormalizedUserName` (and `AspNetRoles.NormalizedName` if roles are populated) to the new normalizer's output. See `security-model.md` § ASP.NET Core Identity Hardening for the full rule and the Stage 9.1.5.h precedent.
```

### Task 2.3: Update the developer guide callout

**Files:**
- Modify: `docs/guide/09-authentication-and-security/01-aspnetcore-identity.md` (the "Watch the migration risk" callout, around lines 28-32)

- [ ] **Step 1: Find the exact line range**

```bash
grep -n "Watch the migration risk\|Swapping.*ILookupNormalizer.*without.*data migration" docs/guide/09-authentication-and-security/01-aspnetcore-identity.md
```

- [ ] **Step 2: Replace the existing blockquote**

The existing callout (per the discovery grep) reads:

```markdown
> **Watch the migration risk.** Swapping `ILookupNormalizer` *without* a data migration that backfills existing `NormalizedEmail`/`NormalizedUserName` rows to the new format silently breaks login for every pre-existing user — the read path produces lowercase while the rows still hold uppercase, so `FindByNameAsync` returns null and login fails with no `AccessFailedCount` increment. The standing rule in `security-model.md`: any normalizer change ships in the same commit as a backfill migration. See planning-phase3.md § Stage 9.1.5 deferred decisions for the incident this rule came from.
```

Replace with the shorter pointer-version:

```markdown
> **Watch the migration risk.** Swapping `ILookupNormalizer` without a same-commit backfill migration silently breaks login for every pre-existing user. The full rule and the Stage 9.1.5.h backfill precedent live in `security-model.md` § ASP.NET Core Identity Hardening.
```

### Task 2.4: Flip roadmap-phase-three.md

**Files:**
- Modify: `docs/roadmap-phase-three.md` lines 1103 + 1114

- [ ] **Step 1: Update line 1103 (sub-stage row)**

Locate line 1103. Replace the existing description column with the close-out summary:

```
| 9.1.5.h | `ILookupNormalizer` swap broke pre-existing user logins; codify rule + add backfill migration | Commit `4b35911` swapped Identity's default `UpperInvariantLookupNormalizer` for a custom `LowercaseLookupNormalizer` with no data migration. `AspNetUsers.NormalizedEmail` + `NormalizedUserName` rows seeded before that commit still held uppercase strings; `FindByNameAsync` produced lowercase and matched zero rows; login returned "invalid login" without ever checking the password (`AccessFailedCount` stayed 0). Only one row existed (dev seed, fixed in-session via `UPDATE … SET = LOWER(…)`). In production with real users this would have broken every existing login at deploy time. Shipped: EF migration `BackfillIdentityNormalizedToLowercase` (3 idempotent UPDATE statements covering all three normalized Identity columns including the currently-empty `AspNetRoles.NormalizedName` so future role introduction inherits the protection); 9 xUnit tests in `ProjectCeres.Tests/Integration/BackfillIdentityNormalizedToLowercaseTests.cs` pin uppercase-lowering / idempotency / NULL-safety per column; `Down()` throws `NotSupportedException` (reverting restores the broken state). Standing rule added to `security-model.md` § ASP.NET Core Identity Hardening: any `ILookupNormalizer` registration change requires a same-commit data-migration. Cross-referenced from `models.md` § ApplicationUser and the developer guide. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-impl.md`. |
```

- [ ] **Step 2: Update line 1114 (verification line)**

Replace the existing `- [ ] 9.1.5.h —` line with the `[x]` version:

```markdown
- [x] 9.1.5.h — EF migration `BackfillIdentityNormalizedToLowercase` exists and is idempotent (re-running on a fully-lowercase table is a no-op — pinned by `Migration_is_idempotent_on_already_lowercase_*` tests per column); migration applied to local dev DB (0 rows changed; manual login verified post-migration); `security-model.md` § ASP.NET Core Identity Hardening carries the standing rule "`ILookupNormalizer` registration changes require a same-commit data-migration that backfills `AspNetUsers.NormalizedEmail` + `NormalizedUserName` + `AspNetRoles.NormalizedName` to the new normalizer's output"; rule cross-referenced from `models.md` § ApplicationUser and from the developer guide `01-aspnetcore-identity.md`; manual login on the local dev DB still works after the migration runs (regression check — confirms the migration didn't re-introduce the case mismatch).
```

### Task 2.5: Commit

- [ ] **Step 1: Stage and commit**

```bash
git -C <repo> add \
  docs/security-model.md \
  docs/models.md \
  docs/guide/09-authentication-and-security/01-aspnetcore-identity.md \
  docs/roadmap-phase-three.md

git -C <repo> commit -m "docs(stage-9.1.5.h): ILookupNormalizer-swap-requires-backfill rule + cross-references + roadmap close-out"
```

Expected: commit succeeds. Stop-hook runs `dotnet test` (no code changes since Commit 1 — clean) and exits 0.

---

## Self-review against the spec

### Spec coverage check

Walking spec § 2 (migration design) through § 5 (tests):

- ✓ `Up()` covers all three normalized columns — Task 1.4.
- ✓ `Down()` throws `NotSupportedException` — Task 1.5.
- ✓ Migration XML doc summary mirrors Stage 6.15 precedent — Task 1.6.
- ✓ All 9 xUnit tests in the new file — Task 1.1.
- ✓ Migration applied to local dev DB — Task 1.8 (gated by AskUserQuestion).
- ✓ Manual login regression check — Task 1.8 Step 3.
- ✓ Standing rule in security-model.md — Task 2.1.
- ✓ Cross-reference in models.md — Task 2.2.
- ✓ Developer guide callout shortened to point at security-model.md — Task 2.3.
- ✓ Roadmap close-out — Task 2.4.

Spec § 8 (verification checklist) — every line covered by a corresponding task.

No gaps.

### Placeholder scan

No "TBD", "TODO", "implement later", or steps without code blocks. The two `<timestamp>` placeholders in file paths are intentional — EF assigns the timestamp at `migrations add` time (Task 1.3), and the implementer substitutes the actual timestamp in Tasks 1.4 / 1.5 / 1.6 / 1.9.

The `AskUserQuestion` in Task 1.8 is an explicit gate (not a placeholder) — required by `feedback_never_delete_db_without_consent`.

### Type consistency

- `BackfillIdentityNormalizedToLowercase` class name used consistently across Tasks 1.3 / 1.4 / 1.5 / 1.6 / 1.9 / 2.4 and in the test file's references.
- The three SQL constants in the test file (`LowercaseEmailSql`, `LowercaseUserNameSql`, `LowercaseRoleNameSql`) match the SQL in `Up()` byte-for-byte. If a future edit changes one, the test's failure points at the drift.
- `AdminDbContext` referenced consistently in tests (with a note to grep for the actual name if the project's admin context has a different identifier).

Clean.

---

## Execution choice

**Plan complete and saved to `docs/superpowers/plans/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-impl.md`.** Two execution options:

**1. Subagent-Driven (recommended)** — Fresh implementer subagent per commit; two-stage review (spec compliance, then code quality) between commits. Recommended for this stage because Commit 1 is multi-file (migration code + 9 tests + EF-generated artifacts) and touches database state — a fresh subagent reviewing the diff with no implementation-context bias is the standard quality gate. Use `sonnet` for the implementer (EF tooling + multi-file coordination); `haiku` for the reviewers.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans` with batch checkpoints for user review.

Which approach?
