using Microsoft.Extensions.Options;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Resolves the operator-configured support mailbox.
///
/// Exists for the same reason <see cref="EmailRecipientResolver"/> does: to give
/// <see cref="EmailRecipient.ForConfiguredSupportAddress"/> exactly one call site, so
/// "which addresses can this app send to?" stays answerable by reading a short, fixed
/// list of files. Pinned by the architecture tests in
/// <c>ProjectCeres.Tests/Integration/Email/EmailRecipientTests.cs</c>.
///
/// It also keeps email configuration out of the controller's dependency surface — the
/// support controller asks for a recipient, not for an address it then has to validate.
/// </summary>
public interface ISupportRecipientResolver
{
    /// <summary>Null when <c>Email:SupportAddress</c> is unset — non-Production only.</summary>
    EmailRecipient? Resolve();
}

public sealed class SupportRecipientResolver(IOptions<EmailOptions> options) : ISupportRecipientResolver
{
    public EmailRecipient? Resolve() => EmailRecipient.ForConfiguredSupportAddress(options);
}
