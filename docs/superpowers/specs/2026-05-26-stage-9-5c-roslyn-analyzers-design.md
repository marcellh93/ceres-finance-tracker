# Stage 9.5c — Roslyn analyzers + EN/ES resx parity source generator

> **Diataxis type:** Reference + explanation — design spec for sub-stage 9.5c under `## Stage 9.5h — Phase 1 hardening container` in `docs/roadmap-phase-three.md`.
>
> **Status:** Draft — design approved 2026-05-26. Awaiting implementation plan + user review of this spec.
>
> **Predecessor:** 9.5a (shipped). **Successor:** 9.5b (DualContextWebApplicationFactory + RLS-parity assertion).

## 1. Problem

Phase 1 feature stages keep shipping with class-of-bug regressions that are syntactically locatable but only catch fire at runtime (or worse, in production):

- **Pre-auth tx misuse (mid-9.3 Register-handler 500):** `AuthController.Register` opened a plain `BeginTransactionAsync` so `CategorySeedService` was rejected by the row-level security policy on `Categories`. The correct call is `BeginPreAuthUserScopeAsync(user.Id, ...)` which sets the per-request user context BEFORE the seed runs.
- **Missing row-level-security wall on new IUserOwned tables (mid-9.3 `EmailConfirmationTokens` gap):** The `UserOwnedTables.All` hand-list was a single source of drift. Stage 7.5's RLS-policy migration loop skipped tables not on the list.
- **Class-level `[Authorize]` invisible at the call site:** the global authn filter enforces it, but a reader of `AuthController.cs` cannot tell from the file whether the endpoint is gated. Drift happens.
- **`DateTime.UtcNow` reads scattered across production code:** prevents deterministic time in integration tests; the auth-tier flake class from 9.1.5.a had this as a contributing factor.
- **EN/ES resx drift:** today both files have perfect 30-key parity, but no mechanism prevents a future Stage 11+ email-template addition from landing in EN-only.

The 4-agent confirmation pass during the 9.5a hardening session named this rule-by-rule and locked the scope: ship Roslyn analyzers that pin each class-of-bug at compile time, plus a source generator that pins the resx parity contract.

## 2. Locked decisions

| Lock | Decision | Source |
|---|---|---|
| L1 | 4 analyzers (CER001 + CER002 + CER004 + CER010) + 1 source generator (CER020). CER003 dropped during planning (2026-05-26) after discovering existing architecture test `Every_controller_action_declares_authorization_intent` already enforces per-action authz intent — class-level `[Authorize]` is incompatible with that test's `GetCustomAttribute<AuthorizeAttribute>()` (singular) usage. CER003's intended safety surface is already covered. No CER005/CER006/CER003 in 9.5c — see §12 for routing. | User Q3 (2026-05-26 brainstorm) + user CER003 resolution (2026-05-26 planning) |
| L2 | CER002 fires only on IUserOwned-entity queries via `AppDbContext` (not `AdminDbContext`). Expected baseline at N+1b: **15 distinct methods** (29 raw call sites collapsed by enclosing-method scope of `[RlsBypassJustified]`). Initial planning audit predicted 0–2 — that estimate undercounted because it missed the pre-auth services that use `AppDbContext` via the `BeginPreAuthUserScopeAsync` helper. Revised 2026-05-27 after Task 3 audit found the actual sites (see Appendix A). All 15 methods get `[RlsBypassJustified("CER-NNNN")]` in the N+1b retro-decoration commit; CER002 baseline at N+2 then becomes 0. | User Q1 (2026-05-26 brainstorm) + Task 3 audit (2026-05-27) |
| L3 | CER004 fires on production code, excluding `ProjectCeres.Models` namespace property-initialisers + `ProjectCeres/Migrations/`. Expected baseline: 40–60 violations. | User Q2 (2026-05-26 brainstorm) |
| L4 | Escape attributes accept free-string reasons. `[RlsBypassJustified]` ticket format is enforced by side-analyzer CER010 (regex `^(CER\|TICKET\|ADR)-\d+$`). | User Q4 (2026-05-26 brainstorm) |
| L5 | Resx source-gen output: parity-assertion-only (no typed accessor class). | User Q5 (2026-05-26 brainstorm) |
| L6 | C-2 flip-to-error condition: 48h soak AND baseline drained to zero (both required). | User Q (post-research 2026-05-26) |
| L7 | Baseline-suppression file: single repo-root `.editorconfig` with per-analyzer + path-scoped sections. | User Q (post-research 2026-05-26) |
| L8 | Testing approach: `Microsoft.CodeAnalysis.Testing` (unsuffixed package) + `Verifier<TAnalyzer, TVerifier>` + inline markup. | Roslyn SDK research subagent (2026-05-26) |
| L9 | Project structure: one `ProjectCeres.Analyzers` csproj (4 analyzers + source-gen) + separate `ProjectCeres.Analyzers.Annotations` csproj for the 3 escape attributes (`[PreAuthScope]`, `[RlsBypassJustified]`, `[AllowsWallClock]`; `[RequiresAdminContext]` reserved for 9.5b) + `ProjectCeres.Analyzers.Tests`. | Roslyn SDK research subagent (2026-05-26) |
| L10 | Rollout shape: sequential 4-commit chain. N (annotations) → N+1 (retro-decoration, one commit per diagnostic ID) → N+2 (analyzers at `warning`) → N+M+1 (flip to `error`). | Brainstorm Option A (2026-05-26) |

## 3. Inherited conditions (from 4-agent confirmation pass)

- **C-1** — Escape attributes ship in commits BEFORE the analyzers turn on. Operationally: the `ProjectCeres.Analyzers.Annotations` csproj + attribute definitions ship in commit N. Retro-decoration sites get the attributes in N+1. The analyzer project + `<ProjectReference OutputItemType="Analyzer">` arrives in N+2.
- **C-2** — Analyzers ship at `warning` severity for 48 hours AND the per-analyzer baseline reaches zero before a single follow-up commit flips severity to `error`. Both clocks green required.
- **C-3** — If any analyzer surfaces >15 violations against `main` at first run (CER004 will), a baseline-suppression file ships alongside the analyzer in the same commit. The file is a single repo-root `.editorconfig`. Baseline rows get worked off in sibling commits before the warning→error flip.

## 4. Pre-flight findings (from verify-against-codebase)

Three findings from the 2026-05-26 verify-against-codebase audit must be honoured:

**F1 — `Directory.Build.props` and `.editorconfig` do not exist today.** 9.5c partially satisfies Stage 9.1.6's tripwire bullet ("enable `EnforceCodeStyleInBuild` + ship a project-wide `.editorconfig`"). The 9.1.6 tripwire bullet must be updated in the same commit chain to cross-reference 9.5c, and the 9.5c spec/roadmap entry must cross-reference 9.1.6's tripwire. Per `feedback_defer_work_to_all_three_docs`: both directions linked.

**F2 — Existing analyzer wiring in `ProjectCeres.csproj:13-16`.** `Microsoft.EntityFrameworkCore.Design` is already pulled with `<IncludeAssets>...analyzers...</IncludeAssets>` + `<PrivateAssets>all</PrivateAssets>`. When `ProjectCeres.Analyzers` is added via `<ProjectReference OutputItemType="Analyzer">`, the existing line is the precedent for `<PrivateAssets>all</PrivateAssets>`. Match it.

**F3 — `ProjectCeres.csproj` has a `BuildTailwind` MSBuild target.** The N+2 smoke check must verify `pnpm run build:css` still runs (analyzers are loaded by csc, not by the BuildTailwind exec — there is no theoretical reason for interference, but verify on first build).

## 5. Architecture

### 5.1 Three new csproj projects

**`ProjectCeres.Analyzers.Annotations/`** — TargetFramework `netstandard2.0` (consumable by the `net10.0` `ProjectCeres/` project). Holds four attribute class definitions:

- `PreAuthScopeAttribute` — `[AttributeUsage(AttributeTargets.Class)]`. Marker, no payload. Placed on classes whose database calls must use `BeginPreAuthUserScopeAsync`.
- `RlsBypassJustifiedAttribute(string ticket)` — `[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]`. Placed on a method-level call site where `IgnoreQueryFilters()` is being used on an IUserOwned-entity query via `AppDbContext`. Argument is a ticket string validated by CER010.
- `AllowsWallClockAttribute(string reason)` — `[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, AllowMultiple = false)]`. Placed on members that legitimately need `DateTime.UtcNow` / `DateTime.Now`. Argument is prose explanation.
- `RequiresAdminContextAttribute` — `[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]`. Marker for Stage 9.5b's architecture-test allow-list (no analyzer fires on it in 9.5c; defined here so 9.5b doesn't need to reopen the Annotations csproj).

No analyzer dependencies. No external references. Pure data classes.

**`ProjectCeres.Analyzers/`** — TargetFramework `netstandard2.0`, `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>`. Holds 5 `DiagnosticAnalyzer` subclasses + 1 `IIncrementalGenerator`:

| Class | Diagnostic | Roslyn analysis pattern |
|---|---|---|
| `PreAuthScopeTransactionAnalyzer` | CER001 | `RegisterSyntaxNodeAction` on `InvocationExpression`; checks `BeginTransactionAsync` receiver + walks up to enclosing type's attributes |
| `IgnoreQueryFiltersOnUserOwnedAnalyzer` | CER002 | `RegisterOperationAction` on `IInvocationOperation`; checks receiver's element-type implements `IUserOwned` AND containing DbContext expression type is `AppDbContext` |
| `DateTimeWallClockAnalyzer` | CER004 | `RegisterSyntaxNodeAction` on `MemberAccessExpression`; checks `DateTime.UtcNow`/`DateTime.Now` with namespace + folder exclusions |
| `RlsBypassJustifiedTicketFormatAnalyzer` | CER010 | `RegisterSymbolAction` on attribute applications; regex-validates the ticket argument string |
| `ResxParityGenerator` (IIncrementalGenerator) | CER020 | Reads AdditionalFiles for `*.{en,es}.resx`; emits compile-time error if culture key-sets diverge |

**`ProjectCeres.Analyzers.Tests/`** — TargetFramework `net10.0`, xUnit. Uses `Microsoft.CodeAnalysis.Testing` (unsuffixed package) with `Verifier<TAnalyzer, TVerifier>` pattern and inline markup `[|...|]` (single-descriptor) / `{|CER00X:...|}` (multi-descriptor). One test fixture file per analyzer + one for the source generator.

### 5.2 Repo-root additions

**`Directory.Build.props`** (new file) — common analyzer + style settings shared by every csproj in the solution:

```xml
<Project>
  <PropertyGroup>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

**`.editorconfig`** (new file) — root-level. Holds:
- Minimal C# style baseline (matches existing implicit conventions; not aiming to reformat the codebase).
- Per-analyzer baseline-suppression sections, populated at the N+2 commit. Format:

```
# Baseline-suppression: CER004 sites pending TimeProvider migration (worked off in sibling commits)
[ProjectCeres/Common/Authentication/FailedLoginRecorder.cs]
dotnet_diagnostic.CER004.severity = none

[ProjectCeres/Common/Authentication/MfaBackupCodeService.cs]
dotnet_diagnostic.CER004.severity = none

# ... per-file entries until baseline drained
```

When a site is migrated to `TimeProvider`, the corresponding `.editorconfig` line is deleted in the same commit. The baseline-drain progress is grep-countable: `grep -c 'dotnet_diagnostic.CER' .editorconfig`.

### 5.3 Solution wiring

`ProjectCeres.sln` gains three new project entries: `ProjectCeres.Analyzers`, `ProjectCeres.Analyzers.Annotations`, `ProjectCeres.Analyzers.Tests`. The `ProjectCeres/ProjectCeres.csproj` gains two new `<ProjectReference>` blocks (Annotations as normal reference, Analyzers as analyzer reference per F2's pattern).

## 6. The four analyzers + the source generator

### 6.1 CER001 — Pre-auth transaction type

| Field | Value |
|---|---|
| Title | `[PreAuthScope]`-marked class must use `BeginPreAuthUserScopeAsync`, not `BeginTransactionAsync` |
| Category | `Reliability` |
| Severity at N+2 | `warning` |
| Expected baseline at N+2 | 0 (no production class is marked `[PreAuthScope]` yet; gets retro-decorated to ~8 classes in N+1) |
| Triggers on | `InvocationExpression` where receiver chain ends in `.Database.BeginTransactionAsync()` AND the enclosing `TypeDeclarationSyntax` carries `[PreAuthScope]` |
| Excludes | Test code (project root path contains `.Tests`), `PreAuthRlsScope.cs` itself (the helper that LEGITIMATELY calls `BeginTransactionAsync` inside its implementation at line 92) |
| Suggested fix message | "Use `_db.BeginPreAuthUserScopeAsync(userId, ct)` instead. See `Common/Authentication/PreAuthRlsScope.cs`." |
| Test coverage | (a) `[PreAuthScope]` class + plain tx → fires; (b) `[PreAuthScope]` class + helper call → no fire; (c) non-marked class + plain tx → no fire; (d) `PreAuthRlsScope.cs` itself → no fire (file exclusion) |

### 6.2 CER002 — IgnoreQueryFilters on IUserOwned via AppDbContext

| Field | Value |
|---|---|
| Title | `IgnoreQueryFilters()` on IUserOwned entity via `AppDbContext` requires `[RlsBypassJustified("ticket")]` |
| Category | `Security` |
| Severity at N+2 | `warning` |
| Expected baseline at N+2 | 0 — all 15 violating methods (see Appendix A) get `[RlsBypassJustified("CER-NNNN")]` in the N+1b commit (Task 3 of the impl plan), so the analyzer sees zero violations at the time it ships. Pre-auth services that use `AppDbContext` via `BeginPreAuthUserScopeAsync` are the bulk of the violations — these are legitimate bypasses (the pre-auth helper sets the per-request user context at the Postgres GUC level, so Postgres RLS is still active; the EF query filter is the layer being stripped because the user context isn't known at the C# layer yet). The `[RlsBypassJustified]` annotation documents that intent at every call site. |
| Triggers on | `IInvocationOperation` where `TargetMethod.Name == "IgnoreQueryFilters"` AND receiver's element-type implements `IUserOwned` AND the containing scope's DbContext expression resolves to a type named `AppDbContext` (or descendant) AND the enclosing method does not carry `[RlsBypassJustified(...)]` |
| Excludes | Tests, methods carrying `[RlsBypassJustified(ticket)]` (with valid ticket — CER010 fires separately on bad tickets) |
| Suggested fix message | "Either route this query through `AdminDbContext` (which bypasses RLS by design), or add `[RlsBypassJustified(\"CER-NNNN\")]` to the containing method with the justification ticket." |
| Test coverage | (a) AppDbContext + IUserOwned entity + IgnoreQueryFilters → fires; (b) AdminDbContext + same → no fire; (c) AppDbContext + non-IUserOwned (e.g. Identity table) → no fire; (d) method with valid `[RlsBypassJustified]` → no fire; (e) method with `[RlsBypassJustified("temp")]` → CER002 no fire, CER010 fires |

### 6.3 CER003 — WITHDRAWN

**Status:** Withdrawn during planning (2026-05-26). The intended safety surface (every controller declares authz intent) is already enforced at runtime by an existing architecture test: `ArchitectureTests.Every_controller_action_declares_authorization_intent` calls `GetCustomAttribute<AuthorizeAttribute>()` (singular) on every action method, and the project convention is action-level enforcement. Class-level `[Authorize]` is **incompatible** with the existing test (7 controllers carry `// [Authorize] would cause AmbiguousMatchException` comments documenting the constraint).

**Where the safety lives:** `ProjectCeres.Tests/Unit/Architecture/ArchitectureTests.cs::Every_controller_action_declares_authorization_intent` (existing test, already green on `main`). Any new controller action that lacks both `[Authorize]` and `[AllowAnonymous]` fails this test on `dotnet test`.

**Why this is at least as strong as CER003 would have been:** the architecture test enforces at the action-method granularity, which is the unit of HTTP routing. CER003's class-level check was a fallback for "did the class declare intent" — but the action is what the request lands on; the class is just where it's hosted. Per-action enforcement is the tighter contract.

See §12 for the routing entry: CER003 → existing architecture test (no new sub-stage; existing test is the receiving structure).

### 6.4 CER004 — DateTime.UtcNow / DateTime.Now ban

| Field | Value |
|---|---|
| Title | Use `TimeProvider.GetUtcNow()` instead of `DateTime.UtcNow` (or add `[AllowsWallClock(reason)]`) |
| Category | `Reliability` |
| Severity at N+2 | `warning` |
| Expected baseline at N+2 | 40–60 violations (baseline-suppress per C-3; drained in sibling commits) |
| Triggers on | `MemberAccessExpressionSyntax` where expression chain is `DateTime.UtcNow` or `DateTime.Now` AND enclosing context does NOT match any exclusion |
| Excludes | (1) Property/field initialisers in any type whose namespace begins with `ProjectCeres.Models` (entity-construction defaults); (2) any file path containing `/Migrations/` (EF-generated); (3) test code; (4) enclosing method/property/constructor carrying `[AllowsWallClock(reason)]` |
| Suggested fix message | "Replace with `_timeProvider.GetUtcNow().UtcDateTime` after injecting `TimeProvider`. If the read genuinely requires the wall clock (e.g. one-time initialisation, log timestamp), add `[AllowsWallClock(\"reason\")]` to the containing method." |
| Test coverage | (a) Service method + DateTime.UtcNow → fires; (b) entity property initialiser in `Models/` → no fire; (c) Migrations/*.cs + DateTime.UtcNow → no fire; (d) method with `[AllowsWallClock("entity-default")]` → no fire; (e) constructor in non-Models namespace → fires unless attributed |

### 6.5 CER010 — RlsBypassJustified ticket format

| Field | Value |
|---|---|
| Title | `[RlsBypassJustified("ticket")]` ticket argument must match `^(CER\|TICKET\|ADR)-\d+$` |
| Category | `Style` |
| Severity at N+2 | `warning` (flips to `error` on same schedule as the others) |
| Expected baseline at N+2 | 0 |
| Triggers on | Attribute application of `RlsBypassJustifiedAttribute` where the constant string argument does not match the regex |
| Excludes | None (the regex is the contract) |
| Suggested fix message | "Ticket must be CER-NNNN (analyzer ID), TICKET-NNNN (issue tracker), or ADR-NNNN (architecture decision record). Got: \"{argument}\"." |
| Test coverage | (a) `[RlsBypassJustified("CER-1234")]` → no fire; (b) `[RlsBypassJustified("TICKET-99")]` → no fire; (c) `[RlsBypassJustified("ADR-0042")]` → no fire; (d) `[RlsBypassJustified("temp")]` → fires; (e) `[RlsBypassJustified("")]` → fires; (f) `[RlsBypassJustified("CER-NNNN")]` (no digits) → fires |

### 6.6 CER020 — EN/ES resx parity (source generator)

| Field | Value |
|---|---|
| Title | EN and ES resx files must share the same key set |
| Category | `Localization` |
| Severity from day 1 | `error` (no warning soak — a missing translation is a hard ship-blocker; not a code-style call) |
| Expected baseline | 0 (audit confirmed 30/30 key parity today) |
| Generator pattern | `IIncrementalGenerator`. Discovers `AdditionalFiles` matching `*.{en,es}.resx` (and any future culture pair). For each base name, computes the symmetric difference of `<data name="...">` keys. Emits a `DiagnosticDescriptor` with span pointing at the resx file missing keys. Emits a trivial `// resx-parity-check-ok` placeholder `.cs` file on green. |
| Excludes | None |
| Test coverage | (a) EN + ES with identical keys → no diagnostic; (b) EN missing a key ES has → diagnostic with EN file span; (c) ES missing a key EN has → diagnostic with ES file span; (d) only EN file present (no ES sibling) → no diagnostic (parity check only fires when ≥2 cultures exist) |

## 7. Data flow

Compile-time only. No runtime artifacts. The analyzer DLL is loaded by `csc.exe`, walks the syntax tree + semantic model during build, emits diagnostics to MSBuild. The source generator runs in the same compilation pass.

No persistence, no I/O outside the build pipeline. No telemetry. No state across builds (other than incremental cache, which Roslyn manages).

## 8. Error handling

Analyzers do not "fail" in the user-facing sense — they emit diagnostics. Defensive programming inside the analyzers themselves:
- Every `IOperation` cast guarded by a null check (the semantic model can return null on partial/broken code; the analyzer must not crash the build).
- Regex compiled once as a `static readonly` to avoid per-symbol allocation.
- The source generator's resx parsing tolerates malformed XML by emitting CER021 (separate diagnostic) rather than throwing.

## 9. Testing

Per L8 (locked): `Microsoft.CodeAnalysis.Testing` package + `Verifier<TAnalyzer, TVerifier>` + inline markup.

Each analyzer has at minimum: positive case, negative case, excluded-path case, plus rule-specific edge cases (enumerated in §6's "Test coverage" rows).

**Pitfall to honour** (from research subagent): when the analyzer targets newer C# language features than the test host's bundled Roslyn, the test project must explicitly override `Microsoft.CodeAnalysis.CSharp.Workspaces` to a version ≥ the analyzer's. Pin in test csproj.

**Integration smoke test:** one xUnit test that runs `dotnet build` against a synthetic source string + asserts expected diagnostic count. Verifies the `.editorconfig` baseline-suppression mechanism end-to-end.

## 10. Rollout (commit chain — Option A locked)

Per L10:

| Commit | Content | Build state after |
|---|---|---|
| **N** | `ProjectCeres.Analyzers.Annotations/` csproj + 4 attribute definitions. `ProjectCeres.csproj` gains `<ProjectReference Include="..\ProjectCeres.Analyzers.Annotations\ProjectCeres.Analyzers.Annotations.csproj" />`. `Directory.Build.props` created at repo root. `ProjectCeres.sln` updated. | `dotnet build` green; no analyzer runs. |
| **N+1a** | Retro-decoration sweep for CER001: ~8 `[PreAuthScope]` markers on `AuthController`, `EmailConfirmationService`, `LockoutUnlockService`, `AuditLogWriter`, `MfaBackupCodeService`, `PasswordResetService`, `TotpReplayGuard`, and the Register-handler context. | `dotnet build` green. |
| **N+1b** | Retro-decoration sweep for CER002: 0–2 sites get `[RlsBypassJustified("CER-NNNN")]` (likely none — see L2's expected baseline of 0–2). | `dotnet build` green. |
| **N+1c** | (intentionally empty — CER003 was withdrawn during planning; commit number reserved for traceability) | n/a |
| **N+1d** | Retro-decoration sweep for CER004: of the ~75 production reads outside `Models/` + `Migrations/` (planning audit 2026-05-26 found higher density than initial estimate), the auth-layer + controllers + middlewares get either `TimeProvider` injection (preferred — DI registration shipped in this commit too, since `TimeProvider` is not currently registered) OR `[AllowsWallClock("reason")]`. This commit is the largest — single PR scope. | `dotnet build` green. |
| **N+2** | `ProjectCeres.Analyzers/` + `ProjectCeres.Analyzers.Tests/` csproj added. `<ProjectReference OutputItemType="Analyzer">` wiring added to `ProjectCeres.csproj` matching F2's pattern. `.editorconfig` created at repo root with per-analyzer baseline-suppression sections listing any remaining CER004 sites that didn't get migrated in N+1d. | `dotnet build` shows warnings (suppressed sites are silent); 0 errors. |
| **N+3 … N+M** | Sibling commits work off the CER004 baseline: each commit migrates one or more sites to `TimeProvider`, removes the corresponding `.editorconfig` line, builds clean. | `dotnet build` green per commit. |
| **N+M+1** | After (a) 48h have elapsed since N+2 commit timestamp AND (b) baseline is drained (grep returns 0 `dotnet_diagnostic.CER` lines in `.editorconfig`), single commit flips severity to `error` for CER001–CER010. CER020 was already `error` from day 1. Stage 9.5h's 9.5c row ticked. | `dotnet build` green; severity is now `error`; future violations fail the build. |

**Tripwire integration:** N+M+1 commit also updates `docs/roadmap-phase-three.md` § Stage 9.1.6's tripwire bullet (line 1179) to cross-reference 9.5c as the realised version. Per F1.

## 11. Verification checklist (cited from `docs/roadmap-phase-three.md` § Stage 9.5h)

For 9.5c (already on the roadmap, verbatim per the user-approved L1 ordering):

- Roslyn analyzers shipped: CER001 (pre-auth `[PreAuthScope]`-marked classes forbid plain `BeginTransactionAsync`); CER002 (`IgnoreQueryFilters` on IUserOwned-entity queries via `AppDbContext` requires `[RlsBypassJustified("ticket")]`); CER004 (`DateTime.UtcNow`/`DateTime.Now` in production code requires `[AllowsWallClock("reason")]`); CER010 (side-analyzer validates `[RlsBypassJustified]` ticket format against `^(CER\|TICKET\|ADR)-\d+$`). CER003 withdrawn during planning — existing architecture test `Every_controller_action_declares_authorization_intent` covers the same safety surface (see §6.3 + §12).
- EN/ES resx parity source generator emits a compile-time error if either culture is missing a key its sibling has.
- Condition C-1: escape attributes ship in commits BEFORE the analyzers turn on.
- Condition C-2: 48h soak AND baseline drained to zero before warning→error flip.
- Condition C-3: baseline-suppression file (`.editorconfig`) ships alongside the analyzer; baseline drained before the flip.

Plus additional verification:
- `BuildTailwind` MSBuild target still runs on `dotnet build` (per F3).
- `pnpm --dir ProjectCeres.Client build`, `pnpm --dir ProjectCeres.Client test --run`, `dotnet build`, `dotnet test` all exit 0 at each commit.
- Stop-hook tier 2 fires on `.csproj`/`.sln` writes (automatic per project setup).
- `docs/roadmap-phase-three.md` § Stage 9.1.6's tripwire bullet (line 1179) cross-references 9.5c in N+M+1's commit.

## 12. Out of 9.5c — scope-adjacent work routed to receiving stages

All adjacent work has a receiving `[ ]` line per `feedback_deferral_requires_receiving_stage_checkbox`. No item below is held by memory only; each one cites the row that owns the work.

| Item | Receiving `[ ]` line | Why not in 9.5c |
|---|---|---|
| CER005 — HMAC TokenLookup discipline analyzer for new `*Token` model classes | Stage 9.5h sub-stage 9.5f (`roadmap-phase-three.md` § Stage 9.5h verification checklist) | User L1 lock (Q3 2026-05-26): scope of 9.5c is exactly 4 analyzers + 1 source generator; CER005's value is regression-prevention against a future 4th token table that doesn't exist yet. 0 violations against today's `main`. Routed to 9.5f for independent rollout. |
| CER006 — reverse-direction `[PreAuthScope]` marker analyzer | Stage 9.5h sub-stage 9.5g | Same L1 lock. CER001 covers the forward direction (class is marked → must use helper); CER006 covers the reverse (class uses helper → must carry marker). 9.5c's N+1a retro-decoration marks all 8 current callers, so CER006 catches future drift, not today's state. Routed to 9.5g. |
| Typed accessor source generator for `IStringLocalizer<EmailsResource>` consumers | Stage 9.5h sub-stage 9.5i | User L5 lock (Q5 2026-05-26): the resx source generator's output shape was locked at parity-assertion-only. A typed accessor (emitting `Strings.Auth_Register_Title` properties so typos compile-error) is additive to CER020 and requires a consumer-call-site audit before commit shape can be planned. Routed to 9.5i. |
| Code-fix providers for CER001 / CER004 / CER010 (mechanical-shape analyzers) | Stage 9.5h sub-stage 9.5j | Brainstorm Section 4 boundary: code-fix providers add ~2x test surface per analyzer. The diagnostic itself + its suggested-fix prose is sufficient for the 9.5h close-out (developers can apply the manual rewrite). Code-fixes upgrade ergonomics on the three diagnostics with mechanical fix shapes (CER001 substitution, CER004 substitution + TimeProvider injection, CER010 ticket-format completion). Routed to 9.5j. |
| CER003 — "every controller action declares authz intent" | Existing test: `ProjectCeres.Tests/Unit/Architecture/ArchitectureTests.cs::Every_controller_action_declares_authorization_intent` (already green on `main`) | Withdrawn from 9.5c during planning (2026-05-26). The architecture test enforces the same safety contract at action-method granularity, which is tighter than CER003's class-level check would have been. 7 controllers carry `// [Authorize] would cause AmbiguousMatchException` comments documenting the routing-level incompatibility between class-level `[Authorize]` and `GetCustomAttribute<AuthorizeAttribute>()` (singular) — the existing test is the receiving structure. Mechanical tripwire: the test itself, run by every `dotnet test` invocation. |
| Full `.editorconfig` style normalisation pass (renames, brace placement, expression preferences) | Stage 9.1.6's verification-checklist tripwire bullet at `docs/roadmap-phase-three.md:1179` | 9.5c's `.editorconfig` is scoped to per-analyzer baseline-suppression sections + minimal C# defaults that match the existing implicit style. The broader style sweep is Stage 9.1.6.g's chartered scope, with its own tripwire bullet that opened 2026-05-21. Cross-reference both directions: 9.1.6's tripwire bullet is updated in this stage's N+M+1 commit to cite 9.5c as the partial-realisation per pre-flight finding F1. |

## 13. Open questions (resolved in this spec — none remaining)

All open questions from the brainstorm (Q1–Q8 plus the three post-research clarifications) are locked in §2 and §3. No deferred decisions.

## 14. Cross-references

- **Roadmap:** `docs/roadmap-phase-three.md` § Stage 9.5h sub-stage 9.5c (the `[ ]` line that gates this stage's close-out)
- **Sibling spec:** `docs/superpowers/specs/2026-05-26-stage-9-5h-phase-1-hardening-design.md` (does not yet exist; would consolidate 9.5b/d/e specs — out of scope for 9.5c)
- **Sibling stage's tripwire to cross-reference:** `docs/roadmap-phase-three.md:1179` (Stage 9.1.6 tripwire bullet, to be updated in N+M+1)
- **Decommissioned-skills tombstone:** `docs/decommissioned-skills.md` — relevant because the lexical-prose Stop hooks were the prior-art mechanism that 9.5c's compile-time analyzers structurally replace
- **Project rules:** `CLAUDE.md` § "What NOT to Do" — feedback rules on test handling, deferral discipline, finished-stage cleanliness all apply to this work
- **Sub-stage feedback memories:** `feedback_iuserowned_requires_five_registries` (the class-of-bug that motivated CER002), `feedback_persist_deferred_decisions` (cross-ref both directions per F1), `feedback_finished_stages_have_no_unchecked_items` (every `[ ]` ticked at close-out)

## Appendix A — CER002 ticket mapping (added 2026-05-27 after Task 3 audit)

The N+1b retro-decoration commit applies `[RlsBypassJustified("CER-NNNN")]` to 15 distinct methods across 8 files. Each ticket identifies one method scope (one method = one decoration, even if the method contains multiple `IgnoreQueryFilters()` calls).

| Ticket | File | Method | Why this method bypasses the EF query filter on an IUserOwned entity via AppDbContext |
|---|---|---|---|
| CER-1001 | `ProjectCeres/Common/Authentication/EmailConfirmationService.cs` | `IssueAsync` (L88) | Pre-auth path during register-flow email confirmation. `BeginPreAuthUserScopeAsync(userId)` sets the Postgres GUC for RLS; EF query filter is stripped because the C# `ICurrentUserAccessor` is not yet populated. |
| CER-1002 | `ProjectCeres/Common/Authentication/EmailConfirmationService.cs` | `ConfirmAsync` | Same pattern. User-id is recovered from the consumed token, not from auth context. |
| CER-1003 | `ProjectCeres/Common/Authentication/EmailChangeService.cs` | `RequestAsync` (L80) | Authenticated-but-pre-RLS-scope path. User-id from session claim, scope set via pre-auth helper. |
| CER-1004 | `ProjectCeres/Common/Authentication/EmailChangeService.cs` | `ConfirmAsync` (L225) | Token-driven user-id resolution; pre-auth helper sets scope. |
| CER-1005 | `ProjectCeres/Common/Authentication/EmailChangeService.cs` | `RevokeAsync` (L378) | Token-driven user-id resolution; pre-auth helper sets scope. |
| CER-1006 | `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` | `IssueAsync` (L72) | Pre-auth path — lockout flow operates on a user that is BY DEFINITION not signed in. |
| CER-1007 | `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` | `ConfirmAsync` (L136) | Token-driven user-id resolution from the unlock link; pre-auth scope set via helper. |
| CER-1008 | `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs` | `VerifyAndConsumeLockedAsync` (L68) | MFA verification path — runs after password auth but before full sign-in completes. Pre-auth scope. |
| CER-1009 | `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs` | `RegenerateAsync` (L105) | Signed-in user regenerating backup codes; query filter stripped because the operation needs to ensure ALL prior codes are replaced atomically. |
| CER-1010 | `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs` | `PurgeAsync` (L117) | Same pattern as Regenerate — purge needs to see every prior code for the user. |
| CER-1011 | `ProjectCeres/Common/Authentication/PasswordResetService.cs` | `RequestAsync` (L98) | Anonymous request — only the email is known until a user lookup. Helper sets scope once user is resolved. |
| CER-1012 | `ProjectCeres/Common/Authentication/PasswordResetService.cs` | `ConfirmAsync` (L236) | Token-driven user-id resolution; pre-auth scope set via helper. |
| CER-1013 | `ProjectCeres/Common/Authentication/TotpReplayGuard.cs` | `TryAcceptLockedAsync` (L53) | MFA verification at sign-in time — user is not fully signed in yet. Pre-auth scope. |
| CER-1014 | `ProjectCeres/Common/Email/LanguageResolver.cs` | `ResolveForUserAsync` (L22) | Email-template language resolution is invoked from pre-auth call sites (password reset, lockout). User-id is supplied by the caller, not by the auth context. |
| CER-1015 | `ProjectCeres/Services/CategorySeedService.cs` | `CopyDefaultsForUserAsync` (L19) | Called during register before the user is fully signed in. Idempotency check (`AnyAsync`) reads cross-user to verify NO categories exist for the just-created user. |

**All 15 tickets share a common explanation class:** the EF global query filter on `IUserOwned` entities requires `ICurrentUserAccessor` to be populated; pre-auth paths and token-driven paths populate the user context via `BeginPreAuthUserScopeAsync(userId)` (which sets the Postgres `app.current_user_ref` GUC for RLS enforcement at the database layer) but cannot populate `ICurrentUserAccessor` because the user is not signed in. `IgnoreQueryFilters()` strips the EF layer; Postgres RLS still enforces. The `[RlsBypassJustified]` annotation documents the intentional EF-layer bypass while making the audit trail searchable.
