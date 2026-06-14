using System;
using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Localization;

namespace ProjectCeres.Common.Email;

public interface IEmailComposer
{
    /// <summary>
    /// Renders the resx-backed template for the given key + culture. Caller is
    /// responsible for setting <see cref="EmailMessage.To"/> via the recipient resolver.
    /// </summary>
    EmailMessage Compose(EmailTemplateKey key, CultureInfo culture, params object[] args);
}

public sealed class EmailComposer : IEmailComposer
{
    private readonly IStringLocalizer<EmailsResource> _localizer;

    public EmailComposer(IStringLocalizer<EmailsResource> localizer) => _localizer = localizer;

    public EmailMessage Compose(EmailTemplateKey key, CultureInfo culture, params object[] args)
    {
        var prior = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            var (subjectKey, bodyTextKey, bodyHtmlKey) = KeysFor(key);

            var subjectTpl = _localizer[subjectKey].Value;
            var bodyTextTpl = _localizer[bodyTextKey].Value;
            var bodyHtmlTpl = _localizer[bodyHtmlKey].Value;

            // HTML body sanitizes interpolations. Subject + plain-text bodies don't
            // HTML-encode (would mangle URLs) but strip CR/LF as a header-injection
            // defense in depth.
            var htmlArgs = args.Select(a => (object)HtmlEncoder.Default.Encode(a?.ToString() ?? "")).ToArray();
            var textArgs = args.Select(a => (object)(a?.ToString() ?? "").Replace("\r", "").Replace("\n", " ")).ToArray();

            var subject = string.Format(culture, subjectTpl, textArgs).Replace("\r", "").Replace("\n", "");
            var bodyText = string.Format(culture, bodyTextTpl, textArgs);
            var bodyHtml = string.Format(culture, bodyHtmlTpl, htmlArgs);

            return new EmailMessage(EmailRecipient.None, subject, bodyHtml, bodyText);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prior;
        }
    }

    // Generated EmailKeys.* constants (from EmailKeysGenerator) — a resx-key rename breaks this switch at compile time.
    private static (string Subject, string BodyText, string BodyHtml) KeysFor(EmailTemplateKey key) => key switch
    {
        EmailTemplateKey.PasswordResetRequest =>
            (EmailKeys.PasswordResetRequest.Subject, EmailKeys.PasswordResetRequest.BodyText, EmailKeys.PasswordResetRequest.BodyHtml),
        EmailTemplateKey.PasswordChanged =>
            (EmailKeys.PasswordChanged.Subject, EmailKeys.PasswordChanged.BodyText, EmailKeys.PasswordChanged.BodyHtml),
        EmailTemplateKey.PasswordResetCancelledEmailChange =>
            (EmailKeys.PasswordResetCancelledEmailChange.Subject, EmailKeys.PasswordResetCancelledEmailChange.BodyText, EmailKeys.PasswordResetCancelledEmailChange.BodyHtml),
        EmailTemplateKey.EmailChangeVerifyNew =>
            (EmailKeys.EmailChangeVerifyNew.Subject, EmailKeys.EmailChangeVerifyNew.BodyText, EmailKeys.EmailChangeVerifyNew.BodyHtml),
        EmailTemplateKey.EmailChangeRevokeOld =>
            (EmailKeys.EmailChangeRevokeOld.Subject, EmailKeys.EmailChangeRevokeOld.BodyText, EmailKeys.EmailChangeRevokeOld.BodyHtml),
        EmailTemplateKey.EmailChangeConfirmed =>
            (EmailKeys.EmailChangeConfirmed.Subject, EmailKeys.EmailChangeConfirmed.BodyText, EmailKeys.EmailChangeConfirmed.BodyHtml),
        EmailTemplateKey.EmailChangeConfirmedToOld =>
            (EmailKeys.EmailChangeConfirmedToOld.Subject, EmailKeys.EmailChangeConfirmedToOld.BodyText, EmailKeys.EmailChangeConfirmedToOld.BodyHtml),
        EmailTemplateKey.EmailChangeRevokeNotificationToOld =>
            (EmailKeys.EmailChangeRevokeNotificationToOld.Subject, EmailKeys.EmailChangeRevokeNotificationToOld.BodyText, EmailKeys.EmailChangeRevokeNotificationToOld.BodyHtml),
        EmailTemplateKey.LockoutUnlock =>
            (EmailKeys.LockoutUnlock.Subject, EmailKeys.LockoutUnlock.BodyText, EmailKeys.LockoutUnlock.BodyHtml),
        EmailTemplateKey.RegistrationConfirmation =>
            (EmailKeys.RegistrationConfirmation.Subject, EmailKeys.RegistrationConfirmation.BodyText, EmailKeys.RegistrationConfirmation.BodyHtml),
        EmailTemplateKey.TotpEnrolled =>
            (EmailKeys.TotpEnrolled.Subject, EmailKeys.TotpEnrolled.BodyText, EmailKeys.TotpEnrolled.BodyHtml),
        EmailTemplateKey.TotpDisabled =>
            (EmailKeys.TotpDisabled.Subject, EmailKeys.TotpDisabled.BodyText, EmailKeys.TotpDisabled.BodyHtml),
        EmailTemplateKey.BackupCodesRegenerated =>
            (EmailKeys.BackupCodesRegenerated.Subject, EmailKeys.BackupCodesRegenerated.BodyText, EmailKeys.BackupCodesRegenerated.BodyHtml),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
