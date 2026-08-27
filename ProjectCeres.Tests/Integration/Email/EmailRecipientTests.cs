using System.Reflection;
using FluentAssertions;
using ProjectCeres.Common.Email;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

public sealed class EmailRecipientTests
{
    [Fact]
    public void EmailMessage_has_no_public_string_To_constructor()
    {
        // Pins the compile-time recipient lock from spec § Architecture / EmailRecipient.
        // A regression here would mean a service could re-introduce caller-supplied
        // To: addresses, breaking the security-model § Layer 2 recipient-lock rule.
        var publicCtors = typeof(EmailMessage).GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        foreach (var ctor in publicCtors)
        {
            var firstParam = ctor.GetParameters().FirstOrDefault();
            firstParam.Should().NotBeNull();
            firstParam!.ParameterType.Should().Be(typeof(EmailRecipient),
                "the first constructor parameter of EmailMessage must be EmailRecipient, " +
                "not string — otherwise services can bypass the recipient lock.");
        }
    }

    [Fact]
    public void EmailRecipient_OverrideForEmailChange_is_called_only_by_EmailChangeService()
    {
        // Pins the spec § Architecture audit story: only EmailChangeService is allowed
        // to call OverrideForEmailChange. A new caller is either the email-change flow
        // (in which case rename or extend the factory + update this test) or a security
        // regression that must be reverted.
        var factoryMethod = typeof(EmailRecipient).GetMethod(
            "OverrideForEmailChange",
            BindingFlags.Static | BindingFlags.NonPublic);
        factoryMethod.Should().NotBeNull();

        // We grep the on-disk source rather than reflecting over IL: the IL doesn't carry
        // a stable indication of which type contains a call to an internal static method
        // without a full MethodBody walk, and this test is meant to be cheap.
        //
        // Walk up from the test bin directory to the repository root (identified by
        // ProjectCeres.sln) — matches ArchitectureTests.FindRepoRoot. Anchoring on the
        // .sln file is robust under dotnet-test working-dir overrides.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProjectCeres.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("test must be able to locate the repo root (ProjectCeres.sln)");

        var authDir = Path.Combine(dir!.FullName, "ProjectCeres", "Common", "Authentication");
        Directory.Exists(authDir).Should().BeTrue("auth services directory must exist");

        // Strip pure-comment lines before scanning so that xmldoc references such as
        // `<see cref="EmailRecipient.OverrideForEmailChange"/>` in unrelated files do
        // not produce a false positive — matches ArchitectureTests comment-strip rules.
        var matches = Directory.EnumerateFiles(authDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => FileContainsCallSite(f, "OverrideForEmailChange"))
            .Select(Path.GetFileName)
            .ToList();

        matches.Should().BeEquivalentTo(["EmailChangeService.cs"]);
    }

    [Fact]
    public void EmailRecipient_is_constructed_only_at_known_call_sites()
    {
        // The recipient lock's real guarantee is not "there are exactly N factories" — it
        // is that the set of construction sites is CLOSED and enumerable, so "what
        // addresses can this app send to?" is answerable by reading a short list of files.
        //
        // This scans all of ProjectCeres/ rather than one directory. The older
        // OverrideForEmailChange test below greps Common/Authentication/ only, which means
        // a factory call from a new namespace (Controllers/, Admin/, a future
        // Services/Notifications/) would pass unnoticed simply because the scan pointed
        // somewhere else. Same idiom as ArchitectureTests'
        // IgnoreQueryFilters_only_appears_in_documented_exception_paths.
        var allowed = new[]
        {
            "ProjectCeres/Common/Email/EmailRecipient.cs",             // the declarations
            "ProjectCeres/Common/Email/EmailRecipientResolver.cs",     // FromVerifiedUser
            "ProjectCeres/Common/Email/SupportRecipientResolver.cs",   // ForConfiguredSupportAddress
            "ProjectCeres/Common/Email/EmailComposer.cs",              // EmailRecipient.None
            "ProjectCeres/Common/Authentication/EmailChangeService.cs" // OverrideForEmailChange
        };

        var tokens = new[]
        {
            "FromVerifiedUser", "OverrideForEmailChange",
            "ForConfiguredSupportAddress", "EmailRecipient.None",
        };

        var root = FindRepoRoot();
        var projectDir = Path.Combine(root, "ProjectCeres");

        var found = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => tokens.Any(t => FileContainsCallSite(f, t)))
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        found.Should().BeEquivalentTo(allowed,
            "every EmailRecipient construction site must be on the allow-list above. A new "
            + "entry is either a genuinely new server-resolved recipient source (extend the "
            + "list AND the security-model § Email Security Rules Layer 2 carve-out in the "
            + "same commit) or a recipient-lock regression.");
    }

    [Fact]
    public void EmailRecipient_cannot_be_built_from_a_bare_string()
    {
        // The mutation that would silently void every grep above: a public constructor, or
        // an implicit string conversion, either of which makes `To = "anything"` legal
        // project-wide without touching a single file the scans look at.
        typeof(EmailRecipient)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Should().OnlyContain(c => c.IsPrivate,
                "EmailRecipient's constructor must stay private — the factories are the only way in.");

        typeof(EmailRecipient)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .Should().BeEmpty(
                "a string->EmailRecipient conversion would bypass the recipient lock entirely.");
    }

    /// <summary>Repo root, located by the solution file — matches ArchitectureTests.FindRepoRoot.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProjectCeres.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("test must be able to locate the repo root (ProjectCeres.sln)");
        return dir!.FullName;
    }

    /// <summary>
    /// Returns true if any non-comment line in the file contains the given token.
    /// Drops lines whose first non-whitespace characters are "//" (line comment) or
    /// "*" (block-comment continuation / xmldoc).
    /// </summary>
    private static bool FileContainsCallSite(string path, string token)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("//")) continue;        // line comment
            if (line.StartsWith("*"))  continue;         // block comment / xmldoc continuation
            if (line.Contains(token, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
