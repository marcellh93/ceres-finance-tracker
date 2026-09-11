using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel3")]
public class PasswordResetConcurrencyTests : IntegrationTestBase<Bucket3AuthFactory>
{
    private readonly Bucket3AuthFactory _factory;

    public PasswordResetConcurrencyTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    // ── Test #19 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Concurrent_confirm_with_same_token_only_one_wins()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService(mock.Object);

        var email = $"concurrent-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        // Issue a token.
        var clientA = factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientA, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // Two parallel confirm calls with the same token.
        var clientB = factory.CreateClient();
        var task1 = AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientA, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        var task2 = AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientB, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        var results = await Task.WhenAll(task1, task2).WaitAsync(TimeSpan.FromSeconds(30));

        results.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var consumed = await db.PasswordResetTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == user.Id && t.ConsumedAt != null)
                .CountAsync(Timeout30s());
            consumed.Should().Be(1);
        }
    }

    // ── Test #22 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Token_resolution_uses_token_row_UserId_not_caller_supplied_id()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var emailA = $"isolation-a-{Guid.NewGuid():N}@example.com";
        var emailB = $"isolation-b-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, emailA);
        var userB = await AuthTestFixture.RegisterUserAsync(factory, emailB);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email = emailA });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var tokenA = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // Submit token A. Resolve to user A. User B's password must remain intact.
        var confirm = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = tokenA, newPassword = "fresh horse battery staple" });
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var freshA = await um.FindByEmailAsync(emailA);
            var freshB = await um.FindByEmailAsync(emailB);

            (await um.CheckPasswordAsync(freshA!, "fresh horse battery staple")).Should().BeTrue();
            (await um.CheckPasswordAsync(freshB!, AuthTestFixture.ValidPassword)).Should().BeTrue(
                "user B's password must remain unchanged when only A's token was used");
        }
    }

    // ── Test #23 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Concurrent_request_then_confirm_serialises_via_semaphore()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService(mock.Object);
        var clientA = factory.CreateClient();
        var clientB = factory.CreateClient();

        var email = $"serialise-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, email);

        // Issue first token.
        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientA, "/api/auth/password-reset/request", new { email });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token1 = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // Fire (a) confirm with token1 and (b) a new request, in parallel.
        var taskConfirm = AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientA, "/api/auth/password-reset/confirm",
            new { token = token1, newPassword = "fresh horse battery staple" });
        var taskRequest = AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientB, "/api/auth/password-reset/request", new { email });

        var results = await Task.WhenAll(taskConfirm, taskRequest).WaitAsync(TimeSpan.FromSeconds(30));
        var confirmResult = results[0];
        var requestResult = results[1];
        requestResult.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm must be either Success (it acquired the lock first) or Unauthorized
        // (the new request superseded it). Either is correct; the assertion is that
        // both serialised cleanly without throwing.
        confirmResult.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.Unauthorized);
    }
}
