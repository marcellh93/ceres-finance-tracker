using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpAnalyzerVerifier<ProjectCeres.Analyzers.RlsBypassJustifiedTicketFormatAnalyzer>;

namespace ProjectCeres.Analyzers.Tests;

public class CER010_RlsBypassJustifiedTicketFormatAnalyzerTests
{
    private const string AttributePreamble = @"
namespace ProjectCeres.Analyzers.Annotations
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class RlsBypassJustifiedAttribute : System.Attribute
    {
        public RlsBypassJustifiedAttribute(string ticket) { Ticket = ticket; }
        public string Ticket { get; }
    }
}
";

    // (a) CER-NNNN format → no fire
    [Fact]
    public async Task NoFire_When_Ticket_Is_CER_Format()
    {
        var source = AttributePreamble + @"
namespace Test
{
    using ProjectCeres.Analyzers.Annotations;
    public class SomeService
    {
        [RlsBypassJustified(""CER-1234"")]
        public void DoWork() { }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // (b) TICKET-NNNN format → no fire
    [Fact]
    public async Task NoFire_When_Ticket_Is_TICKET_Format()
    {
        var source = AttributePreamble + @"
namespace Test
{
    using ProjectCeres.Analyzers.Annotations;
    public class SomeService
    {
        [RlsBypassJustified(""TICKET-99"")]
        public void DoWork() { }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // (c) ADR-NNNN format → no fire
    [Fact]
    public async Task NoFire_When_Ticket_Is_ADR_Format()
    {
        var source = AttributePreamble + @"
namespace Test
{
    using ProjectCeres.Analyzers.Annotations;
    public class SomeService
    {
        [RlsBypassJustified(""ADR-0042"")]
        public void DoWork() { }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // (d) "temp" → fires
    [Fact]
    public async Task Fires_When_Ticket_Is_Temp()
    {
        var source = AttributePreamble + @"
namespace Test
{
    using ProjectCeres.Analyzers.Annotations;
    public class SomeService
    {
        [{|#0:RlsBypassJustified(""temp"")|}]
        public void DoWork() { }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER010_RlsBypassJustifiedTicketFormat)
            .WithLocation(0)
            .WithArguments("temp");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    // (e) "" (empty) → fires
    [Fact]
    public async Task Fires_When_Ticket_Is_Empty()
    {
        var source = AttributePreamble + @"
namespace Test
{
    using ProjectCeres.Analyzers.Annotations;
    public class SomeService
    {
        [{|#0:RlsBypassJustified("""")|}]
        public void DoWork() { }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER010_RlsBypassJustifiedTicketFormat)
            .WithLocation(0)
            .WithArguments("");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    // (f) "CER-NNNN" (letters instead of digits) → fires
    [Fact]
    public async Task Fires_When_Ticket_Has_No_Digits()
    {
        var source = AttributePreamble + @"
namespace Test
{
    using ProjectCeres.Analyzers.Annotations;
    public class SomeService
    {
        [{|#0:RlsBypassJustified(""CER-NNNN"")|}]
        public void DoWork() { }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER010_RlsBypassJustifiedTicketFormat)
            .WithLocation(0)
            .WithArguments("CER-NNNN");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }
}
