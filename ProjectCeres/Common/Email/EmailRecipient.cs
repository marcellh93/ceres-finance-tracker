namespace ProjectCeres.Common.Email;

/// <summary>
/// Compile-time enforcement of the security-model § Email Security Rules § Layer 2
/// recipient-lock rule: every outgoing email's `To:` address is constructed via this
/// type, never from a raw string. The only legitimate factories are
/// <see cref="FromVerifiedUser"/> (server-resolved from <c>ApplicationUser.Email</c>)
/// and <see cref="OverrideForEmailChange"/> (server-resolved from
/// <c>EmailChangeToken</c> — only the email-change flow uses this).
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

    public override string ToString() => Address;
}
