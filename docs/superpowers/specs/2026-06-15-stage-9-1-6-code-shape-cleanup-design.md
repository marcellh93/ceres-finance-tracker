# Stage 9.1.6 — Code-shape cleanup (raw SQL + namespace prefixes) (design)

**Date:** 2026-06-15
**Status:** Draft — pending user review
**Roadmap:** `docs/roadmap-phase-three.md` § Stage 9.1.6 (opened 2026-05-21; ❌ Pending). Closes sub-stages a–g.

**Origin:** User noticed two recurring code-shape smells reading the post-9.1.5 codebase — EF raw-SQL where the interpolated/guarded equivalent communicates intent, and `.cs` files referencing types by full namespace prefix despite the matching `using`. Closed-batch container following the 9.1.5 precedent.

**Locked decisions (user, 2026-06-15):**
1. **IDE0001 detection** — promote to `warning` in `.editorconfig` (`dotnet_diagnostic.IDE0001.severity = warning`) as a permanent regression tripwire. **(Revised 2026-06-15, see § IDE0001 EXECUTION CORRECTION: the tripwire is `dotnet format style --verify-no-changes`, NOT `dotnet build` — IDE0001 is not a build diagnostic. Scope is the full 163-site surface, swept via `dotnet format`, not a 6-file hand-edit.)**
2. **AuthController `SignInResult`** — keep it fully qualified (load-bearing; disambiguates MVC's `SignInResult`); drop from 9.1.6.e's removal list.
3. **9.1.6.g simplification sweep** — scope to **`ProjectCeres/` production `.cs` only**; test + React sweeps queued as sibling `[ ]` lines.

**ceres-researcher fact-find** (2026-06-15) + **verify-against-codebase** pre-flight (2026-06-15) corrections folded in — cited inline.

---

## 1. What this stage is

Discharge a documented security rule (`security-model.md` § List Endpoint Scoping line 246: "`FromSqlRaw`/`ExecuteSqlRaw` are prohibited without explicit code-review approval — use the interpolated forms") AND make redundant namespace prefixes a build-enforced tripwire — verified against the codebase **as it is now**, not the 2026-05-21 snapshot. No new ADR (shape cleanup against existing decisions ADR-0068 / ADR-0077).

**Two corrections from the pre-flight reshaped the original scope** (see §2.a and §2.IDE0001). No sub-stage drifted to already-done; all 3 raw-SQL sites and all IDE0001 prefix sites still exist. **All production line numbers drifted** (the 9.5x/9.11 churn); test-fixture line numbers are stable.

## 2. Sub-stages (corrected against current code)

### 2.a — `SeedDevUser.cs` raw-SQL: add a throwing allow-list guard (NOT a raw→interpolated swap)

**Pre-flight correction (🔴):** the original framing "swap both sites to `ExecuteSqlInterpolatedAsync`, sentinel as a parameter" does not fit the actual code:
- **UPDATE site (`SeedDevUser.cs:204`):** the SQL **interpolates the table name** (`UPDATE "{table}" SET "UserId" = …`). Microsoft Learn confirms table identifiers **cannot** be passed as `DbParameter`s — so this site stays string-interpolated by necessity regardless of API. (https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
- **COUNT site (`SeedDevUser.cs:358`):** already drops to **raw Npgsql** because, per the code's own comment, "EF Core doesn't expose a scalar-returning ExecuteSql." There is no `ExecuteSqlInterpolated` to swap to here.

**So the genuine improvement is the guard, not an API swap.** Both sites already iterate `UserOwnedModel.FinanceTables(adminDb.Model).Select(t => t.PostgresTableName)` (`:199`, `:358`) — the table source is already the EF-model-derived allow-list (the hand-typed `UserOwnedTables.All` array the 2026-05-21 stage text named was **deleted** commit `b0995d9`, 2026-06-01).

**The work:**
- Extract a small local guard (e.g. `AssertKnownTable(string table, ISet<string> allowed)`) that **throws** (`InvalidOperationException`) if `table` is not in the `FinanceTables` set. Call it before each interpolation. Today the loop iterates the allow-list so no off-list name *can* appear — the guard hardens against a future caller/refactor that supplies a name another way, and documents the invariant at the interpolation site.
- For the UPDATE site, parameterize the **sentinel UUID and target `userId`** as EF parameters where the API permits (the table name stays interpolated behind the guard). `ExecuteSqlInterpolatedAsync` is the correct EF Core 10 API for the UPDATE (it accepts the interpolated string with the GUID values as parameters; only the identifier can't be a parameter).
- Replace the `:204` `ExecuteSqlRawAsync` with `ExecuteSqlInterpolatedAsync` for the value-parameterized UPDATE; keep the `:358` COUNT on raw Npgsql (no EF scalar-exec exists) but add the guard + a comment naming why it stays raw.
- Same-line comment at each interpolation pins the table name to the `FinanceTables` allow-list.

### 2.b — `PreAuthRlsScope.cs:81` `SqlQueryRaw<string>` → `SqlQuery<string>($"…")`

Confirmed against Microsoft Learn: **`SqlQuery<T>($"…")`** is the EF Core 10 method for a scalar query; the interpolated `$""` overload accepts a fully-constant string with zero parameters — exactly the `SELECT current_setting('app.current_user_ref', true) AS "Value"` case. No guard needed (constant string, no injection surface).

**Analyzer-safe (confirmed):** `PreAuthRlsScope` is a `static` class (`:52`), **unmarked** (no `[PreAuthScope]`/`[RlsBypassJustified]`), and is the **definition site** of `BeginPreAuthUserScopeAsync`, not a caller. CER001 fires on `BeginTransactionAsync` inside `[PreAuthScope]` classes; CER006 fires on *callers* of the helper lacking the marker — neither applies here. CER005/CER006 are at `error` severity (`.editorconfig:24-25`), so any *structural* change would hard-fail; this swap is a pure SQL-API change and raises **no** CER diagnostic. The same file already uses `ExecuteSqlInterpolatedAsync` at `:95` — this swap matches an in-file precedent.

### 2.c — `AppDbContext.cs` ×4 — drop `Microsoft.EntityFrameworkCore.` prefix

Four `catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)` clauses; file already has `using Microsoft.EntityFrameworkCore;`. **Current lines 46/50/62/66** (stage said 37/41/53/57 — drifted).

### 2.d — `Program.cs` ×3 — drop prefixes

- `Microsoft.AspNetCore.Mvc.UnprocessableEntityObjectResult` → `UnprocessableEntityObjectResult` (**current line 48**). This is the 422 `VALIDATION_ERROR` envelope factory — the emitted JSON must stay **byte-identical** (removing a prefix is a no-op on behavior; verified by the existing 422 tests).
- `System.Security.Claims.ClaimTypes.NameIdentifier` → `ClaimTypes.NameIdentifier` (**current lines 401, 420**; stage said 376/395).

### 2.e — `PasswordResetService.cs` (clean) + `AuthController.cs` (stays qualified)

- `PasswordResetService.cs:335` (stage said 327) — `Microsoft.AspNetCore.Identity.TokenOptions.DefaultAuthenticatorProvider` → drop prefix (file imports `Microsoft.AspNetCore.Identity`). Clean removal.
- `AuthController.cs:199` (stage said 152) — `Microsoft.AspNetCore.Identity.SignInResult` **STAYS fully qualified.** Confirmed (🔴): the file imports both `Microsoft.AspNetCore.Identity` (`:6`) and `Microsoft.AspNetCore.Mvc` (`:7`), both of which define a `SignInResult` type — an unqualified name is a `CS0104` ambiguous-reference compile error. The prefix is load-bearing. Add a one-line comment ("qualified — disambiguates Mvc.SignInResult; see CS0104") so a future reader doesn't try to shorten it. IDE0001 recognizes the ambiguity and will not flag this site under the promoted severity.

### 2.f — Test-fixture prefix drops

`AuthTestFixture.cs` ×8, `RateLimitedAuthTestWebApplicationFactory.cs` ×6, `ArchitectureTests.cs` ×3, `FailedLoginRecorderTests.cs` ×1, `LockoutUnlockIssuanceTests.cs` ×1, `LockoutUnlockConfirmTests.cs` ×1 (`System.Diagnostics.Stopwatch`). Fixture line numbers are **stable** (the test files didn't absorb the 9.5x churn). **One path correction:** `RateLimitedAuthTestWebApplicationFactory.cs` lives at `ProjectCeres.Tests/Integration/`, NOT `…/Integration/Authentication/`.

### 2.g — Code-simplification sweep — `ProjectCeres/` production `.cs` only

**Tooling correction (🟡):** there is **no `simplify` skill** (`.claude/skills/` has none). The tooling is the **`code-simplifier` plugin agent** (`code-simplifier:code-simplifier`) plus the `/simplify` / `/code-review` slash commands. The stage body's "the project's `simplify` skill" references are stale.

Discovery-then-fix over `ProjectCeres/` production `.cs` only (per locked decision 3). Classes: target-typed `new()`, collection expressions, `is null`/`is not null`, switch expressions over if-chains, LINQ over hand-rolled loops, primary constructors *where they shrink the file*, `ArgumentNullException.ThrowIfNull`, expression-bodied members where they aid readability, `?.` over null-guards, `await using` over manual dispose. Findings table (grouped by class) captured in the stage body **before** fixes flatten it (commit hash referenced); safe-mechanical fixes shipped one-commit-per-class (or one batched commit if a class is small). **Anything that would change behavior, perf class, or public API → queued sibling `[ ]`, not rolled in.**

### IDE0001 detection — promote to `warning` + `dotnet format` tripwire

Add `dotnet_diagnostic.IDE0001.severity = warning` to `.editorconfig` (currently unset; `.editorconfig` confirmed present, `EnforceCodeStyleInBuild=true` in `Directory.Build.props:3`).

> **⚠️ EXECUTION CORRECTION (2026-06-15, during subagent-driven execution — supersedes the scope/mechanism below).** Resuming after an abrupt session close surfaced two evidence-backed facts that falsify this section's original assumptions:
>
> 1. **IDE0001 does NOT surface at `dotnet build`, even when promoted to `warning`.** Tested directly: with `dotnet_diagnostic.IDE0001.severity = warning` set AND `EnforceCodeStyleInBuild=true`, `dotnet build --no-incremental` emits **0** IDE0001 diagnostics. IDE0001 ("Simplify name") is an IDE/`dotnet format`-only analyzer that the build compiler does not run. The 163 sites only surface via `dotnet format style --diagnostics IDE0001`. **Therefore the original Task 6 tripwire ("clean `dotnet build` proves completion") does not work** — it would pass an entirely undone sweep.
> 2. **The real IDE0001 surface is 163 sites across ~28 files, not "32 across 6."** 154 in `ProjectCeres.Tests/`, 6 in `ProjectCeres/` (3 `Program.cs` sites beyond Task 3's, plus `RecurringTransactionsApiController`, `ResendEmailService`, `LockoutCacheOptions`). IDE0001 fires on `typeof(Fully.Qualified.Name)`, `System.IO.File`, `<see cref>` doc-comments — far beyond the catch-clause-prefix pattern originally enumerated.
>
> **User decision (2026-06-15):** full sweep + `dotnet format` tripwire. Revised mechanism:
> - **Sweep:** `dotnet format style --diagnostics IDE0001` auto-fixes all 163 sites mechanically (compiler-verified simplifications) — deterministic, not 163 hand-edits. This supersedes the per-file hand-edit Task 4. It completes the 2 pre-existing partially-edited files and covers the 6 production sites too.
> - **Tripwire:** `dotnet format style --verify-no-changes` (returns non-zero on any IDE0001 regression once the severity is `warning`) — this replaces the broken `dotnet build`-clean gate. Documented in `docs/testing.md` as the IDE0001 regression check (no CI until Stage 16, so the durable tripwire is the severity line + the documented format-verify command).
> - The severity line still ships **last** (after the sweep is clean), and the proof-of-completion is `dotnet format style --verify-no-changes` clean.

**Pre-flight correction (🟡):** `Directory.Build.props:5` has **`TreatWarningsAsErrors=false`** — so the promotion makes `dotnet build` show IDE0001 **warnings (yellow), not errors (red)** *if they appeared at build at all* (per the execution correction above, they do not). Cost of the severity line is in-IDE/format visibility, not a build gate.

## 3. Sequencing (commit order)

The IDE0001 flip is load-bearing-last: once promoted, every unfixed prefix site is a warning, so the flip must come after every site is clean — making the flip itself the proof-of-completion tripwire.

| # | Commit | Rationale |
|---|---|---|
| 1 | **9.1.6.b** — `PreAuthRlsScope.cs:81` raw→interpolated | Smallest + highest-scrutiny (pre-auth RLS); ship alone so the CER-clean proof is isolated |
| 2 | **9.1.6.a** — `SeedDevUser.cs` guard + sentinel parameterization | The only sub-stage with real logic (throwing guard + its test); isolated so seed-CLI verification is clean |
| 3 | **9.1.6.c + d + e** — production prefix drops | Pure mechanical removals, same class, one commit; AuthController:199 gets its load-bearing comment here |
| 4 | **9.1.6.f** — test-fixture prefix drops | Test-only; isolated so production verifies independently |
| 5 | **9.1.6.g** — code-simplifier sweep over `ProjectCeres/` | Discovery-then-fix; findings table captured before fixes; one commit per class |
| 6 | **IDE0001 → `warning`** in `.editorconfig` | **Last.** Every site clean → warning-free build → permanent tripwire. Any warning = a–f missed something |

**Branch:** `stage-9.1.6-code-shape-cleanup` off `main`.

## 4. Verification (ship gates)

Before close-out (Phase E): `dotnet build`, `dotnet test`, `pnpm build`, `pnpm test` all exit 0 (suite stays green even though no frontend code changes).

**Per sub-stage:**
- **9.1.6.a** — `SeedDevUser.cs` UPDATE site calls `ExecuteSqlInterpolatedAsync` with the sentinel/userId as parameters; both sites call a guard that **throws** for an off-`FinanceTables` table name. New unit test: guard throws on a bogus table, passes on a real one. Seed CLI still produces a usable login on a fresh DB (existing e2e). IDOR/RLS suites green (same admin-DB path).
- **9.1.6.b** — `PreAuthRlsScope.cs:81` uses `SqlQuery<string>($"…")`; nested-scope cross-user guard test stays green; RLS smoke tests green; `dotnet build` emits **zero new CER005/CER006** diagnostics (explicit check — pre-auth machinery).
- **9.1.6.c/d/e** — named production sites read the short form; **AuthController:199 stays `Microsoft.AspNetCore.Identity.SignInResult`** with its rationale comment; the 422 `VALIDATION_ERROR` envelope is byte-identical (422 tests green); auth integration tests green.
- **9.1.6.f** — the 6 fixtures clear of redundant prefixes whose namespace is imported; full `dotnet test` green.
- **9.1.6.g** — findings table (by class) in stage body with commit hash; fixes one-commit-per-class; **test count before == after** (no test added/modified/skipped to pass a simplification, per `feedback_never_skip_tests_to_make_them_pass`); behavior/perf/API-changing findings → queued sibling `[ ]`. Per `feedback_dont_handwave_perf_variance`: if suite runtime moves >30% off baseline, re-run once before concluding.
- **IDE0001 flip** — after `.editorconfig` sets `IDE0001.severity = warning`, `dotnet build` is **clean (zero IDE0001 warnings)**. A clean build proves c–f complete.

## 5. Out of scope

- **Test files that deliberately use raw SQL stay raw** — `BackfillIdentityNormalizedToLowercaseTests.cs` (exercises migration behavior), `Group1_BypassCaseTests.cs` (exercises the RLS-bypass invariant), and **`RlsTestFixture.cs`** (the third such file; the roadmap's out-of-scope list omits it — it stays out on the same RLS-invariant-exercising rationale).
- **Project-wide severity policy** (treat-IDE0001-as-error, CI noise budget, `TreatWarningsAsErrors`) — stays deferred to Batch 5 per the stage's existing out-of-scope note. This stage promotes IDE0001 to `warning` only (its detection mechanism), not to error.
- **9.1.6.g for `ProjectCeres.Tests/` and `ProjectCeres.Client/src/`** — queued as sibling `[ ]` lines (per locked decision 3), each with a receiving stage + back-reference (no bare "deferred" notes; `no-unjustified-deferrals` gate).

## 6. Deferral entry — 9.1.6.g test-project + React sweeps (no-unjustified-deferrals gate)

The roadmap's existing 9.1.6.g checklist line (`roadmap-phase-three.md:1176`) scopes the sweep across **all three** trees (`ProjectCeres/`, `ProjectCeres.Tests/`, `ProjectCeres.Client/src/`). Locked decision 3 narrows *this stage* to `ProjectCeres/` production only — so the test + React sweeps are **already-scoped work being deferred**, which the no-defer gate governs. This entry discharges that gate.

### Code-simplification sweep — test project + React client (narrowed out of 9.1.6.g)

**Where:** `ProjectCeres.Tests/` and `ProjectCeres.Client/src/` (the two trees dropped from 9.1.6.g's production-only scope).
**Cost to user:** None directly — this is code-quality cleanup, not a user-facing defect. The cost of *not* tracking it is that already-scoped work silently vanishes from the roadmap.

**Deferral reason:** Reason 2 — Already-scheduled (user-locked scope narrowing, locked decision 3, 2026-06-15). The work stays inside the 9.1.6 container as two new sibling sub-stages, not pushed to a later phase.

- Active batch stage: Stage 9.1.6 (this stage's own container).
- Existing scope-line covering this: `roadmap-phase-three.md:1176` — "code-simplification sweep complete: … has run across `ProjectCeres/`, `ProjectCeres.Tests/`, and `ProjectCeres.Client/src/`".
- Why covered: the two new sibling lines are the test/React halves of that exact existing scope line, split out so the production sweep ships with a reviewable diff.

**Receiving stage:** Stage 9.1.6 — new sub-stages **9.1.6.h** (test-project sweep) + **9.1.6.i** (React sweep), added as roadmap `[ ]` lines **in the close-out commit** (the commit that marks a–g done). Per `feedback_deferral_requires_receiving_stage_checkbox` + `feedback_persist_deferred_decisions`, these land in the **roadmap** (durable), not only in this spec.

**Receiving-stage line text** (copy-paste into `roadmap-phase-three.md` § Stage 9.1.6 sub-stage table + checklist in the close-out commit):
```
- [ ] 9.1.6.h — code-simplification sweep over `ProjectCeres.Tests/` (narrowed out of 9.1.6.g, 2026-06-15; see spec §6). Same discovery-then-fix procedure + findings table + test-count-invariant gate as 9.1.6.g.
- [ ] 9.1.6.i — code-simplification sweep over `ProjectCeres.Client/src/` (narrowed out of 9.1.6.g, 2026-06-15; see spec §6). Routes through `frontend-orchestrator` → `vercel-react-best-practices` (React `useMemo`/`useCallback` removal where cost exceeds win, `?.` chains, etc.).
```

**Mechanical tripwire:** rewrite the existing `roadmap-phase-three.md:1176` 9.1.6.g checklist line **in the close-out commit** so its scope reads `ProjectCeres/` **only**, with an explicit pointer: "test + React sweeps split to 9.1.6.h / 9.1.6.i — see spec §6." This converts the over-broad line into an accurate one whose unchecked 9.1.6.h/.i siblings are caught by Phase E's close-out scan (the gate that blocks ✅ Done on any unchecked `[ ]` under the stage heading). The split is the tripwire: 9.1.6 cannot close with .h/.i unchecked, and they cannot silently disappear because the source line now names them.

**Trip-wire location:** `docs/roadmap-phase-three.md` § Stage 9.1.6 — the rewritten 9.1.6.g checklist line + the new 9.1.6.h / 9.1.6.i `[ ]` lines (all in the close-out commit).

---

**Note on the IDE0001 *severity-policy* deferral (§5):** that one (project-wide treat-as-error / CI-noise budget → Batch 5) is **pre-existing and already gate-compliant** — the roadmap already tracks it under Batch 5 with a tripwire bullet (`roadmap-phase-three.md:1154`, `:1177`). This stage carries that boundary forward unchanged and promotes IDE0001 to `warning` only. No new deferral entry needed.
