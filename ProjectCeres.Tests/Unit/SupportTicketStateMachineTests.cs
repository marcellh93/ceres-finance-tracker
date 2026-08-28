using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using Xunit;

namespace ProjectCeres.Tests.Unit;

public class SupportTicketStateMachineTests
{
    [Theory]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.Open)]      // reply to Open stays Open
    [InlineData(SupportTicketStatus.Pending, SupportTicketStatus.Open)]   // ball returns to operator
    [InlineData(SupportTicketStatus.OnHold, SupportTicketStatus.Open)]
    [InlineData(SupportTicketStatus.Solved, SupportTicketStatus.Open)]    // the reopen
    public void UserReply_moves_status_to_Open(SupportTicketStatus from, SupportTicketStatus expected)
    {
        var r = SupportTicketStateMachine.ResolveUserReply(from);
        r.Allowed.Should().BeTrue();
        r.NewStatus.Should().Be(expected);
    }

    [Fact]
    public void UserReply_to_Closed_is_refused()
    {
        var r = SupportTicketStateMachine.ResolveUserReply(SupportTicketStatus.Closed);
        r.Allowed.Should().BeFalse();
        r.Reason.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.Pending)]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.OnHold)]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.Solved)]
    [InlineData(SupportTicketStatus.Pending, SupportTicketStatus.Open)]
    [InlineData(SupportTicketStatus.OnHold, SupportTicketStatus.Solved)]
    [InlineData(SupportTicketStatus.Solved, SupportTicketStatus.Closed)]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.Closed)]   // direct close (spam/dup)
    public void OperatorAction_sets_the_chosen_status(SupportTicketStatus from, SupportTicketStatus chosen)
    {
        var r = SupportTicketStateMachine.ResolveOperatorAction(from, chosen);
        r.Allowed.Should().BeTrue();
        r.NewStatus.Should().Be(chosen);
    }

    [Theory]
    [InlineData(SupportTicketStatus.Closed, SupportTicketStatus.Open)]
    [InlineData(SupportTicketStatus.Closed, SupportTicketStatus.Pending)]
    public void OperatorAction_cannot_leave_Closed(SupportTicketStatus from, SupportTicketStatus chosen)
    {
        var r = SupportTicketStateMachine.ResolveOperatorAction(from, chosen);
        r.Allowed.Should().BeFalse();
    }
}
