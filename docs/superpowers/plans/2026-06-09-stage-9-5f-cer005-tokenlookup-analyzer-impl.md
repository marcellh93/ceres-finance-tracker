# CER005 HMAC TokenLookup Discipline Analyzer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Roslyn analyzer (CER005) that fails the build, after a 48-hour warning soak, when a class named `*Token` under `ProjectCeres.Models` implementing `IUserOwned` lacks a `byte[] TokenLookup` property.

**Architecture:** One `DiagnosticAnalyzer` registered on `SymbolKind.NamedType`. For each named type it ANDs four symbol-level checks (is-class + name-ends-`Token` + in-`Models`-namespace + implements `ProjectCeres.Common.IUserOwned`) and, if all true, requires a `byte[] TokenLookup` property; missing or wrong-typed → diagnostic on the class declaration. No cross-file index/migration analysis (owned by the E3 `MigrationDriftTests` from 9.5e). Mirrors the five existing CER analyzers exactly.

**Tech Stack:** C# / .NET 10, `Microsoft.CodeAnalysis.CSharp` 4.11.0 (netstandard2.0 analyzer target), xUnit + `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` 1.1.4 (`CSharpAnalyzerVerifier<T>` harness).

**Spec:** `docs/superpowers/specs/2026-06-09-stage-9-5f-cer005-tokenlookup-analyzer-design.md`

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `ProjectCeres.Analyzers/Diagnostics.cs` | add `CER005_TokenLookupDiscipline` descriptor | 1 |
| `ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md` | add CER005 release-tracking row (RS2008) | 1 |
| `ProjectCeres.Analyzers/TokenLookupDisciplineAnalyzer.cs` | the analyzer — gate + diagnostic | 1 (skeleton), 3 (gate) |
| `ProjectCeres.Analyzers.Tests/CER005_TokenLookupAnalyzerTests.cs` | 7 ship-gate tests | 2 |
| `.editorconfig` | `severity = error` flip line | 4 (deferred ≥48 h) |

**Build-ordering note (why Task 1 lands the descriptor + an inert analyzer + the release row together):** The test project references `ProjectCeres.Analyzers` and reads `Diagnostics.CER005_TokenLookupDiscipline` directly (via the analyzer csproj's `InternalsVisibleTo`). So the descriptor must exist before the test file compiles. And RS2008 fails the build the instant a descriptor exists without a matching release-tracking row. Therefore Task 1 ships the descriptor, the release row, and an analyzer that registers but does not yet report — everything compiles, RS2008 is satisfied, and the analyzer is inert. Task 2 then adds tests (the two fires-cases go Red against the inert analyzer); Task 3 implements the gate (Green).

---

## Task 1: Descriptor + release-tracking row + inert analyzer skeleton

**Files:**
- Modify: `ProjectCeres.Analyzers/Diagnostics.cs` (append after the `CER020` descriptor, before the closing `}`)
- Modify: `ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md` (append one row under the `### New Rules` table)
- Create: `ProjectCeres.Analyzers/TokenLookupDisciplineAnalyzer.cs`

- [ ] **Step 1: Add the CER005 descriptor to `Diagnostics.cs`**

Insert this block immediately after the `CER020_ResxParityMissing` descriptor's closing `);` and before the final `}` of the `Diagnostics` class:

```csharp

    public static readonly DiagnosticDescriptor CER005_TokenLookupDiscipline = new(
        id: "CER005",
        title: "Token entity must declare a byte[] TokenLookup property",
        messageFormat: "Token entity '{0}' must declare a 'byte[] TokenLookup' property (HMAC fingerprint with a unique index) so confirm paths look up in O(1) instead of running Argon2id over every candidate",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A class named *Token under ProjectCeres.Models implementing IUserOwned represents a stored single-use token row. It must carry a byte[] TokenLookup column (HMAC-SHA256 of the raw token, uniquely indexed) so /confirm locates its row in O(1). Stage 9.1.5.a shipped LockoutUnlockToken without it and regressed the integration suite ~15min. See docs/superpowers/specs/2026-06-09-stage-9-5f-cer005-tokenlookup-analyzer-design.md.");
```

(No trailing period in `title` or `messageFormat` — RS1032. The `description` is prose and may end with a period.)

- [ ] **Step 2: Add the CER005 row to `AnalyzerReleases.Unshipped.md`**

Append this single line to the existing `### New Rules` table, immediately after the `CER020` row (preserve the exact column format — single-space-delimited pipes, no padding; RS2007 fails on padded columns):

```
CER005 | Reliability | Warning | *Token classes under ProjectCeres/Models/ implementing IUserOwned must declare a byte[] TokenLookup property
```

- [ ] **Step 3: Create the inert analyzer `TokenLookupDisciplineAnalyzer.cs`**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TokenLookupDisciplineAnalyzer : DiagnosticAnalyzer
{
    private const string ModelsNamespacePrefix = "ProjectCeres.Models";
    private const string IUserOwnedFullName = "ProjectCeres.Common.IUserOwned";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER005_TokenLookupDiscipline);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
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
Expected: both **Build succeeded, 0 errors.** Specifically no `RS2008` (descriptor is tracked), no `RS1032` (no trailing period in title/messageFormat), no `RS2007` (release-table format). The analyzer is registered but reports nothing. (Pre-existing CA1707 underscore-naming warnings in the Tests project are acceptable; RS-prefixed are not.)

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Analyzers/Diagnostics.cs ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md ProjectCeres.Analyzers/TokenLookupDisciplineAnalyzer.cs
git commit -m "feat(9.5f): CER005 descriptor + release-tracking row + inert analyzer skeleton"
```

---

## Task 2: The 7 ship-gate tests (Red against the inert analyzer)

**Files:**
- Create: `ProjectCeres.Analyzers.Tests/CER005_TokenLookupAnalyzerTests.cs`

Each test inline-defines a stub `ProjectCeres.Common.IUserOwned` interface in its preamble (the analyzer matches by full name) and places the token type in `ProjectCeres.Models`. The `{|#0:...|}` marker sits on the **class identifier** (`FooToken`) — `INamedTypeSymbol.Locations.First()` points to the type's name token, which is where `ReportDiagnostic` lands. (Common slip: marking the base-type `IUserOwned` instead would assert the wrong location and fail even with a correct gate.) This mirrors the preamble idiom in `CER004_DateTimeWallClockAnalyzerTests.cs`.

- [ ] **Step 1: Write the test file with all 7 cases**

```csharp
using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpAnalyzerVerifier<ProjectCeres.Analyzers.TokenLookupDisciplineAnalyzer>;

namespace ProjectCeres.Analyzers.Tests;

public class CER005_TokenLookupAnalyzerTests
{
    // Stub IUserOwned in the exact namespace the analyzer matches by full name.
    private const string UserOwnedPreamble = @"
namespace ProjectCeres.Common
{
    public interface IUserOwned { }
}
";

    // 1. *Token + IUserOwned in Models, NO TokenLookup -> fires (core regression)
    [Fact]
    public async Task Fires_When_TokenEntity_Lacks_TokenLookup()
    {
        var source = UserOwnedPreamble + @"
namespace ProjectCeres.Models
{
    using ProjectCeres.Common;
    public sealed class {|#0:FooToken|} : IUserOwned
    {
        public System.Guid Id { get; set; }
        public string TokenHash { get; set; } = """";
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER005_TokenLookupDiscipline)
            .WithLocation(0)
            .WithArguments("FooToken");
        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    // 2. *Token + IUserOwned in Models + byte[] TokenLookup -> no fire (real-entity shape)
    [Fact]
    public async Task NoFire_When_TokenEntity_Has_ByteArray_TokenLookup()
    {
        var source = UserOwnedPreamble + @"
namespace ProjectCeres.Models
{
    using ProjectCeres.Common;
    public sealed class FooToken : IUserOwned
    {
        public System.Guid Id { get; set; }
        public byte[] TokenLookup { get; set; } = System.Array.Empty<byte>();
        public string TokenHash { get; set; } = """";
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // 3. *Token + IUserOwned in Models + string TokenLookup -> fires (wrong type)
    [Fact]
    public async Task Fires_When_TokenLookup_Is_Wrong_Type()
    {
        var source = UserOwnedPreamble + @"
namespace ProjectCeres.Models
{
    using ProjectCeres.Common;
    public sealed class {|#0:FooToken|} : IUserOwned
    {
        public System.Guid Id { get; set; }
        public string TokenLookup { get; set; } = """";
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER005_TokenLookupDiscipline)
            .WithLocation(0)
            .WithArguments("FooToken");
        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    // 4. *Token in Models, NO IUserOwned, no TokenLookup -> no fire (non-entity helper)
    [Fact]
    public async Task NoFire_When_TokenClass_Is_Not_UserOwned()
    {
        var source = UserOwnedPreamble + @"
namespace ProjectCeres.Models
{
    public sealed class FooToken
    {
        public System.Guid Id { get; set; }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // 5. non-*Token class + IUserOwned in Models, no TokenLookup -> no fire (only token classes policed)
    [Fact]
    public async Task NoFire_When_NonToken_UserOwned_Entity()
    {
        var source = UserOwnedPreamble + @"
namespace ProjectCeres.Models
{
    using ProjectCeres.Common;
    public sealed class Account : IUserOwned
    {
        public System.Guid Id { get; set; }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // 6. *Token + IUserOwned OUTSIDE Models namespace, no TokenLookup -> no fire (namespace gate)
    [Fact]
    public async Task NoFire_When_TokenClass_Outside_Models_Namespace()
    {
        var source = UserOwnedPreamble + @"
namespace ProjectCeres.Common.Authentication
{
    using ProjectCeres.Common;
    public sealed class FooToken : IUserOwned
    {
        public System.Guid Id { get; set; }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // 7. enum named *Token in Models -> no fire (only classes examined)
    [Fact]
    public async Task NoFire_When_Token_Is_An_Enum()
    {
        var source = UserOwnedPreamble + @"
namespace ProjectCeres.Models
{
    public enum FooToken { A = 1, B = 2 }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
```

- [ ] **Step 2: Run the tests to verify the two fires-cases FAIL and the five no-fire cases PASS**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj --filter "FullyQualifiedName~CER005"`
Expected: tests 1 and 3 (`Fires_When_TokenEntity_Lacks_TokenLookup`, `Fires_When_TokenLookup_Is_Wrong_Type`) **FAIL** — the inert analyzer reports nothing, so the expected diagnostic at `#0` is never produced. Tests 2, 4, 5, 6, 7 **PASS** — an inert analyzer trivially produces no diagnostic, which is what they assert. This is the correct Red state: the catching behavior is unimplemented, the exclusions are (vacuously) satisfied.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Analyzers.Tests/CER005_TokenLookupAnalyzerTests.cs
git commit -m "test(9.5f): CER005 ship-gate matrix — 2 fires + 5 exclusions (Red)"
```

---

## Task 3: Implement the gate (Green)

**Files:**
- Modify: `ProjectCeres.Analyzers/TokenLookupDisciplineAnalyzer.cs` (replace the empty `AnalyzeNamedType` body)

- [ ] **Step 1: Replace the `AnalyzeNamedType` method body with the four-question gate**

Replace the entire `AnalyzeNamedType` method (the one with the `// Gate implemented in Task 3.` comment) with:

```csharp
    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not INamedTypeSymbol type) return;

        // Q1: a class named *Token (TypeKind.Class checked first — cheapest, and excludes the
        // EmailChangeTokenPurpose enum and any future enum/struct named *Token).
        if (type.TypeKind != TypeKind.Class) return;
        if (!type.Name.EndsWith("Token", System.StringComparison.Ordinal)) return;

        // Q2: under the ProjectCeres.Models namespace.
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!ns.StartsWith(ModelsNamespacePrefix, System.StringComparison.Ordinal)) return;

        // Q3: implements ProjectCeres.Common.IUserOwned (the user-owned-row marker).
        var isUserOwned = type.AllInterfaces.Any(i => i.ToDisplayString() == IUserOwnedFullName);
        if (!isUserOwned) return;

        // Q4: carries a byte[] TokenLookup property. Missing OR wrong-typed -> fire.
        var hasByteArrayLookup = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Any(p => p.Name == "TokenLookup"
                      && p.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte });
        if (hasByteArrayLookup) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER005_TokenLookupDiscipline,
            type.Locations.FirstOrDefault() ?? Location.None,
            type.Name));
    }
```

- [ ] **Step 2: Run the CER005 tests to verify ALL 7 PASS**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj --filter "FullyQualifiedName~CER005"`
Expected: **7/7 PASS.** Tests 1 and 3 now fire at `#0`; tests 2, 4, 5, 6, 7 still produce no diagnostic.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Analyzers/TokenLookupDisciplineAnalyzer.cs
git commit -m "feat(9.5f): CER005 four-question gate — class+*Token+Models+IUserOwned requires byte[] TokenLookup (Green)"
```

---

## Task 4: Whole-solution build — verify 0 violations on `main` and the full analyzer suite

**Files:** none (verification + the analyzer-suite regression check)

- [ ] **Step 1: Build the whole solution and confirm CER005 produces 0 warnings against the four real entities**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -i "CER005" || echo "0 CER005 diagnostics"`
Expected: **`0 CER005 diagnostics`.** The four real entities (`PasswordResetToken`, `EmailChangeToken`, `EmailConfirmationToken`, `LockoutUnlockToken`) all carry `byte[] TokenLookup`, so none fires. This is the spec's required "0 violations against today's main" — the precondition for the C-2 soak.

- [ ] **Step 2: Run the full analyzer test suite to confirm no regression in CER001/002/004/010/020**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`
Expected: all existing analyzer tests + the 7 new CER005 tests **PASS** (≈32 tests). No existing test changed.

- [ ] **Step 3: No commit** — this task is pure verification. If either step fails, stop and root-cause (do not weaken a test).

---

## Task 5: Docs — sync, changelog, roadmap tick (warning half)

**Files:**
- Modify: `docs/roadmap-phase-three.md` (the 9.5f line — correct the stale "3 token tables" → "4", and record the warning-ship state)
- Modify: `CHANGELOG.md`
- Possibly modify: `docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md` (add CER005 to the rule table)

**Gate awareness:** This task closes the *implementation* of 9.5f but **not** the roadmap `[x]` — the warning→error flip (Task 6) is a mandatory ≥48 h follow-up, so the 9.5f roadmap line stays `[ ]` until the flip lands, with a close-out note recording "analyzer shipped at warning <date>; flip pending soak." Per the project's pre-stage-close gate (`sync-docs` + `changelog-sync` must fire this session before any roadmap close-out edit), run those skills in this task.

- [ ] **Step 1: Run `sync-docs` against the diff** (descriptor + analyzer + tests + release row). Update ADR-0077's rule table to add the CER005 row if `sync-docs` flags it.

- [ ] **Step 2: Correct the roadmap 9.5f line's stale token count and add the warning-ship close-out note**

In `docs/roadmap-phase-three.md`, the 9.5f verification-checklist line currently reads "existing 3 token tables → no fire". Change "3" to "4" (the live count includes `EmailConfirmationToken`, added Stage 9.3). Add a sub-bullet: "Analyzer shipped at `warning` <YYYY-MM-DD> (descriptor commit). 0 CER005 violations against `main`. **Flip to `error` deferred ≥48 h per C-2** — see Task 6 / §8 of the spec. Roadmap line stays `[ ]` until the flip." Do **not** tick the line `[x]`.

- [ ] **Step 3: Run `changelog-sync`** and add a CHANGELOG entry under Added: "CER005 Roslyn analyzer — `*Token` entities under `Models/` implementing `IUserOwned` must declare a `byte[] TokenLookup` property (shipped at `warning`; `error` flip pending 48 h soak)."

- [ ] **Step 4: Commit**

```bash
git add docs/roadmap-phase-three.md CHANGELOG.md docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md
git commit -m "docs(9.5f): CER005 shipped at warning — sync ADR/changelog; correct stale token count (3->4); roadmap stays [ ] pending error flip"
```

---

## Task 6: DEFERRED ≥48 h — warning→error flip (C-2 soak completion)

> **This task does not run in the same sitting.** It is gated on ≥48 calendar hours since the Task 1 descriptor commit AND 0 outstanding CER005 violations (already true at ship). It is recorded here, and as a `[ ]` on the 9.5f roadmap line, so it is not lost. The roadmap 9.5f line closes `[x]` only when this task completes.

**Files:**
- Modify: `.editorconfig` (add the CER005 error-severity line)
- Modify: `docs/roadmap-phase-three.md` (tick 9.5f `[x]`)
- Modify: `CHANGELOG.md` (record the flip)

- [ ] **Step 1: Confirm both C-2 clocks are green**

Run: `git log -1 --format=%ci -- ProjectCeres.Analyzers/Diagnostics.cs` (the descriptor commit timestamp) and confirm ≥48 h elapsed.
Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -c "CER005"` → expect `0`.

- [ ] **Step 2: Add the error-severity line to `.editorconfig`**

After the existing `dotnet_diagnostic.CER010.severity = error` line (line ~19), add:

```
dotnet_diagnostic.CER005.severity = error
```

The release-tracking row in `AnalyzerReleases.Unshipped.md` **stays `Warning`** (it records initial ship severity — do not edit it).

- [ ] **Step 3: Verify the build still succeeds (0 violations → flip is safe)**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: **Build succeeded, 0 errors.** With 0 CER005 sites, raising severity to error changes nothing today but fails any future violation.

- [ ] **Step 4: Tick the roadmap line `[x]`, record the flip in CHANGELOG, run the pre-stage-close gate**

Tick the 9.5f line `[x]` in `docs/roadmap-phase-three.md` (the `sync-docs` + `changelog-sync` + zero-unchecked-items gate applies). Add a CHANGELOG line: "CER005 flipped `warning` → `error` after 48 h soak (0 violations)."

- [ ] **Step 5: Commit**

```bash
git add .editorconfig docs/roadmap-phase-three.md CHANGELOG.md
git commit -m "feat(9.5f): flip CER005 warning -> error after 48h C-2 soak (0 violations); tick 9.5f [x]"
```

---

## Notes for the executor

- **TDD ordering is inverted from the usual** because a Roslyn analyzer's test project must compile against the descriptor. Task 1 ships an *inert* analyzer (compiles, RS2008-clean, reports nothing); Task 2's two fires-cases are the Red; Task 3 turns them Green. Do not skip Task 1's inert skeleton — writing the tests first would not compile.
- **Never weaken a test to pass.** If a no-fire test (2, 4, 5, 6, 7) fails after Task 3, the gate is over-firing — fix the gate, not the test. If a fires-test (1, 3) won't go green, the gate is under-firing.
- **The `byte[]` check is `IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte }`** — verified against the dotnet/roslyn public API. `ElementType` is `ITypeSymbol`, which has `SpecialType`. The extended-property-pattern compiles under the analyzer csproj's `LangVersion=latest`.
- **Stop-hook tiering:** Tasks 1–4 touch `.cs`/`.csproj`-adjacent analyzer files → the Stop hook runs the relevant `dotnet test` tier. Task 5 is docs-only (Tier 0). Run the analyzer-test filter manually after Task 3 regardless.
- **Do not run the full `ProjectCeres.Tests` suite** for this stage — CER005 touches no runtime code, only the analyzer assembly. The analyzer test project + a `ProjectCeres` build (for the 0-violations check) are the relevant gates.
```