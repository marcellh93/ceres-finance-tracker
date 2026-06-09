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
