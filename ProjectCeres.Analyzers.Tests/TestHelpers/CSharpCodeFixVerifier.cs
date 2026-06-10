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
