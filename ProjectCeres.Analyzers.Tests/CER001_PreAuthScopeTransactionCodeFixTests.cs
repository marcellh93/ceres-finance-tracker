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
        // Errors that SURVIVE the fix (the placeholder to-do list), pinned to the
        // harness's actual diagnostic set: wrong receiver method + two undeclared identifiers.
        test.FixedState.ExpectedDiagnostics.Add(DiagnosticResult.CompilerError("CS1061").WithSpan(19, 55, 19, 81).WithArguments("Test.FakeDatabase", "BeginPreAuthUserScopeAsync"));
        test.FixedState.ExpectedDiagnostics.Add(DiagnosticResult.CompilerError("CS0103").WithSpan(19, 82, 19, 88).WithArguments("userId"));
        test.FixedState.ExpectedDiagnostics.Add(DiagnosticResult.CompilerError("CS0103").WithSpan(19, 90, 19, 92).WithArguments("ct"));
        await test.RunAsync();
    }
}
