using Microsoft.Extensions.Options;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Compile-time enforcement of the security-model § Email Security Rules § Layer 2
/// recipient-lock rule: every outgoing email's `To:` address is constructed via this
/// type, never from a raw string. The legitimate factories are
/// <see cref="FromVerifiedUser"/> (server-resolved from <c>ApplicationUser.Email</c>),
/// <see cref="OverrideForEmailChange"/> (server-resolved from <c>EmailChangeToken</c> —
/// only the email-change flow uses this), and
/// <see cref="ForConfiguredSupportAddress"/> (operator configuration, Stage 12.5).
/// </summary>
public sealed record EmailRecipient
{
    public string Address { get; }

    private EmailRecipient(string address) => Address = address;

    /// <summary>Placeholder used by IEmailComposer (Task 2) before the resolver populates the To slot.</summary>
    internal static readonly EmailRecipient None = new("");

    internal static EmailRecipient FromVerifiedUser(string email) => new(email);

    /// <summary>
    /// Email-change flow only. The user's current <c>ApplicationUser.Email</c> is the new
    /// address; notifications to the OLD address are sent via this factory. The old
    /// address is read server-side from <c>EmailChangeToken</c>, never from a request
    /// payload. EmailRecipientTests pins that only EmailChangeService calls this method.
    /// </summary>
    internal static EmailRecipient OverrideForEmailChange(string oldEmail) => new(oldEmail);

    /// <summary>
    /// The operator-configured support mailbox (Stage 12.5) — the third trusted source of
    /// a recipient, after the verified user and the email-change token.
    ///
    /// Takes the options object rather than a string on purpose. A <c>string</c> parameter
    /// would compile just as happily with <c>request.Email</c> passed in, and the factory's
    /// safety would rest on every caller passing the right value. Taking the configuration
    /// source itself means there is no address a caller can substitute.
    ///
    /// Returns null when unconfigured, which only happens outside Production — Program.cs
    /// refuses to boot without <c>Email:SupportAddress</c> there.
    /// </summary>
    internal static EmailRecipient? ForConfiguredSupportAddress(IOptions<EmailOptions> options)
    {
        var address = options.Value.SupportAddress;
        return string.IsNullOrWhiteSpace(address) ? null : new(address);
    }

    public override string ToString() => Address;
}
