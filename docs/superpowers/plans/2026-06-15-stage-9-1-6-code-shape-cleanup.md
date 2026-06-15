# Stage 9.1.6 — Code-shape cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Discharge the raw-SQL prohibition (3 production sites → interpolated/guarded) and make redundant namespace prefixes a build-enforced tripwire (IDE0001 → warning), against the codebase as it is now.

**Architecture:** Six commits in dependency order — raw-SQL fixes first (security rule), then production prefix drops, then test-fixture prefix drops, then a production-only code-simplification sweep, and finally the IDE0001 severity flip (last, so a clean build proves completion). No new ADR.

**Tech Stack:** .NET 10, EF Core 10 (Npgsql 10.0.1), xUnit + FluentAssertions, Roslyn analyzers (CER family), `.editorconfig` + `Directory.Build.props`.

**Spec:** `docs/superpowers/specs/2026-06-15-stage-9-1-6-code-shape-cleanup-design.md` (commit `e28b181`).
**Branch:** `stage-9.1.6-code-shape-cleanup` (off `main`; spec already committed there).
**All line numbers verified against current `main` 2026-06-15** via an 8-agent read-only fact-gather (workflow `wf_9b17a9f6-062`).

## Key facts (verified this session)
- EF Core 10 scalar-query method is `SqlQuery<T>($"…")` (the interpolated overload; accepts a zero-parameter constant string). `SqlQueryRaw`/`SqlQueryInterpolated` are the older names. Source: Microsoft Learn `ef/core/querying/sql-queries`.
- Table/column **identifiers cannot be `DbParameter`s** in EF — they must stay string-interpolated with a guard. So 9.1.6.a is a *guard*, not a raw→interpolated swap.
- `PreAuthRlsScope` is a `static` class; `BeginPreAuthUserScopeAsync` has **no** `[PreAuthScope]`/`[RlsBypassJustified]`/`[AllowsWallClock]` attribute → the SQL-API swap raises no CER001/005/006 diagnostic.
- `AuthController` imports both `Microsoft.AspNetCore.Identity` (`:6`) and `Microsoft.AspNetCore.Mvc` (`:7`) → `SignInResult` is ambiguous (`CS0104`); the prefix is load-bearing and stays.
- `Directory.Build.props`: `EnforceCodeStyleInBuild=true` (`:3`), `AnalysisLevel=latest-recommended` (`:4`), `TreatWarningsAsErrors=false` (`:5`) → IDE0001=warning shows **yellow, not red**.
- `.editorconfig` severity block ends at `:25` (`dotnet_diagnostic.CER006.severity = error`); IDE0001 not present. Insert after `:25`.

## File map
- Modify: `ProjectCeres/Common/Authentication/PreAuthRlsScope.cs` (Task 1)
- Modify: `ProjectCeres/Tools/SeedDevUser.cs` (Task 2)
- Test: `ProjectCeres.Tests/Unit/Tools/SeedDevUserTableGuardTests.cs` (Task 2, new)
- Modify: `ProjectCeres/Data/AppDbContext.cs`, `ProjectCeres/Program.cs`, `ProjectCeres/Common/Authentication/PasswordResetService.cs`, `ProjectCeres/Controllers/Api/AuthController.cs` (Task 3)
- Modify: `ProjectCeres.Tests/Integration/Authentication/{AuthTestFixture,ArchitectureTests,FailedLoginRecorderTests,LockoutUnlockIssuanceTests,LockoutUnlockConfirmTests}.cs` + `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs` (Task 4)
- Modify: production `.cs` under `ProjectCeres/` (Task 5, sweep)
- Modify: `.editorconfig` (Task 6) + `docs/roadmap-phase-three.md` (Task 6, close-out)

---

### Task 1: 9.1.6.b — `PreAuthRlsScope` raw→interpolated scalar query

**Files:**
- Modify: `ProjectCeres/Common/Authentication/PreAuthRlsScope.cs:81`

- [ ] **Step 1: Confirm the current site + analyzer-safety.**
Run: `grep -n "SqlQueryRaw\|ExecuteSqlInterpolated\|PreAuthScope\|RlsBypassJustified\|class PreAuthRlsScope" ProjectCeres/Common/Authentication/PreAuthRlsScope.cs`
Expected: `SqlQueryRaw<string>` at `:81`, `ExecuteSqlInterpolatedAsync` at `:95`, `public static class PreAuthRlsScope` at `:52`, and **no** attribute hits. (If any `[PreAuthScope]`/`[RlsBypassJustified]` appears on the class or method, STOP and re-scope — the analyzer assumption changed.)

- [ ] **Step 2: Make the edit.** Replace line 81:
  - Old: `                .SqlQueryRaw<string>("SELECT current_setting('app.current_user_ref', true) AS \"Value\"")`
  - New: `                .SqlQuery<string>($"SELECT current_setting('app.current_user_ref', true) AS \"Value\"")`
  (Note: `$"…"` interpolated form with **no** interpolated values — a pure constant, which `SqlQuery<T>` accepts. This matches the file's own `ExecuteSqlInterpolatedAsync` precedent at `:95`.)

- [ ] **Step 3: Build — confirm no CER diagnostics + compiles.**
Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -iE "CER00[156]|error|SqlQuery" || echo "clean build, no CER, no errors"`
Expected: `clean build, no CER, no errors`. (CER005/CER006 are at `error` severity — any hit fails the build, which would mean the swap changed transaction shape. It must not.)

- [ ] **Step 4: Run the pre-auth scope tests.**
Run: `dotnet test --filter "FullyQualifiedName~PreAuth|FullyQualifiedName~Rls" 2>&1 | tail -5`
Expected: all pass (the nested-scope cross-user guard test + RLS smoke tests stay green — behavior unchanged, only the SQL-API name differs).

- [ ] **Step 5: Commit.**
```bash
git add ProjectCeres/Common/Authentication/PreAuthRlsScope.cs
git commit -m "refactor(9.1.6.b): PreAuthRlsScope SqlQueryRaw -> SqlQuery (interpolated constant)

Matches the file's own ExecuteSqlInterpolatedAsync precedent at :95.
Definition site, unmarked class — no CER001/005/006 trip. Discharges the
security-model raw-SQL prohibition for this site.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: 9.1.6.a — `SeedDevUser` throwing allow-list guard

**Files:**
- Modify: `ProjectCeres/Tools/SeedDevUser.cs` (UPDATE foreach ~`:199-204`; COUNT foreach ~`:358`)
- Test: `ProjectCeres.Tests/Unit/Tools/SeedDevUserTableGuardTests.cs` (new)

**Context:** Both raw-SQL sites iterate `UserOwnedModel.FinanceTables(adminDb.Model).Select(t => t.PostgresTableName)` — the table names come from the EF model, not user input, so no injection vector exists *today*. The work hardens against a future caller and documents the invariant. The table name **cannot** be a `DbParameter` (EF rule), so it stays interpolated; the guard is the improvement. The UPDATE site can move to `ExecuteSqlInterpolatedAsync` (sentinel/userId as parameters); the COUNT site stays raw Npgsql (no EF scalar-exec API).

- [ ] **Step 1: Write the failing guard test.** Create `ProjectCeres.Tests/Unit/Tools/SeedDevUserTableGuardTests.cs`:
```csharp
using FluentAssertions;
using ProjectCeres.Tools;
using Xunit;

namespace ProjectCeres.Tests.Unit.Tools;

public class SeedDevUserTableGuardTests
{
    [Fact]
    public void AssertKnownTable_throws_for_table_not_in_allowed_set()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Transactions", "Accounts" };

        var act = () => SeedDevUser.AssertKnownTable("Users; DROP TABLE Accounts", allowed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not in the user-owned table allow-list*");
    }

    [Fact]
    public void AssertKnownTable_returns_the_name_for_an_allowed_table()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Transactions", "Accounts" };

        SeedDevUser.AssertKnownTable("Transactions", allowed).Should().Be("Transactions");
    }
}
```

- [ ] **Step 2: Run it — verify it fails (method doesn't exist).**
Run: `dotnet test --filter "FullyQualifiedName~SeedDevUserTableGuardTests" 2>&1 | tail -5`
Expected: FAIL — `SeedDevUser` does not contain a definition for `AssertKnownTable` (compile error).

- [ ] **Step 3: Add the guard method to `SeedDevUser`.** Add this `internal static` method to the `SeedDevUser` class (so the test can reach it; the class is in `ProjectCeres/Tools/`):
```csharp
    // Table names are interpolated into raw SQL (EF cannot parameterize identifiers).
    // They come from UserOwnedModel.FinanceTables, never user input — this guard pins
    // that invariant so a future caller can't introduce an injection vector. (9.1.6.a)
    internal static string AssertKnownTable(string table, ISet<string> allowed)
    {
        if (!allowed.Contains(table))
            throw new InvalidOperationException(
                $"Refusing to interpolate '{table}': not in the user-owned table allow-list.");
        return table;
    }
```

- [ ] **Step 4: Wire the guard into both call sites.**
  At the UPDATE foreach (`~:199`), build the allow-list once before the loop and guard + swap to interpolated:
```csharp
        var allowedTables = UserOwnedModel.FinanceTables(adminDb.Model)
            .Select(t => t.PostgresTableName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var table in allowedTables)
        {
            AssertKnownTable(table, allowedTables);
            await adminDb.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "{table}" SET "UserId" = {userId} WHERE "UserId" = {sentinel}""");
        }
```
  (Where `userId` is the `Guid` and `sentinel` is the sentinel `Guid` — pass the `Guid` values directly so EF parameterizes them; only `{table}` stays a raw identifier. If `sentinelStr`/`userId` are currently `string`/formatted, pass the underlying `Guid` instead so they become real parameters. Keep the `:D` formatting only where a string is genuinely required.)
  At the COUNT foreach (`~:358`), add the guard but keep the existing raw-Npgsql block (no EF scalar-exec exists):
```csharp
        var allowedTables = UserOwnedModel.FinanceTables(adminDb.Model)
            .Select(t => t.PostgresTableName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var table in allowedTables)
        {
            AssertKnownTable(table, allowedTables);
            // EF cannot parameterize an identifier and has no scalar-returning ExecuteSql;
            // table is guarded above, sentinel is a constant — raw Npgsql is correct here. (9.1.6.a)
            var sql = $"""SELECT COUNT(*) FROM "{table}" WHERE "UserId" = '{sentinelStr}'""";
            // ... existing GetDbConnection / CreateCommand / ExecuteScalarAsync block unchanged ...
        }
```

- [ ] **Step 5: Run the guard test — verify it passes.**
Run: `dotnet test --filter "FullyQualifiedName~SeedDevUserTableGuardTests" 2>&1 | tail -5`
Expected: PASS (2 tests).

- [ ] **Step 6: Build + confirm no raw `ExecuteSqlRawAsync` remains at the UPDATE site.**
Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | tail -3 && grep -n "ExecuteSqlRawAsync" ProjectCeres/Tools/SeedDevUser.cs || echo "no ExecuteSqlRawAsync remaining"`
Expected: build succeeds; `no ExecuteSqlRawAsync remaining` (the COUNT site uses raw Npgsql, not `ExecuteSqlRawAsync`, so this grep should be empty).

- [ ] **Step 7: Run the IDOR/RLS suite (same admin-DB path).**
Run: `dotnet test --filter "FullyQualifiedName~Idor|FullyQualifiedName~Rls" 2>&1 | tail -5`
Expected: all pass.

- [ ] **Step 8: Commit.**
```bash
git add ProjectCeres/Tools/SeedDevUser.cs ProjectCeres.Tests/Unit/Tools/SeedDevUserTableGuardTests.cs
git commit -m "refactor(9.1.6.a): SeedDevUser table-name allow-list guard + interpolated UPDATE

Table identifiers can't be EF parameters, so they stay interpolated — but
now behind an AssertKnownTable guard validating against UserOwnedModel.
FinanceTables (the old UserOwnedTables.All array was deleted b0995d9). UPDATE
site moves to ExecuteSqlInterpolatedAsync (sentinel/userId parameterized);
COUNT stays raw Npgsql (no EF scalar-exec) with the guard added.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: 9.1.6.c/d/e — production prefix drops

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs:46,50,62,66`
- Modify: `ProjectCeres/Program.cs:401,420`
- Modify: `ProjectCeres/Common/Authentication/PasswordResetService.cs:335`
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs:199` (comment only — stays qualified)

- [ ] **Step 1: AppDbContext — drop `Microsoft.EntityFrameworkCore.` from 4 catch clauses.** File has `using Microsoft.EntityFrameworkCore;` at `:4`. In all four `catch` clauses (`:46,50,62,66`), change `catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)` → `catch (DbUpdateException ex)`. Use replace-all on the exact substring `Microsoft.EntityFrameworkCore.DbUpdateException` → `DbUpdateException` within this file.

- [ ] **Step 2: Program.cs — drop `System.Security.Claims.` from 2 sites.** File has `using System.Security.Claims;` at `:2`. At `:401` and `:420`, change `FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)` → `FindFirst(ClaimTypes.NameIdentifier)`. **Leave `:48` `UnprocessableEntityObjectResult` qualified-or-not as-is for now** — it's the 422 envelope; the unqualified form is `new UnprocessableEntityObjectResult(...)` and `using Microsoft.AspNetCore.Mvc;` is at `:10`, so dropping the prefix is safe IF done — but the envelope JSON must stay byte-identical. Drop it: `new Microsoft.AspNetCore.Mvc.UnprocessableEntityObjectResult(new` → `new UnprocessableEntityObjectResult(new`. (The object initializer at `:49-56` is untouched, so the shape is byte-identical.)

- [ ] **Step 3: PasswordResetService — drop `Microsoft.AspNetCore.Identity.` prefix.** File has `using Microsoft.AspNetCore.Identity;` at `:2`. At `:335`, change `Microsoft.AspNetCore.Identity.TokenOptions.DefaultAuthenticatorProvider` → `TokenOptions.DefaultAuthenticatorProvider`.

- [ ] **Step 4: AuthController — keep `:199` qualified, add the load-bearing comment.** At `:199`, the line is `        Microsoft.AspNetCore.Identity.SignInResult signIn;`. Do **NOT** shorten it. Add a comment on the line above:
```csharp
        // Fully qualified: both Identity and Mvc define SignInResult (CS0104). Do not shorten.
        Microsoft.AspNetCore.Identity.SignInResult signIn;
```

- [ ] **Step 5: Build — confirm clean (and CS0104 didn't sneak in).**
Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -iE "error|CS0104" || echo "clean"`
Expected: `clean`.

- [ ] **Step 6: Run auth + 422 tests.**
Run: `dotnet test --filter "FullyQualifiedName~ProjectCeres.Tests.Unit|FullyQualifiedName~Auth" 2>&1 | tail -5`
Expected: all pass (422 `VALIDATION_ERROR` envelope tests confirm the shape is byte-identical).

- [ ] **Step 7: Commit.**
```bash
git add ProjectCeres/Data/AppDbContext.cs ProjectCeres/Program.cs ProjectCeres/Common/Authentication/PasswordResetService.cs ProjectCeres/Controllers/Api/AuthController.cs
git commit -m "refactor(9.1.6.c-e): drop redundant namespace prefixes in production .cs

AppDbContext (4 DbUpdateException catches), Program.cs (2 ClaimTypes + the
422 UnprocessableEntityObjectResult — envelope shape byte-identical),
PasswordResetService (TokenOptions). AuthController:199 SignInResult STAYS
qualified (Identity vs Mvc CS0104) with a load-bearing comment.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: 9.1.6.f — test-fixture prefix drops (32 sites)

**Files (verified site counts):**
- Modify: `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs` (14 sites: `:193,262,266,272,273,279,280,292,294,295,297,300,302,324`)
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` (4: `:88,92,93,840`)
- Modify: `ProjectCeres.Tests/Integration/Authentication/FailedLoginRecorderTests.cs` (1: `:186`)
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs` (1: `:58`)
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs` (2: `:276,517`)
- Modify: `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs` (10: `:40,60,107,254,273,301,305,326,327,353`)

**Important:** some sites need a **new `using` added at the top**, not just a prefix drop. Add the `using` only if the file lacks it.

- [ ] **Step 1: AuthTestFixture.cs — drop prefixes + add 5 usings.** Add at the top (if absent): `using System.Security.Cryptography;`, `using System.Globalization;`, `using ProjectCeres.Data;`, `using Microsoft.AspNetCore.Mvc.Testing;` (only if not already imported — many are; grep first). Then shorten each site:
  - `:193` `System.Security.Cryptography.HMACSHA1` → `HMACSHA1`
  - `:262` `Microsoft.AspNetCore.Http.DefaultHttpContext` → `DefaultHttpContext`
  - `:266` `System.Globalization.CultureInfo.InvariantCulture` → `CultureInfo.InvariantCulture`
  - `:272` `Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory` → `IUserClaimsPrincipalFactory`
  - `:273` `Microsoft.AspNetCore.Http.IHttpContextAccessor` → `IHttpContextAccessor`
  - `:279` `ProjectCeres.Data.AppDbContext` → `AppDbContext`
  - `:280` `ProjectCeres.Models.UserSession` → `UserSession` (file already has `using ProjectCeres.Models;`)
  - `:292` `Microsoft.AspNetCore.Authentication.AuthenticationTicket` → `AuthenticationTicket`
  - `:294` `Microsoft.AspNetCore.Authentication.AuthenticationProperties` → `AuthenticationProperties`
  - `:295` `Microsoft.AspNetCore.Identity.IdentityConstants` → `IdentityConstants`
  - `:297` `Microsoft.AspNetCore.DataProtection.IDataProtectionProvider` → `IDataProtectionProvider`
  - `:300` `Microsoft.AspNetCore.Identity.IdentityConstants` → `IdentityConstants`
  - `:302` `Microsoft.AspNetCore.Authentication.TicketDataFormat` → `TicketDataFormat`
  - `:324` `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions` → `WebApplicationFactoryClientOptions`
  (For `Microsoft.AspNetCore.Authentication`, `Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.DataProtection`, `Microsoft.AspNetCore.Identity` — add the `using` if absent. Grep the file head first.)

- [ ] **Step 2: ArchitectureTests.cs.** Add `using Microsoft.EntityFrameworkCore.Metadata;` and `using ProjectCeres.Data;` if absent.
  - `:88` `ProjectCeres.Tests.Integration.AuthTestWebApplicationFactory` → `AuthTestWebApplicationFactory` (add `using ProjectCeres.Tests.Integration;` if absent)
  - `:92` `ProjectCeres.Data.AppDbContext` → `AppDbContext`
  - `:93` `ProjectCeres.Models.FailedLoginAttempt` → `FailedLoginAttempt` (file has `using ProjectCeres.Models;`)
  - `:840` `Microsoft.EntityFrameworkCore.Metadata.IEntityType` → `IEntityType`

- [ ] **Step 3: FailedLoginRecorderTests.cs:186** `System.Net.HttpStatusCode` → `HttpStatusCode` (file has `using System.Net;`).

- [ ] **Step 4: LockoutUnlockIssuanceTests.cs:58** `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory` → `WebApplicationFactory` (add `using Microsoft.AspNetCore.Mvc.Testing;` if absent).

- [ ] **Step 5: LockoutUnlockConfirmTests.cs:276,517** `System.Diagnostics.Stopwatch` → `Stopwatch` (file has `using System.Diagnostics;`).

- [ ] **Step 6: RateLimitedAuthTestWebApplicationFactory.cs.** All 10 sites' namespaces are already imported (RateLimiting `:9`, Claims `:2`, Http `:6`, System.Threading.RateLimiting `:3`) — pure prefix drops, no new usings:
  - `:40` `Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware` → `RateLimitingMiddleware` (in a `<see cref>` doc comment)
  - `:60,107` `Microsoft.AspNetCore.RateLimiting.RateLimiterOptions` → `RateLimiterOptions`
  - `:254,273,305` `System.Security.Claims.ClaimTypes` → `ClaimTypes`
  - `:301,327,353` `Microsoft.AspNetCore.Http.HttpContext` → `HttpContext`
  - `:326` `System.Threading.RateLimiting.PartitionedRateLimiter` → `PartitionedRateLimiter`

- [ ] **Step 7: Build the test project.**
Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj 2>&1 | grep -iE "error|CS0104|CS0246" || echo "clean"`
Expected: `clean`. (CS0246 = a needed `using` is missing — add it. CS0104 = an added `using` created an ambiguity — revert that site to qualified.)

- [ ] **Step 8: Full test suite stays green.**
Run: `dotnet test 2>&1 | tail -5`
Expected: all pass, same count as before this task.

- [ ] **Step 9: Commit.**
```bash
git add ProjectCeres.Tests/Integration/
git commit -m "refactor(9.1.6.f): drop redundant namespace prefixes in auth test fixtures

32 IDE0001 sites across 6 files (AuthTestFixture 14, RateLimitedAuthTest 10,
ArchitectureTests 4, others 4). Some required adding the now-relied-on using
(System.Security.Cryptography, System.Globalization, ProjectCeres.Data,
EFCore.Metadata, Mvc.Testing). Test-only; production verified independently.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: 9.1.6.g — code-simplification sweep over `ProjectCeres/` production only

**Files:** production `.cs` under `ProjectCeres/` (NOT `ProjectCeres.Tests/`, NOT `ProjectCeres.Client/src/` — those are deferred to 9.1.6.h/.i per spec §6).

**Tooling:** the `code-simplifier:code-simplifier` plugin agent (there is NO `simplify` skill — confirmed). Dispatch it scoped to production code.

- [ ] **Step 1: Capture the baseline test count + suite runtime.**
Run: `dotnet test 2>&1 | tail -3` — record the "Passed: N" count and elapsed time (the post-sweep run must match the count and stay within 30% on runtime, per `feedback_dont_handwave_perf_variance`).

- [ ] **Step 2: Run the discovery pass.** Dispatch the `code-simplifier` agent against `ProjectCeres/` production `.cs` (exclude `Migrations/` — generated). Ask it for a findings table grouped by class: target-typed `new()`, collection expressions, `is null`/`is not null`, switch expressions over if-chains, LINQ over hand-rolled loops, primary constructors *where they shrink the file*, `ArgumentNullException.ThrowIfNull`, expression-bodied members, `?.` over null-guards, `await using`. The agent must NOT touch behavior, perf class, or public API — those go to a sibling `[ ]`.

- [ ] **Step 3: Record the findings table in the stage body.** Before applying fixes, paste the grouped findings table into `docs/roadmap-phase-three.md` § Stage 9.1.6 (so the count is captured before fixes flatten it), with the eventual commit hash referenced.

- [ ] **Step 4: Apply safe-mechanical fixes, one commit per class.** For each simplification class, apply the fixes, then:
Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build clean; test count == baseline (Step 1). Commit per class:
```bash
git add ProjectCeres/
git commit -m "refactor(9.1.6.g): simplify <class-name> across ProjectCeres production

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5: Verify count + runtime invariant.**
Run: `dotnet test 2>&1 | tail -3`
Expected: `Passed: N` == Step 1's N (no test added/modified/skipped to make a simplification pass). If runtime moved >30%, re-run once before concluding (per `feedback_dont_handwave_perf_variance`).

---

### Task 6: IDE0001 → warning + close-out

**Files:**
- Modify: `.editorconfig` (insert after `:25`)
- Modify: `docs/roadmap-phase-three.md` § Stage 9.1.6 (tick a–g, add 9.1.6.h/.i siblings, rewrite the .g line)

- [ ] **Step 1: Pre-flight — confirm a–f left zero IDE0001 sites.** Temporarily add the severity line (next step) is how we detect; but first confirm no FQ-prefix-whose-namespace-is-imported remains via grep across the touched files:
Run: `grep -rnE "Microsoft\.(AspNetCore|EntityFrameworkCore)\.[A-Za-z.]+\.[A-Z]" ProjectCeres/Data/AppDbContext.cs ProjectCeres/Common/Authentication/PasswordResetService.cs | grep -v "SignInResult" || echo "no obvious FQ remnants"`
Expected: `no obvious FQ remnants` (AuthController's intentional `SignInResult` is excluded).

- [ ] **Step 2: Add the IDE0001 severity line.** In `.editorconfig`, after line 25 (`dotnet_diagnostic.CER006.severity = error`), add:
```
# Stage 9.1.6 — redundant namespace prefixes (IDE0001) are now a build-visible
# warning + regression tripwire. TreatWarningsAsErrors=false so this is yellow, not red.
dotnet_diagnostic.IDE0001.severity = warning
```

- [ ] **Step 3: Build the whole solution — confirm ZERO IDE0001 warnings.**
Run: `dotnet build 2>&1 | grep -c "IDE0001"`
Expected: `0`. (If non-zero, a–f missed a site — `dotnet build 2>&1 | grep "IDE0001"` lists them; fix, re-commit under the relevant task, then re-run.)

- [ ] **Step 4: Full verification — all four gates green.**
Run: `dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3 && pnpm --dir ProjectCeres.Client build 2>&1 | tail -3 && pnpm --dir ProjectCeres.Client test --run 2>&1 | tail -3`
Expected: all exit 0.

- [ ] **Step 5: Roadmap close-out (Phase E).** In `docs/roadmap-phase-three.md` § Stage 9.1.6:
  - Tick `[x]` for 9.1.6.a–g in the verification checklist.
  - Rewrite the existing 9.1.6.g checklist line (`~:1176`) so its scope reads `ProjectCeres/` **only**, appending: "test + React sweeps split to 9.1.6.h / 9.1.6.i (2026-06-15) — see spec §6."
  - Add the two sibling `[ ]` lines (sub-stage table + checklist) verbatim from spec §6:
```
- [ ] 9.1.6.h — code-simplification sweep over `ProjectCeres.Tests/` (narrowed out of 9.1.6.g, 2026-06-15; see spec §6). Same discovery-then-fix procedure + findings table + test-count-invariant gate as 9.1.6.g.
- [ ] 9.1.6.i — code-simplification sweep over `ProjectCeres.Client/src/` (narrowed out of 9.1.6.g, 2026-06-15; see spec §6). Routes through `frontend-orchestrator` → `vercel-react-best-practices`.
```
  - Flip the stage **Status** to ✅ Done only after confirming the only remaining unchecked items under the heading are 9.1.6.h/.i (the deliberately-deferred siblings with their receiving back-reference).

- [ ] **Step 6: Run sync-docs + changelog-sync** (Phase E requirement). sync-docs against the diff (security-model.md already documents the raw-SQL rule — confirm no change needed; the IDE0001 severity is a new `.editorconfig` fact worth a one-line note if security-model/testing.md tracks analyzer severities). changelog-sync: add to `[Unreleased]` under a "Tooling / Code quality" or existing group — "Changed: replaced production raw-SQL (`ExecuteSqlRaw`/`SqlQueryRaw`) with guarded/interpolated equivalents; dropped redundant namespace prefixes; promoted IDE0001 to a build warning as a regression tripwire."

- [ ] **Step 7: Commit the close-out.**
```bash
git add .editorconfig docs/roadmap-phase-three.md CHANGELOG.md
git commit -m "feat(9.1.6): promote IDE0001 to warning + close-out (a-g done; h/i queued)

IDE0001=warning is the regression tripwire — a clean build proves the prefix
sweep complete. Roadmap a-g ticked; 9.1.6.g re-scoped to production-only with
test/React sweeps split to new siblings 9.1.6.h/.i (no-defer gate: receiving
[ ] lines + Phase-E close-out tripwire). sync-docs + changelog-sync fired.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-review (against the spec)
- **Spec §2.a (SeedDevUser guard, not swap):** Task 2 — `AssertKnownTable` guard + interpolated UPDATE + raw-Npgsql COUNT kept. ✓
- **Spec §2.b (SqlQuery, no CER trip):** Task 1 — `SqlQuery<string>($"…")`, build-checked for CER005/006. ✓
- **Spec §2.c/d/e (prefix drops; AuthController stays qualified):** Task 3 — all sites with current line numbers; AuthController:199 keeps the prefix + comment. ✓
- **Spec §2.f (32 fixture sites + new usings):** Task 4 — every site enumerated with its exact change + which need a new `using`. ✓ (corrected count: spec said ~19, gather found 32 — the plan uses the verified 32.)
- **Spec §2.g (production-only sweep, code-simplifier agent):** Task 5 — scoped to `ProjectCeres/`, baseline+invariant gates, no `simplify` skill. ✓
- **Spec §IDE0001 (promote to warning, last):** Task 6 — inserted after `.editorconfig:25`, flip last, clean-build tripwire. ✓
- **Spec §3 (sequencing):** Tasks 1→6 match the spec's commit order (b, a, c-e, f, g, flip). ✓
- **Spec §6 (deferral entry):** Task 6 Step 5 — creates 9.1.6.h/.i roadmap `[ ]` lines + rewrites the .g line (the tripwire), in the close-out commit. ✓
- **Spec §4 (verification):** every task ends with build/test gates; Task 6 runs all four. ✓
- **Spec §5 (out of scope):** the 3 raw-SQL test files are never touched (Tasks edit only the named files). ✓
- **No placeholders:** every code step has verbatim source (from the fact-gather) or an exact command. The one judgment step (Task 5 simplification fixes) is inherently discovery-driven but bounded by the per-class-commit + invariant gates. ✓
- **Line-number consistency:** all line numbers trace to workflow `wf_9b17a9f6-062`'s verified output.
