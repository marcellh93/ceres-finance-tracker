using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectCeres.Common.Email;
using Xunit;

namespace ProjectCeres.Tests.Unit.Email;

public class FileSinkEmailServiceTests
{
    // EmailRecipient.FromVerifiedUser is internal; reach it via reflection (same pattern as ResendEmailServiceTests).
    private static EmailRecipient BuildRecipient(string email)
    {
        var factory = typeof(EmailRecipient).GetMethod(
            "FromVerifiedUser",
            BindingFlags.Static | BindingFlags.NonPublic);
        factory.Should().NotBeNull("EmailRecipient.FromVerifiedUser must exist");
        return (EmailRecipient)factory!.Invoke(null, new object[] { email })!;
    }

    private static EmailMessage SampleMessage() => new(
        To: BuildRecipient("user@example.test"),
        Subject: "Confirm your email",
        BodyHtml: "<a href=\"https://localhost/email-verify#token=abc\">verify</a>",
        BodyText: "Verify: https://localhost/email-verify#token=abc");

    [Fact]
    public async Task SendAsync_writes_one_json_file_with_the_expected_shape()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ceres-emailsink-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var sut = new FileSinkEmailService(dir, NullLogger<FileSinkEmailService>.Instance);

            await sut.SendAsync(SampleMessage(), CancellationToken.None);

            var files = Directory.GetFiles(dir, "*.json");
            files.Should().ContainSingle();

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(files[0]));
            var root = doc.RootElement;
            root.GetProperty("to").GetString().Should().Be("user@example.test");
            root.GetProperty("subject").GetString().Should().Be("Confirm your email");
            root.GetProperty("bodyText").GetString().Should().Contain("/email-verify#token=abc");
            root.GetProperty("bodyHtml").GetString().Should().Contain("/email-verify#token=abc");
            root.TryGetProperty("sentAtUtc", out _).Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
