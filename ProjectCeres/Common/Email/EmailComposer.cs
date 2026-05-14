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
            var subjectTpl = _localizer[$"{key}.Subject"].Value;
            var bodyTextTpl = _localizer[$"{key}.BodyText"].Value;
            var bodyHtmlTpl = _localizer[$"{key}.BodyHtml"].Value;

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
}
