using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpAnalyzerVerifier<ProjectCeres.Analyzers.IgnoreQueryFiltersOnUserOwnedAnalyzer>;

namespace ProjectCeres.Analyzers.Tests;

public class CER002_IgnoreQueryFiltersOnUserOwnedAnalyzerTests
{
    // The inline type definitions below mirror the full names the analyzer checks:
    //   ProjectCeres.Common.IUserOwned
    //   ProjectCeres.Data.AppDbContext
    // The test runner compiles these as part of the test source.

    private const string Preamble = @"
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ProjectCeres.Common
{
    public interface IUserOwned { }
}

namespace ProjectCeres.Data
{
    public class AppDbContext
    {
        public IQueryable<T> Set<T>() => throw new System.NotImplementedException();
    }
}

namespace ProjectCeres.Analyzers.Annotations
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class RlsBypassJustifiedAttribute : System.Attribute
    {
        public RlsBypassJustifiedAttribute(string ticket) { }
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
    }
}
";

    [Fact]
    public async Task Fires_When_AppDbContext_IUserOwned_IgnoreQueryFilters()
    {
        var source = Preamble + @"
namespace ProjectCeres.Services
{
    using ProjectCeres.Common;
    using ProjectCeres.Data;

    public class UserItem : IUserOwned { }

    public class SomeService
    {
        private readonly AppDbContext _db;
        public SomeService(AppDbContext db) { _db = db; }

        public void DoWork()
        {
            var q = {|#0:_db.Set<UserItem>().IgnoreQueryFilters()|};
        }
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER002_IgnoreQueryFiltersOnUserOwned)
            .WithLocation(0)
            .WithArguments("UserItem");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFire_When_AdminDbContext_IUserOwned_IgnoreQueryFilters()
    {
        var source = Preamble + @"
namespace ProjectCeres.Data
{
    public class AdminDbContext
    {
        public IQueryable<T> Set<T>() => throw new System.NotImplementedException();
    }
}

namespace ProjectCeres.Services
{
    using ProjectCeres.Common;
    using ProjectCeres.Data;

    public class UserItem : IUserOwned { }

    public class AdminService
    {
        private readonly AdminDbContext _adminDb;
        public AdminService(AdminDbContext adminDb) { _adminDb = adminDb; }

        public void DoWork()
        {
            var q = _adminDb.Set<UserItem>().IgnoreQueryFilters();
        }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFire_When_AppDbContext_NonUserOwned_IgnoreQueryFilters()
    {
        var source = Preamble + @"
namespace ProjectCeres.Services
{
    using ProjectCeres.Data;

    public class SystemItem { }

    public class SomeService
    {
        private readonly AppDbContext _db;
        public SomeService(AppDbContext db) { _db = db; }

        public void DoWork()
        {
            var q = _db.Set<SystemItem>().IgnoreQueryFilters();
        }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFire_When_Method_Has_RlsBypassJustified()
    {
        var source = Preamble + @"
namespace ProjectCeres.Services
{
    using ProjectCeres.Common;
    using ProjectCeres.Data;
    using ProjectCeres.Analyzers.Annotations;

    public class UserItem : IUserOwned { }

    public class SomeService
    {
        private readonly AppDbContext _db;
        public SomeService(AppDbContext db) { _db = db; }

        [RlsBypassJustified(""CER-1234"")]
        public void DoWorkWithJustification()
        {
            var q = _db.Set<UserItem>().IgnoreQueryFilters();
        }
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
