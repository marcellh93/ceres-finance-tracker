using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class EmailChangeCrossFeatureTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangeCrossFeatureTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    /// <summary>
    /// Arranges: register user, request email-change (fresh reauth), then run a full
    /// password-reset request → confirm cycle for the same user. Returns the verify
    /// + revoke tokens so subsequent assertions can probe /confirm and /revoke.
    /// </summary>
    private async Task<(WebApplicationFactory<Program> Factory, ApplicationUser User,
                       string OldEmail, string NewEmail, string VerifyToken, string RevokeToken,
                       List<EmailMessage> Captured)>
        ArrangePendingChangePlusPasswordResetAsync()
    {
        var captured = new List<EmailMessage>();
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-cf-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-cf-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        // Step 1: request email-change with fresh reauth.
        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, fresh);
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);

        var clientForChange = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/request")
        {
            Content = JsonContent.Create(new { newEmail }),
        };
        changeReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        changeReq.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var changeResp = await clientForChange.SendAsync(changeReq);
        changeResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var verifyToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To == newEmail));
        var revokeToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To == oldEmail));
        captured.Clear();

        // Step 2: run password-reset request → confirm.
        var clientForReset = factory.CreateClient();
        var resetReqResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientForReset, "/api/auth/password-reset/request", new { email = oldEmail });
        resetReqResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resetMsg = captured.Single(m => m.To == oldEmail && m.Subject.Contains("Reset"));
        var resetToken = AuthTestFixture.ExtractResetTokenFromMessage(resetMsg);

        var confirmResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, clientForReset, "/api/auth/password-reset/confirm",
            new { token = resetToken, newPassword = "fresh horse battery staple" });
        confirmResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return (factory, user, oldEmail, newEmail, verifyToken, revokeToken, captured);
    }

    private static async Task<HttpResponseMessage> PostConfirmAsync(
        WebApplicationFactory<Program> factory, AuthTestWebApplicationFactory baseFactory, string token)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(baseFactory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/confirm")
        {
            Content = JsonContent.Create(new { token }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PostRevokeAsync(
        WebApplicationFactory<Program> factory, AuthTestWebApplicationFactory baseFactory, string token)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(baseFactory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/revoke")
        {
            Content = JsonContent.Create(new { token }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    // ── Test #29 ────────────────────────────────────────────────────────────
    // Pending email-change + successful password-reset → both EmailChangeToken
    // rows for that user have ConsumedAt != null.
    [Fact]
    public async Task Pending_email_change_plus_successful_password_reset_consumes_both_email_change_rows()
    {
        var arr = await ArrangePendingChangePlusPasswordResetAsync();
        await using var factory = arr.Factory;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == arr.User.Id)
            .ToListAsync(Timeout30s());

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.ConsumedAt != null,
            "password-reset must atomically cancel any pending email-change");
    }

    // ── Test #30 ────────────────────────────────────────────────────────────
    // The cross-feature cancellation email is sent to the OLD address (the
    // address-of-record at password-reset time).
    [Fact]
    public async Task Cancellation_email_fires_to_OLD_address_when_password_reset_consumes_pending_email_change()
    {
        var arr = await ArrangePendingChangePlusPasswordResetAsync();
        await using var factory = arr.Factory;

        arr.Captured.Should().Contain(m =>
            m.To == arr.OldEmail && m.Subject.Contains("Pending email change cancelled"),
            "the cancellation notification must land at the old address-of-record");
    }

    // ── Test #31 ────────────────────────────────────────────────────────────
    // After cross-feature cancellation, /email-change/confirm with the verify
    // token returns 401.
    [Fact]
    public async Task After_password_reset_cancels_pending_email_change_subsequent_email_change_confirm_returns_401()
    {
        var arr = await ArrangePendingChangePlusPasswordResetAsync();
        await using var factory = arr.Factory;

        var resp = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    // ── Test #32 ────────────────────────────────────────────────────────────
    // After cross-feature cancellation, /email-change/revoke with the revoke
    // token returns 401.
    [Fact]
    public async Task After_password_reset_cancels_pending_email_change_subsequent_email_change_revoke_returns_401()
    {
        var arr = await ArrangePendingChangePlusPasswordResetAsync();
        await using var factory = arr.Factory;

        var resp = await PostRevokeAsync(factory, _factory, arr.RevokeToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }
}
