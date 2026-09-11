using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 12.5.3 — the new-sign-in-from-a-new-IP security alert. Novelty is exact-IP: an alert
/// fires only when the user HAS prior sessions and NONE was created from the current IP.
/// First-ever login is suppressed.
///
/// The TestServer records RemoteIpAddress as "" for every request, so the login under test
/// always records IpCreatedAt = "". We drive the three cases by seeding the user's PRIOR
/// session rows with controlled IPs: a concrete IP makes "" novel (alert), an "" prior makes
/// "" known (no alert), and no prior at all is the first-login suppression case (no alert).
/// </summary>
[Collection("IntegrationParallel4")]
public class NewSessionAlertTests : IntegrationTestBase<AuthTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public NewSessionAlertTests(AuthTestWebApplicationFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@newsession-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    private static (Mock<IEmailService> mock, List<EmailMessage> captured) CapturingEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Loose);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { lock (captured) { captured.Add(m); } return Task.CompletedTask; });
        return (mock, captured);
    }

    private async Task SeedPriorSessionAsync(WebApplicationFactory<Program> factory, Guid userId, string ip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserSessions.Add(new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IpCreatedAt = ip,
            UserAgent = "newsession-test-seed",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastUsedAt = DateTime.UtcNow.AddDays(-1),
            IsPersistent = false,
        });
        await db.SaveChangesAsync();
    }

    private async Task LoginAsync(WebApplicationFactory<Program> factory, string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        (await client.SendAsync(req)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task First_ever_login_sends_no_alert()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedEmailService(mock.Object);
        // RegisterUserAsync does NOT create a session; the login below is the user's first.
        await AuthTestFixture.RegisterUserAsync(factory, "first@newsession-test.local");

        await LoginAsync(factory, "first@newsession-test.local");

        captured.Should().NotContain(m => m.Subject.Contains("New sign-in") || m.Subject.Contains("Nuevo inicio"),
            "the first-ever sign-in has no prior session to be 'new' against — the alert must be suppressed");
    }

    [Fact]
    public async Task Login_from_a_new_ip_sends_the_alert_to_the_user()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedEmailService(mock.Object);
        var user = await AuthTestFixture.RegisterUserAsync(factory, "novel@newsession-test.local");

        // A prior session from a DIFFERENT IP than the login will record ("") makes this login novel.
        await SeedPriorSessionAsync(factory, user.Id, "198.51.100.9");

        await LoginAsync(factory, "novel@newsession-test.local");

        var alert = captured.SingleOrDefault(m => m.Subject.Contains("New sign-in"));
        alert.Should().NotBeNull("a sign-in from an IP with no matching prior session must alert the user");
        alert!.To.Address.Should().Be("novel@newsession-test.local", "the alert goes to the account's own address");
    }

    [Fact]
    public async Task Login_from_a_known_ip_sends_no_alert()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedEmailService(mock.Object);
        var user = await AuthTestFixture.RegisterUserAsync(factory, "known@newsession-test.local");

        // A prior session from the SAME IP the login will record ("") — the IP is not novel.
        await SeedPriorSessionAsync(factory, user.Id, "");

        await LoginAsync(factory, "known@newsession-test.local");

        captured.Should().NotContain(m => m.Subject.Contains("New sign-in"),
            "a sign-in from an IP the user has signed in from before must not alert");
    }
}
