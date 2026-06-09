# ADR-0077 — Roslyn analyzers for invariant enforcement (CER001/CER002/CER004/CER010 + CER020 resx parity)

> **Diataxis type:** Explanation — architectural decision record.

**Status:** Accepted — 2026-05-27; CER001/CER002/CER004/CER010 flipped `warning`→`error` 2026-05-30 after the 48h soak (commit `665f182`). CER020 has been `error` from day 1. CER005 (token-lookup discipline) added at `warning` 2026-06-09 (Stage 9.5f); `error` flip pending the 48h C-2 soak. CER006 (reverse-direction `[PreAuthScope]` marker) added at `warning` 2026-06-09 (Stage 9.5g); `error` flip pending the 48h C-2 soak.

**Phase:** Phase 3 (Hosted Beta) — Stage 9.5h Phase 1 hardening container, sub-stage 9.5c.

**Supersedes:** None.

**Related decisions:** ADR-0065 (EF global query filters with explicit `.Where()` redundancy) · ADR-0067 (background-job user scope via `IUserScope` + runner) · ADR-0068 (Postgres RLS as Phase 3 defence-in-depth).

## Context

Three classes of bug kept slipping past `dotnet test` during Stage 9.3 and Stage 9.5:

1. **Pre-auth transaction misuse** — `AuthController.Register` opened a plain `BeginTransactionAsync` instead of `BeginPreAuthUserScopeAsync(userId)`. The `CategorySeedService.CopyDefaultsForUserAsync` call inside it hit Postgres RLS' `42501` rejection because no per-request user context had been set. Caught only at browser-test time; the integration suite missed it because the `WebApplicationFactory` fixture routes through `ceres_admin` (BYPASSRLS).
2. **`IgnoreQueryFilters` on `IUserOwned` via `AppDbContext`** — eight services in `Common/Authentication/` call `IgnoreQueryFilters()` on user-owned entities during pre-auth flows (password reset, email confirmation, MFA, etc.). These are legitimate bypasses, but the codebase had no compile-time mechanism to demand justification at the call site. A future addition could leak rows across users silently.
3. **`DateTime.UtcNow` scattered across production code** — prevented deterministic time in integration tests; was a contributing factor to the auth-tier flake class fixed in Stage 9.1.5.a. Roughly 80 production reads bypassed `TimeProvider` injection.

A fourth concern: **EN/ES resx parity** — both `EmailsResource.{en,es}.resx` shared a perfect 30/30 key set at the start of 9.5c, but no mechanism prevented a future email-template addition from landing in EN-only and surfacing as a missing-key runtime fallback for ES-locale users.

Existing tooling layers that could have caught these:
- **`dotnet test`**: misses class 1 because the test fixture uses `ceres_admin` (no RLS). Misses class 2 entirely (no architecture test enforces `[RlsBypassJustified]`). Misses class 3 (no enforcement of `TimeProvider` injection). Misses class 4 (one existing one-test count check fires AT BUILD time, not AT spec-write time — and only verifies count, not content).
- **`pnpm test`**: frontend only.
- **Architecture tests in `ProjectCeres.Tests/Integration/Authentication/`**: catch the `IgnoreQueryFilters` allow-list at runtime but produce no IDE feedback and require running tests to discover violations.
- **Lexical-prose Stop hooks (deleted in 9.5a)**: read the agent's own output to detect violations — fundamentally limited to whatever the agent wrote, not what the agent's tool calls actually did.

## Decision

Ship **four Roslyn analyzers + one source generator + three escape attributes** in a new `ProjectCeres.Analyzers` csproj wired into `ProjectCeres` via `<ProjectReference OutputItemType="Analyzer">`. Enforcement is at compile time (`csc.exe`), runs on every `dotnet build`, and surfaces in the IDE immediately:

| ID | Category | Severity | Trigger |
|---|---|---|---|
| **CER001** | Reliability | `warning` → `error` | `[PreAuthScope]`-marked classes forbid plain `BeginTransactionAsync` — must use `BeginPreAuthUserScopeAsync` |
| **CER002** | Security | `warning` → `error` | `IgnoreQueryFilters()` on `IUserOwned` entity via `AppDbContext` requires `[RlsBypassJustified("ticket")]` on the enclosing method |
| **CER004** | Reliability | `warning` → `error` | `DateTime.UtcNow` / `DateTime.Now` in production code requires `[AllowsWallClock("reason")]` on the enclosing member (excludes `ProjectCeres.Models` property initialisers + `Migrations/`) |
| **CER010** | Style | `warning` → `error` | `[RlsBypassJustified(ticket)]` ticket argument must match `^(CER\|TICKET\|ADR)-\d+$` |
| **CER020** | Localization | `error` from day 1 | EN/ES resx parity — emits compile-time error if either culture is missing a key its sibling has |
| **CER005** | Reliability | `warning` (flip pending) | A class named `*Token` under `ProjectCeres.Models` implementing `IUserOwned` must declare a `byte[] TokenLookup` property. Property-presence only — the index/migration coupling is owned by the E3 `MigrationDriftTests` (Stage 9.5e), not this analyzer. Added Stage 9.5f (2026-06-09) |
| **CER006** | Reliability | `warning` (flip pending) | A class that calls `BeginPreAuthUserScopeAsync` must carry the `[PreAuthScope]` marker — the reverse of CER001. Mirrors CER001's syntax-tree walk (`FirstAncestorOrSelf<TypeDeclarationSyntax>`), so it does NOT inherit CER002's `GetEnclosingSymbol` gap; zero exclusion logic. Added Stage 9.5g (2026-06-09) |

Three **escape attributes** ship as a separate `netstandard2.0` csproj (`ProjectCeres.Analyzers.Annotations`):
- `[PreAuthScope]` — marks classes that operate on the pre-authentication code path.
- `[RlsBypassJustified("CER-NNNN")]` — documents intentional EF query-filter bypass on `IUserOwned` queries.
- `[AllowsWallClock("reason")]` — documents intentional wall-clock read where `TimeProvider` injection is infeasible (entity property initialisers, static classes, view-model computed properties, pure-functional services).

A fourth attribute (`[RequiresAdminContext]`) is reserved for Stage 9.5b's architecture test (no analyzer fires on it in 9.5c).

**Release tracking (binding for every future CER rule).** The analyzer project registers `AnalyzerReleases.Shipped.md` + `AnalyzerReleases.Unshipped.md` as `<AdditionalFiles>` (added 2026-05-30, commit `c1ea134`). The Roslyn meta-analyzer **RS2008** fails the analyzer build for any `DiagnosticDescriptor` whose ID is not listed in one of those files. **Therefore: every new CER rule (CER005/CER006/… in Stages 9.5f/9.5g and beyond) MUST add a row to `AnalyzerReleases.Unshipped.md` under `### New Rules` in the same commit that declares its descriptor.** The table format is strict — `Rule ID | Category | Severity | Notes` with a plain `--------|...` separator and no column padding (padded headers trip RS2007). `Shipped.md` stays empty until the analyzer set is cut as a versioned release; at that point the unshipped rows move under a `## Release X.Y` header in `Shipped.md`. Related: diagnostic messages take no trailing period (RS1032).

**Rollout shape:** four-commit chain — annotations csproj (commit N) → retro-decoration sweeps (commits N+1a/b/d) → analyzers csproj + tests + `.editorconfig` baseline at `warning` severity (commit N+2) → warning→error flip after 48h soak AND baseline drained to zero (commit N+M+1 = `665f182`, landed 2026-05-30; ~72h after N+2, 0 suppressions, 0 production violations).

## Alternatives considered

### Alternative 1 — Architecture-test-only enforcement (extend existing `ProjectCeres.Tests/Integration/Authentication/`)

Rejected. Architecture tests:
- Don't surface in the IDE — developer must run `dotnet test` to discover violations.
- Run after build — slower feedback loop.
- Produce a single failure message — harder to enumerate every violation in one pass than analyzers' per-site warnings.
- Cannot easily exclude specific paths (`/Migrations/`) without ad-hoc string matching.

Architecture tests remain the right tool for **runtime invariants** that can only be observed at runtime (DI registrations match expected services, every IUserOwned entity has an RLS migration, every controller action declares authz intent). The CER003 analyzer was withdrawn during planning for exactly this reason — the existing `Every_controller_action_declares_authorization_intent` test enforces per-action authz at runtime, which is tighter than CER003's class-level check would have been (see `docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md` § 6.3).

### Alternative 2 — Lexical-prose Stop hooks (the 9.5a-deleted layer)

Rejected. The 9.5a sweep deleted 10 Stop hooks that read assistant prose to detect violations. Same architectural defect for all: a regex over the agent's own text uses the same context window that produced the violation. Roslyn analyzers read the tool-grounded artifact (the compiled C# code), not the prose around it.

### Alternative 3 — Code-fix providers shipped alongside the analyzers

Deferred to Stage 9.5j (a sub-stage scheduled in `roadmap-phase-three.md` § Stage 9.5h). Code-fix providers add ~2x test surface per analyzer. The diagnostics themselves + their suggested-fix prose are sufficient for the 9.5h close-out; developers can apply manual rewrites. 9.5j upgrades ergonomics on CER001 / CER004 / CER010 (the three diagnostics with mechanical fix shapes).

### Alternative 4 — One csproj per analyzer

Rejected per Roslyn SDK research (2026-05-26): no major published analyzer (Meziantou, Roslynator, ErrorProne.NET) splits per rule. Shared scaffolding (descriptor registry, test harness, project configuration) is the dominant cost; per-rule splitting multiplies plumbing without buying isolation.

## Consequences

**Positive:**

- **Compile-time guarantee:** the three class-of-bugs are now caught by the compiler, not by tests, browsers, or production observability. Once warning→error flips at N+M+1, `dotnet build` exits non-zero on any new violation.
- **IDE feedback:** Visual Studio + Rider + VS Code all surface CER warnings/errors in real time. Developer sees the violation as they type, not at PR review time.
- **Documented bypass intent:** every retroactive bypass site now carries a justification ticket (15 `[RlsBypassJustified]` decorations) or wall-clock reason (5 `[AllowsWallClock]` decorations). Future readers can grep for the ticket to find the explanation; the architecture-test allow-list pattern that previously documented bypasses in a single file (`ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`) remains in place as a defence-in-depth layer.
- **`TimeProvider` injection unlocks deterministic time** in integration tests. Stage 9.1.5.a's auth-tier flake class is structurally prevented from recurring; future time-sensitive tests can swap in a fake clock.
- **Cross-cutting cleanup as side-effect:** 21 services migrated to `TimeProvider`, 15 methods documented with `[RlsBypassJustified]`, 7 classes marked `[PreAuthScope]`. The retro-decoration phase produced consistent enforcement of conventions that previously lived in mental models.

**Negative:**

- **`netstandard2.0` constraint** on the analyzer assembly limits the analyzer's own implementation to APIs available in that older target. Modern .NET 10 APIs (string overloads with `StringComparison`, etc.) can't be used inside the analyzer code. Documented in spec §9 pitfall list.
- **Test fixture churn:** 23 test files needed updates to pass `TimeProvider.System` to service constructors that gained the parameter. Caught by the spec-compliance reviewer pattern (53 compile errors initially missed by the implementer's filtered test run).
- **Soak-based rollout:** condition C-2 requires 48 hours between the analyzer's first appearance at `warning` and the flip to `error`. The chain spans multiple sessions.
- **CER002 scope creep risk:** the analyzer's "containing scope" check uses `GetEnclosingSymbol as IMethodSymbol`, which silently exits for property getters, lambdas, local functions. Documented inline in the analyzer source; CER006 (reverse-direction marker, planned for Stage 9.5g) is expected to surface gaps this analyzer can't.
- **Per-analyzer baseline-suppression in `.editorconfig`:** a future addition that produces >15 violations against `main` would need a baseline-suppression section (per condition C-3). Current state: zero suppressions needed (Task 5's complete retro-decoration brought CER004 to 0; CER001/CER002/CER010 had 0 baseline by construction; CER020 has 0 baseline because EN/ES parity is already perfect).

## References

- **Spec:** `docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md`
- **Plan:** `docs/superpowers/plans/2026-05-26-stage-9-5c-roslyn-analyzers-impl.md`
- **Commit chain (in order):** `9d6b839` → `ae42c17` → `6f9802c` → `a9b2379` → `892cfe7` → `b4a7239` (ship at `warning`) → `665f182` (warning→error flip, 2026-05-30) → `c1ea134` (RS2008 release-tracking files + RS1032 message fix).
- **Roadmap entry:** `docs/roadmap-phase-three.md` § Stage 9.5h sub-stage 9.5c.
- **Sister stages this enables/defers:**
  - Stage 9.5b (DualContextWebApplicationFactory + RLS-parity assertion) gains compile-time enforcement of the conventions it tests at runtime.
  - Stage 9.5d (`ProjectCeres.Tests.Integration.AppRole` test project) will exercise the analyzer-enforced bypass annotations against a `ceres_app` (RLS-active) database.
  - Stage 9.5e (3-agent reviewer pipeline) treats analyzer warnings as evidence; the reviewer pipeline + the analyzers together form the Phase 1 hardening core.
  - Stages 9.5f (CER005 token-lookup) / 9.5g (CER006 reverse-direction marker) / 9.5j (code-fix providers) follow the same analyzer architecture.
- **Pre-flight finding F1 (cross-reference):** `Directory.Build.props` shipped in commit N as a partial realisation of Stage 9.1.6's tripwire bullet (line 1179 of `roadmap-phase-three.md`). The full `EnforceCodeStyleInBuild` + project-wide `.editorconfig` style normalisation remains Stage 9.1.6.g's chartered scope.
