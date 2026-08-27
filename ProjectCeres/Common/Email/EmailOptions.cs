namespace ProjectCeres.Common.Email;

/// <summary>
/// Root configuration record for the email subsystem. Bound from the
/// <c>Email</c> configuration section in <c>Program.cs</c>.
/// </summary>
public sealed class EmailOptions
{
    public ResendOptions Resend { get; init; } = new();

    /// <summary>
    /// Where support-ticket notifications are sent (Stage 12.5). Required in Production —
    /// Program.cs refuses to boot without it, because a silently-unset address means
    /// tickets are filed and nobody is ever told. Outside Production an unset value simply
    /// skips the notification, so the test suite and a fresh clone run without configuring
    /// a mailbox.
    /// </summary>
    public string? SupportAddress { get; init; }
}

/// <summary>
/// Resend-provider configuration. The API key is sourced from environment
/// variables (<c>Email__Resend__ApiKey</c>) in Production and from
/// <c>dotnet user-secrets</c> in Development. Defaults target the
/// <c>onboarding@resend.dev</c> sandbox sender so the first Stage 8 commit can
/// be smoke-tested without a verified domain.
/// </summary>
public sealed class ResendOptions
{
    public string? ApiKey { get; init; }
    public string FromAddress { get; init; } = "onboarding@resend.dev";
    public string FromName { get; init; } = "Ceres";
    public string? WebhookSecret { get; init; }
}
