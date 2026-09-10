using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// SupportMessageService against the real project_ceres_test database.
///
/// A user can only ever CREATE an Open ticket — Pending / Solved / Closed / OnHold and
/// another user's rows do not exist on any path the app-role fixture can drive. Those states
/// are seeded through the admin (BYPASSRLS) context on purpose: the app-role connection is
/// subject to the same RLS policy as production, so it physically cannot forge them. That the
/// fixture cannot forge these rows is the wall working — the tests below need the rows to
/// exist anyway, to prove the SERVICE (not just RLS) enforces the state machine and ownership.
/// </summary>
[Collection("IntegrationParallel3")]
public class SupportMessageServiceTests : IAsyncLifetime
{
    private static readonly Guid Sentinel = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = new("00000000-0000-0000-0000-0000000000ff");

    private readonly TestDbFixture _fixture = new();
    private SupportMessageService _service = null!;
    private readonly List<Guid> _adminSeededTicketIds = [];

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        var user = new FakeCurrentUserAccessor(Sentinel);
        _service = new SupportMessageService(_fixture.Db, user, TimeProvider.System);
    }

    public async Task DisposeAsync()
    {
        // Roll back the fixture's app transaction FIRST. A successful PostUserReplyAsync
        // UPDATEs the seeded ticket inside that still-open transaction, holding a row lock;
        // the admin DELETE below on the same row would block on it until the command times
        // out. Releasing the lock before the cross-user cleanup avoids the deadlock.
        await _fixture.DisposeAsync();

        if (_adminSeededTicketIds.Count > 0)
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.SupportMessages
                .Where(m => _adminSeededTicketIds.Contains(m.SupportTicketId)).ExecuteDeleteAsync();
            await admin.SupportTickets
                .Where(t => _adminSeededTicketIds.Contains(t.Id)).ExecuteDeleteAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// A ticket in an arbitrary status, owned by the given user, inserted through the admin
    /// (BYPASSRLS) context. Rows written this way sit outside the fixture's transaction, so
    /// they are deleted explicitly on teardown via <see cref="_adminSeededTicketIds"/>.
    /// </summary>
    private async Task<SupportTicket> SeedTicketAsync(SupportTicketStatus status, Guid owner, string marker)
    {
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            Subject = $"seeded {marker} {Guid.NewGuid():N}",
            Status = status,
            Priority = SupportTicketPriority.Normal,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await using var admin = _fixture.CreateAdminContext();
        admin.SupportTickets.Add(ticket);
        await admin.SaveChangesAsync();
        _adminSeededTicketIds.Add(ticket.Id);
        return ticket;
    }

    private async Task SeedMessageAsync(Guid ticketId, Guid owner, SupportMessageAuthor authorRole, string body, DateTime createdAt)
    {
        await using var admin = _fixture.CreateAdminContext();
        admin.SupportMessages.Add(new SupportMessage
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            SupportTicketId = ticketId,
            AuthorRole = authorRole,
            Body = body,
            CreatedAt = createdAt,
        });
        await admin.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // PostUserReplyAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostUserReplyAsync_appends_a_user_message_and_returns_status_to_Open()
    {
        var marker = $"reply-open-{Guid.NewGuid():N}";
        var ticket = await SeedTicketAsync(SupportTicketStatus.Pending, Sentinel, marker);

        var (message, returnedTicket) = await _service.PostUserReplyAsync(ticket.Id, "Still broken, here's more detail.");

        message.AuthorRole.Should().Be(SupportMessageAuthor.User);
        message.SupportTicketId.Should().Be(ticket.Id);
        message.UserId.Should().Be(Sentinel);
        message.Body.Should().Be("Still broken, here's more detail.");

        // The returned ticket is the one the reply loaded — the controller reuses its
        // header for the operator notification instead of re-fetching the whole thread.
        returnedTicket.Id.Should().Be(ticket.Id);
        returnedTicket.Status.Should().Be(SupportTicketStatus.Open,
            "the returned ticket reflects the post-reply status the notification quotes");

        var persistedMessage = await _fixture.Db.SupportMessages.AsNoTracking()
            .SingleAsync(m => m.Id == message.Id);
        persistedMessage.AuthorRole.Should().Be(SupportMessageAuthor.User);

        var persistedTicket = await _fixture.Db.SupportTickets.AsNoTracking()
            .SingleAsync(t => t.Id == ticket.Id);
        persistedTicket.Status.Should().Be(SupportTicketStatus.Open,
            "any user reply returns the ball to the operator");
        persistedTicket.UpdatedAt.Should().BeOnOrAfter(ticket.UpdatedAt);
    }

    [Fact]
    public async Task PostUserReplyAsync_reopens_a_Solved_ticket()
    {
        var marker = $"reply-solved-{Guid.NewGuid():N}";
        var ticket = await SeedTicketAsync(SupportTicketStatus.Solved, Sentinel, marker);

        await _service.PostUserReplyAsync(ticket.Id, "Actually this came back.");

        var persisted = await _fixture.Db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        persisted.Status.Should().Be(SupportTicketStatus.Open,
            "a Solved ticket is still reopenable by a user reply");
    }

    [Fact]
    public async Task PostUserReplyAsync_refuses_a_reply_to_a_Closed_ticket()
    {
        var marker = $"reply-closed-{Guid.NewGuid():N}";
        var ticket = await SeedTicketAsync(SupportTicketStatus.Closed, Sentinel, marker);

        var act = () => _service.PostUserReplyAsync(ticket.Id, "please reopen");

        await act.Should().ThrowAsync<InvalidOperationException>(
            "closing is final — a follow-up ticket is the only path forward");

        var persisted = await _fixture.Db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        persisted.Status.Should().Be(SupportTicketStatus.Closed, "the refused reply must not have moved status");

        var messageCount = await _fixture.Db.SupportMessages.AsNoTracking()
            .CountAsync(m => m.SupportTicketId == ticket.Id);
        messageCount.Should().Be(0, "the refused reply must not have been persisted either");
    }

    [Fact]
    public async Task PostUserReplyAsync_cannot_reach_another_users_ticket()
    {
        var marker = $"reply-foreign-{Guid.NewGuid():N}";
        var foreign = await SeedTicketAsync(SupportTicketStatus.Open, OtherUser, marker);

        var act = () => _service.PostUserReplyAsync(foreign.Id, "not yours to reply to");

        await act.Should().ThrowAsync<InvalidOperationException>(
            "IDOR — someone else's ticket must be indistinguishable from one that does not exist");

        await using var admin = _fixture.CreateAdminContext();
        var untouched = await admin.SupportTickets.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == foreign.Id);
        untouched.Status.Should().Be(SupportTicketStatus.Open, "and it must actually be unchanged");

        var messageCount = await admin.SupportMessages.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(m => m.SupportTicketId == foreign.Id);
        messageCount.Should().Be(0, "no message should have been written against a ticket we cannot reach");
    }

    // -------------------------------------------------------------------------
    // GetThreadAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetThreadAsync_returns_messages_in_order_including_agent_messages()
    {
        var marker = $"thread-{Guid.NewGuid():N}";
        var ticket = await SeedTicketAsync(SupportTicketStatus.Open, Sentinel, marker);

        var first = DateTime.UtcNow.AddMinutes(-10);
        var second = DateTime.UtcNow.AddMinutes(-5);
        await SeedMessageAsync(ticket.Id, Sentinel, SupportMessageAuthor.User, $"{marker} user first", first);
        await SeedMessageAsync(ticket.Id, Sentinel, SupportMessageAuthor.Agent, $"{marker} agent second", second);

        var thread = await _service.GetThreadAsync(ticket.Id);

        thread.Should().NotBeNull();
        var mine = thread!.Messages.Where(m => m.Body.Contains(marker)).ToList();
        mine.Should().HaveCount(2);
        mine[0].AuthorRole.Should().Be(SupportMessageAuthor.User);
        mine[0].Body.Should().Contain("user first");
        mine[1].AuthorRole.Should().Be(SupportMessageAuthor.Agent);
        mine[1].Body.Should().Contain("agent second");
        mine[0].CreatedAt.Should().BeBefore(mine[1].CreatedAt);
    }

    [Fact]
    public async Task GetThreadAsync_cannot_reach_another_users_ticket()
    {
        var marker = $"thread-foreign-{Guid.NewGuid():N}";
        var foreign = await SeedTicketAsync(SupportTicketStatus.Open, OtherUser, marker);

        var thread = await _service.GetThreadAsync(foreign.Id);

        thread.Should().BeNull("someone else's ticket must read as absent, same as a nonexistent one");
    }
}
