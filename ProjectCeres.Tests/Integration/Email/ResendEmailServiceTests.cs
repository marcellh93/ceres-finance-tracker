using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Moq;
using ProjectCeres.Common.Email;
using Resend;
using Xunit;
using ResendEmailMessage = Resend.EmailMessage;

namespace ProjectCeres.Tests.Integration.Email;

public sealed class ResendEmailServiceTests
{
    [Fact]
    public async Task Retries_on_500_three_times_then_throws()
    {
        // Pins spec § Reliability: 5xx is transient → 3 attempts with exponential backoff
        // (250ms / 1s / 4s). The wrapper must throw after the third attempt; without this
        // guarantee a flaky upstream would silently drop transactional mail.
        var resend = new Mock<IResend>();
        var calls = 0;
        resend.Setup(r => r.EmailSendAsync(It.IsAny<ResendEmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<ResendEmailMessage, CancellationToken>((_, _) =>
            {
                calls++;
                throw new ResendException(
                    HttpStatusCode.InternalServerError,
                    ErrorType.InternalServerError,
                    "transient");
            });

        var svc = new ResendEmailService(
            resend.Object,
            Options.Create(new EmailOptions
            {
                Resend = new ResendOptions { FromAddress = "x@example.invalid", FromName = "Ceres" }
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ResendEmailService>.Instance);

        var msg = new ProjectCeres.Common.Email.EmailMessage(
            BuildRecipient("test@example.invalid"),
            "Subject", "<p>html</p>", "text");

        await FluentActions.Awaiting(() => svc.SendAsync(msg, CancellationToken.None))
            .Should().ThrowAsync<Exception>();

        calls.Should().Be(3);
    }

    [Fact]
    public async Task Does_not_retry_on_400()
    {
        // Pins spec § Reliability: 4xx is permanent → no retries, rethrow on the first
        // attempt. A retry on 400 would waste quota and delay surfacing the bug.
        var resend = new Mock<IResend>();
        var calls = 0;
        resend.Setup(r => r.EmailSendAsync(It.IsAny<ResendEmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<ResendEmailMessage, CancellationToken>((_, _) =>
            {
                calls++;
                throw new ResendException(
                    HttpStatusCode.BadRequest,
                    ErrorType.ValidationError,
                    "bad request");
            });

        var svc = new ResendEmailService(
            resend.Object,
            Options.Create(new EmailOptions
            {
                Resend = new ResendOptions { FromAddress = "x@example.invalid", FromName = "Ceres" }
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ResendEmailService>.Instance);

        var msg = new ProjectCeres.Common.Email.EmailMessage(
            BuildRecipient("test@example.invalid"),
            "Subject", "<p>html</p>", "text");

        await FluentActions.Awaiting(() => svc.SendAsync(msg, CancellationToken.None))
            .Should().ThrowAsync<Exception>();

        calls.Should().Be(1);
    }

    [Fact]
    public void Production_without_api_key_throws_at_startup()
    {
        // Pins spec § Configuration: Production must fail loud when Email:Resend:ApiKey
        // is unbound — silently falling back to LogOnlyEmailService would drop every
        // transactional email in prod with no visible signal.
        //
        // NOTE: this uses a bare WebApplicationFactory<Program> (NOT the project's
        // AuthTestWebApplicationFactory), because the test factory overrides config and
        // services in ways that would mask the production codepath under test.
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("Email:Resend:ApiKey", "");
            });

        FluentActions.Invoking(() => _ = factory.Services)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Email:Resend:ApiKey is required in Production*");
    }

    [Fact]
    public void Production_without_public_base_url_throws_at_startup()
    {
        // Stage 12.6: PublicBaseUrl backs the support-thread link in outgoing email;
        // unset in Production it falls back to the request Host header, which an
        // operator could influence. Must fail loud at boot like SupportAddress. The
        // earlier api-key + support-address guards are satisfied so this one is what
        // fires (guard order: api key → support address → public base url).
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("Email:Resend:ApiKey", "re_test_key");
                b.UseSetting("Email:SupportAddress", "support@example.com");
                b.UseSetting("Email:PublicBaseUrl", "");
            });

        FluentActions.Invoking(() => _ = factory.Services)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Email:PublicBaseUrl is required in Production*");
    }

    /// <summary>
    /// EmailRecipient.FromVerifiedUser is internal to the production assembly and the test
    /// assembly has no InternalsVisibleTo, so we reach the factory via reflection. Same
    /// shape the resolver uses; the value object is intentionally not constructible from
    /// arbitrary strings to preserve the security-model § Layer 2 recipient-lock rule.
    /// </summary>
    private static EmailRecipient BuildRecipient(string email)
    {
        var factory = typeof(EmailRecipient).GetMethod(
            "FromVerifiedUser",
            BindingFlags.Static | BindingFlags.NonPublic);
        factory.Should().NotBeNull("EmailRecipient.FromVerifiedUser must exist");
        return (EmailRecipient)factory!.Invoke(null, [email])!;
    }
}
