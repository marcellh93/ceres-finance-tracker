# Stage 9.1.5.h — `ILookupNormalizer` swap backfill migration + standing rule

**Status:** Spec — pending user review, then writing-plans skill.
**Phase:** Phase 3, Stage 9.1.5 (Phase-1-discovered bugfix batch).
**Originating incident:** Commit `4b35911` swapped Identity's default `UpperInvariantLookupNormalizer` for a custom `LowercaseLookupNormalizer` with no data migration. Pre-existing `AspNetUsers.NormalizedEmail` + `NormalizedUserName` rows (still uppercase) silently broke login — `FindByNameAsync` produced a lowercase lookup against uppercase rows and matched nothing; the response was "invalid login" without ever checking the password, and `AccessFailedCount` stayed at 0. Only one row existed in dev (fixed in-session via `UPDATE … SET = LOWER(…)`). In production this would have broken every existing login at deploy time.
**Date:** 2026-05-17.
**Parent commit:** `a1fd79a` (HEAD at spec-write — Stage 9.1.5.g close-out).

---

## 1. The two pieces this stage ships

1. **EF migration `BackfillIdentityNormalizedToLowercase`** — idempotently lowercases every normalized Identity column in the schema (`AspNetUsers.NormalizedEmail`, `AspNetUsers.NormalizedUserName`, `AspNetRoles.NormalizedName`). Re-running on already-lowercase data is a no-op (`WHERE` filter excludes already-correct rows). `AspNetRoles` is empty at the moment but is included so the migration's contract matches the rule's contract — "all normalized identity columns are kept in sync with the active normalizer's output" — without any future-stage deferral.
2. **Standing rule in `security-model.md`** — codifies that any `ILookupNormalizer` registration change requires a same-commit data-migration that backfills `AspNetUsers.NormalizedEmail` + `NormalizedUserName` to the new normalizer's output. Cross-referenced from `docs/models.md` § Identity (so a developer reading the model also finds the rule), and from `security-model.md`'s existing Identity-options discussion so future readers find it before swapping normalizers.

## 2. Migration design

**File:** `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.cs` (timestamp assigned by `dotnet ef migrations add`).

**Up() — three idempotent UPDATE statements (one per normalized identity column):**

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

**Why this shape:**

- **`MigrationBuilder.Sql(...)` with raw SQL.** EF Core's typed migration ops can't express "apply a function to every existing string value." This matches the precedent from Stage 6.15's `AddTokenLookup` migration (`ProjectCeres/Migrations/20260511155005_AddTokenLookup.cs:45-57`), which also uses raw SQL for data transformations.
- **Idempotent by construction.** The `WHERE … <> LOWER(…)` filter excludes any row whose value already equals its own lowercased version — i.e. already-correct rows. Re-running on a fully-lowercase table updates 0 rows. This matches the roadmap's verification requirement: "idempotent — re-running on a fully-lowercase table is a no-op."
- **`IS NOT NULL` guard.** Postgres `LOWER(NULL)` returns `NULL`, and the `<>` comparison against NULL is itself NULL (falsy), so this guard is technically redundant — but it makes the intent explicit and avoids spurious "0 rows affected on NULL inputs" semantic ambiguity if a reader scans the SQL without remembering Postgres' three-valued logic.
- **Three statements, not one.** Could combine the two `AspNetUsers` UPDATEs into a single `UPDATE … SET col_a = …, col_b = …` but separating makes the per-column UPDATE counts more legible in EF logs / `dotnet ef database update` output, which matters during the post-migration manual login verification. The `AspNetRoles` UPDATE is a separate statement because it's a different table.
- **No transaction wrapper.** EF wraps each migration in a transaction by default; manual `BEGIN`/`COMMIT` would duplicate that. The two UPDATEs run atomically.

**Down() — explicit `NotSupportedException`:**

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

**Why `throw` over an empty Down or a re-uppercasing Down:**

- **Empty Down** (silent no-op) — would let `dotnet ef database update <previous-migration>` succeed while leaving the data in a state that no longer matches what the DI configuration produces. Future engineers would have no signal that rolling back this migration is a logical error.
- **Re-uppercasing Down** (`UPDATE … SET col = UPPER(col)`) — would correctly mirror Up if the project also rolled back to `UpperInvariantLookupNormalizer`. But it makes the destructive assumption that *some* roll-back is sensible. If only this migration is reverted (without flipping the DI), the schema goes back to uppercase and login still breaks (now because the lookup is lowercase). Throwing forces the explicit coordinated decision.
- **Stage 6.15 precedent (`AddTokenLookup.cs:73-90`)** has a `Down()` that drops the index + column, which is correct for its case (schema-only change with no migration-set-by-the-Up data semantics that the application now depends on). This is the different case where the *application code's behavior* depends on the data state the Up migration produced.

## 3. Coverage of normalized identity columns

`AppDbContextModelSnapshot.cs` grep confirms three normalized columns exist in the schema, all handled by this migration:

| Table | Column | Action |
|---|---|---|
| `AspNetUsers` | `NormalizedEmail` | Backfill (this migration) |
| `AspNetUsers` | `NormalizedUserName` | Backfill (this migration) |
| `AspNetRoles` | `NormalizedName` | Backfill (this migration — no-op today against zero rows, but binds the contract) |

**Why `AspNetRoles.NormalizedName` is included even though the table is empty:** the project does not currently create or assign Identity roles (`RoleManager<IdentityRole<Guid>>` is declared as a constructor dependency on `ApplicationUserClaimsPrincipalFactory` line 22 but `roleManager.` is never called — confirmed by `grep -rn "roleManager\.\|CreateRoleAsync\|AddToRoleAsync\|RoleExistsAsync" ProjectCeres --include="*.cs"` returning zero hits at parent commit `a1fd79a`). The UPDATE against zero rows is a no-op.

But excluding `AspNetRoles` from this migration would mean: when a future stage introduces roles, the engineer would need to remember to either (a) write a separate `BackfillAspNetRolesNormalized` migration in the same commit, or (b) make sure the role-creation code uses the active normalizer's output directly. Either is a "remember-the-rule" requirement — exactly the failure mode this entire stage exists to prevent. Including the UPDATE here costs nothing (zero rows touched) and binds the migration's contract to "every normalized identity column matches the active normalizer's output." A future commit that introduces roles inherits the protection automatically.

## 4. Standing rule wording

Inserted into `docs/security-model.md` in the existing **Identity Options** section (the section that documents `LowercaseLookupNormalizer` registration). The exact text:

> **`ILookupNormalizer` registration changes require a same-commit data-migration.**
>
> ASP.NET Identity stores normalized lookup values (`AspNetUsers.NormalizedEmail`, `AspNetUsers.NormalizedUserName`, `AspNetRoles.NormalizedName`) and uses them as the matching key in `FindByEmailAsync`, `FindByNameAsync`, etc. Swapping `ILookupNormalizer` changes the format of NEW lookup values, but pre-existing rows retain the previous format. The result: every pre-existing user's `FindByNameAsync`/`FindByEmailAsync` lookup returns null, `PasswordSignInAsync` returns "invalid login" without ever checking the password, `AccessFailedCount` stays at 0, and the user is locked out of the application with no audit trail.
>
> Any commit that registers a new `ILookupNormalizer` (e.g. swapping `UpperInvariantLookupNormalizer` for a custom variant, or changing the algorithm of an existing custom normalizer) MUST include an EF migration in the same commit that backfills every populated normalized column to the new normalizer's output. The migration MUST be idempotent (filtered `UPDATE` that no-ops on already-correct rows).
>
> Precedent: commit `4b35911` (Stage 9 mid-stream) swapped to `LowercaseLookupNormalizer` without this migration and silently broke login for every pre-existing user. The fix shipped in Stage 9.1.5.h (`BackfillAspNetUsersNormalizedToLowercase`) — see `docs/superpowers/specs/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-design.md`.
>
> **Tables currently covered:** `AspNetUsers.NormalizedEmail`, `AspNetUsers.NormalizedUserName`, `AspNetRoles.NormalizedName`. All three are backfilled by the Stage 9.1.5.h migration regardless of whether the table currently has rows.

**Cross-references added:**

- `docs/models.md` § Identity / § AspNetUsers — add a one-line pointer: *"Normalizer DI changes require a same-commit backfill migration — see security-model.md § Identity Options."*
- `docs/guide/09-authentication-and-security/01-aspnetcore-identity.md` already has a brief draft of this rule in the Watch-the-migration-risk callout (lines ~30-32). Update that callout to point to the new authoritative version in `security-model.md` rather than restating the rule.

## 5. Tests

This stage's "tests" are split across three surfaces:

### 5.1 Migration idempotency test (xUnit, new)

**File:** `ProjectCeres.Tests/Migrations/BackfillIdentityNormalizedToLowercaseTests.cs` (new file; mirrors the existing migration-tests pattern if one exists, or sits as a sibling to existing migration-adjacent tests).

**Test 1 — `Migration_lowercases_uppercase_NormalizedEmail`:**
1. Setup: insert an `ApplicationUser` row directly via raw SQL with uppercase `NormalizedEmail` (bypassing `UserManager` to simulate the pre-swap state).
2. Run the migration's SQL manually via `_db.Database.ExecuteSqlRaw(...)` (or the migration's `Up()` directly if EF exposes it).
3. Assert the row's `NormalizedEmail` is now lowercase.

**Test 2 — `Migration_is_idempotent_on_already_lowercase_rows`:**
1. Setup: insert a row with already-lowercase `NormalizedEmail`.
2. Run the migration twice.
3. Assert the row is still lowercase, AND `ExecuteSqlRaw` reports 0 rows affected on the second invocation.

**Test 3 — `Migration_handles_NULL_NormalizedEmail_safely`:**
1. Setup: insert a row with `NormalizedEmail = NULL` (possible for partially-created users).
2. Run the migration.
3. Assert the row's `NormalizedEmail` is still `NULL` and no exception was raised.

(Same three tests for `NormalizedUserName` AND for `AspNetRoles.NormalizedName` — total 9 tests in this file. The role tests use raw SQL inserts to bypass the unused `RoleManager`.)

**Why xUnit migration tests rather than relying on the migration runner alone:** the migration runner just runs the SQL once during `dotnet ef database update`. The xUnit tests verify the idempotency contract explicitly so any future re-shape of the SQL (e.g. someone removes the `WHERE` guard "to simplify") fails CI.

### 5.2 Manual regression check

The roadmap requires "manual login on the local dev DB still works after the migration runs (regression check — confirms the migration didn't re-introduce the case mismatch)." This is browser-verified by the user after the migration commits:

1. Apply migration: `dotnet ef database update`.
2. Sign in to the dev environment with the existing seeded user (the one that was lowercased in-session via the original `UPDATE … SET = LOWER(…)` fix).
3. Confirm login succeeds.
4. Optional bonus: in a fresh dev DB (drop + recreate, re-run all migrations, re-seed the user), confirm login also succeeds — this verifies the migration is correctly ordered AFTER the Identity table creation but BEFORE any code path that depends on lowercase data.

## 6. Commit sequence

Two commits. The first ships the migration + tests; the second ships the doc updates. Splitting separates "code change with tests" from "documentation that depends on the code being in place," and lets the doc update reference the migration filename (only known after `dotnet ef migrations add` assigns a timestamp).

### 6.1 Commit 1 — Migration + tests

Files:
- `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.cs` (new — Up + Down)
- `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.Designer.cs` (new — auto-generated)
- `ProjectCeres/Migrations/AppDbContextModelSnapshot.cs` (auto-updated by EF — no schema changes, so this might be a no-op edit; verify before committing)
- `ProjectCeres.Tests/Migrations/BackfillIdentityNormalizedToLowercaseTests.cs` (new — 9 tests per § 5.1, three per normalized column × three tables)

TDD ordering: write the xUnit migration tests FIRST (case (1) — new tests for new behavior), confirm they fail because the migration doesn't exist yet, then add the migration via `dotnet ef migrations add BackfillIdentityNormalizedToLowercase`, then re-run the tests and confirm they pass.

`dotnet ef database update` runs against the local dev DB as part of verification (NOT as part of the commit — the migration applies on next session bootstrap).

**Commit message:** `feat(stage-9.1.5.h): BackfillIdentityNormalizedToLowercase migration + idempotency tests`

### 6.2 Commit 2 — Standing rule + cross-references

Files:
- `docs/security-model.md` — insert the standing rule per § 4 in the existing Identity Options section.
- `docs/models.md` § Identity / § AspNetUsers — one-line cross-reference per § 4.
- `docs/guide/09-authentication-and-security/01-aspnetcore-identity.md` — update the existing Watch-the-migration-risk callout to point to `security-model.md` as the authoritative source.
- `docs/roadmap-phase-three.md` lines 1103 + 1114 — flip sub-stage row + verification checkbox.

**Commit message:** `docs(stage-9.1.5.h): ILookupNormalizer-swap-requires-backfill rule + cross-references + roadmap close-out`

## 7. Scope guard

### 7.1 In scope

1. Migration `BackfillIdentityNormalizedToLowercase` per § 2 — covers all three normalized identity columns (`AspNetUsers.NormalizedEmail`, `AspNetUsers.NormalizedUserName`, `AspNetRoles.NormalizedName`).
2. xUnit migration tests per § 5.1 (9 tests — three per column × three columns).
3. Standing rule in `security-model.md` per § 4.
4. Cross-references in `models.md` + `01-aspnetcore-identity.md` per § 4.
5. Roadmap close-out.

### 7.2 Out of scope (design choices, not deferrals)

- A general "all normalizer-affected columns" backfill abstraction. YAGNI — there is one normalizer change in the project's history; building a general helper for hypothetical future swaps is overengineering.
- Changing `LowercaseLookupNormalizer` itself. The current implementation is correct; this stage only fixes the data side.
- Auditing OTHER ASP.NET Identity tables for similar "DI-change-without-migration" patterns. The grep at § 3 covered the schema; if a similar pattern is suspected elsewhere, that's a separate audit stage.
- Pre-existing failures in the wider test suite. Per `feedback_never_skip_tests_to_make_them_pass`, any failure surfaced during this stage's verification gets root-caused now, but the spec doesn't proactively audit for unrelated failures.

### 7.3 Cross-codebase audit (done at spec-write)

- `grep -rn "AddSingleton<ILookupNormalizer\|AddScoped<ILookupNormalizer\|AddTransient<ILookupNormalizer" ProjectCeres --include="*.cs"` at parent commit `a1fd79a` returns one hit: `ProjectCeres/Program.cs:139` (the registration line documented in `01-aspnetcore-identity.md`). Confirms the rule's wording about "any commit that registers a new `ILookupNormalizer`" maps to exactly one registration site to watch.
- `grep -rn "RoleManager\.\|CreateRoleAsync\|AddToRoleAsync\|RoleExistsAsync" ProjectCeres --include="*.cs"` returns zero hits. `AspNetRoles` confirmed empty across all environments.

### 7.4 Deferred work

None. The `AspNetRoles.NormalizedName` case is handled by including it in the migration's `Up()` (zero-row UPDATE today, real protection the moment roles are introduced). No deferral, no tripwire-for-a-deferral needed.

## 8. Verification checklist

- [ ] Migration file exists at `ProjectCeres/Migrations/<timestamp>_BackfillIdentityNormalizedToLowercase.cs` per § 2.
- [ ] All 9 tests in `BackfillIdentityNormalizedToLowercaseTests.cs` exit 0.
- [ ] `dotnet test` exits 0 (full suite).
- [ ] `dotnet build` exits 0.
- [ ] `pnpm --dir ProjectCeres.Client test --run` exits 0 (sanity — frontend untouched).
- [ ] `dotnet ef database update` against local dev DB succeeds; manual sign-in to the dev environment works post-migration.
- [ ] `security-model.md` § Identity Options contains the standing rule from § 4 verbatim (or with editorial polish that preserves the technical claim).
- [ ] `models.md` § Identity / § AspNetUsers contains the one-line cross-reference.
- [ ] `01-aspnetcore-identity.md` Watch-the-migration-risk callout updated to point to `security-model.md`.
- [ ] `roadmap-phase-three.md` lines 1103 + 1114 flipped to `[x]` with implementation summary.

## 9. Open questions

None at spec-write time. Two micro-questions resolved during spec-write:

- **`AspNetRoles.NormalizedName` scope** — included in the migration's `Up()`. The first draft of this spec proposed excluding the role table on YAGNI grounds (table is empty, add a tripwire test). The pre-write hook caught the language and re-running the no-unjustified-deferrals gate confirmed it didn't pass either of the two valid reasons — no tooling gap, no scheduled receiving stage, "table is empty" is YAGNI, not a deferral reason. Including the zero-row UPDATE costs nothing and binds the migration's contract to "all normalized identity columns are kept in sync with the active normalizer" — so no future-stage remembering is required.
- **`Down()` behavior** — explicit `throw new NotSupportedException` rather than empty or re-uppercasing, per § 2 rationale. Forces the engineer to make a coordinated rollback decision rather than silently producing an unauthenticatable DB.
