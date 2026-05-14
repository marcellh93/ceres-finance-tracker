using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Email;
using ProjectCeres.Tests.Integration;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection("IntegrationTests")]
public sealed class EmailComposerTests
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailComposerTests(AuthTestWebApplicationFactory factory) => _factory = factory;

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
    public void Renders_all_nine_templates_en_and_es(EmailTemplateKey key, string culture)
    {
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        // Two placeholders is enough for any template (LockoutUnlock and EmailChangeRevokeOld
        // are the only ones with {1}); extras are ignored by String.Format.
        var msg = composer.Compose(key, new CultureInfo(culture), "https://example.invalid/x", "203.0.113.5");

        msg.Subject.Should().NotBeNullOrWhiteSpace();
        msg.BodyText.Should().NotBeNullOrWhiteSpace();
        msg.BodyHtml.Should().NotBeNullOrWhiteSpace();
        msg.Subject.Should().NotContain("{0}").And.NotContain("{1}");
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
    public void All_resx_keys_present_in_both_cultures()
    {
        // Reads both .resx files via ResourceManager and asserts the key set is identical.
        // Catches a translator dropping a key on a future edit.
        // ResourcesPath "Resources" + type-fullname-minus-assembly "EmailsResource"
        // = manifest base name "ProjectCeres.Resources.EmailsResource".
        var en = new System.Resources.ResourceManager(
            "ProjectCeres.Resources.EmailsResource",
            typeof(global::ProjectCeres.EmailsResource).Assembly);

        // Enumerate via ResourceSet for both cultures.
        var enSet = en.GetResourceSet(new CultureInfo("en"), createIfNotExists: true, tryParents: false);
        var esSet = en.GetResourceSet(new CultureInfo("es"), createIfNotExists: true, tryParents: false);
        enSet.Should().NotBeNull("Emails.en.resx must compile into the assembly");
        esSet.Should().NotBeNull("Emails.es.resx must compile into the assembly");

        var enKeys = enSet!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).OrderBy(k => k).ToList();
        var esKeys = esSet!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).OrderBy(k => k).ToList();

        enKeys.Should().BeEquivalentTo(esKeys);
        enKeys.Should().HaveCount(27, "9 templates × 3 keys each");
    }
}
