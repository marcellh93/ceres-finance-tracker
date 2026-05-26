using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpAnalyzerVerifier<ProjectCeres.Analyzers.PreAuthScopeTransactionAnalyzer>;

namespace ProjectCeres.Analyzers.Tests;

public class CER001_PreAuthScopeTransactionAnalyzerTests
{
    [Fact]
    public async Task Fires_When_PreAuthScope_Class_Uses_BeginTransactionAsync()
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
    public class FakeDb
    {
        public FakeDatabase Database => new();
    }

    public class FakeDatabase
    {
        public Task BeginTransactionAsync() => Task.CompletedTask;
    }

    [ProjectCeres.Analyzers.Annotations.PreAuthScope]
    public class MarkedClass
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await {|#0:_db.Database.BeginTransactionAsync()|};
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER001_PreAuthScopeTransactionType)
            .WithLocation(0)
            .WithArguments("MarkedClass");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFire_When_Class_Without_PreAuthScope_Uses_BeginTransactionAsync()
    {
        var source = @"
using System.Threading.Tasks;

namespace Test
{
    public class FakeDb { public FakeDatabase Database => new(); }
    public class FakeDatabase { public Task BeginTransactionAsync() => Task.CompletedTask; }

    public class PlainClass
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await _db.Database.BeginTransactionAsync();
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFire_When_File_Is_PreAuthRlsScope_cs()
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
        public async Task Bar() => await _db.Database.BeginTransactionAsync();
    }
}
";
        var test = new Verifier.Test();
        test.TestState.Sources.Add(("PreAuthRlsScope.cs", source));
        await test.RunAsync();
    }

    [Fact]
    public async Task NoFire_When_PreAuthScope_Class_Uses_BeginPreAuthUserScopeAsync()
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
    public class MarkedClass
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await _db.BeginPreAuthUserScopeAsync(Guid.NewGuid());
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
