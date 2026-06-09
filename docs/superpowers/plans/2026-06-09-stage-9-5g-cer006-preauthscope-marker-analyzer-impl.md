# CER006 Reverse-Direction [PreAuthScope] Marker Analyzer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Roslyn analyzer (CER006) that fails the build, after a 48-hour warning soak, when a class calls `BeginPreAuthUserScopeAsync` but its enclosing class lacks the `[PreAuthScope]` marker.

**Architecture:** A `DiagnosticAnalyzer` that is a near-exact mirror of CER001 (`PreAuthScopeTransactionAnalyzer`). It registers on invocation expressions, matches the method name `BeginPreAuthUserScopeAsync`, walks up to the enclosing `TypeDeclarationSyntax`, and fires if that class does NOT carry `[PreAuthScope]`. Three deltas from CER001: target method name, negated marker check, and no `PreAuthRlsScope.cs` path exclusion. Zero exclusion logic. Reports at the call-expression location.

**Tech Stack:** C# / .NET 10, `Microsoft.CodeAnalysis.CSharp` 4.11.0 (netstandard2.0 analyzer target), xUnit + `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` 1.1.4 (`CSharpAnalyzerVerifier<T>` harness).

**Spec:** `docs/superpowers/specs/2026-06-09-stage-9-5g-cer006-preauthscope-marker-analyzer-design.md`

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `ProjectCeres.Analyzers/Diagnostics.cs` | add `CER006_PreAuthScopeMarkerMissing` descriptor | 1 |
| `ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md` | add CER006 release-tracking row (RS2008) | 1 |
| `ProjectCeres.Analyzers/PreAuthScopeMarkerAnalyzer.cs` | the analyzer — mirror of CER001 | 1 (skeleton), 3 (gate) |
| `ProjectCeres.Analyzers.Tests/CER006_PreAuthScopeMarkerAnalyzerTests.cs` | 4 ship-gate tests | 2 |
| `.editorconfig` | `severity = error` flip line | 6 (deferred ≥48 h) |

**Build-ordering note (inverted-TDD, same as CER005):** The test project reads `Diagnostics.CER006_PreAuthScopeMarkerMissing` directly, so the descriptor must exist before the test file compiles; and RS2008 fails the build the instant a descriptor exists without a matching release row. So Task 1 ships the descriptor, the release row, and an analyzer that registers but does not yet report. Task 2 adds tests (the fires-case goes Red against the inert analyzer). Task 3 implements the gate (Green).

**Marker-placement note (differs from CER005):** CER006 reports at `invocation.GetLocation()` (the call expression), exactly like CER001. So in the test sources, the `{|#0:...|}` marker wraps the **call expression** (e.g. `{|#0:_db.BeginPreAuthUserScopeAsync(Guid.NewGuid())|}`), NOT the class name. (CER005 reported on the class identifier; CER006 does not — do not copy CER005's marker placement.)

---

## Task 1: Descriptor + release-tracking row + inert analyzer skeleton

**Files:**
- Modify: `ProjectCeres.Analyzers/Diagnostics.cs` (append after the `CER005_TokenLookupDiscipline` descriptor, before the closing `}`)
- Modify: `ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md` (append one row under `### New Rules`, after the CER005 row)
- Create: `ProjectCeres.Analyzers/PreAuthScopeMarkerAnalyzer.cs`

- [ ] **Step 1: Add the CER006 descriptor to `Diagnostics.cs`**

Insert this block immediately after the `CER005_TokenLookupDiscipline` descriptor's closing `);` and before the final `}` of the `Diagnostics` class:

```csharp

    public static readonly DiagnosticDescriptor CER006_PreAuthScopeMarkerMissing = new(
        id: "CER006",
        title: "Class calling BeginPreAuthUserScopeAsync must be marked [PreAuthScope]",
        messageFormat: "Class '{0}' calls BeginPreAuthUserScopeAsync but is not marked [PreAuthScope]. Add [PreAuthScope] to the class so the pre-auth surface stays auditable.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "BeginPreAuthUserScopeAsync opens a per-request user-scoped transaction for pre-authentication writes. Any class that calls it operates on the pre-auth path and must declare [PreAuthScope] so the contract stays enforced from both directions (CER001 covers marked-class-must-use-helper; CER006 covers caller-must-be-marked). See docs/superpowers/specs/2026-06-09-stage-9-5g-cer006-preauthscope-marker-analyzer-design.md.");
```

**RS1032 note (corrected):** RS1032 requires the message to be *internally consistent* — either a single sentence with no trailing period, OR a multi-sentence message that DOES end with a period. It does NOT mean "never a period." This `messageFormat` is two sentences and ends with a period, exactly matching the house style of CER001/CER002/CER004/CER010 (all multi-sentence, all period-terminated). The `title` is a single phrase with no trailing period. (The earlier draft of this plan said "no trailing period in messageFormat" — that was an over-generalization from CER005's single-sentence message and is wrong for multi-sentence messages.)

- [ ] **Step 2: Add the CER006 row to `AnalyzerReleases.Unshipped.md`**

Append this single line to the `### New Rules` table, immediately after the `CER005` row (preserve the exact format — single-space pipes, no padding; RS2007 fails on padded columns):

```
CER006 | Reliability | Warning | A class that calls BeginPreAuthUserScopeAsync must carry the [PreAuthScope] marker
```

- [ ] **Step 3: Create the inert analyzer `PreAuthScopeMarkerAnalyzer.cs`**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreAuthScopeMarkerAnalyzer : DiagnosticAnalyzer
{
    private const string PreAuthScopeAttributeFullName = "ProjectCeres.Analyzers.Annotations.PreAuthScopeAttribute";
    private const string BeginPreAuthUserScopeMethodName = "BeginPreAuthUserScopeAsync";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER006_PreAuthScopeMarkerMissing);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        // Gate implemented in Task 3.
    }
}
```

- [ ] **Step 4: Build the analyzer + test projects to verify RS2008 is satisfied and everything compiles**

Run (two single-project builds — `dotnet build` takes one project arg at a time):
```
dotnet build ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj
dotnet build ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj
```
Expected: both **Build succeeded, 0 errors.** No `RS2008` (descriptor tracked), no `RS1032` (message punctuation consistent — see the RS1032 note above; the two-sentence period-terminated form is correct), no `RS2007` (release-table format). The analyzer is registered but reports nothing. (Pre-existing CA1707 underscore-naming warnings in the Tests project are acceptable; RS-prefixed are not.)

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Analyzers/Diagnostics.cs ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md ProjectCeres.Analyzers/PreAuthScopeMarkerAnalyzer.cs
git commit -m "feat(9.5g): CER006 descriptor + release-tracking row + inert analyzer skeleton"
```

---

## Task 2: The 4 ship-gate tests (Red against the inert analyzer)

**Files:**
- Create: `ProjectCeres.Analyzers.Tests/CER006_PreAuthScopeMarkerAnalyzerTests.cs`

Each test inline-defines a stub `PreAuthScopeAttribute` in namespace `ProjectCeres.Analyzers.Annotations` (the analyzer matches by full display-string name) and a stub `BeginPreAuthUserScopeAsync` extension method so the source compiles — the exact stub shape CER001's own tests use (see `CER001_PreAuthScopeTransactionAnalyzerTests.cs`, the `NoFire_When_PreAuthScope_Class_Uses_BeginPreAuthUserScopeAsync` case). The `{|#0:...|}` marker wraps the **call expression** (CER006 reports at `invocation.GetLocation()`), and the message argument is the enclosing class name.

- [ ] **Step 1: Write the test file with all 4 cases**

```csharp
using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpAnalyzerVerifier<ProjectCeres.Analyzers.PreAuthScopeMarkerAnalyzer>;

namespace ProjectCeres.Analyzers.Tests;

public class CER006_PreAuthScopeMarkerAnalyzerTests
{
    // 1. Unmarked class calls BeginPreAuthUserScopeAsync -> fires (the drift case)
    [Fact]
    public async Task Fires_When_Unmarked_Class_Calls_Helper()
    {
        var source = @"
using System;
using System.Threading.Tasks;

namespace ProjectCeres.Analyzers.Annotations
{
    public class PreAuthScopeAttribute : System.Attribute { }
}

namespace Test
{
    public static class ScopeExtensions
    {
        public static Task BeginPreAuthUserScopeAsync(this FakeDb db, Guid userId) => Task.CompletedTask;
    }
    public class FakeDb { }

    public class UnmarkedService
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await {|#0:_db.BeginPreAuthUserScopeAsync(Guid.NewGuid())|};
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER006_PreAuthScopeMarkerMissing)
            .WithLocation(0)
            .WithArguments(""UnmarkedService"");
        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    // 2. [PreAuthScope] class calls BeginPreAuthUserScopeAsync -> no fire (real callers' shape)
    [Fact]
    public async Task NoFire_When_Marked_Class_Calls_Helper()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using ProjectCeres.Analyzers.Annotations;

namespace ProjectCeres.Analyzers.Annotations
{
    public class PreAuthScopeAttribute : System.Attribute { }
}

namespace Test
{
    public static class ScopeExtensions
    {
        public static Task BeginPreAuthUserScopeAsync(this FakeDb db, Guid userId) => Task.CompletedTask;
    }
    public class FakeDb { }

    [ProjectCeres.Analyzers.Annotations.PreAuthScope]
    public class MarkedService
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await _db.BeginPreAuthUserScopeAsync(Guid.NewGuid());
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // 3. Unmarked class, no call to the helper -> no fire (only callers checked)
    [Fact]
    public async Task NoFire_When_Class_Does_Not_Call_Helper()
    {
        var source = @"
using System.Threading.Tasks;

namespace Test
{
    public class PlainService
    {
        public Task Bar() => Task.CompletedTask;
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // 4. Unmarked class calls some OTHER method (not the helper) -> no fire (only the helper triggers it)
    [Fact]
    public async Task NoFire_When_Unmarked_Class_Calls_Other_Method()
    {
        var source = @"
using System;
using System.Threading.Tasks;

namespace Test
{
    public static class ScopeExtensions
    {
        public static Task BeginSomethingElseAsync(this FakeDb db, Guid userId) => Task.CompletedTask;
    }
    public class FakeDb { }

    public class UnmarkedService
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await _db.BeginSomethingElseAsync(Guid.NewGuid());
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
```

(Note on escaping: this is markdown. In the ACTUAL `.cs` file, `.WithArguments(""UnmarkedService"")` is written `.WithArguments("UnmarkedService")` — the verbatim string sources use `@"..."` so any inner quote inside the source string is doubled, but `.WithArguments(...)` is plain C# outside the source string and uses single quotes. Compare against `CER001_PreAuthScopeTransactionAnalyzerTests.cs`.)

- [ ] **Step 2: Run the CER006 tests to verify the fires-case FAILS and the three no-fire cases PASS**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj --filter "FullyQualifiedName~CER006"`
Expected: **1 failed, 3 passed.** `Fires_When_Unmarked_Class_Calls_Helper` **FAILS** with "Mismatch between number of diagnostics returned, expected 1 actual 0 — Diagnostics: NONE" (the inert analyzer produces nothing). The three `NoFire_*` cases **PASS** (an inert analyzer trivially produces no diagnostic). If the fires-test fails for a DIFFERENT reason (a location/argument mismatch, or a compile error), STOP and report — that signals a test-authoring bug, not the expected Red.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Analyzers.Tests/CER006_PreAuthScopeMarkerAnalyzerTests.cs
git commit -m "test(9.5g): CER006 ship-gate matrix — 1 fires + 3 exclusions (Red)"
```

---

## Task 3: Implement the mirror gate (Green)

**Files:**
- Modify: `ProjectCeres.Analyzers/PreAuthScopeMarkerAnalyzer.cs` (replace the empty `AnalyzeInvocation` body)

- [ ] **Step 1: Replace the `AnalyzeInvocation` method body with the CER001-mirror gate**

Replace the entire `AnalyzeInvocation` method (the one with the `// Gate implemented in Task 3.` comment) with:

```csharp
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.Node is not InvocationExpressionSyntax invocation) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Name.Identifier.Text != BeginPreAuthUserScopeMethodName) return;

        // Walk up to the enclosing type declaration (lexical, not semantic — no GetEnclosingSymbol
        // null cases for lambdas / property getters / local functions; see spec §2.1).
        var enclosingType = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingType is null) return;

        var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingType);
        if (typeSymbol is null) return;

        // Fire when the enclosing class is NOT marked [PreAuthScope] (the reverse of CER001).
        var hasPreAuthScope = typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == PreAuthScopeAttributeFullName);
        if (hasPreAuthScope) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER006_PreAuthScopeMarkerMissing,
            invocation.GetLocation(),
            typeSymbol.Name));
    }
```

(This is CER001's `AnalyzeInvocation` with three deltas: the method-name const is `BeginPreAuthUserScopeMethodName`; the marker check is negated (`if (hasPreAuthScope) return;` instead of `if (!hasPreAuthScope) return;`); and CER001's `PreAuthRlsScope.cs` path-exclusion block is omitted. Report location and message argument are identical to CER001.)

- [ ] **Step 2: Run the CER006 tests to verify ALL 4 PASS**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj --filter "FullyQualifiedName~CER006"`
Expected: **Passed: 4, Failed: 0.** Test 1 now fires at the call expression `#0`; tests 2, 3, 4 still produce nothing. If any no-fire test now fails, the gate over-fires — fix the gate, not the test.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Analyzers/PreAuthScopeMarkerAnalyzer.cs
git commit -m "feat(9.5g): CER006 gate — calls BeginPreAuthUserScopeAsync without [PreAuthScope] fires (Green)"
```

---

## Task 4: Whole-solution build — verify 0 violations on `main` and the full analyzer suite

**Files:** none (verification)

- [ ] **Step 1: Build the whole solution and confirm CER006 produces 0 warnings against the 8 marked callers**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -i "CER006" || echo "0 CER006 diagnostics"`
Expected: **`0 CER006 diagnostics`.** All 8 production callers (`AuditLogWriter`, `MfaBackupCodeService`, `LockoutUnlockService`, `EmailChangeService`, `PasswordResetService`, `EmailConfirmationService`, `TotpReplayGuard`, `AuthController`) carry `[PreAuthScope]` on the class, so none fires. This is the spec's required "0 violations against `main`" — the precondition for the C-2 soak.

- [ ] **Step 2: Run the full analyzer test suite to confirm no regression**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`
Expected: all existing analyzer tests + the 4 new CER006 tests **PASS** (≈36 tests: 32 from before + 4 CER006). No existing test changed.

- [ ] **Step 3: No commit** — pure verification. If either step fails, stop and root-cause (do not weaken a test).

---

## Task 5: Docs — sync, changelog, roadmap tick (warning half)

**Files:**
- Modify: `docs/roadmap-phase-three.md` (the 9.5g line — correct stale "7 callers" → "8"; record the warning-ship state; keep `[ ]`)
- Modify: `CHANGELOG.md`
- Modify: `docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md` (add CER006 to the rule table + Status line)
- Modify: `docs/testing.md` (note the 7th analyzer + its 4-case matrix)

**Gate awareness:** This closes the *implementation* of 9.5g but NOT the roadmap `[x]` — the warning→error flip (Task 6) is a mandatory ≥48 h follow-up, so the 9.5g line stays `[ ]` with a close-out note. Run `sync-docs` + `changelog-sync` this session (the pre-stage-close gate requires both before any roadmap close-out edit).

- [ ] **Step 1: Run `sync-docs` against the diff.** Add the CER006 row to ADR-0077's rule table + Status line; update testing.md's analyzer-test section to the 7th analyzer.

- [ ] **Step 2: Add the CER006 row to ADR-0077's rule table** (after the CER005 row):

```
| **CER006** | Reliability | `warning` (flip pending) | A class that calls `BeginPreAuthUserScopeAsync` must carry the `[PreAuthScope]` marker — the reverse of CER001. Mirrors CER001's syntax-tree walk; no exclusion logic. Added Stage 9.5g (2026-06-09) |
```

And append to the Status line: `CER006 (reverse-direction [PreAuthScope] marker) added at warning 2026-06-09 (Stage 9.5g); error flip pending the 48h C-2 soak.`

- [ ] **Step 3: Correct the roadmap 9.5g line's stale caller count and add the warning-ship note.** In `docs/roadmap-phase-three.md`, change "7 callers" to "8 callers" in the 9.5g line, and add a sub-bullet: "Analyzer shipped at `warning` <YYYY-MM-DD>. 0 CER006 violations against `main` (all 8 callers marked). **Flip to `error` deferred ≥48 h per C-2** — line stays `[ ]` until the flip." Do NOT tick the line `[x]`.

- [ ] **Step 4: Run `changelog-sync`** and add a CHANGELOG entry under Added: "CER006 Roslyn analyzer — a class calling `BeginPreAuthUserScopeAsync` must carry the `[PreAuthScope]` marker (the reverse of CER001). Shipped at `warning`; `error` flip pending the 48 h soak."

- [ ] **Step 5: Commit**

```bash
git add docs/roadmap-phase-three.md CHANGELOG.md docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md docs/testing.md
git commit -m "docs(9.5g): CER006 shipped at warning — sync ADR/changelog/testing; correct stale caller count (7->8); roadmap stays [ ] pending error flip"
```

---

## Task 6: DEFERRED ≥48 h — warning→error flip (C-2 soak completion)

> **This task does not run in the same sitting.** Gated on ≥48 calendar hours since the Task 1 descriptor commit AND 0 outstanding CER006 violations (already true at ship). Recorded here and as a `[ ]` on the 9.5g roadmap line. The roadmap 9.5g line closes `[x]` only when this task completes. (Can ride alongside CER005's flip, which becomes eligible around the same date.)

**Files:**
- Modify: `.editorconfig` (add the CER006 error-severity line)
- Modify: `docs/roadmap-phase-three.md` (tick 9.5g `[x]`)
- Modify: `CHANGELOG.md` (record the flip)

- [ ] **Step 1: Confirm both C-2 clocks are green**

Run: `git log -1 --format=%ci -- ProjectCeres.Analyzers/PreAuthScopeMarkerAnalyzer.cs` (the descriptor/analyzer commit timestamp) and confirm ≥48 h elapsed.
Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -c "CER006"` → expect `0`.

- [ ] **Step 2: Add the error-severity line to `.editorconfig`**

After the existing CER block (`dotnet_diagnostic.CER010.severity = error`, and the CER005 line if it landed first), add:

```
dotnet_diagnostic.CER006.severity = error
```

The release-tracking row in `AnalyzerReleases.Unshipped.md` **stays `Warning`** (records initial ship severity — do not edit it).

- [ ] **Step 3: Verify the build still succeeds (0 violations → flip is safe)**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: **Build succeeded, 0 errors.** With 0 CER006 sites, raising severity to error changes nothing today but fails any future violation.

- [ ] **Step 4: Tick the roadmap line `[x]`, record the flip in CHANGELOG, run the pre-stage-close gate**

Tick the 9.5g line `[x]` in `docs/roadmap-phase-three.md` (the `sync-docs` + `changelog-sync` + zero-unchecked-items gate applies). Add a CHANGELOG line: "CER006 flipped `warning` → `error` after 48 h soak (0 violations)."

- [ ] **Step 5: Commit**

```bash
git add .editorconfig docs/roadmap-phase-three.md CHANGELOG.md
git commit -m "feat(9.5g): flip CER006 warning -> error after 48h C-2 soak (0 violations); tick 9.5g [x]"
```

---

## Notes for the executor

- **Inverted-TDD ordering** (same as CER005): Task 1 ships an inert analyzer (compiles, RS2008-clean, reports nothing); Task 2's fires-case is the Red; Task 3 turns it Green. Do not skip Task 1's inert skeleton — writing the tests first would not compile (the descriptor wouldn't exist).
- **The test marker goes on the CALL expression**, not the class name — CER006 reports at `invocation.GetLocation()`. This is the single most important difference from CER005 (which marked the class identifier). Wrapping the class name instead would assert the wrong location and fail even with a correct gate.
- **The gate is CER001 with three deltas** (method name, negated marker check, no path exclusion). When in doubt, open `ProjectCeres.Analyzers/PreAuthScopeTransactionAnalyzer.cs` and diff against it mentally — CER006 should look almost identical.
- **Never weaken a test to pass.** A no-fire test (2/3/4) failing after Task 3 means the gate over-fires — fix the gate. The fires-test (1) not going green means it under-fires.
- **Do not run the full `ProjectCeres.Tests` suite** for this stage — CER006 touches no runtime code, only the analyzer assembly. The analyzer test project + a `ProjectCeres` build (for the 0-violations check) are the relevant gates. (The evidence-bundle Stop hook will still want a `build-matrix.json`; generate it via `tools/agent-env/build-matrix.sh 9.5g` at close-out, as in 9.5f.)
