using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpAnalyzerVerifier<ProjectCeres.Analyzers.DbContextInControllerAnalyzer>;

namespace ProjectCeres.Analyzers.Tests;

// CER007 — ADR-0017 puts business logic in services; architecture.md lists
// "call DbContext directly" under what controllers may NOT do. The 2026-08-15
// audit found 11 controllers bypassing the service layer, one of which
// (SessionsApiController) wrote two tables with no transaction and returned
// 500 on a duplicate IP block.
public class CER007_DbContextInControllerAnalyzerTests
{
    private const string DbStub = @"
namespace ProjectCeres.Data { public class AppDbContext { } }
";

    // 1. Controller injecting AppDbContext -> fires
    [Fact]
    public async Task Fires_When_Controller_Injects_AppDbContext()
    {
        var source = DbStub + @"
namespace Test
{
    using ProjectCeres.Data;
    public class AccountsApiController
    {
        public AccountsApiController({|#0:AppDbContext db|}) { }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER007_DbContextInController)
            .WithLocation(0)
            .WithArguments("AccountsApiController");

        await RunAsync("/proj/ProjectCeres/Controllers/Api/AccountsApiController.cs", source, expected);
    }

    // 2. Primary-constructor form (the shape every Ceres API controller uses)
    [Fact]
    public async Task Fires_For_Primary_Constructor_Parameter()
    {
        var source = DbStub + @"
namespace Test
{
    using ProjectCeres.Data;
    public class DashboardApiController({|#0:AppDbContext db|}) { }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER007_DbContextInController)
            .WithLocation(0)
            .WithArguments("DashboardApiController");

        await RunAsync("/proj/ProjectCeres/Controllers/Api/DashboardApiController.cs", source, expected);
    }

    // 3. A service injecting AppDbContext -> no fire (that IS the allowed layer)
    [Fact]
    public async Task Does_Not_Fire_In_A_Service()
    {
        var source = DbStub + @"
namespace Test
{
    using ProjectCeres.Data;
    public class AccountService(AppDbContext db) { }
}
";
        await RunAsync("/proj/ProjectCeres/Services/AccountService.cs", source);
    }

    // 4. Controller injecting only a service -> no fire (the target shape)
    [Fact]
    public async Task Does_Not_Fire_When_Controller_Injects_Only_Services()
    {
        var source = @"
namespace Test
{
    public interface ISessionService { }
    public class SessionsApiController(ISessionService sessions) { }
}
";
        await RunAsync("/proj/ProjectCeres/Controllers/Api/SessionsApiController.cs", source);
    }

    // 5. Migrations are excluded — they legitimately reference the context
    [Fact]
    public async Task Does_Not_Fire_In_Migrations()
    {
        var source = DbStub + @"
namespace Test
{
    using ProjectCeres.Data;
    public class SomeMigration(AppDbContext db) { }
}
";
        await RunAsync("/proj/ProjectCeres/Migrations/20260101_X.cs", source);
    }

    // 6. A same-named type outside the Controllers tree -> no fire
    [Fact]
    public async Task Does_Not_Fire_Outside_Controllers_Directory()
    {
        var source = DbStub + @"
namespace Test
{
    using ProjectCeres.Data;
    public class BackgroundJob(AppDbContext db) { }
}
";
        await RunAsync("/proj/ProjectCeres/Common/BackgroundJob.cs", source);
    }

    private static Task RunAsync(string path, string source, params DiagnosticResult[] expected)
    {
        var test = new Verifier.Test();
        test.TestState.Sources.Add((path, source));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }
}
