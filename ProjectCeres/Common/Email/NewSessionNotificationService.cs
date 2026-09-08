using System.Globalization;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Sends the "new sign-in from a network we haven't seen" security alert (Stage 12.5.3).
/// The novelty decision (is this IP new for the user? is it the first-ever login?) is made
/// by the caller at the session-creation path; this service only composes + delivers.
///
/// Recipient resolves via <see cref="IEmailRecipientResolver"/> — the server reads the
/// user's own ApplicationUser.Email, never a request payload. Failures are logged and
/// swallowed: the login has already succeeded and a mail outage must not fail it.
///
/// No opt-out is consulted: a new-sign-in security alert is conventionally not opt-out, and
/// the notification-preferences surface it would live in does not exist yet (deferred — see
/// roadmap § Notification preferences).
/// </summary>
public interface INewSessionNotificationService
{
    Task NotifyNewSessionAsync(Guid userId, string ipAddress, string deviceSummary, DateTime signedInAtUtc, CancellationToken ct);
}

public sealed class NewSessionNotificationService(
    IEmailComposer composer,
    IEmailService email,
    IEmailRecipientResolver userRecipient,
    ILogger<NewSessionNotificationService> logger) : INewSessionNotificationService
{
    public async Task NotifyNewSessionAsync(
        Guid userId, string ipAddress, string deviceSummary, DateTime signedInAtUtc, CancellationToken ct)
    {
        try
        {
            var recipient = await userRecipient.ResolveAsync(userId, ct);
            var message = composer.Compose(
                EmailTemplateKey.NewSessionAlert,
                CultureInfo.CurrentUICulture,
                ipAddress,
                deviceSummary,
                signedInAtUtc.ToString("u", CultureInfo.InvariantCulture)) with { To = recipient };
            await email.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            // Swallow: the sign-in already succeeded. Do not leak the failure to the login flow.
            logger.LogError(ex,
                "New-session alert email failed for user {UserId} from IP {Ip}.", userId, ipAddress);
        }
    }
}
