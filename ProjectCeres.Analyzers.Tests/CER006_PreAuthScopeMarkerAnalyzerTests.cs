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
            .WithArguments("UnmarkedService");
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
