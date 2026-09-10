using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

/// <summary>
/// The admin notification sent when a ticket is filed.
///
/// With no admin ticket-list UI yet (deferred to roadmap § 12.5.2), this email is the ONLY
/// way an operator learns a ticket exists — so what it carries, and that it is addressed to
/// the configured mailbox rather than to anything user-supplied, are both load-bearing.
/// </summary>
[Collection("IntegrationParallel1")]
public class SupportNotificationTests : IAsyncLifetime
{
    private const string SupportAddress = "support-desk@ceres-test.local";

    private readonly TestWebApplicationFactory _factory;
    private readonly List<Guid> _seededTicketIds = [];

    public SupportNotificationTests(TestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTicketIds.Count > 0)
        {
            await db.SupportTickets.IgnoreQueryFilters()
                .Where(t => _seededTicketIds.Contains(t.Id)).ExecuteDeleteAsync();
        }
    }

    private static (Mock<IEmailService> mock, List<EmailMessage> captured) CapturingEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Loose);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                lock (captured) { captured.Add(m); }
                return Task.CompletedTask;
            });
        return (mock, captured);
    }

    [Fact]
    public async Task Filing_a_ticket_notifies_the_configured_support_address()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory
            .WithConfiguredSupportAddress(SupportAddress)
            .WithReplacedEmailService(mock.Object);
        var client = factory.CreateClient();

        var subject = $"Everything is on fire {Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject,
            message = "The dashboard shows a negative net worth.",
            priority = SupportTicketPriority.Urgent,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _seededTicketIds.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        captured.Should().ContainSingle("filing a ticket sends exactly one notification");
        var sent = captured[0];
        sent.To.Address.Should().Be(SupportAddress,
            "the recipient comes from Email:SupportAddress, never from the request");
        sent.Subject.Should().Contain(subject);
        sent.Subject.Should().StartWith("[Ceres ticket ",
            "a fixed marker leads the subject so user-authored text can never be the first "
            + "thing the operator's mail client renders");
        sent.BodyText.Should().Contain("The dashboard shows a negative net worth.");
        sent.BodyText.Should().Contain("Urgent");
    }

    [Fact]
    public async Task A_send_failure_does_not_fail_the_ticket()
    {
        var mock = new Mock<IEmailService>(MockBehavior.Loose);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated provider outage"));

        await using var factory = _factory
            .WithConfiguredSupportAddress(SupportAddress)
            .WithReplacedEmailService(mock.Object);
        var client = factory.CreateClient();

        var subject = $"Filed during an outage {Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject,
            message = "Please still record this.",
            priority = SupportTicketPriority.Normal,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the ticket is already committed; a mail outage must not tell the user their "
            + "report failed when it did not");

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededTicketIds.Add(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.SupportTickets.AsNoTracking().AnyAsync(t => t.Id == id))
            .Should().BeTrue("the ticket survives the failed notification");
    }

    [Fact]
    public async Task No_configured_address_means_no_notification_but_still_a_ticket()
    {
        var (mock, captured) = CapturingEmailMock();
        // No WithConfiguredSupportAddress — this is the non-Production shape. Production
        // cannot reach it: Program.cs refuses to boot without Email:SupportAddress.
        await using var factory = _factory.WithReplacedEmailService(mock.Object);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject = $"No mailbox configured {Guid.NewGuid():N}",
            message = "Filed anyway.",
            priority = SupportTicketPriority.Low,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _seededTicketIds.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        captured.Should().BeEmpty("there is nowhere to send it, and that is not an error");
    }
}
