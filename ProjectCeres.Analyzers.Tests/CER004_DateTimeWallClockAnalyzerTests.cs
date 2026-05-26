using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpAnalyzerVerifier<ProjectCeres.Analyzers.DateTimeWallClockAnalyzer>;

namespace ProjectCeres.Analyzers.Tests;

public class CER004_DateTimeWallClockAnalyzerTests
{
    private const string AllowsWallClockPreamble = @"
namespace ProjectCeres.Analyzers.Annotations
{
    [System.AttributeUsage(
        System.AttributeTargets.Method | System.AttributeTargets.Property | System.AttributeTargets.Constructor,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class AllowsWallClockAttribute : System.Attribute
    {
        public AllowsWallClockAttribute(string reason) { }
    }
}
";

    // (a) Service method body with DateTime.UtcNow → fires
    [Fact]
    public async Task Fires_When_ServiceMethod_Uses_DateTimeUtcNow()
    {
        var source = AllowsWallClockPreamble + @"
namespace ProjectCeres.Services
{
    public class SomeService
    {
        public void DoWork()
        {
            var now = {|#0:System.DateTime.UtcNow|};
        }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER004_DateTimeWallClock)
            .WithLocation(0)
            .WithArguments("DateTime.UtcNow");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    // (a variant) DateTime.Now also fires
    [Fact]
    public async Task Fires_When_ServiceMethod_Uses_DateTimeNow()
    {
        var source = AllowsWallClockPreamble + @"
namespace ProjectCeres.Services
{
    public class SomeService
    {
        public void DoWork()
        {
            var now = {|#0:System.DateTime.Now|};
        }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER004_DateTimeWallClock)
            .WithLocation(0)
            .WithArguments("DateTime.Now");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    // (b) Property initialiser in ProjectCeres.Models namespace → no fire
    [Fact]
    public async Task NoFire_When_PropertyInitialiser_In_ModelsNamespace()
    {
        var source = AllowsWallClockPreamble + @"
namespace ProjectCeres.Models
{
    public class SomeEntity
    {
        public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // (d) Method carrying [AllowsWallClock("reason")] → no fire
    [Fact]
    public async Task NoFire_When_Method_Has_AllowsWallClock()
    {
        var source = AllowsWallClockPreamble + @"
namespace ProjectCeres.Services
{
    using ProjectCeres.Analyzers.Annotations;

    public class SomeService
    {
        [AllowsWallClock(""view model computed property — TimeProvider cannot be injected"")]
        public System.DateTime GetNow() => System.DateTime.UtcNow;
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // (e) Constructor in non-Models namespace → fires unless attributed
    [Fact]
    public async Task Fires_When_Constructor_In_NonModels_Namespace_Uses_UtcNow()
    {
        var source = AllowsWallClockPreamble + @"
namespace ProjectCeres.Services
{
    public class SomeService
    {
        public System.DateTime StartedAt { get; }

        public SomeService()
        {
            StartedAt = {|#0:System.DateTime.UtcNow|};
        }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER004_DateTimeWallClock)
            .WithLocation(0)
            .WithArguments("DateTime.UtcNow");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    // Constructor with [AllowsWallClock] → no fire
    [Fact]
    public async Task NoFire_When_Constructor_Has_AllowsWallClock()
    {
        var source = AllowsWallClockPreamble + @"
namespace ProjectCeres.Services
{
    using ProjectCeres.Analyzers.Annotations;

    public class SomeService
    {
        public System.DateTime StartedAt { get; }

        [AllowsWallClock(""static initialiser — TimeProvider not yet available"")]
        public SomeService()
        {
            StartedAt = System.DateTime.UtcNow;
        }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
