using System.Globalization;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Sends the Stage 13.b "your account has been inactive" warning email.
/// Recipient resolves via <see cref="IEmailRecipientResolver"/> — the server reads the
/// user's own ApplicationUser.Email, never a request payload. Failures are logged and swallowed.
///
/// Returns false on a swallowed failure so the sweep does NOT stamp InactivityWarningSentAt —
/// the user stays eligible to retry. No opt-out consulted — a legal-basis storage-limitation
/// notice; if a notification-preferences surface is ever built, this notice stays exempt.
/// </summary>
public interface IInactivityWarningNotificationService
{
    Task<bool> NotifyAsync(Guid userId, int monthsUntilAnonymise, CancellationToken ct);
}

public sealed class InactivityWarningNotificationService(
    IEmailComposer composer,
    IEmailService email,
    IEmailRecipientResolver userRecipient,
    ILogger<InactivityWarningNotificationService> logger) : IInactivityWarningNotificationService
{
    public async Task<bool> NotifyAsync(Guid userId, int monthsUntilAnonymise, CancellationToken ct)
    {
        try
        {
            var recipient = await userRecipient.ResolveAsync(userId, ct);
            var message = composer.Compose(
                EmailTemplateKey.InactivityWarning,
                CultureInfo.CurrentUICulture,
                monthsUntilAnonymise.ToString(CultureInfo.InvariantCulture)) with { To = recipient };
            await email.SendAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Inactivity warning email failed for user {UserId}.", userId);
            return false;
        }
    }
}
