using System.Globalization;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProjectCeres.Common.Email;

namespace ProjectCeres.Tests.Unit.Email;

public class InactivityWarningNotificationServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IEmailComposer> _composer = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IEmailRecipientResolver> _resolver = new();
    private readonly InactivityWarningNotificationService _sut;

    public InactivityWarningNotificationServiceTests()
    {
        // EmailRecipient.FromVerifiedUser is internal; reach it via reflection.
        var factory = typeof(EmailRecipient).GetMethod(
            "FromVerifiedUser", BindingFlags.Static | BindingFlags.NonPublic);
        factory.Should().NotBeNull();
        var recipient = (EmailRecipient)factory!.Invoke(null, new object[] { "inactive@test.invalid" })!;

        _resolver.Setup(r => r.ResolveAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(recipient);
        _composer.Setup(c => c.Compose(EmailTemplateKey.InactivityWarning, It.IsAny<CultureInfo>(), It.IsAny<object[]>()))
            .Returns(new EmailMessage(recipient, "subject", "<p>html</p>", "text"));

        _sut = new InactivityWarningNotificationService(
            _composer.Object, _email.Object, _resolver.Object,
            NullLogger<InactivityWarningNotificationService>.Instance);
    }

    [Fact]
    public async Task Returns_true_and_sends_on_success()
    {
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ok = await _sut.NotifyAsync(_userId, 18, CancellationToken.None);

        ok.Should().BeTrue();
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Returns_false_and_swallows_on_send_failure()
    {
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var ok = await _sut.NotifyAsync(_userId, 18, CancellationToken.None);

        ok.Should().BeFalse();
    }
}
