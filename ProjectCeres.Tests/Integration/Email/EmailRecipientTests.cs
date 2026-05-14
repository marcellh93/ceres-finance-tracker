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
        var asm = typeof(EmailRecipient).Assembly;
        var override_ = typeof(EmailRecipient).GetMethod(
            "OverrideForEmailChange",
            BindingFlags.Static | BindingFlags.NonPublic);
        override_.Should().NotBeNull();

        // We grep the on-disk source rather than reflecting over IL: the IL doesn't carry
        // a stable indication of which type contains a call to an internal static method
        // without a full MethodBody walk, and this test is meant to be cheap.
        var projectRoot = AppContext.BaseDirectory;
        // Walk up until we find ProjectCeres/Common/Authentication/EmailChangeService.cs
        var dir = new DirectoryInfo(projectRoot);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ProjectCeres", "Common", "Authentication")))
            dir = dir.Parent;
        dir.Should().NotBeNull("test must be able to locate the ProjectCeres source tree");

        var authDir = Path.Combine(dir!.FullName, "ProjectCeres", "Common", "Authentication");
        var matches = Directory.EnumerateFiles(authDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => File.ReadAllText(f).Contains("OverrideForEmailChange", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        matches.Should().BeEquivalentTo(new[] { "EmailChangeService.cs" });
    }
}
