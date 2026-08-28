using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// The support-ticket status transitions, as data. Pure and static (like BudgetPeriod):
/// no DbContext, no request state, no fields. Rules live here; the DB writes live in the
/// services, so the rules exist in one place and are unit-testable with no database.
/// User actions derive status; operator actions carry an explicit chosen status.
/// </summary>
public static class SupportTicketStateMachine
{
    public static TransitionResult ResolveUserReply(SupportTicketStatus current) =>
        current == SupportTicketStatus.Closed
            ? new(false, current, "This ticket is closed. File a follow-up ticket to continue.")
            : new(true, SupportTicketStatus.Open, null);   // any open reply returns the ball to the operator

    public static TransitionResult ResolveOperatorAction(SupportTicketStatus current, SupportTicketStatus chosen) =>
        current == SupportTicketStatus.Closed
            ? new(false, current, "This ticket is closed and cannot change status.")
            : new(true, chosen, null);
}

public readonly record struct TransitionResult(bool Allowed, SupportTicketStatus NewStatus, string? Reason);
