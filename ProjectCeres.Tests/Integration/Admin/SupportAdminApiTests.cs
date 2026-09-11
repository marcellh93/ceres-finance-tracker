using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Admin;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.Admin;

/// <summary>
/// The operator reply/status endpoint (Stage 12.6) — the first cross-user WRITE under Admin/.
/// The load-bearing invariant is that the agent message it writes is stamped with the TICKET
/// OWNER's id (from the loaded row), NOT the acting admin's, so the user reads the reply under
/// their own RLS scope. These tests seed real registered users, grant one Admin, drive the real
/// [RequireAdmin] live-check + CSRF pipeline over HTTP, and assert ownership on the written rows.
///
/// Every DB read is keyed on this run's own ids; users and their owned rows are purged on teardown.
/// </summary>
[Collection("IntegrationParallel2")]
public class SupportAdminApiTests : IntegrationTestBase<AuthTestWebApplicationFactory>, IAsyncLifetime
{
    private const string EmailSuffix = "@support-admin-test.local";
    private readonly AuthTestWebApplicationFactory _factory;
    private readonly List<Guid> _userIds = [];

    public SupportAdminApiTests(AuthTestWebApplicationFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Registers a user (tracked for cleanup) and returns them.</summary>
    private async Task<ApplicationUser> RegisterAsync(string localPart)
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"{localPart}-{Guid.NewGuid():N}{EmailSuffix}");
        _userIds.Add(user.Id);
        return user;
    }

    /// <summary>An HTTP client signed in as an admin: registers, logs in, grants Admin.</summary>
    private async Task<(HttpClient Client, string Session, ApplicationUser Admin)> SignedInAdminAsync()
    {
        var admin = await RegisterAsync("admin");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var session = await AuthTestFixture.LoginViaHttpAsync(_factory, client, admin.Email!);

        using (var scope = _factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await roles.GrantAsync(admin.Id);
        }
        return (client, session, admin);
    }

    private HttpRequestMessage PostMessage(Guid ticketId, string session, Guid callerId, object payload)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, callerId);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/support/tickets/{ticketId}/messages")
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={session}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return req;
    }

    /// <summary>
    /// A ticket in an arbitrary status owned by <paramref name="owner"/>, written past the EF
    /// filter through the WAF's admin-connected AppDbContext. The opening message is seeded too,
    /// so the ticket looks like a real one. Cleaned up via the owner purge on teardown.
    /// </summary>
    private async Task<Guid> SeedTicketAsync(
        SupportTicketStatus status, Guid owner, string marker,
        SupportTicketPriority priority = SupportTicketPriority.Normal)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            Subject = $"seeded {marker} {Guid.NewGuid():N}",
            Status = status,
            Priority = priority,
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
        return ticket.Id;
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Operator_reply_writes_an_agent_message_stamped_with_the_ticket_owner()
    {
        var (client, session, admin) = await SignedInAdminAsync();
        var owner = await RegisterAsync("owner");
        var marker = $"op-reply-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker);

        var resp = await client.SendAsync(PostMessage(ticketId, session, admin.Id, new
        {
            body = $"{marker} here is your answer",
            status = SupportTicketStatus.Pending,
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var message = await db.SupportMessages.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(m => m.SupportTicketId == ticketId && m.Body == $"{marker} here is your answer");
        message.AuthorRole.Should().Be(SupportMessageAuthor.Agent);
        message.UserId.Should().Be(owner.Id, "the agent message is stamped with the TICKET OWNER's id");
        message.UserId.Should().NotBe(admin.Id, "NOT the acting admin's id — the interceptor trap");

        var ticket = await db.SupportTickets.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(SupportTicketStatus.Pending, "the chosen transition was applied");

        var audit = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(a => a.Action == AuditLogAction.SupportMessageByAgent && a.EntityId == ticketId);
        audit.UserId.Should().Be(owner.Id, "the interim audit row is owned by the ticket owner");
        audit.EntityType.Should().Be(nameof(SupportTicket));
    }

    [Fact]
    public async Task Status_only_change_writes_no_message_row()
    {
        var (client, session, admin) = await SignedInAdminAsync();
        var owner = await RegisterAsync("owner");
        var marker = $"status-only-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var before = await db.SupportMessages.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(m => m.SupportTicketId == ticketId);
            before.Should().Be(1, "only the seeded opening message exists");
        }

        var resp = await client.SendAsync(PostMessage(ticketId, session, admin.Id, new
        {
            body = (string?)null,
            status = SupportTicketStatus.OnHold,
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var after = await db2.SupportMessages.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(m => m.SupportTicketId == ticketId);
        after.Should().Be(1, "a status-only change creates NO message row");

        var ticket = await db2.SupportTickets.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(SupportTicketStatus.OnHold, "only the transition happened");
    }

    [Fact]
    public async Task Reply_to_a_nonexistent_ticket_is_404()
    {
        var (client, session, admin) = await SignedInAdminAsync();

        var resp = await client.SendAsync(PostMessage(Guid.NewGuid(), session, admin.Id, new
        {
            body = "into the void",
            status = SupportTicketStatus.Pending,
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unresolved ticket id is 404, matching the user surface and the IDOR 404-not-403 rule");
    }

    [Fact]
    public async Task A_non_admin_caller_is_403()
    {
        // Signed in, but never granted Admin.
        var caller = await RegisterAsync("plain");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var session = await AuthTestFixture.LoginViaHttpAsync(_factory, client, caller.Email!);

        var owner = await RegisterAsync("owner");
        var marker = $"non-admin-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker);

        var resp = await client.SendAsync(PostMessage(ticketId, session, caller.Id, new
        {
            body = "I am not an operator",
            status = SupportTicketStatus.Pending,
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "holding a session is not holding the Admin role — the [RequireAdmin] gate refuses");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var written = await db.SupportMessages.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(m => m.SupportTicketId == ticketId && m.Body == "I am not an operator");
        written.Should().Be(0, "a refused caller must not have written a message");
    }

    [Fact]
    public async Task An_illegal_transition_on_a_closed_ticket_is_422()
    {
        var (client, session, admin) = await SignedInAdminAsync();
        var owner = await RegisterAsync("owner");
        var marker = $"closed-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Closed, owner.Id, marker);

        var resp = await client.SendAsync(PostMessage(ticketId, session, admin.Id, new
        {
            body = $"{marker} reopening attempt",
            status = SupportTicketStatus.Open,
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "Closed is terminal — the state machine refuses the transition, surfaced as 422");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ticket = await db.SupportTickets.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(SupportTicketStatus.Closed, "the refused transition must not have moved status");
        var written = await db.SupportMessages.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(m => m.SupportTicketId == ticketId && m.Body == $"{marker} reopening attempt");
        written.Should().Be(0, "the transition is validated BEFORE any write — no message on refusal");
    }

    [Fact]
    public async Task The_owner_can_read_the_agent_reply_in_their_own_thread()
    {
        var (adminClient, adminSession, admin) = await SignedInAdminAsync();
        var owner = await RegisterAsync("owner");
        var marker = $"owner-reads-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker);

        var opResp = await adminClient.SendAsync(PostMessage(ticketId, adminSession, admin.Id, new
        {
            body = $"{marker} the operator answer",
            status = SupportTicketStatus.Pending,
        }));
        opResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Switch to the OWNER's context: sign them in and GET their own thread. The agent
        // message is visible only because it was stamped with the owner's id — it is under
        // their RLS/query-filter scope, not the admin's.
        var ownerClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var ownerSession = await AuthTestFixture.LoginViaHttpAsync(_factory, ownerClient, owner.Email!);
        var get = new HttpRequestMessage(HttpMethod.Get, $"/api/support/tickets/{ticketId}");
        get.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={ownerSession}");

        var threadResp = await ownerClient.SendAsync(get);
        threadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await threadResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        var agentMessages = thread.GetProperty("messages").EnumerateArray()
            .Where(m => m.GetProperty("body").GetString() == $"{marker} the operator answer")
            .ToList();
        agentMessages.Should().HaveCount(1, "the owner reads the operator's reply in their own thread");
        agentMessages[0].GetProperty("authorRole").GetInt32().Should().Be((int)SupportMessageAuthor.Agent);
    }

    // -------------------------------------------------------------------------
    // Stage 12.5.2 — admin list + thread read surface
    // -------------------------------------------------------------------------

    private static HttpRequestMessage Get(string url, string session)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={session}");
        return req;
    }

    [Fact]
    public async Task List_returns_tickets_across_all_users_with_owner_email()
    {
        var (client, session, _) = await SignedInAdminAsync();
        var userA = await RegisterAsync("owner-a");
        var userB = await RegisterAsync("owner-b");
        var marker = $"list-{Guid.NewGuid():N}";
        var tA = await SeedTicketAsync(SupportTicketStatus.Open, userA.Id, marker);
        var tB = await SeedTicketAsync(SupportTicketStatus.Pending, userB.Id, marker);

        var resp = await client.SendAsync(Get("/api/admin/support/tickets?pageSize=100", session));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        var items = body.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("subject").GetString()!.Contains(marker)).ToList();
        items.Should().HaveCount(2, "the admin list spans all users, not just the admin's own");
        var byId = items.ToDictionary(i => Guid.Parse(i.GetProperty("id").GetString()!));
        byId[tA].GetProperty("ownerEmail").GetString().Should().Be(userA.Email);
        byId[tA].GetProperty("ownerUserId").GetString().Should().Be(userA.Id.ToString());
        byId[tB].GetProperty("ownerEmail").GetString().Should().Be(userB.Email);
        byId[tA].GetProperty("messageCount").GetInt32().Should().Be(1, "the seeded opening message is counted");
    }

    [Fact]
    public async Task List_paginates_with_a_total_count()
    {
        var (client, session, _) = await SignedInAdminAsync();
        var owner = await RegisterAsync("pager");
        var marker = $"page-{Guid.NewGuid():N}";
        await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker);
        await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker);

        // pageSize=1 filtered to this run's two tickets: assert the envelope shape. `total` is the
        // unfiltered global count, so assert it is >= 2 rather than == 2 (the DB is shared).
        var resp = await client.SendAsync(Get("/api/admin/support/tickets?page=1&pageSize=1", session));
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(1);
        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("pageSize").GetInt32().Should().Be(1);
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var (client, session, _) = await SignedInAdminAsync();
        var owner = await RegisterAsync("filter");
        var marker = $"filter-{Guid.NewGuid():N}";
        await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker);
        await SeedTicketAsync(SupportTicketStatus.Solved, owner.Id, marker);

        var resp = await client.SendAsync(Get(
            $"/api/admin/support/tickets?pageSize=100&status={SupportTicketStatus.Solved}", session));
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var mine = body.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("subject").GetString()!.Contains(marker)).ToList();
        mine.Should().HaveCount(1, "only the Solved ticket matches the filter");
        mine[0].GetProperty("status").GetInt32().Should().Be((int)SupportTicketStatus.Solved);
    }

    [Fact]
    public async Task List_filters_by_priority()
    {
        // The priority `.Where` branch is symmetric to status but was untested (test-audit
        // finding, 2026-09-07). Seed two priorities, filter to one.
        var (client, session, _) = await SignedInAdminAsync();
        var owner = await RegisterAsync("prio");
        var marker = $"prio-{Guid.NewGuid():N}";
        await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker, SupportTicketPriority.Normal);
        await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker, SupportTicketPriority.Urgent);

        var resp = await client.SendAsync(Get(
            $"/api/admin/support/tickets?pageSize=100&priority={SupportTicketPriority.Urgent}", session));
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var mine = body.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("subject").GetString()!.Contains(marker)).ToList();
        mine.Should().HaveCount(1, "only the Urgent ticket matches the filter");
        mine[0].GetProperty("priority").GetInt32().Should().Be((int)SupportTicketPriority.Urgent);
    }

    [Fact]
    public async Task List_is_403_for_a_non_admin()
    {
        var caller = await RegisterAsync("plain-list");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var session = await AuthTestFixture.LoginViaHttpAsync(_factory, client, caller.Email!);

        var resp = await client.SendAsync(Get("/api/admin/support/tickets", session));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Thread_returns_owner_and_ordered_messages()
    {
        var (client, session, _) = await SignedInAdminAsync();
        var owner = await RegisterAsync("thread-owner");
        var marker = $"thread-{Guid.NewGuid():N}";
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, marker);

        var resp = await client.SendAsync(Get($"/api/admin/support/tickets/{ticketId}", session));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        body.GetProperty("ownerUserId").GetString().Should().Be(owner.Id.ToString());
        body.GetProperty("ownerEmail").GetString().Should().Be(owner.Email);
        body.GetProperty("messages").GetArrayLength().Should().Be(1);
        body.GetProperty("messages")[0].GetProperty("body").GetString().Should().Contain("opening");
    }

    [Fact]
    public async Task Thread_is_404_for_an_unknown_id()
    {
        var (client, session, _) = await SignedInAdminAsync();
        var resp = await client.SendAsync(Get($"/api/admin/support/tickets/{Guid.NewGuid()}", session));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unresolved ticket id is 404, matching the IDOR 404-not-403 rule");
    }

    [Fact]
    public async Task Thread_is_403_for_a_non_admin()
    {
        var owner = await RegisterAsync("thread-owner2");
        var ticketId = await SeedTicketAsync(SupportTicketStatus.Open, owner.Id, $"t-{Guid.NewGuid():N}");
        var caller = await RegisterAsync("plain-thread");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var session = await AuthTestFixture.LoginViaHttpAsync(_factory, client, caller.Email!);

        var resp = await client.SendAsync(Get($"/api/admin/support/tickets/{ticketId}", session));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
