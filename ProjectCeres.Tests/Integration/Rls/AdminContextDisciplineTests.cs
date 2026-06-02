using System.Reflection;
using FluentAssertions;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 9.5b. Every production type that reaches the BYPASSRLS AdminDbContext must declare
/// it with [RequiresAdminContext], so a reviewer can find every RLS-bypass by grepping one
/// attribute. A reflection pass catches ctor/field injection; a source-text pass catches the
/// shapes reflection can't see — method-parameter injection (middleware InvokeAsync) and
/// service-locator resolution (GetRequiredService&lt;AdminDbContext&gt;). [PreAuthScope] and
/// [RlsBypassJustified] are NOT accepted as substitutes: they mark code where RLS is still
/// active (EF-filter stripped, GUC set), the opposite of AdminDbContext's role-level bypass.
/// </summary>
public class AdminContextDisciplineTests
{
    // Files that reference AdminDbContext but are not "consumers" needing the marker:
    // the type definition, the DI composition root, and dev-only tooling outside the
    // request/auth surface. Repo-relative, forward-slashed.
    private static readonly HashSet<string> InfrastructureAllowList = new(StringComparer.Ordinal)
    {
        "ProjectCeres/Data/AdminDbContext.cs",        // the type itself
        "ProjectCeres/Program.cs",                    // AddDbContext<AdminDbContext> composition root
        "ProjectCeres/Tools/SeedDevUser.cs",          // dev-seed tooling, not a request-path consumer
    };

    [Fact]
    public void Every_AdminDbContext_consumer_carries_RequiresAdminContext()
    {
        var repoRoot = FindRepoRoot();
        var projectCeres = Path.Combine(repoRoot, "ProjectCeres");

        var offenders = ScanAdminContextConsumers(projectCeres)
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Where(rel => !InfrastructureAllowList.Contains(rel))
            .Where(rel => !FileDeclaresRequiresAdminContext(Path.Combine(repoRoot, rel)))
            .ToList();

        offenders.Should().BeEmpty(
            "every type that injects/resolves AdminDbContext (BYPASSRLS) must carry " +
            "[RequiresAdminContext] so all RLS bypasses are greppable by one attribute. " +
            "[PreAuthScope]/[RlsBypassJustified] are not substitutes — they mark RLS-still-active code.");
    }

    [Fact]
    public void Reflection_pass_catches_ctor_and_field_AdminDbContext_injection()
    {
        // Belt-and-suspenders for the injection shapes reflection CAN see (ctor param, field).
        // The source-text pass above is the authority for method-param + service-locator shapes.
        var offenders = typeof(AppDbContext).Assembly.GetTypes()
            .Where(t => !IsCompilerGenerated(t))   // async state machines hoist method params into fields
            .Where(InjectsAdminContextByCtorOrField)
            .Where(t => !HasMarker(t))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty(
            "ctor/field AdminDbContext injection must carry [RequiresAdminContext]");
    }

    [Fact]
    public void Meta_an_undecorated_AdminDbContext_consumer_is_detected_by_reflection()
    {
        // Proves the reflection detector actually fires on the input it must catch.
        InjectsAdminContextByCtorOrField(typeof(FixtureWithUndecoratedAdmin)).Should().BeTrue();
        HasMarker(typeof(FixtureWithUndecoratedAdmin)).Should().BeFalse();
    }

    [Fact]
    public void Meta_the_source_scan_matches_every_real_injection_shape()
    {
        // Proves the text scan recognises all three shapes via representative lines, so a
        // future shape change can't silently empty the offender set.
        IsAdminContextInjectionLine("    AdminDbContext db,").Should().BeTrue("ctor/method param");
        IsAdminContextInjectionLine("    private readonly AdminDbContext _admin;").Should().BeTrue("field");
        IsAdminContextInjectionLine(
            "var db = ctx.HttpContext.RequestServices.GetRequiredService<AdminDbContext>();")
            .Should().BeTrue("service locator");
        IsAdminContextInjectionLine("    // Uses AdminDbContext because ...").Should().BeFalse("comment");
        IsAdminContextInjectionLine("builder.Services.AddDbContext<AdminDbContext>((sp, options) =>")
            .Should().BeFalse("DI registration is not a consumer");
        IsAdminContextInjectionLine("public sealed class AdminDbContext : AppDbContext")
            .Should().BeFalse("the type definition is not a consumer");
    }

    // ── scan helpers ──────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ScanAdminContextConsumers(string projectCeresRoot)
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectCeresRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/Migrations/")) continue;
            foreach (var raw in File.ReadAllLines(file))
            {
                if (IsAdminContextInjectionLine(raw))
                {
                    hits.Add(file);
                    break; // one hit per file is enough
                }
            }
        }
        return hits;
    }

    /// <summary>
    /// True when a line injects or resolves AdminDbContext as a dependency: a typed
    /// declaration (ctor/method param or field) or a service-locator resolution. Excludes
    /// comments, the type definition, and DI registration.
    /// </summary>
    private static bool IsAdminContextInjectionLine(string raw)
    {
        var line = raw.TrimStart();
        if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("///")) return false;
        if (line.Contains("class AdminDbContext")) return false;          // the type definition
        if (line.Contains("AddDbContext<AdminDbContext>")) return false;  // DI registration, not a consumer

        if (line.Contains("GetRequiredService<AdminDbContext>") ||
            line.Contains("GetService<AdminDbContext>"))
            return true;

        // Typed declaration: "AdminDbContext <identifier>" (param or field), e.g.
        // "AdminDbContext db," / "private readonly AdminDbContext _admin;".
        return System.Text.RegularExpressions.Regex.IsMatch(line, @"\bAdminDbContext\s+[A-Za-z_]");
    }

    private static bool FileDeclaresRequiresAdminContext(string absolutePath) =>
        File.ReadAllLines(absolutePath)
            .Select(l => l.TrimStart())
            .Any(l => l.StartsWith("[RequiresAdminContext]"));

    // ── reflection helpers ────────────────────────────────────────────────────

    private static bool InjectsAdminContextByCtorOrField(Type t) =>
        t.GetConstructors().SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(AdminDbContext))   // EXACT type — AdminDbContext : AppDbContext
        || t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Any(f => f.FieldType == typeof(AdminDbContext));

    private static bool HasMarker(Type t) =>
        t.GetCustomAttribute<RequiresAdminContextAttribute>() is not null
        || t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(m => m.GetCustomAttribute<RequiresAdminContextAttribute>() is not null);

    private static bool IsCompilerGenerated(Type t) =>
        t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null
        || t.Name.Contains('<'); // closures / async state machines: <Method>d__N, <>c

    // Reflection-fixture: a ctor param of AdminDbContext with no marker — the input the
    // reflection detector must catch. Ctor form (not a field) avoids an unused-field warning.
    private sealed class FixtureWithUndecoratedAdmin(AdminDbContext db)
    {
        public AdminDbContext Db { get; } = db;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProjectCeres.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (ProjectCeres.sln).");
    }
}
