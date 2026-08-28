using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// The user side of a support ticket's conversation. A reply is not just a new row —
/// it is also the signal that drives <see cref="SupportTicketStateMachine.ResolveUserReply"/>,
/// so posting one and moving the ticket's status happen together or not at all.
/// </summary>
public sealed class SupportMessageService : ISupportMessageService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly TimeProvider _timeProvider;

    public SupportMessageService(AppDbContext db, ICurrentUserAccessor user, TimeProvider timeProvider)
    {
        _db = db;
        _user = user;
        _timeProvider = timeProvider;
    }

    public async Task<SupportMessage> PostUserReplyAsync(Guid ticketId, string body, CancellationToken ct = default)
    {
        var ticket = await _db.SupportTickets.Owned(_user)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new InvalidOperationException($"Support ticket {ticketId} not found.");

        var result = SupportTicketStateMachine.ResolveUserReply(ticket.Status);
        if (!result.Allowed)
            throw new InvalidOperationException(result.Reason);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var message = new SupportMessage
        {
            Id = Guid.NewGuid(),
            UserId = _user.UserId,
            SupportTicketId = ticket.Id,
            AuthorRole = SupportMessageAuthor.User,
            Body = body.Trim(),
            CreatedAt = now,
        };

        _db.SupportMessages.Add(message);
        ticket.Status = result.NewStatus;
        ticket.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return message;
    }

    public async Task<SupportTicket?> GetThreadAsync(Guid ticketId, CancellationToken ct = default) =>
        await _db.SupportTickets.Owned(_user)
            .AsNoTracking()
            .Include(t => t.Messages.OrderBy(m => m.CreatedAt))
            .ThenInclude(m => m.Attachments)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);
}
