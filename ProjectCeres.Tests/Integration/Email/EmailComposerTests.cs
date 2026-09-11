using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Email;
using ProjectCeres.Tests.Integration;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection("IntegrationParallel3")]
public sealed class EmailComposerTests : IntegrationTestBase<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailComposerTests(AuthTestWebApplicationFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    [Theory]
    [InlineData(EmailTemplateKey.PasswordResetRequest, "en")]
    [InlineData(EmailTemplateKey.PasswordResetRequest, "es")]
    [InlineData(EmailTemplateKey.PasswordChanged, "en")]
    [InlineData(EmailTemplateKey.PasswordChanged, "es")]
    [InlineData(EmailTemplateKey.PasswordResetCancelledEmailChange, "en")]
    [InlineData(EmailTemplateKey.PasswordResetCancelledEmailChange, "es")]
    [InlineData(EmailTemplateKey.EmailChangeVerifyNew, "en")]
    [InlineData(EmailTemplateKey.EmailChangeVerifyNew, "es")]
    [InlineData(EmailTemplateKey.EmailChangeRevokeOld, "en")]
    [InlineData(EmailTemplateKey.EmailChangeRevokeOld, "es")]
    [InlineData(EmailTemplateKey.EmailChangeConfirmed, "en")]
    [InlineData(EmailTemplateKey.EmailChangeConfirmed, "es")]
    [InlineData(EmailTemplateKey.EmailChangeConfirmedToOld, "en")]
    [InlineData(EmailTemplateKey.EmailChangeConfirmedToOld, "es")]
    [InlineData(EmailTemplateKey.EmailChangeRevokeNotificationToOld, "en")]
    [InlineData(EmailTemplateKey.EmailChangeRevokeNotificationToOld, "es")]
    [InlineData(EmailTemplateKey.LockoutUnlock, "en")]
    [InlineData(EmailTemplateKey.LockoutUnlock, "es")]
    [InlineData(EmailTemplateKey.RegistrationConfirmation, "en")]
    [InlineData(EmailTemplateKey.RegistrationConfirmation, "es")]
    [InlineData(EmailTemplateKey.TotpEnrolled, "en")]
    [InlineData(EmailTemplateKey.TotpEnrolled, "es")]
    [InlineData(EmailTemplateKey.TotpDisabled, "en")]
    [InlineData(EmailTemplateKey.TotpDisabled, "es")]
    [InlineData(EmailTemplateKey.BackupCodesRegenerated, "en")]
    [InlineData(EmailTemplateKey.BackupCodesRegenerated, "es")]
    [InlineData(EmailTemplateKey.NewSessionAlert, "en")]
    [InlineData(EmailTemplateKey.NewSessionAlert, "es")]
    public void Renders_all_templates_en_and_es(EmailTemplateKey key, string culture)
    {
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        // Three placeholders covers every template (NewSessionAlert uses {0}{1}{2}); extras
        // are ignored by String.Format, but a MISSING arg would throw — so pass the max.
        var msg = composer.Compose(key, new CultureInfo(culture),
            "https://example.invalid/x", "203.0.113.5", "2026-09-08 00:00:00Z");

        msg.Subject.Should().NotBeNullOrWhiteSpace();
        msg.BodyText.Should().NotBeNullOrWhiteSpace();
        msg.BodyHtml.Should().NotBeNullOrWhiteSpace();
        msg.Subject.Should().NotContain("{0}").And.NotContain("{1}");
        msg.Subject.Should().NotBe($"{key}.Subject", "localizer must load the actual resx value, not fall back to returning the key");
        msg.BodyText.Should().NotBe($"{key}.BodyText");
        msg.BodyHtml.Should().NotBe($"{key}.BodyHtml");
    }

    [Fact]
    public void Strips_cr_lf_in_subject()
    {
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        // PasswordResetRequest subject doesn't include placeholders, but a future template
        // might inject user-supplied data — defense in depth.
        var msg = composer.Compose(EmailTemplateKey.PasswordResetRequest, new CultureInfo("en"),
            "https://example.invalid/\r\nBcc: attacker@evil.invalid");

        msg.Subject.Should().NotContain("\r").And.NotContain("\n");
    }

    [Fact]
    public void Html_encodes_args_in_html_body()
    {
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var msg = composer.Compose(EmailTemplateKey.LockoutUnlock, new CultureInfo("en"),
            "https://example.invalid/unlock",
            "<script>alert(1)</script>");

        msg.BodyHtml.Should().NotContain("<script>alert(1)</script>");
        msg.BodyHtml.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void LockoutUnlock_args_map_to_correct_slots()
    {
        // {0} = unlock URL (appears in href). {1} = IP address (appears as a separate strong/text).
        // A swap at the call site would put the URL in the IP slot. Pin it.
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var msg = composer.Compose(
            EmailTemplateKey.LockoutUnlock,
            new CultureInfo("en"),
            "https://example.invalid/unlock-token-XYZ",
            "203.0.113.5");

        // URL must appear in the HTML body's href attribute.
        msg.BodyHtml.Should().Contain("href=\"https://example.invalid/unlock-token-XYZ\"");
        // IP must appear in the body but NOT inside an href.
        msg.BodyHtml.Should().Contain("203.0.113.5");
        msg.BodyHtml.Should().NotContain("href=\"203.0.113.5\"");
        msg.BodyHtml.Should().NotContain("href=\"<strong>");
    }

    [Fact]
    public void TotpEnrolled_args_map_to_correct_slots()
    {
        // {0} = timestamp (UTC), {1} = IP address. Pin slot order to catch a swap at the call site.
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var msg = composer.Compose(
            EmailTemplateKey.TotpEnrolled,
            new CultureInfo("en"),
            "2026-06-14 12:00",
            "203.0.113.5");

        // Timestamp ({0}) must appear before IP ({1}) in the plain-text body.
        var timestampPos = msg.BodyText.IndexOf("2026-06-14 12:00", StringComparison.Ordinal);
        var ipPos = msg.BodyText.IndexOf("203.0.113.5", StringComparison.Ordinal);
        timestampPos.Should().BeGreaterThan(-1, "timestamp slot {0} must appear in BodyText");
        ipPos.Should().BeGreaterThan(-1, "IP slot {1} must appear in BodyText");
        timestampPos.Should().BeLessThan(ipPos, "timestamp ({0}) must precede IP ({1}) in the body");

        // Both values must also appear in the HTML body.
        msg.BodyHtml.Should().Contain("2026-06-14 12:00");
        msg.BodyHtml.Should().Contain("203.0.113.5");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void SupportReplyToUser_renders_subject_reply_body_and_link(string culture)
    {
        // {0} = subject, {1} = agent reply body, {2} = /support/<id> thread link.
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var msg = composer.Compose(
            EmailTemplateKey.SupportReplyToUser,
            new CultureInfo(culture),
            "Login broken on mobile",
            "We shipped a fix; please retry.",
            "https://ceres.invalid/support/abc123");

        msg.Subject.Should().NotBeNullOrWhiteSpace().And.NotContain("{0}");
        msg.BodyText.Should().Contain("Login broken on mobile");
        msg.BodyText.Should().Contain("We shipped a fix; please retry.");
        msg.BodyText.Should().Contain("https://ceres.invalid/support/abc123");
        msg.BodyHtml.Should().Contain("We shipped a fix; please retry.");
        msg.BodyHtml.Should().Contain("href=\"https://ceres.invalid/support/abc123\"");
    }

    [Fact]
    public void SupportReplyToUser_escapes_agent_authored_body_in_html()
    {
        // Spec § Notifications H2 — MANDATORY sanitisation negative test. The reply email
        // carries agent-authored free text to a USER's inbox. A body containing HTML /
        // template-breaking characters MUST be HTML-escaped in the rendered BodyHtml, so a
        // hostile or careless agent cannot inject markup/script into the user's mail client.
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var msg = composer.Compose(
            EmailTemplateKey.SupportReplyToUser,
            new CultureInfo("en"),
            "Subject line",
            "<script>alert(1)</script>",
            "https://ceres.invalid/support/abc123");

        msg.BodyHtml.Should().NotContain("<script>alert(1)</script>");
        msg.BodyHtml.Should().Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void SupportTicketSolved_renders_subject_and_link(string culture)
    {
        // {0} = subject, {1} = /support/<id> thread link.
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var msg = composer.Compose(
            EmailTemplateKey.SupportTicketSolved,
            new CultureInfo(culture),
            "Export fails",
            "https://ceres.invalid/support/xyz789");

        msg.Subject.Should().Contain("Export fails");
        msg.BodyText.Should().Contain("https://ceres.invalid/support/xyz789");
        msg.BodyHtml.Should().Contain("href=\"https://ceres.invalid/support/xyz789\"");
    }

    [Fact]
    public void All_resx_keys_present_in_both_cultures()
    {
        // Reads both .resx files via ResourceManager and asserts the key set is identical.
        // Catches a translator dropping a key on a future edit.
        // ResourcesPath "Resources" + type-fullname-minus-assembly "EmailsResource"
        // = manifest base name "ProjectCeres.Resources.EmailsResource".
        var en = new System.Resources.ResourceManager(
            "ProjectCeres.Resources.EmailsResource",
            typeof(EmailsResource).Assembly);

        // Enumerate via ResourceSet for both cultures.
        var enSet = en.GetResourceSet(new CultureInfo("en"), createIfNotExists: true, tryParents: false);
        var esSet = en.GetResourceSet(new CultureInfo("es"), createIfNotExists: true, tryParents: false);
        enSet.Should().NotBeNull("Emails.en.resx must compile into the assembly");
        esSet.Should().NotBeNull("Emails.es.resx must compile into the assembly");

        var enKeys = enSet!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).OrderBy(k => k).ToList();
        var esKeys = esSet!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).OrderBy(k => k).ToList();

        enKeys.Should().BeEquivalentTo(esKeys);
        enKeys.Should().HaveCount(51, "17 templates × 3 keys each (NewSessionAlert added Stage 12.5.3)");
    }
}
