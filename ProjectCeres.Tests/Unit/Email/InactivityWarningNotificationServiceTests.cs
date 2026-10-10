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
    private readonly Mock<ILanguageResolver> _language = new();
    private readonly EmailRecipient _recipient;
    private readonly InactivityWarningNotificationService _sut;

    public InactivityWarningNotificationServiceTests()
    {
        // EmailRecipient.FromVerifiedUser is internal; reach it via reflection.
        var factory = typeof(EmailRecipient).GetMethod(
            "FromVerifiedUser", BindingFlags.Static | BindingFlags.NonPublic);
        factory.Should().NotBeNull();
        _recipient = (EmailRecipient)factory!.Invoke(null, new object[] { "inactive@test.invalid" })!;

        _resolver.Setup(r => r.ResolveAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_recipient);
        _composer.Setup(c => c.Compose(EmailTemplateKey.InactivityWarning, It.IsAny<CultureInfo>(), It.IsAny<object[]>()))
            .Returns(new EmailMessage(_recipient, "subject", "<p>html</p>", "text"));

        _language.Setup(l => l.ResolveForUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CultureInfo("en"));

        _sut = new InactivityWarningNotificationService(
            _composer.Object, _email.Object, _resolver.Object, _language.Object,
            NullLogger<InactivityWarningNotificationService>.Instance);
    }

    [Fact]
    public async Task Returns_true_and_sends_on_success()
    {
        EmailMessage? sent = null;
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => sent = m)
            .Returns(Task.CompletedTask);

        var ok = await _sut.NotifyAsync(_userId, 18, CancellationToken.None);

        ok.Should().BeTrue();
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        sent!.To.Should().Be(_recipient);
        _composer.Verify(c => c.Compose(EmailTemplateKey.InactivityWarning, It.IsAny<CultureInfo>(),
            It.Is<object[]>(a => a.Length == 1 && (string)a[0] == "18")), Times.Once);
    }

    [Fact]
    public async Task Returns_false_and_swallows_on_send_failure()
    {
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var ok = await _sut.NotifyAsync(_userId, 18, CancellationToken.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task Composes_in_the_recipients_culture_not_the_thread_default()
    {
        _language.Setup(l => l.ResolveForUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CultureInfo("es"));
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.NotifyAsync(_userId, 18, CancellationToken.None);

        _composer.Verify(c => c.Compose(EmailTemplateKey.InactivityWarning,
            It.Is<CultureInfo>(ci => ci.Name == "es"), It.IsAny<object[]>()), Times.Once);
    }
}
