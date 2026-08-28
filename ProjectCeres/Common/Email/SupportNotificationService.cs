using System.Globalization;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Owns every support-ticket notification, extracted from SupportApiController so the
/// three operator-notify call sites (create, user-reply) and the two user-notify call
/// sites (operator reply, Solved) share one place. Stage 12.6.
///
/// Two recipient directions, two factories:
/// operator mail resolves via <see cref="ISupportRecipientResolver"/>
/// (<see cref="EmailRecipient.ForConfiguredSupportAddress"/>); user mail resolves via
/// <see cref="IEmailRecipientResolver"/> (<see cref="EmailRecipient.FromVerifiedUser"/>,
/// server-reading the ticket owner's ApplicationUser.Email — never a request payload).
///
/// The operator-reply email carries agent-authored free text to the user's inbox. It
/// flows through the composer's HTML-encoded arg path, which escapes it — pinned by the
/// mandatory sanitisation test (spec § Notifications H2).
///
/// Failures are logged and swallowed: the ticket/message is already committed, and a mail
/// outage must not surface to the caller as a failed operation.
/// </summary>
public interface ISupportNotificationService
{
    Task NotifyOperatorOfNewTicketAsync(SupportTicket ticket, string fromDisplay, CancellationToken ct);

    Task NotifyOperatorOfUserReplyAsync(SupportTicket ticket, string fromDisplay, string replyBody, CancellationToken ct);

    Task NotifyUserOfAgentReplyAsync(SupportTicket ticket, string agentBody, string supportUrlBase, CancellationToken ct);

    Task NotifyUserSolvedAsync(SupportTicket ticket, string supportUrlBase, CancellationToken ct);
}

public sealed class SupportNotificationService(
    IEmailComposer composer,
    IEmailService email,
    ISupportRecipientResolver supportRecipient,
    IEmailRecipientResolver userRecipient,
    ILogger<SupportNotificationService> logger) : ISupportNotificationService
{
    public async Task NotifyOperatorOfNewTicketAsync(SupportTicket ticket, string fromDisplay, CancellationToken ct)
    {
        var recipient = supportRecipient.Resolve();
        if (recipient is null)
        {
            logger.LogInformation(
                "Support ticket {TicketId} filed; no Email:SupportAddress configured, so no notification was sent.",
                ticket.Id);
            return;
        }

        try
        {
            var message = composer.Compose(
                EmailTemplateKey.SupportTicketReceived,
                CultureInfo.CurrentUICulture,
                ticket.Priority.ToString(),
                ticket.Subject,
                fromDisplay,
                ticket.Id.ToString(),
                FirstMessageBody(ticket)) with { To = recipient };
            await email.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Support ticket {TicketId} was filed but the operator notification email failed.", ticket.Id);
        }
    }

    public async Task NotifyOperatorOfUserReplyAsync(SupportTicket ticket, string fromDisplay, string replyBody, CancellationToken ct)
    {
        var recipient = supportRecipient.Resolve();
        if (recipient is null)
        {
            logger.LogInformation(
                "User replied on ticket {TicketId}; no Email:SupportAddress configured, so no notification was sent.",
                ticket.Id);
            return;
        }

        try
        {
            var message = composer.Compose(
                EmailTemplateKey.SupportTicketReceived,
                CultureInfo.CurrentUICulture,
                ticket.Priority.ToString(),
                ticket.Subject,
                fromDisplay,
                ticket.Id.ToString(),
                replyBody) with { To = recipient };
            await email.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "User replied on ticket {TicketId} but the operator notification email failed.", ticket.Id);
        }
    }

    public async Task NotifyUserOfAgentReplyAsync(SupportTicket ticket, string agentBody, string supportUrlBase, CancellationToken ct)
    {
        try
        {
            var recipient = await userRecipient.ResolveAsync(ticket.UserId, ct);
            var message = composer.Compose(
                EmailTemplateKey.SupportReplyToUser,
                CultureInfo.CurrentUICulture,
                ticket.Subject,
                agentBody,
                ThreadUrl(supportUrlBase, ticket.Id)) with { To = recipient };
            await email.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Operator replied on ticket {TicketId} but the user notification email failed.", ticket.Id);
        }
    }

    public async Task NotifyUserSolvedAsync(SupportTicket ticket, string supportUrlBase, CancellationToken ct)
    {
        try
        {
            var recipient = await userRecipient.ResolveAsync(ticket.UserId, ct);
            var message = composer.Compose(
                EmailTemplateKey.SupportTicketSolved,
                CultureInfo.CurrentUICulture,
                ticket.Subject,
                ThreadUrl(supportUrlBase, ticket.Id)) with { To = recipient };
            await email.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ticket {TicketId} was marked solved but the user notification email failed.", ticket.Id);
        }
    }

    private static string ThreadUrl(string supportUrlBase, Guid ticketId) =>
        $"{supportUrlBase.TrimEnd('/')}/support/{ticketId}";

    // The new-ticket notification quotes the opening message. On a freshly created ticket
    // that is the only message; CreateAsync populates Messages before returning.
    private static string FirstMessageBody(SupportTicket t) =>
        t.Messages.OrderBy(m => m.CreatedAt).FirstOrDefault()?.Body ?? "";
}
