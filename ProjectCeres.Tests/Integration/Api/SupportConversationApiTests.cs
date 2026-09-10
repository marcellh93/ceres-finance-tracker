using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

/// <summary>
/// The conversation HTTP surface: the user-reply endpoint, the thread GET, and the
/// message rollup on the list.
///
/// Non-Open, foreign, and agent rows cannot be created through the app's own API — a user
/// can only ever file an Open ticket and post their own replies. They are seeded through the
/// factory's admin-connected context (BYPASSRLS) on purpose, so the tests can prove the
/// endpoints enforce the state machine and ownership rather than relying on RLS alone. Every
/// DB read is keyed on a marker unique to the test, because the test database is shared.
/// </summary>
[Collection("IntegrationParallel3")]
public class SupportConversationApiTests : IAsyncLifetime
{
    private static readonly Guid Sentinel = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = new("00000000-0000-0000-0000-0000000000fd");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTicketIds = [];

    public SupportConversationApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_seededTicketIds.Count == 0) return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Attachment -> message and message -> ticket are both Cascade; deleting the tickets
        // removes their admin-seeded messages too. Messages first only for explicitness.
        await db.SupportMessages.IgnoreQueryFilters()
            .Where(m => _seededTicketIds.Contains(m.SupportTicketId)).ExecuteDeleteAsync();
        await db.SupportTickets.IgnoreQueryFilters()
            .Where(t => _seededTicketIds.Contains(t.Id)).ExecuteDeleteAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateTicketAsync(string subject)
    {
        var response = await _client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject,
            message = "Something went wrong.",
            priority = SupportTicketPriority.Normal,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededTicketIds.Add(id);
        return id;
    }

    /// <summary>
    /// A ticket in an arbitrary status, owned by the given user, written past RLS via the
    /// admin-connected context. The opening message is seeded too, because a real ticket
    /// always has one and the state machine reads the ticket, not the messages.
    /// </summary>
    private async Task<Guid> SeedTicketAsync(SupportTicketStatus status, Guid owner, string marker)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
        ticket.Messages.Add(new SupportMessage
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            SupportTicketId = ticket.Id,
            AuthorRole = SupportMessageAuthor.User,
            Body = $"{marker} opening",
            CreatedAt = DateTime.UtcNow,
        });
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        _seededTicketIds.Add(ticket.Id);
        return ticket.Id;
    }

    private async Task SeedMessageAsync(Guid ticketId, Guid owner, SupportMessageAuthor role, string body, DateTime createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SupportMessages.Add(new SupportMessage
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            SupportTicketId = ticketId,
            AuthorRole = role,
            Body = body,
            CreatedAt = createdAt,
        });
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Reply endpoint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Reply_appends_a_user_message_and_flips_a_Pending_ticket_to_Open()
    {
        var marker = $"reply-{Guid.NewGuid():N}";
        // The ticket must be Pending (not Open) so the Open assertion below is meaningful —
        // a fresh ticket is already Open. A user cannot create a Pending ticket, so seed it.
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Pending, Sentinel, marker);

        var post = await _client.PostAsJsonAsync(
            $"/api/support/tickets/{ticketId}/messages", new { body = $"{marker} still broken" });

        post.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("authorRole").GetInt32().Should().Be((int)SupportMessageAuthor.User);
        created.GetProperty("body").GetString().Should().Be($"{marker} still broken");
        var newMessageId = created.GetProperty("id").GetGuid();

        // The reply appears in the thread, in order after the opening message.
        var thread = await (await _client.GetAsync($"/api/support/tickets/{ticketId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var bodies = thread.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("body").GetString()).ToList();
        bodies.Should().ContainInOrder($"{marker} opening", $"{marker} still broken");
        thread.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("id").GetGuid()).Should().Contain(newMessageId);

        // Any user reply hands the ball back to the operator: status returns to Open.
        thread.GetProperty("status").GetInt32().Should().Be((int)SupportTicketStatus.Open);
    }

    [Fact]
    public async Task Reply_to_a_Closed_ticket_is_422()
    {
        var marker = $"reply-closed-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Closed, Sentinel, marker);

        var post = await _client.PostAsJsonAsync(
            $"/api/support/tickets/{ticketId}/messages", new { body = "please reopen" });

        post.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "closing is final — the state machine refuses the reply, and the throw is a 422");

        // Nothing was written, and the status did not move.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.SupportTickets.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == ticketId);
        persisted.Status.Should().Be(SupportTicketStatus.Closed);
        var extra = await db.SupportMessages.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(m => m.SupportTicketId == ticketId && m.Body == "please reopen");
        extra.Should().Be(0, "the refused reply must not have been persisted");
    }

    [Fact]
    public async Task Reply_to_another_users_ticket_is_404_not_403()
    {
        var marker = $"reply-foreign-{Guid.NewGuid():N}";
        var foreignTicketId = await SeedTicketAsync(SupportTicketStatus.Open, OtherUser, marker);

        var post = await _client.PostAsJsonAsync(
            $"/api/support/tickets/{foreignTicketId}/messages", new { body = "not yours to reply to" });

        // IDOR — the service cannot reach a ticket it does not own and throws
        // SupportTicketNotFoundException; the controller surfaces that as 404, matching
        // GetOne / Close / thread GET. A foreign ticket is indistinguishable from an absent
        // one, the caller learns nothing, and no message is written.
        post.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "someone else's ticket must be indistinguishable from one that does not exist — 404, never 403");
        (await post.Content.ReadAsStringAsync()).Should().NotContain("not yours to reply to");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var written = await db.SupportMessages.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(m => m.SupportTicketId == foreignTicketId && m.Body == "not yours to reply to");
        written.Should().Be(0, "no message may be written against a ticket we cannot reach");
    }

    // -------------------------------------------------------------------------
    // Thread GET
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Thread_returns_messages_in_order_including_an_agent_message()
    {
        var marker = $"thread-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Open, Sentinel, marker);

        var t1 = DateTime.UtcNow.AddMinutes(-10);
        var t2 = DateTime.UtcNow.AddMinutes(-5);
        await SeedMessageAsync(ticketId, Sentinel, SupportMessageAuthor.User, $"{marker} user first", t1);
        await SeedMessageAsync(ticketId, Sentinel, SupportMessageAuthor.Agent, $"{marker} agent second", t2);

        var thread = await (await _client.GetAsync($"/api/support/tickets/{ticketId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var mine = thread.GetProperty("messages").EnumerateArray()
            .Where(m => m.GetProperty("body").GetString()!.Contains(marker)
                        && !m.GetProperty("body").GetString()!.Contains("opening"))
            .ToList();
        mine.Should().HaveCount(2);
        mine[0].GetProperty("authorRole").GetInt32().Should().Be((int)SupportMessageAuthor.User);
        mine[0].GetProperty("body").GetString().Should().Contain("user first");
        mine[1].GetProperty("authorRole").GetInt32().Should().Be((int)SupportMessageAuthor.Agent);
        mine[1].GetProperty("body").GetString().Should().Contain("agent second");
    }

    [Fact]
    public async Task Thread_of_another_users_ticket_is_404()
    {
        var marker = $"thread-foreign-{Guid.NewGuid():N}";
        var foreignTicketId = await SeedTicketAsync(SupportTicketStatus.Open, OtherUser, marker);

        var response = await _client.GetAsync($"/api/support/tickets/{foreignTicketId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "someone else's ticket reads as absent — 404, never 403");
    }

    // -------------------------------------------------------------------------
    // List rollup
    // -------------------------------------------------------------------------

    [Fact]
    public async Task List_reports_the_message_count_and_last_message_timestamp()
    {
        var marker = $"list-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Open, Sentinel, marker);
        // One opening message already exists, stamped ~now by the seeder. Add two more, the
        // last of them the genuine newest so lastMessageAt has an unambiguous answer.
        await SeedMessageAsync(ticketId, Sentinel, SupportMessageAuthor.Agent, $"{marker} m2", DateTime.UtcNow.AddMinutes(2));
        var newest = DateTime.UtcNow.AddMinutes(5);
        await SeedMessageAsync(ticketId, Sentinel, SupportMessageAuthor.User, $"{marker} m3", newest);

        var list = await (await _client.GetAsync("/api/support/tickets"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var item = list.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == ticketId);
        item.GetProperty("messageCount").GetInt32().Should().Be(3,
            "opening message plus the two seeded replies");
        item.GetProperty("lastMessageAt").GetDateTime().Should()
            .BeCloseTo(newest, TimeSpan.FromSeconds(1),
                "lastMessageAt is the newest message's timestamp");
    }
}
