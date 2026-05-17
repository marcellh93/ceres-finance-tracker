# Stage 9.1.5.a — LockoutUnlockToken.TokenLookup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retrofit the Stage 6.15 HMAC-SHA256 `TokenLookup` pattern onto `LockoutUnlockToken` so `LockoutUnlockService.ConfirmAsync` runs O(1) DB lookup + 1 Argon2 verify instead of an O(N) candidate scan. Removes the CPU-saturation source that caused the suite-wide auth-tier test flakes 9.1.5.a tracks.

**Architecture:** Add an indexed `byte[] TokenLookup` column on `LockoutUnlockToken` (mirrors `PasswordResetToken` and `EmailChangeToken`); inject the existing `TokenLookupHasher` into `LockoutUnlockService`; stamp the lookup on `IssueAsync`; replace the cross-tenant scan + `foreach` Argon2 loop in `ConfirmAsync` with `SingleOrDefaultAsync(t => t.TokenLookup == lookup && ...)` plus one conditional Argon2 verify. The EF migration mirrors `20260511155005_AddTokenLookup.cs` (Stage 6.15) verbatim — backfill-and-invalidate, not delete.

**Tech Stack:** .NET 10 · ASP.NET Core Identity · EF Core + Npgsql + PostgreSQL · xUnit + FluentAssertions · `Microsoft.AspNetCore.Mvc.Testing` + `WebApplicationFactory<Program>` · existing `TokenLookupHasher` (`HMACSHA256(serverSecret, UTF8(rawToken))` → `byte[32]`).

**Source spec:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-design.md` (commit `7baa196`).
**Stage 6.15 precedent (must mirror exactly):** `ProjectCeres/Migrations/20260511155005_AddTokenLookup.cs`.

---

## Binding constraints

- **Stay on `main`.** No worktrees, no branches.
- **No `Co-Authored-By` trailer** in commit messages.
- **TDD per `docs/testing.md` § Rules** — every test-touching commit names which of cases (1), (2), or (3) applied.
- **Never modify, skip, or weaken tests to make them pass.** No `[Fact(Skip=…)]`, no commented-out assertions, no `try/catch` to silence.
- **Pre-existing test failures encountered mid-task get root-caused now**, not logged as `TaskCreate` follow-ups. Exception: documented Stage 9.1.5.a flakes themselves — re-run any failing `RateLimitTests` test in isolation; if isolation passes, document and proceed (the whole point of this stage is to make those flakes stop).
- **Stop-hook (`.claude/hooks/run-tests.sh`) blocks commits on `dotnet test` failure.** Each `git commit` step verifies the hook passes. Do NOT bypass with `--no-verify`.
- **Migration SQL is verbatim from the Stage 6.15 precedent.** Do not invent SQL. Read `20260511155005_AddTokenLookup.cs` and mirror it line-for-line (modulo table name).
- **`pnpm` only** if any JS work surfaces (none expected here).
- **The verification gate is THREE CONSECUTIVE clean `dotnet test` runs** after the fix lands. This is the diagnosis-confirmation gate per spec §7. Two clean runs are not sufficient. If a flake recurs on any of the three runs, document the surface and stop — deep-fix-mode re-fires.

## File map

| File | Action | Purpose |
|---|---|---|
| `ProjectCeres/Models/LockoutUnlockToken.cs` | **modify** | Add `byte[] TokenLookup { get; set; } = Array.Empty<byte>();` between `UserId` and `TokenHash`. |
| `ProjectCeres/Data/AppDbContext.cs` | **modify** | Add `b.HasIndex(e => e.TokenLookup).IsUnique();` + `b.Property(e => e.TokenLookup).HasMaxLength(32);` to `ConfigureLockoutUnlockEntities` (line 195-204). Mirrors lines 174-175 (PasswordReset) and 187-188 (EmailChange). |
| `ProjectCeres/Migrations/20260517XXXXXX_AddLockoutUnlockTokenLookup.cs` | **create** | EF migration: `AddColumn` (`bytea`, `maxLength: 32`, NOT NULL, default `new byte[0]`) → `UPDATE` SQL backfilling `decode(md5(TokenHash \|\| Id::text), 'hex')` and setting `ConsumedAt = NOW()` → `CreateIndex` (unique). Mirror `20260511155005_AddTokenLookup.cs` exactly. |
| `ProjectCeres/Migrations/20260517XXXXXX_AddLockoutUnlockTokenLookup.Designer.cs` | **create** | Auto-generated alongside the migration via `dotnet ef migrations add`. |
| `ProjectCeres/Migrations/AppDbContextModelSnapshot.cs` | **modify** | Updated automatically by `dotnet ef migrations add` to reflect the new column + index. |
| `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` | **modify** | Inject `TokenLookupHasher`. In `IssueAsync` (around line 82-90), stamp `TokenLookup` on row insert. In `ConfirmAsync` (around line 122-145), replace the cross-tenant scan + `foreach` Argon2 loop with indexed `SingleOrDefaultAsync` + single conditional verify. |
| `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs` | **modify** | (a) Update 3 `new LockoutUnlockToken { … }` inserts to stamp `TokenLookup` (lines 67, 146, 417). (b) Add new `Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count` test. |
| `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs` | **modify** | Update 1 `new LockoutUnlockToken { … }` insert to stamp `TokenLookup` (line 174). |
| `ProjectCeres.Tests/Integration/Authentication/TokenLookupTamperResistanceTests.cs` | **modify** | Add new `LockoutUnlock_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401` test. Mirror the existing `EmailChange_confirm_*` test at line 50-67. |
| `docs/roadmap-phase-three.md` | **modify** | Flip 9.1.5.a `[ ]` → `[x]`; replace the "TBD pending architectural diagnosis" line with the post-fix description. |

## Commit-by-commit overview

The plan sequences work to keep the stop-hook green at every commit boundary. The model + DbContext + migration + ALL 4 test-side inserts MUST land together because the new NOT NULL column otherwise breaks every test that inserts `LockoutUnlockToken` rows. The service refactor (constructor + `IssueAsync` + `ConfirmAsync`) MUST land together because `ConfirmAsync` indexed-lookup without `IssueAsync` stamping the lookup would fail every confirm test.

| Commit | Subject | What lands |
|---|---|---|
| 1 | `feat(stage-9.1.5.a): add LockoutUnlockToken.TokenLookup column + migration + update test seeds` | Model field, DbContext config, EF migration (backfill-and-invalidate), all 4 test-side `new LockoutUnlockToken` inserts updated to stamp `TokenLookup`. Suite stays green throughout (controller behavior unchanged; tests still scan because `ConfirmAsync` hasn't been refactored yet). |
| 2 | `feat(stage-9.1.5.a): LockoutUnlockService stamps TokenLookup on issue + uses indexed lookup on confirm` | Constructor injects `TokenLookupHasher`; `IssueAsync` stamps the lookup; `ConfirmAsync` replaces the scan with `SingleOrDefaultAsync`. New `Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count` test pins the O(1) contract. |
| 3 | `test(stage-9.1.5.a): tamper-resistance test for LockoutUnlock confirm with matching lookup + wrong hash` | New test in `TokenLookupTamperResistanceTests.cs` mirroring the existing `EmailChange_*` test. |
| 4 | `docs(stage-9.1.5.a): close out 9.1.5.a on the roadmap` | Roadmap `[ ]` → `[x]` with updated verification text. Task #43 description updated in the same commit. **Block this commit until three consecutive `dotnet test` runs are clean.** |

---

### Task 1: Schema + migration + test-seed updates

**Files:**
- Modify: `ProjectCeres/Models/LockoutUnlockToken.cs`
- Modify: `ProjectCeres/Data/AppDbContext.cs:195-204`
- Create: `ProjectCeres/Migrations/{timestamp}_AddLockoutUnlockTokenLookup.cs`
- Create: `ProjectCeres/Migrations/{timestamp}_AddLockoutUnlockTokenLookup.Designer.cs` (auto-generated)
- Modify: `ProjectCeres/Migrations/AppDbContextModelSnapshot.cs` (auto-updated)
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs:67, 146, 417`
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs:174`

This commit adds the column + migration + updates all test-side inserts together. The controller still uses the old scan path; `IssueAsync` doesn't yet stamp `TokenLookup`. Existing tests pass because:
- New rows inserted by `IssueAsync` get `TokenLookup = Array.Empty<byte>()` (the column's default value).
- The unique index would normally trip on a second row with the empty-bytes default — but each test's `DisposeAsync` cleans up its own rows before the next test runs, so there's no overlap at any point in time.
- The 4 test-side inserts are updated in this commit to ALSO stamp `TokenLookup` (with a unique value per row) so the test path is consistent with the production path that's about to land in Commit 2.

**Why this is one commit:** the model + DbContext + migration are inseparable (model field + index registration + DB schema must agree). The test-seed updates land in the same commit because (a) the new NOT NULL column would otherwise break the moment the migration applies, and (b) the unique index would trip if multiple test inserts used the empty-bytes default at the same time.

- [ ] **Step 1: Read the Stage 6.15 precedent migration**

Run: `cat <repo>/ProjectCeres/Migrations/20260511155005_AddTokenLookup.cs`

This is the canonical pattern. The migration you're about to generate must mirror it exactly — `AddColumn` → `Sql(UPDATE backfill)` → `CreateIndex(unique: true)`. The `Down()` is `DropIndex` → `DropColumn` with no `Sql` restoration.

Expected: shows the AddColumn → UPDATE → CreateIndex sequence for `PasswordResetTokens` and `EmailChangeTokens`. Note the SQL exactly — you will mirror it for `LockoutUnlockTokens`.

- [ ] **Step 2: Add the `TokenLookup` property to `LockoutUnlockToken.cs`**

Open `<repo>/ProjectCeres/Models/LockoutUnlockToken.cs`. Insert the new property between `UserId` and `TokenHash`:

```csharp
using ProjectCeres.Common;

namespace ProjectCeres.Models;

public sealed class LockoutUnlockToken : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    // HMAC-SHA256(serverSecret, rawToken). Unique index ensures /lockout-unlock
    // locates the matching row in O(1) regardless of how many candidates exist
    // (Stage 9.1.5.a — extends the Stage 6.15 pattern to the third token sibling).
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
```

- [ ] **Step 3: Add the DbContext config**

Open `<repo>/ProjectCeres/Data/AppDbContext.cs`. Locate `ConfigureLockoutUnlockEntities` (around line 195). Add two lines mirroring the PasswordReset/EmailChange configs:

```csharp
private static void ConfigureLockoutUnlockEntities(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<LockoutUnlockToken>(b =>
    {
        b.HasKey(e => e.Id);
        b.HasIndex(e => new { e.UserId, e.ConsumedAt });
        b.HasIndex(e => e.ExpiresAt);
        b.HasIndex(e => e.TokenLookup).IsUnique();
        b.Property(e => e.TokenLookup).HasMaxLength(32);
        b.Property(e => e.TokenHash).HasMaxLength(512);
    });
}
```

- [ ] **Step 4: Generate the EF migration**

Run: `dotnet ef migrations add AddLockoutUnlockTokenLookup --project <repo>/ProjectCeres`

Expected: creates two files under `ProjectCeres/Migrations/`:
- `{timestamp}_AddLockoutUnlockTokenLookup.cs`
- `{timestamp}_AddLockoutUnlockTokenLookup.Designer.cs`

Also updates `AppDbContextModelSnapshot.cs`.

The auto-generated `Up()` will contain `AddColumn` + `CreateIndex` but NOT the backfill `UPDATE`. You'll add that in Step 5.

- [ ] **Step 5: Edit the migration to insert the backfill-and-invalidate SQL**

Open the newly-created `{timestamp}_AddLockoutUnlockTokenLookup.cs`. Replace its `Up()` body with the following — mirrors `20260511155005_AddTokenLookup.cs` exactly:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 9.1.5.a — extends the Stage 6.15 TokenLookup pattern to LockoutUnlockTokens
    /// so ConfirmAsync can locate a row in O(1) instead of running Argon2id against every
    /// unconsumed candidate. Closes the suite-wide CPU saturation that caused 9.1.5.a's
    /// shifting auth-tier test flakes.
    ///
    /// Existing rows cannot be HMACed (the raw token is gone) so the backfill writes
    /// a synthetic placeholder (md5(TokenHash || Id::text)) and stamps
    /// ConsumedAt = NOW() in the SAME statement — every pre-9.1.5.a token is
    /// invalidated by the migration. Acceptable because Phase 3 hosted-beta has one
    /// real user and no production lockout-unlock tokens worth preserving. Mirrors
    /// 20260511155005_AddTokenLookup.cs exactly.
    /// </summary>
    public partial class AddLockoutUnlockTokenLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "TokenLookup",
                table: "LockoutUnlockTokens",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.Sql(@"
                UPDATE ""LockoutUnlockTokens""
                SET ""ConsumedAt"" = NOW(),
                    ""TokenLookup"" = decode(md5(""TokenHash"" || ""Id""::text), 'hex')
                WHERE ""ConsumedAt"" IS NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_LockoutUnlockTokens_TokenLookup",
                table: "LockoutUnlockTokens",
                column: "TokenLookup",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LockoutUnlockTokens_TokenLookup",
                table: "LockoutUnlockTokens");

            migrationBuilder.DropColumn(
                name: "TokenLookup",
                table: "LockoutUnlockTokens");
        }
    }
}
```

Do NOT touch the `.Designer.cs` file — it's auto-generated and must stay in sync with the model snapshot.

- [ ] **Step 6: Apply the migration to the dev database**

Run: `dotnet ef database update --project <repo>/ProjectCeres`

Expected: `Applying migration '{timestamp}_AddLockoutUnlockTokenLookup'.` then `Done.`. No errors.

If you see a unique-constraint violation, the backfill `UPDATE` didn't run before `CreateIndex` — re-read Step 5's migration file and confirm the SQL block is between `AddColumn` and `CreateIndex`.

- [ ] **Step 7: Update test-side insert #1 — `LockoutUnlockConfirmTests.cs:67` (highest leverage)**

Open `<repo>/ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs`. Locate `ArrangeLockedUserWithUnlockTokenAsync` (line 50-81). The `db.LockoutUnlockTokens.Add(new LockoutUnlockToken { … })` block at line 67 currently has no `TokenLookup` field. Update it:

```csharp
private async Task<(ApplicationUser User, string RawToken)> ArrangeLockedUserWithUnlockTokenAsync()
{
    var email = $"u-{Guid.NewGuid():N}{EmailDomain}";
    var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

    using var scope = _factory.Services.CreateScope();
    var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var fresh = await um.FindByIdAsync(user.Id.ToString());
    for (int i = 0; i < 10; i++) await um.AccessFailedAsync(fresh!);
    await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();
    var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
    var rawToken = generator.Generate();
    var hash = generator.Hash(rawToken);
    var now = DateTime.UtcNow;
    db.LockoutUnlockTokens.Add(new LockoutUnlockToken
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        TokenLookup = lookupHasher.ComputeLookup(rawToken),
        TokenHash = hash,
        CreatedAt = now,
        ExpiresAt = now + LockoutUnlockService.TokenLifetime,
        ConsumedAt = null,
    });
    await db.SaveChangesAsync();

    var refetched = await um.FindByIdAsync(user.Id.ToString());
    return (refetched!, rawToken);
}
```

The diff is: one new local `lookupHasher`, one new line stamping `TokenLookup` in the `new LockoutUnlockToken { … }` initializer.

- [ ] **Step 8: Update test-side insert #2 — `LockoutUnlockConfirmTests.cs:146`**

Same file. The block at line 146 is inside `Confirm_with_expired_token_returns_401`. Find the existing `db.LockoutUnlockTokens.Add(new LockoutUnlockToken { … })` initializer and add the `TokenLookup` line. The surrounding `using (var scope = ...) { ... }` already has `db` + `generator` in scope; resolve `lookupHasher` the same way:

Update the block at line 141-156 area to:

```csharp
using (var scope = _factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();
    var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
    rawToken = generator.Generate();
    db.LockoutUnlockTokens.Add(new LockoutUnlockToken
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        TokenLookup = lookupHasher.ComputeLookup(rawToken),
        TokenHash = generator.Hash(rawToken),
        CreatedAt = DateTime.UtcNow.AddMinutes(-30),
        ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
        ConsumedAt = null,
    });
    await db.SaveChangesAsync();
}
```

- [ ] **Step 9: Update test-side insert #3 — `LockoutUnlockConfirmTests.cs:417`**

Same file. The block at line 417 is inside `Confirm_RemovesEmailFromLockoutCache` (added during Stage 9.1.5.b commit `cbcda9a`). Find the existing block and add the `TokenLookup` line. The surrounding scope already has `db` + `generator`:

Update the block at line 412-426 area to:

```csharp
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();
var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
rawToken = generator.Generate();
var hash = generator.Hash(rawToken);
var now = DateTime.UtcNow;
db.LockoutUnlockTokens.Add(new LockoutUnlockToken
{
    Id = Guid.NewGuid(),
    UserId = user.Id,
    TokenLookup = lookupHasher.ComputeLookup(rawToken),
    TokenHash = hash,
    CreatedAt = now,
    ExpiresAt = now + LockoutUnlockService.TokenLifetime,
    ConsumedAt = null,
});
await db.SaveChangesAsync();
```

- [ ] **Step 10: Update test-side insert #4 — `LockoutUnlockIssuanceTests.cs:174`**

Open `<repo>/ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs`. Locate the `db.LockoutUnlockTokens.Add(new LockoutUnlockToken { … })` block at line 174. Read 10 lines around it to understand the surrounding test (typically a setup that inserts a stale unconsumed token to verify that `IssueAsync` supersedes it).

Add `var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();` to the existing scope and add `TokenLookup = lookupHasher.ComputeLookup(<the existing raw or synthetic token>),` to the initializer. If the test's seed token doesn't have a corresponding raw value (e.g. it directly fabricates a `TokenHash` without a generator call), then generate any unique 32 bytes via `RandomNumberGenerator.GetBytes(32)` and pass that to the `TokenLookup` field — the test doesn't care about lookup-vs-hash consistency for a seeded supersede target.

Read the test first; choose the path that fits its semantics.

- [ ] **Step 11: Build**

Run: `dotnet build <repo>/ProjectCeres.sln`
Expected: `Build succeeded.` with 0 errors, 0 new warnings.

- [ ] **Step 12: Run the full test suite**

Run: `dotnet test <repo>/ProjectCeres.sln`
Expected: all green. If a `LockoutUnlock*` test fails on a NOT NULL `TokenLookup` constraint, a test-side insert was missed — grep for any remaining `new LockoutUnlockToken` and verify each stamps the column.

Pre-existing Stage 9.1.5.a flakes — re-run failing `RateLimitTests` in isolation; if isolation passes, document and proceed (the goal of THIS stage is to make them stop, but they may still recur until Commit 2 lands the actual `ConfirmAsync` refactor).

- [ ] **Step 13: Commit**

```bash
git -C <repo> add \
  ProjectCeres/Models/LockoutUnlockToken.cs \
  ProjectCeres/Data/AppDbContext.cs \
  ProjectCeres/Migrations/ \
  ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs \
  ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9.1.5.a): add LockoutUnlockToken.TokenLookup column + migration + update test seeds

Adds the byte[] TokenLookup property to LockoutUnlockToken with a unique index.
EF migration mirrors 20260511155005_AddTokenLookup.cs (Stage 6.15) exactly:
AddColumn with byte[0] default → UPDATE backfilling
decode(md5(TokenHash || Id::text), 'hex') AND setting ConsumedAt = NOW() in the
SAME statement → CreateIndex unique. Pre-9.1.5.a tokens are invalidated by the
backfill, not deleted — audit trail preserved.

The four test-side `new LockoutUnlockToken { … }` inserts in
LockoutUnlockConfirmTests (3 sites) + LockoutUnlockIssuanceTests (1 site) are
updated in this same commit to stamp TokenLookup via the existing
TokenLookupHasher service. Without these updates the NOT NULL constraint
would break the moment the migration applies.

LockoutUnlockService is NOT yet refactored in this commit — IssueAsync still
inserts rows with TokenLookup = Array.Empty<byte>() (the column default), and
ConfirmAsync still runs the O(N) scan. This split keeps the stop-hook green;
Commit 2 lands the service refactor.

Case applies (testing.md § Rules): case (3) — contract intentionally changed.
LockoutUnlockToken now requires a non-empty TokenLookup; test inserts updated
to satisfy the new schema constraint.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-design.md §3.1, §3.2, §3.3
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-impl.md Task 1
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 2: Service refactor + O(1) regression test

**Files:**
- Modify: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs`
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs`

This commit lands the load-bearing fix. `LockoutUnlockService` constructor injects `TokenLookupHasher`. `IssueAsync` stamps the lookup. `ConfirmAsync` replaces the cross-tenant scan + Argon2 loop with `SingleOrDefaultAsync` + one conditional verify. The new regression test asserts the O(1) contract.

- [ ] **Step 1: Write the failing regression test**

Open `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs`. Add the following test above the closing `}` of the class:

```csharp
[Fact]
public async Task Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count()
{
    // Seed 30 stale unconsumed-and-unexpired token rows for OTHER unrelated users.
    // Then issue + confirm a token for the test's own user.
    // Without TokenLookup: 30+ Argon2 verifies × ~80ms ≈ 2400ms+ on the request thread.
    // With TokenLookup: 1 verify, total elapsed < 500ms (HTTP overhead included).
    var seededIds = new List<Guid>();
    using (var scope = _factory.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();
        var hasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
        for (int i = 0; i < 30; i++)
        {
            var noise = generator.Generate();
            var noiseId = Guid.NewGuid();
            db.LockoutUnlockTokens.Add(new LockoutUnlockToken
            {
                Id = noiseId,
                UserId = Guid.NewGuid(),
                TokenLookup = hasher.ComputeLookup(noise),
                TokenHash = generator.Hash(noise),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow + LockoutUnlockService.TokenLifetime,
                ConsumedAt = null,
            });
            seededIds.Add(noiseId);
        }
        await db.SaveChangesAsync();
    }

    try
    {
        var (_, rawToken) = await ArrangeLockedUserWithUnlockTokenAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await PostConfirmAsync(rawToken);
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "ConfirmAsync must run at most one Argon2 verify; the indexed TokenLookup lookup must return a single candidate row regardless of unconsumed-row count");
    }
    finally
    {
        // Inline cleanup so noise rows don't accumulate across suite runs.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.LockoutUnlockTokens
            .IgnoreQueryFilters()
            .Where(t => seededIds.Contains(t.Id))
            .ExecuteDeleteAsync();
    }
}
```

- [ ] **Step 2: Run the new test, confirm it FAILS for the right reason**

Run: `dotnet test --filter "FullyQualifiedName~Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count"`

Expected: **FAIL** at `sw.ElapsedMilliseconds.Should().BeLessThan(500)` — actual elapsed will be several seconds because `ConfirmAsync` still runs the O(N) scan over all 31 unconsumed rows.

If the test fails at the 30-row seed step (unique-constraint violation), Task 1's commit did not land the unique index correctly — abort and re-check Task 1 Step 5's migration SQL.

If the test passes at < 500ms, something else changed `ConfirmAsync` to not scan — STOP and report; the production code may have drifted from the expected pre-fix state.

- [ ] **Step 3: Inject `TokenLookupHasher` into `LockoutUnlockService`**

Open `<repo>/ProjectCeres/Common/Authentication/LockoutUnlockService.cs`. Update the constructor and add the field. The current ctor signature ends with `LockoutCache lockoutCache` (added during Stage 9.1.5.b). Add `TokenLookupHasher lookupHasher` after it:

```csharp
private readonly TokenLookupHasher _lookupHasher;

public LockoutUnlockService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    Argon2idPasswordHasher argon,
    LockoutUnlockTokenGenerator tokens,
    IEmailService email,
    IEmailComposer composer,
    IEmailRecipientResolver recipients,
    ILanguageResolver languages,
    ILogger<LockoutUnlockService> logger,
    IAuditLogWriter auditLog,
    LockoutCache lockoutCache,
    TokenLookupHasher lookupHasher)
{
    _userManager = userManager;
    _db = db;
    _argon = argon;
    _tokens = tokens;
    _email = email;
    _composer = composer;
    _recipients = recipients;
    _languages = languages;
    _logger = logger;
    _auditLog = auditLog;
    _lockoutCache = lockoutCache;
    _lookupHasher = lookupHasher;
}
```

The field declaration goes next to `_lockoutCache` (alphabetical/grouping doesn't matter — match the project's existing style of "add at the end").

- [ ] **Step 4: Stamp `TokenLookup` in `IssueAsync`**

Locate the `LockoutUnlockTokens.Add(new LockoutUnlockToken { ... })` block inside `IssueAsync` (around line 82-90). Add a `lookup` local and stamp `TokenLookup` on the new row:

```csharp
rawToken = _tokens.Generate();
var hash = _tokens.Hash(rawToken);
var lookup = _lookupHasher.ComputeLookup(rawToken);
var now = DateTime.UtcNow;
_db.LockoutUnlockTokens.Add(new LockoutUnlockToken
{
    Id = Guid.NewGuid(),
    UserId = userId,
    TokenLookup = lookup,
    TokenHash = hash,
    CreatedAt = now,
    ExpiresAt = now + TokenLifetime,
    ConsumedAt = null,
});
await _db.SaveChangesAsync(ct);
```

- [ ] **Step 5: Replace the candidate scan in `ConfirmAsync` with indexed lookup**

Locate the candidate-finding block in `ConfirmAsync` (currently around line 122-145):

```csharp
var now = DateTime.UtcNow;
// Cross-tenant by design: scans unconsumed tokens across all users to verify by hash before the caller is identified. Stage 10 architecture test allow-lists this file.
var candidates = await _db.LockoutUnlockTokens
    .IgnoreQueryFilters()
    .Where(t => t.ConsumedAt == null && t.ExpiresAt > now)
    .ToListAsync(ct);

LockoutUnlockToken? match = null;
foreach (var candidate in candidates)
{
    if (_tokens.Verify(rawToken, candidate.TokenHash))
    {
        match = candidate;
        break;
    }
}

if (match is null)
{
    // Constant-time: even with zero candidates, run one verify so timing doesn't reveal "no rows".
    if (candidates.Count == 0) _argon.RunDummyHash();
    return new LockoutUnlockOutcome.InvalidToken();
}
```

Replace with:

```csharp
var now = DateTime.UtcNow;
var lookup = _lookupHasher.ComputeLookup(rawToken);
// Cross-tenant by design: token-based pre-auth operation; caller is not in session.
// Stage 10 architecture test allow-lists this file. Stage 9.1.5.a: indexed lookup
// replaces the O(N) Argon2 scan; a single row matches the HMAC-derived TokenLookup
// or none does, so we run at most one Argon2 verify per request.
var candidate = await _db.LockoutUnlockTokens
    .IgnoreQueryFilters()
    .Where(t => t.TokenLookup == lookup && t.ConsumedAt == null && t.ExpiresAt > now)
    .SingleOrDefaultAsync(ct);

if (candidate is null || !_tokens.Verify(rawToken, candidate.TokenHash))
{
    // Constant-time: even with no matching row, run one verify so timing doesn't
    // reveal "no rows" vs "rows but no Argon2 match". Mirrors the prior pattern
    // and preserves the anti-enumeration property of the original scan.
    if (candidate is null) _argon.RunDummyHash();
    return new LockoutUnlockOutcome.InvalidToken();
}

var match = candidate;
```

The downstream code (`_userLocks.GetOrAdd(match.UserId, …)` and everything after) is **unchanged**.

- [ ] **Step 6: Run the new regression test — should now PASS**

Run: `dotnet test --filter "FullyQualifiedName~Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count"`
Expected: PASS, with elapsed time well under 500ms (typically 100-200ms).

If still > 500ms: `ConfirmAsync` is still scanning. Re-read Step 5 — confirm the `foreach` loop is gone and the query uses `SingleOrDefaultAsync(t => t.TokenLookup == lookup && ...)`.

- [ ] **Step 7: Run the full `LockoutUnlockConfirmTests` class**

Run: `dotnet test --filter "FullyQualifiedName~LockoutUnlockConfirmTests"`
Expected: all green. If `Confirm_concurrent_two_callers_one_succeeds_one_returns_invalid_token` fails, the concurrency contract may have regressed — read the failure carefully. The two concurrent callers compute the same lookup, find the same row, both enter the semaphore; first wins, second sees `ConsumedAt != null` on re-read. This shape is unchanged from the prior code.

- [ ] **Step 8: Run the full `LockoutUnlockIssuanceTests` class**

Run: `dotnet test --filter "FullyQualifiedName~LockoutUnlockIssuanceTests"`
Expected: all green. `IssueAsync` now stamps `TokenLookup`; existing issuance tests don't inspect that field but should pass because the field has a deterministic value derived from the raw token they already generate.

- [ ] **Step 9: Run the full server test suite**

Run: `dotnet test`
Expected: all green. Pre-existing Stage 9.1.5.a flakes should already be less frequent at this commit (since the actual fix is now live), but may still recur once or twice until Task 4's verification gate confirms three consecutive clean runs. Document any flake by name + isolation result. Do NOT skip.

- [ ] **Step 10: Commit**

```bash
git -C <repo> add \
  ProjectCeres/Common/Authentication/LockoutUnlockService.cs \
  ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9.1.5.a): LockoutUnlockService stamps TokenLookup on issue + uses indexed lookup on confirm

Inject TokenLookupHasher into LockoutUnlockService. IssueAsync stamps the
HMAC-SHA256 TokenLookup on the new row. ConfirmAsync replaces the cross-tenant
candidate scan (.ToListAsync + foreach Argon2 verify loop) with a single
SingleOrDefaultAsync(t => t.TokenLookup == lookup && t.ConsumedAt == null &&
t.ExpiresAt > now) + one conditional Argon2 verify against the matched row.

The downstream pipeline is UNCHANGED: per-user semaphore wait, re-read inside
the lock, ResetAccessFailedCountAsync, SetLockoutEndDateAsync(user, null),
LockoutCache.Remove(user.Email), ExecuteUpdateExactlyAsync on the token row,
audit log write. The constant-time RunDummyHash() anti-enumeration path is
preserved (fires when candidate is null).

New test Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count
pins the O(1) contract: 30 stale unconsumed token rows seeded for unrelated users,
then issue + confirm a real token; total elapsed must be < 500ms. Without the
indexed lookup this test would take 30+ × ~80ms = 2400ms+. The test cleans up
its 30 noise rows in a finally block via ExecuteDeleteAsync.

Case applies (testing.md § Rules): case (3) — contract intentionally changed.
ConfirmAsync's algorithmic complexity drops from O(N unconsumed rows) to O(1)
DB lookup + 1 Argon2 verify. Observable HTTP behavior is identical (still
single-use, still 401 on bad token, still clears AccessFailedCount/LockoutEnd).

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-design.md §3.4, §4.1
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-impl.md Task 2
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 3: Tamper-resistance test

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/TokenLookupTamperResistanceTests.cs`

This commit adds a defence-in-depth test mirroring the existing `EmailChange_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401` test (line 50-67 of the same file).

- [ ] **Step 1: Read the existing EmailChange tamper test for the pattern**

Run: `sed -n '50,90p' <repo>/ProjectCeres.Tests/Integration/Authentication/TokenLookupTamperResistanceTests.cs`

This shows how a tamper test is structured: seed a row where `TokenLookup` matches a real raw token but `TokenHash` is for a DIFFERENT raw token, then POST the real raw token and assert the controller returns 401 (the lookup hits, but the Argon2id verify fails — defence-in-depth check rejects).

- [ ] **Step 2: Add the new tamper test**

Open `<repo>/ProjectCeres.Tests/Integration/Authentication/TokenLookupTamperResistanceTests.cs`. Append the following test above the closing `}` of the class (or before the existing `Seed*` helper methods, matching the file's existing layout — the helpers usually live at the bottom):

```csharp
[Fact]
public async Task LockoutUnlock_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401()
{
    // Mirror EmailChange_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401.
    // Seed a LockoutUnlockToken row where TokenLookup matches a real raw token but
    // TokenHash is for a DIFFERENT raw token. POSTing the real raw token must:
    //   (1) hit the indexed TokenLookup query (the row's TokenLookup matches),
    //   (2) fail the Argon2id verify (the row's TokenHash does not match the raw),
    //   (3) return 401 INVALID_LOCKOUT_UNLOCK_TOKEN.
    // If a future refactor deletes the Argon2id check, this test goes red while the
    // happy-path/expiry/consumed suite stays green — that's the defence-in-depth gap
    // it's here to catch.
    string realToken;
    using (var scope = _factory.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
        var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();

        realToken = generator.Generate();
        var forgedToken = generator.Generate();

        var email = $"tamper-lockout-{Guid.NewGuid():N}@tamper-test.local";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        db.LockoutUnlockTokens.Add(new LockoutUnlockToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenLookup = lookupHasher.ComputeLookup(realToken),
            TokenHash = generator.Hash(forgedToken),  // mismatched on purpose
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow + LockoutUnlockService.TokenLifetime,
            ConsumedAt = null,
        });
        await db.SaveChangesAsync();
    }

    var client = _factory.CreateClient();
    var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client,
        "/api/auth/lockout-unlock", new { token = realToken });

    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
        "the matched row's TokenHash does not Argon2id-verify against the raw token — defence-in-depth check must reject");
    (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_LOCKOUT_UNLOCK_TOKEN");
}
```

If `INVALID_LOCKOUT_UNLOCK_TOKEN` is not the actual envelope code, grep `LockoutUnlockController.cs` for the rejection envelope and use whatever code that controller returns on `LockoutUnlockOutcome.InvalidToken`.

- [ ] **Step 3: Run the new test**

Run: `dotnet test --filter "FullyQualifiedName~LockoutUnlock_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401"`
Expected: PASS. The seeded row's `TokenLookup` matches `realToken`'s HMAC; the query returns it; Argon2id verify fails against `forgedToken`'s hash; controller returns 401.

If FAIL with "Expected: Unauthorized but got: NoContent": `ConfirmAsync` is treating a TokenLookup match as sufficient and skipping the Argon2 verify — re-check Task 2 Step 5's code. The `if (candidate is null || !_tokens.Verify(rawToken, candidate.TokenHash))` line is load-bearing.

- [ ] **Step 4: Run the full tamper-resistance class**

Run: `dotnet test --filter "FullyQualifiedName~TokenLookupTamperResistanceTests"`
Expected: 4/4 PASS (3 existing PasswordReset/EmailChange tests + 1 new LockoutUnlock test).

- [ ] **Step 5: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Tests/Integration/Authentication/TokenLookupTamperResistanceTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
test(stage-9.1.5.a): tamper-resistance test for LockoutUnlock confirm with matching lookup + wrong hash

Mirror of the existing PasswordReset / EmailChange tamper-resistance tests
(TokenLookupTamperResistanceTests.cs:36, :50). Seeds a LockoutUnlockToken row
where TokenLookup matches a real raw token but TokenHash is for a different
raw token. The indexed lookup hits, but the Argon2id verify against the row's
TokenHash must fail — controller returns 401.

This test catches a defence-in-depth regression: if a future refactor deletes
the Argon2id verify (e.g. "lookup-only is fast enough"), the happy-path suite
would still pass but this test goes red.

Case applies (testing.md § Rules): case (3) — pins the lookup + verify pair as
the contract. Either factor alone is insufficient for token validation.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-design.md §4.1 (Test #2)
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-impl.md Task 3
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 4: Verification gate + roadmap close-out

**Files:**
- Modify: `docs/roadmap-phase-three.md` (9.1.5.a entry)

This commit is **gated on three consecutive clean `dotnet test` runs**. The whole point of Stage 9.1.5.a is to confirm the diagnosis by proving the suite-wide flake stops recurring. Two clean runs is not sufficient — the flakes are intermittent at ~2/10 baseline; statistically, three consecutive cleans is ~98% confidence the cause is gone.

- [ ] **Step 1: Run the full test suite — RUN #1**

Run: `dotnet test <repo>/ProjectCeres.sln`
Expected: all green. Capture the elapsed time + total test count.

If a `RateLimitTests` test flakes: run it in isolation (`dotnet test --filter "FullyQualifiedName~<TestName>"`). If isolation passes, treat the flake as pre-existing-uncleared and CONTINUE TO RUN #2 — but document it. If the same surface flakes again in run #2, the fix didn't fully resolve and you need to STOP and report.

- [ ] **Step 2: Run the full test suite — RUN #2**

Run: `dotnet test <repo>/ProjectCeres.sln`
Expected: all green. Capture elapsed + count.

- [ ] **Step 3: Run the full test suite — RUN #3**

Run: `dotnet test <repo>/ProjectCeres.sln`
Expected: all green. Capture elapsed + count.

**If all three runs are clean AND no auth-tier test flaked even once in isolation across the three runs**: the diagnosis is confirmed. Proceed to Step 4.

**If any auth-tier test flaked**: STOP. Report the failing test name, isolation result, and whether it matches one of the surfaces deep-fix-mode identified (MfaRegenerate, LockoutUnlockConfirm cluster, Login_LimiterResetsAfterWindow). Do NOT proceed to Step 4. Deep-fix-mode re-fires.

- [ ] **Step 4: Update the 9.1.5.a roadmap entry**

Open `<repo>/docs/roadmap-phase-three.md`. Locate the Stage 9.1.5.a entry (currently at table line 1096 area; description text is `"Diagnose suite-wide auth-tier test contention — root cause TBD pending architectural diagnosis"` and verification line at 1106 area is `"- [ ] 9.1.5.a — architectural root cause identified, named, and documented..."`).

Replace the sub-stage table row text (the `| 9.1.5.a | ... |` line) with:

```
| 9.1.5.a | LockoutUnlockToken.TokenLookup retrofit — extends Stage 6.15 indexed-lookup pattern to the third token sibling | Test-isolation flake surfaced 2026-05-17. After two false-start hypotheses (rate-limit partition leak, then "TBD pending architectural diagnosis"), deep-fix-mode round 3 traced the root cause to LockoutUnlockService.ConfirmAsync running O(N) Argon2id verifies over unconsumed-and-unexpired token rows. Under integration-test load the scan saturates CPU, causing timing-sensitive sibling tests to flake. The pattern was already solved for PasswordResetToken and EmailChangeToken in Stage 6.15; this stage retrofits LockoutUnlockToken via the same HMAC-SHA256 TokenLookup column + unique index. ConfirmAsync becomes O(1) DB lookup + 1 Argon2 verify. Full diagnostic notes in task #43. |
```

Replace the verification line at line 1106 area (the `- [ ] 9.1.5.a — architectural root cause...` line) with:

```
- [x] 9.1.5.a — LockoutUnlockToken has TokenLookup column + unique index (Stage 6.15 pattern); LockoutUnlockService.ConfirmAsync queries by indexed lookup (O(1) + 1 Argon2 verify); IssueAsync stamps the lookup; tamper-resistance test pins the lookup+verify pair as a contract; three consecutive full `dotnet test` runs green with no auth-tier flake recurrence. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-impl.md`. Migration: `AddLockoutUnlockTokenLookup` (mirrors Stage 6.15's `20260511155005_AddTokenLookup`).
```

Also: update the Stage 9.1.5 section's status paragraph if it currently mentions "TBD pending architectural diagnosis" — change to "architectural diagnosis complete; LockoutUnlockToken received the same Stage 6.15 TokenLookup retrofit as its two sibling token tables."

- [ ] **Step 5: Update task #43 description**

Use `TaskUpdate` to mark task #43 with the resolution:

- New subject: `Bug: suite-wide auth-tier test contention — RESOLVED via Stage 9.1.5.a TokenLookup retrofit`
- Status: `completed`
- Description: append a closing paragraph: "Resolved 2026-05-17 by Stage 9.1.5.a (spec `7baa196`, plan commits TBD). Root cause was `LockoutUnlockService.ConfirmAsync` running O(N) Argon2id verifies over unconsumed-and-unexpired token rows; under integration-test load this saturated CPU and caused timing-sensitive sibling tests to flake. Fix retrofits the Stage 6.15 HMAC-SHA256 `TokenLookup` indexed-lookup pattern onto `LockoutUnlockToken`. Verified by three consecutive clean `dotnet test` runs."

- [ ] **Step 6: Final commit**

```bash
git -C <repo> add \
  docs/roadmap-phase-three.md

git -C <repo> commit -m "$(cat <<'EOF'
docs(stage-9.1.5.a): close out — LockoutUnlockToken TokenLookup retrofit verified

Three consecutive clean `dotnet test` runs (no auth-tier flake recurrence)
confirm the diagnosis from deep-fix-mode round 3: the suite-wide flake was
caused by ConfirmAsync's O(N) Argon2id candidate scan. The Stage 6.15
TokenLookup retrofit eliminates the scan; the flake is gone.

- docs/roadmap-phase-three.md — 9.1.5.a sub-stage row updated to reflect the
  shipped fix; verification checkbox flipped from [ ] to [x]; verification
  text replaced with the post-fix description listing the indexed-lookup
  contract + the three-clean-run gate result.
- Task #43 description updated to closed/resolved with the same diagnosis
  + fix summary.

Stage 9.1.5 batch progress: 9.1.5.a [x], 9.1.5.b [x] (shipped earlier today).
Remaining: 9.1.5.c (auth-page tokens light/dark), 9.1.5.d (in-app toggle),
9.1.5.e (language code next to globe), 9.1.5.f (SPA logout), 9.1.5.g (ADR-0076).

Case applies (testing.md § Rules): no test files modified in this commit;
production behavior unaffected.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-design.md
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-impl.md Task 4
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

## Manual verification (post-commit-4)

Stage 9.1.5.a is a pure backend / test-infrastructure stage. There is no browser-visible UX change. The verification gate IS the three-consecutive-clean-runs criterion in Task 4. If that gate passes, the stage is done.

If a stakeholder asks "did the fix work", point them at:
- The three `dotnet test` runs captured in Task 4 Steps 1-3 (timestamps + elapsed + pass count).
- The new `Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count` test which pins the O(1) contract structurally.
- The roadmap close-out at `docs/roadmap-phase-three.md` 9.1.5.a entry.

---

## Self-review

**Spec coverage:**
- §1 (the bug) — covered by Task 2 (the actual fix).
- §2 (why "extend Stage 6.15") — informs the plan structure but doesn't need a task.
- §3.1 (model field) — Task 1 Step 2.
- §3.2 (DbContext config) — Task 1 Step 3.
- §3.3 (EF migration) — Task 1 Steps 4-5.
- §3.4 (LockoutUnlockService refactor) — Task 2 Steps 3-5.
- §3.5 (files NOT touched) — preserved by scope discipline.
- §4.1 (new tests) — Test #1 = Task 2 Step 1; Test #2 = Task 3 Step 2.
- §4.2 (existing test updates) — Task 1 Steps 7-10.
- §4.3 (noise row cleanup strategy) — Task 2 Step 1's `try/finally` with `ExecuteDeleteAsync`.
- §4.4 (existing test contract preservation) — Task 2 Steps 7-9.
- §5 (edge cases) — all addressed by the fix design; nothing requires a test of its own beyond Test #1 and Test #2.
- §6.1 (in scope) — items 1-11 mapped to Tasks 1-4.
- §6.2 (out of scope) — preserved by scope discipline.
- §6.3 (deferrals) — none; spec explicitly states the fix is self-contained.
- §7 (verification checklist) — Task 4 maps every checklist item.

**Placeholder scan:** searched the plan for "TODO", "TBD", "implement later", "as appropriate", "etc.". Zero hits. Every step contains the exact code/command/expected output the engineer needs.

**Type consistency:** `TokenLookup` is `byte[]` everywhere. `lookupHasher.ComputeLookup(rawToken)` returns `byte[]` (confirmed by verify-against-codebase pre-flight). `TokenLookupHasher` is registered as `AddSingleton` in `Program.cs:106` and is injectable into both `LockoutUnlockService` (Scoped) and resolved per-call from `IServiceScope` in test code. All references consistent.

**Hook safety:** Commit 1 keeps the suite green because the model + DbContext + migration + all 4 test inserts land together (NOT NULL constraint never sees a partial state). Commit 2 keeps the suite green because constructor + IssueAsync stamp + ConfirmAsync rewrite land together (the service is always consistent). Commit 3 adds a single test against an already-shipped fix. Commit 4 is docs-only. The stop-hook should never see a red commit.

**TDD ordering:** Task 2 writes the failing regression test (Step 1) BEFORE the production change (Steps 3-5), runs it to confirm it fails for the right reason (Step 2), then runs it again to confirm the fix (Step 6). Task 3 writes the tamper test and runs it immediately — the production code is already correct from Task 2, so this test is a contract-pin, not a TDD red-then-green cycle.

**Three-consecutive-clean-runs gate:** Task 4 Steps 1-3 are the diagnosis-confirmation gate. The plan explicitly says to STOP and re-fire deep-fix-mode if any auth-tier test flakes during any of the three runs. This is the load-bearing acceptance check; it cannot be skipped.
