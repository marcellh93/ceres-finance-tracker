# Stage 9.5j — Code-Fix Providers (CER001 + CER004) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship IDE lightbulb quick-fix actions for two existing analyzer warnings — CER004 (a safe rewrite to the injected time provider) and CER001 (a placeholder rewrite to the pre-auth scope helper) — with a code-fix test harness pinning both the happy path and the deliberate non-fix / placeholder boundaries.

**Architecture:** Two `CodeFixProvider` classes live in the existing `ProjectCeres.Analyzers/` project (Roslyn loads code-fix providers from the same assembly as the analyzers, so no new wiring in the main app). Each consumes one diagnostic ID and rewrites a single syntax node via `root.ReplaceNode`. CER004 gates on the presence of a `_timeProvider` field and registers no fix when absent (the "safe" guarantee); CER001 always registers and inserts deliberately-undeclared identifiers (the "placeholder" posture). CER010 ships no fix — re-scoped because a format-valid invented ticket would defeat the audit-trail rule it removes.

**Tech Stack:** .NET 10 / netstandard2.0 analyzer project, Roslyn `Microsoft.CodeAnalysis.CSharp.Workspaces` 4.11.0 (the CodeFixProvider base + syntax rewrite helpers), xUnit + `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` (`CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>`).

**Spec:** `docs/superpowers/specs/2026-06-10-stage-9-5j-code-fix-providers-design.md`

---

## File Structure

| File | Create / Modify | Responsibility |
|---|---|---|
| `ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj` | Modify | Add the `Microsoft.CodeAnalysis.CSharp.Workspaces` package ref (CodeFixProvider base) |
| `ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj` | Modify | Add the `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` package ref (test harness) |
| `ProjectCeres.Analyzers.Tests/TestHelpers/CSharpCodeFixVerifier.cs` | Create | Generic `VerifyCodeFixAsync` wrapper, mirrors the existing `CSharpAnalyzerVerifier` |
| `ProjectCeres.Analyzers/DateTimeWallClockCodeFixProvider.cs` | Create | CER004 safe fix |
| `ProjectCeres.Analyzers.Tests/CER004_DateTimeWallClockCodeFixTests.cs` | Create | CER004 fix tests (present / absent-negative / subtype) |
| `ProjectCeres.Analyzers/PreAuthScopeTransactionCodeFixProvider.cs` | Create | CER001 placeholder fix |
| `ProjectCeres.Analyzers.Tests/CER001_PreAuthScopeTransactionCodeFixTests.cs` | Create | CER001 placeholder-fix test (3 expected errors) |

**Build/test commands used throughout:**
- Build analyzer project: `dotnet build ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj`
- Run a single test: `dotnet test ProjectCeres.Analyzers.Tests --filter "FullyQualifiedName~<TestName>"`
- Run the whole analyzer test project: `dotnet test ProjectCeres.Analyzers.Tests`

---

## Implementation notes for the engineer (read before Task 1)

1. **Empirical-confirmation discipline (from 9.5i).** Two values in this plan are *expected* shapes, not guesses to take on faith:
   - the exact compiler-error codes the CER001 placeholder yields (`CS0103` for the undeclared identifiers, `CS1061` for the wrong receiver method);
   - the exact id/version of the code-fix testing package.

   For both: **run the test / build and read the actual output, then set the assertion/ref to match what the harness reports.** Do NOT weaken an assertion to make a test pass — if the real error code differs from the plan's expectation, update the `ExpectedDiagnostics` to the real code and keep the assertion as strong (or stronger). This is exactly how the 9.5i CS0426→CS0117 surprise was caught and fixed.

2. **Node lookup.** `root.FindNode(diagnostic.Location.SourceSpan)` returns the node whose span best matches. Because CER001 reports at `invocation.GetLocation()` and CER004 at `memberAccess.GetLocation()`, `FindNode` lands on the invocation / member-access respectively. Use `.FirstAncestorOrSelf<InvocationExpressionSyntax>()` / `.FirstAncestorOrSelf<MemberAccessExpressionSyntax>()` defensively so a span that resolves to a child token still climbs to the expected node type. Bail (`return`) if the cast yields null.

3. **Comment hygiene (project rule).** Keep XML docs ≤ 3 sentences and inline `//` ≤ 1 line. No multi-paragraph rationale blocks; the "why" lives in the spec/ADR.

4. **No `AnalyzerReleases` change, no `.editorconfig` change, no soak.** Code-fix providers emit no diagnostic descriptor. Do not touch `AnalyzerReleases.Unshipped.md`.

---

## Task 1: Add the Workspaces package to the analyzer project + the CodeFix.Testing package to the test project

**Files:**
- Modify: `ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj:12-14`
- Modify: `ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj:11-20`

- [ ] **Step 1: Add the Workspaces package to the analyzer project**

In `ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj`, the existing `<ItemGroup>` has one package ref:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" PrivateAssets="all" />
  </ItemGroup>
```

Add the Workspaces ref directly below it, inside the same `<ItemGroup>`:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.11.0" PrivateAssets="all" />
  </ItemGroup>
```

- [ ] **Step 2: Add the CodeFix.Testing package to the test project**

In `ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`, the test-package `<ItemGroup>` already references `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` and `...SourceGenerators.Testing` at `1.1.4`. Add the code-fix testing sibling right after the SourceGenerators.Testing line:

```xml
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.CodeFix.Testing" Version="1.1.4" />
```

- [ ] **Step 3: Restore and build both projects to confirm the packages resolve**

Run: `dotnet build ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`
Expected: **build succeeds**. If NuGet reports the CodeFix.Testing package id or `1.1.4` version does not exist, read the error, find the correct id/version in the `Microsoft.CodeAnalysis.*.Testing` `1.1.x` family (it ships alongside the two already-referenced packages), set the ref to the real value, and rebuild. Do not proceed until both projects build green.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj
git commit -m "build(9.5j): add CodeAnalysis Workspaces (analyzer) + CodeFix.Testing (tests) package refs"
```

---

## Task 2: Create the code-fix test harness helper

**Files:**
- Create: `ProjectCeres.Analyzers.Tests/TestHelpers/CSharpCodeFixVerifier.cs`

This mirrors the existing `ProjectCeres.Analyzers.Tests/TestHelpers/CSharpAnalyzerVerifier.cs` (analyzer-only) but for the analyzer+fix pair.

- [ ] **Step 1: Write the harness helper**

Create `ProjectCeres.Analyzers.Tests/TestHelpers/CSharpCodeFixVerifier.cs`:

```csharp
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace ProjectCeres.Analyzers.Tests.TestHelpers;

public static class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public class Test : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100;
        }
    }

    // Source flagged, fix applied, fixedSource is the expected result.
    // expectedInFixedCode pins any compiler diagnostics that SURVIVE the fix (placeholder posture).
    public static System.Threading.Tasks.Task VerifyCodeFixAsync(
        string source,
        string fixedSource,
        params DiagnosticResult[] expectedInFixedCode)
    {
        var test = new Test
        {
            TestCode = source,
            FixedCode = fixedSource,
        };
        test.FixedState.ExpectedDiagnostics.AddRange(expectedInFixedCode);
        return test.RunAsync();
    }

    // Source flagged but NO fix is registered: fixedSource == source.
    // Caller passes the analyzer diagnostic(s) that remain (the warning is not fixed).
    public static System.Threading.Tasks.Task VerifyNoFixAsync(
        string source,
        params DiagnosticResult[] analyzerDiagnostics)
    {
        var test = new Test
        {
            TestCode = source,
            FixedCode = source,
        };
        test.ExpectedDiagnostics.AddRange(analyzerDiagnostics);
        return test.RunAsync();
    }
}
```

- [ ] **Step 2: Build the test project to confirm the harness compiles**

Run: `dotnet build ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`
Expected: **build succeeds** (no test references it yet, but the helper must compile). If `CSharpCodeFixTest` is not found, the CodeFix.Testing package from Task 1 is missing or the namespace differs — read the error and correct the `using` / package before proceeding.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Analyzers.Tests/TestHelpers/CSharpCodeFixVerifier.cs
git commit -m "test(9.5j): CSharpCodeFixVerifier harness — VerifyCodeFixAsync + VerifyNoFixAsync"
```

---

## Task 3: CER004 safe fix — failing tests first (Red)

**Files:**
- Create: `ProjectCeres.Analyzers.Tests/CER004_DateTimeWallClockCodeFixTests.cs`

Three cases: (a) field present → fix applied; (b) field absent → no fix (the load-bearing negative assertion); (c) field is a TimeProvider *subtype* → fix applied (semantic type check, not string match).

- [ ] **Step 1: Write the three failing tests**

Create `ProjectCeres.Analyzers.Tests/CER004_DateTimeWallClockCodeFixTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpCodeFixVerifier<
    ProjectCeres.Analyzers.DateTimeWallClockAnalyzer,
    ProjectCeres.Analyzers.DateTimeWallClockCodeFixProvider>;

namespace ProjectCeres.Analyzers.Tests;

public class CER004_DateTimeWallClockCodeFixTests
{
    // (a) _timeProvider field present → fix rewrites to _timeProvider.GetUtcNow().UtcDateTime
    [Fact]
    public async Task Fixes_When_TimeProvider_Field_Present()
    {
        var source = @"
namespace ProjectCeres.Services
{
    public class SomeService
    {
        private readonly System.TimeProvider _timeProvider;
        public SomeService(System.TimeProvider tp) { _timeProvider = tp; }
        public System.DateTime DoWork()
        {
            return {|#0:System.DateTime.UtcNow|};
        }
    }
}
";
        var fixedSource = @"
namespace ProjectCeres.Services
{
    public class SomeService
    {
        private readonly System.TimeProvider _timeProvider;
        public SomeService(System.TimeProvider tp) { _timeProvider = tp; }
        public System.DateTime DoWork()
        {
            return _timeProvider.GetUtcNow().UtcDateTime;
        }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER004_DateTimeWallClock)
            .WithLocation(0)
            .WithArguments("DateTime.UtcNow");

        // The flagged source carries the analyzer diagnostic; after the fix it is gone.
        var test = new Verifier.Test { TestCode = source, FixedCode = fixedSource };
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }

    // (b) NO _timeProvider field → NO fix offered (source unchanged). Load-bearing negative.
    [Fact]
    public async Task Offers_No_Fix_When_TimeProvider_Field_Absent()
    {
        var source = @"
namespace ProjectCeres.Services
{
    public class SomeService
    {
        public System.DateTime DoWork()
        {
            return {|#0:System.DateTime.UtcNow|};
        }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER004_DateTimeWallClock)
            .WithLocation(0)
            .WithArguments("DateTime.UtcNow");

        // No field → fix registers nothing → FixedCode == source, diagnostic remains.
        await Verifier.VerifyNoFixAsync(source, expected);
    }

    // (c) Field typed as a TimeProvider SUBTYPE → fix still applies (semantic type check).
    [Fact]
    public async Task Fixes_When_Field_Is_TimeProvider_Subtype()
    {
        var source = @"
namespace ProjectCeres.Services
{
    public class FakeClock : System.TimeProvider { }

    public class SomeService
    {
        private readonly FakeClock _timeProvider;
        public SomeService(FakeClock tp) { _timeProvider = tp; }
        public System.DateTime DoWork()
        {
            return {|#0:System.DateTime.UtcNow|};
        }
    }
}
";
        var fixedSource = @"
namespace ProjectCeres.Services
{
    public class FakeClock : System.TimeProvider { }

    public class SomeService
    {
        private readonly FakeClock _timeProvider;
        public SomeService(FakeClock tp) { _timeProvider = tp; }
        public System.DateTime DoWork()
        {
            return _timeProvider.GetUtcNow().UtcDateTime;
        }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER004_DateTimeWallClock)
            .WithLocation(0)
            .WithArguments("DateTime.UtcNow");

        var test = new Verifier.Test { TestCode = source, FixedCode = fixedSource };
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }
}
```

- [ ] **Step 2: Run the tests — confirm they FAIL to compile (provider type does not exist yet)**

Run: `dotnet test ProjectCeres.Analyzers.Tests --filter "FullyQualifiedName~CER004_DateTimeWallClockCodeFixTests"`
Expected: **compile error** — `DateTimeWallClockCodeFixProvider` is not defined. That is the correct Red state for the next task.

(Do not commit a non-compiling test file alone; Task 4 makes it compile and pass, and they commit together in Task 4 Step 4.)

---

## Task 4: CER004 safe fix — implement the provider (Green)

**Files:**
- Create: `ProjectCeres.Analyzers/DateTimeWallClockCodeFixProvider.cs`

- [ ] **Step 1: Write the code-fix provider**

Create `ProjectCeres.Analyzers/DateTimeWallClockCodeFixProvider.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProjectCeres.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DateTimeWallClockCodeFixProvider)), Shared]
public sealed class DateTimeWallClockCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use _timeProvider.GetUtcNow().UtcDateTime";
    private const string TimeProviderFieldName = "_timeProvider";
    private const string TimeProviderFullName = "System.TimeProvider";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.CER004_DateTimeWallClock.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
        if (node is null) return;

        // Gate: only offer the fix when a _timeProvider field of type System.TimeProvider
        // (or a subtype) exists in the enclosing class. Else register nothing (safe posture).
        var enclosingType = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingType is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;
        if (semanticModel.GetDeclaredSymbol(enclosingType, context.CancellationToken) is not INamedTypeSymbol typeSymbol) return;

        var hasTimeProviderField = typeSymbol.GetMembers(TimeProviderFieldName)
            .OfType<IFieldSymbol>()
            .Any(f => !f.IsStatic && IsTimeProviderOrSubtype(f.Type));
        if (!hasTimeProviderField) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => ReplaceWithTimeProviderAsync(context.Document, root, node, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static bool IsTimeProviderOrSubtype(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == TimeProviderFullName) return true;
        }
        return false;
    }

    private static Task<Document> ReplaceWithTimeProviderAsync(
        Document document, SyntaxNode root, MemberAccessExpressionSyntax node, System.Threading.CancellationToken ct)
    {
        // _timeProvider.GetUtcNow().UtcDateTime
        var replacement = SyntaxFactory.ParseExpression("_timeProvider.GetUtcNow().UtcDateTime")
            .WithTriviaFrom(node);
        var newRoot = root.ReplaceNode(node, replacement);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
```

- [ ] **Step 2: Build the analyzer project**

Run: `dotnet build ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj`
Expected: **build succeeds, 0 warnings.** If `EnforceExtendedAnalyzerRules` flags an RS-rule (e.g. RS1022 forbidding `Workspaces` types in an analyzer assembly, or a ConfigureAwait rule), read the rule, satisfy it the documented way, and rebuild. Do not suppress an RS rule to move on.

- [ ] **Step 3: Run the CER004 fix tests**

Run: `dotnet test ProjectCeres.Analyzers.Tests --filter "FullyQualifiedName~CER004_DateTimeWallClockCodeFixTests"`
Expected: **3/3 pass.** If the rewritten text differs from `fixedSource` by trivia/whitespace, adjust either the `WithTriviaFrom` handling or the expected `fixedSource` so they match exactly — do not delete the assertion. If `ParseExpression` produces an unexpected shape, read the actual produced text from the failure message and reconcile.

- [ ] **Step 4: Commit (test + provider together)**

```bash
git add ProjectCeres.Analyzers/DateTimeWallClockCodeFixProvider.cs ProjectCeres.Analyzers.Tests/CER004_DateTimeWallClockCodeFixTests.cs
git commit -m "feat(9.5j): CER004 code-fix — safe rewrite to _timeProvider.GetUtcNow().UtcDateTime, gated on field presence"
```

---

## Task 5: CER001 placeholder fix — failing test first (Red)

**Files:**
- Create: `ProjectCeres.Analyzers.Tests/CER001_PreAuthScopeTransactionCodeFixTests.cs`

One case: the placeholder fix rewrites the method name + inserts `userId, ct`, leaves the `_db.Database` receiver, and the fixed code still carries three compiler errors (the deliberate to-do list).

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Analyzers.Tests/CER001_PreAuthScopeTransactionCodeFixTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpCodeFixVerifier<
    ProjectCeres.Analyzers.PreAuthScopeTransactionAnalyzer,
    ProjectCeres.Analyzers.PreAuthScopeTransactionCodeFixProvider>;

namespace ProjectCeres.Analyzers.Tests;

public class CER001_PreAuthScopeTransactionCodeFixTests
{
    // Placeholder fix: swap method name + insert undeclared userId, ct.
    // Receiver _db.Database left as-is. Fixed code carries CS0103 (userId), CS0103 (ct),
    // CS1061 (Database has no BeginPreAuthUserScopeAsync) — the deliberate to-do list.
    [Fact]
    public async Task Placeholder_Fix_Leaves_Three_Expected_Compiler_Errors()
    {
        var source = @"
using System.Threading.Tasks;
using ProjectCeres.Analyzers.Annotations;

namespace ProjectCeres.Analyzers.Annotations
{
    public class PreAuthScopeAttribute : System.Attribute { }
}

namespace Test
{
    public class FakeDb { public FakeDatabase Database => new(); }
    public class FakeDatabase { public Task BeginTransactionAsync() => Task.CompletedTask; }

    [ProjectCeres.Analyzers.Annotations.PreAuthScope]
    public class MarkedClass
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await {|#0:_db.Database.BeginTransactionAsync()|};
    }
}
";
        var fixedSource = @"
using System.Threading.Tasks;
using ProjectCeres.Analyzers.Annotations;

namespace ProjectCeres.Analyzers.Annotations
{
    public class PreAuthScopeAttribute : System.Attribute { }
}

namespace Test
{
    public class FakeDb { public FakeDatabase Database => new(); }
    public class FakeDatabase { public Task BeginTransactionAsync() => Task.CompletedTask; }

    [ProjectCeres.Analyzers.Annotations.PreAuthScope]
    public class MarkedClass
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await _db.Database.BeginPreAuthUserScopeAsync(userId, ct);
    }
}
";
        var analyzerDiagnostic = new DiagnosticResult(Diagnostics.CER001_PreAuthScopeTransactionType)
            .WithLocation(0)
            .WithArguments("MarkedClass");

        var test = new Verifier.Test { TestCode = source, FixedCode = fixedSource };
        test.ExpectedDiagnostics.Add(analyzerDiagnostic);
        // Errors that SURVIVE the fix (the placeholder to-do list).
        // NOTE: confirm the exact CS codes empirically from the first run and reconcile.
        test.FixedState.ExpectedDiagnostics.Add(DiagnosticResult.CompilerError("CS0103").WithMessage("The name 'userId' does not exist in the current context"));
        test.FixedState.ExpectedDiagnostics.Add(DiagnosticResult.CompilerError("CS0103").WithMessage("The name 'ct' does not exist in the current context"));
        test.FixedState.ExpectedDiagnostics.Add(DiagnosticResult.CompilerError("CS1061"));
        await test.RunAsync();
    }
}
```

- [ ] **Step 2: Run the test — confirm it FAILS to compile (provider type does not exist yet)**

Run: `dotnet test ProjectCeres.Analyzers.Tests --filter "FullyQualifiedName~CER001_PreAuthScopeTransactionCodeFixTests"`
Expected: **compile error** — `PreAuthScopeTransactionCodeFixProvider` is not defined. Correct Red state.

---

## Task 6: CER001 placeholder fix — implement the provider (Green)

**Files:**
- Create: `ProjectCeres.Analyzers/PreAuthScopeTransactionCodeFixProvider.cs`

- [ ] **Step 1: Write the code-fix provider**

Create `ProjectCeres.Analyzers/PreAuthScopeTransactionCodeFixProvider.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProjectCeres.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PreAuthScopeTransactionCodeFixProvider)), Shared]
public sealed class PreAuthScopeTransactionCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use BeginPreAuthUserScopeAsync(userId, ct)";
    private const string TargetMethodName = "BeginPreAuthUserScopeAsync";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.CER001_PreAuthScopeTransactionType.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var invocation = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => ReplaceWithPreAuthScopeAsync(context.Document, root, invocation, memberAccess, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static Task<Document> ReplaceWithPreAuthScopeAsync(
        Document document, SyntaxNode root, InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess, CancellationToken ct)
    {
        // Swap the method name only — receiver (e.g. _db.Database) is left as-is on purpose.
        var newMemberAccess = memberAccess.WithName(SyntaxFactory.IdentifierName(TargetMethodName));

        // Insert two deliberately-undeclared identifier arguments: userId, ct.
        var args = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
        {
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("userId")),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("ct")),
        }));

        var newInvocation = invocation
            .WithExpression(newMemberAccess)
            .WithArgumentList(args)
            .WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
```

- [ ] **Step 2: Build the analyzer project**

Run: `dotnet build ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj`
Expected: **build succeeds, 0 warnings.**

- [ ] **Step 3: Run the CER001 fix test, reconcile the compiler-error codes**

Run: `dotnet test ProjectCeres.Analyzers.Tests --filter "FullyQualifiedName~CER001_PreAuthScopeTransactionCodeFixTests"`
Expected: **1/1 pass.** The likely failure on first run is a mismatch between the placeholder `FixedState.ExpectedDiagnostics` codes and what the compiler actually reports. **Read the test failure**, which lists the real diagnostics produced by `fixedSource`, then set `FixedState.ExpectedDiagnostics` to exactly those (codes + locations). The assertion must stay — it pins that the placeholder is intentional. If the real codes are not `CS0103`/`CS1061`, update to the real ones; do not drop any.

- [ ] **Step 4: Commit (test + provider together)**

```bash
git add ProjectCeres.Analyzers/PreAuthScopeTransactionCodeFixProvider.cs ProjectCeres.Analyzers.Tests/CER001_PreAuthScopeTransactionCodeFixTests.cs
git commit -m "feat(9.5j): CER001 code-fix — placeholder rewrite to BeginPreAuthUserScopeAsync(userId, ct)"
```

---

## Task 7: Full analyzer-suite green + docs sync + close-out

**Files:**
- Modify: `docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md`
- Modify: `docs/testing.md`
- Modify: `docs/roadmap-phase-three.md` (the 9.5j line → `[x]`)
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Run the FULL analyzer test project**

Run: `dotnet test ProjectCeres.Analyzers.Tests`
Expected: **all pass** — the existing analyzer/generator tests (40 before this stage) plus the 4 new code-fix tests. If any pre-existing test went red, root-cause it now (do not defer).

- [ ] **Step 2: Update ADR-0077**

In `docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md`, add a short note (≤3 sentences) that the family gained **code-fix providers** in 9.5j: CER004 (safe rewrite to the injected time provider, gated on a `_timeProvider` field) and CER001 (placeholder rewrite to `BeginPreAuthUserScopeAsync`). Record the **CER010 re-scope**: no code-fix ships for CER010 because a format-valid invented ticket would pass the rule while corrupting the audit trail — strictly worse than the warning. Mirror the phrasing of the 9.5i generator note already in the doc.

- [ ] **Step 3: Update docs/testing.md**

In the analyzer-test section of `docs/testing.md`, add the code-fix test harness (`CSharpCodeFixVerifier<TAnalyzer, TCodeFix>` with `VerifyCodeFixAsync` / `VerifyNoFixAsync`) and the three CER004 cases (field-present fix, field-absent no-fix, subtype fix) + the CER001 placeholder-3-errors case. Note the negative `VerifyNoFixAsync` case is the ship-gate that pins the CER004 safety boundary.

- [ ] **Step 4: Update the roadmap line to [x]**

In `docs/roadmap-phase-three.md`, rewrite the 9.5j checklist line (currently `- [ ] 9.5j — code-fix providers for CER001 / CER004 / CER010 ...`) to reflect the two-provider delivery + the CER010-no-fix re-scope, and tick it `[x]`. Keep the receiving-stage references intact. Also update the 9.5j rationale-table row (the row keyed `| 9.5j |`) so its "no mechanical fix" list now reads CER002/CER003/CER005/CER006/CER020 **and CER010** (CER010 moved from fixable to no-fix).

- [ ] **Step 5: Update CHANGELOG.md**

Under `## [Unreleased]` → `### Phase 3` → `#### Added`, in the existing **Analyzers** module group, add:
```
- Code-fix providers (Stage 9.5j): CER004 offers a one-click rewrite to `_timeProvider.GetUtcNow().UtcDateTime` (only when a `_timeProvider` field is present); CER001 offers a placeholder rewrite to `BeginPreAuthUserScopeAsync(userId, ct)`. CER010 ships no fix — a format-valid invented ticket would defeat the audit-trail rule.
```
(Match the existing module-group wording; do not invent a new module name.)

- [ ] **Step 6: Commit the docs**

```bash
git add docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md docs/testing.md docs/roadmap-phase-three.md CHANGELOG.md
git commit -m "docs(9.5j): code-fix providers shipped + closed [x] — CER010 re-scoped to no-fix; sync ADR/testing/changelog"
```

---

## Self-review checklist (run after all tasks)

- [ ] Both providers carry `[ExportCodeFixProvider(LanguageNames.CSharp, Name=...)]` + `[Shared]` and override `GetFixAllProvider()`.
- [ ] CER004 registers **no** fix when no `_timeProvider` field — proven by `Offers_No_Fix_When_TimeProvider_Field_Absent`.
- [ ] CER001 placeholder leaves the receiver `_db.Database` untouched and the fixed-code compiler errors are pinned (not dropped).
- [ ] No `AnalyzerReleases.Unshipped.md` change, no `.editorconfig` change.
- [ ] `dotnet test ProjectCeres.Analyzers.Tests` green (44 tests: 40 prior + 4 new).
- [ ] ADR-0077, testing.md, roadmap (ticked `[x]`), CHANGELOG all synced in the docs commit.
