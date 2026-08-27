using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Support tickets for the current user.
///
/// Two rules live here because nothing else can hold them. Closing is final, so the only
/// legal transition a user can drive is Open → Closed. And a follow-up must continue a
/// CLOSED ticket belonging to the SAME user — PostgreSQL checks the self-reference through
/// a referential-integrity trigger that RLS is not applied to, so the database would
/// otherwise accept a pointer at a stranger's ticket.
/// </summary>
public sealed class SupportTicketService : ISupportTicketService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly TimeProvider _timeProvider;

    public SupportTicketService(AppDbContext db, ICurrentUserAccessor user, TimeProvider timeProvider)
    {
        _db = db;
        _user = user;
        _timeProvider = timeProvider;
    }

    public async Task<SupportTicket> CreateAsync(
        string subject,
        string message,
        SupportTicketPriority priority,
        Guid? precedingTicketId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("Subject is required.");
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("Message is required.");

        if (precedingTicketId is { } precedingId)
        {
            // Owned() applies the per-user filter, so a stranger's ticket reads as absent.
            var preceding = await _db.SupportTickets.Owned(_user)
                .FirstOrDefaultAsync(t => t.Id == precedingId, ct)
                ?? throw new InvalidOperationException($"Support ticket {precedingId} not found.");

            if (preceding.Status != SupportTicketStatus.Closed)
                throw new InvalidOperationException(
                    "A follow-up continues a closed ticket. This one is still open — add to it instead.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            UserId = _user.UserId,
            Subject = subject.Trim(),
            Message = message.Trim(),
            Status = SupportTicketStatus.Open,
            Priority = priority,
            PrecedingTicketId = precedingTicketId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<IReadOnlyList<SupportTicket>> ListOwnAsync(CancellationToken ct = default) =>
        await _db.SupportTickets.Owned(_user)
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<SupportTicket?> GetOwnAsync(Guid ticketId, CancellationToken ct = default) =>
        await _db.SupportTickets.Owned(_user)
            .AsNoTracking()
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);

    public async Task CloseAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _db.SupportTickets.Owned(_user)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new InvalidOperationException($"Support ticket {ticketId} not found.");

        if (ticket.Status == SupportTicketStatus.Closed)
            throw new InvalidOperationException("This ticket is already closed.");

        ticket.Status = SupportTicketStatus.Closed;
        ticket.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
    }
}
