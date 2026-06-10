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
