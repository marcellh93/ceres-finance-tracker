using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

/// <summary>
/// Stage 9 close-out — pins that MfaController fires the TotpEnrolled / TotpDisabled /
/// BackupCodesRegenerated security-event emails. Email capture mirrors
/// LockoutUnlockIssuanceTests; the enroll / two-step-login flow mirrors
/// MfaEnrollmentTests + MfaDisableTests. EnrollUserMfaAsync runs against the base
/// _factory (only that overload exists) — both factories share the same DB.
/// </summary>
[Collection("IntegrationTests")]
public class MfaSecurityEmailTests : IAsyncLifetime
{
    private const string EmailDomain = "@mfa-secemail-test.local";

    private readonly AuthTestWebApplicationFactory _factory;

    public MfaSecurityEmailTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith(EmailDomain)).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    private static (Mock<IEmailService> mock, List<EmailMessage> captured) CapturingEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Loose);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                captured.Add(m);
                return Task.CompletedTask;
            });
        return (mock, captured);
    }

    // Single-step session login (user is NOT MFA-enabled yet). Mirrors
    // MfaEnrollmentTests.LoginAndGetSessionCookieAsync.
    private async Task<string> LoginAndGetSessionCookieAsync(
        WebApplicationFactory<Program> factory, HttpClient client, string email)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        return ExtractSetCookie(loginResp, SessionConstants.SessionCookieName)!;
    }

    // Drives register → single-step login → enroll → enroll/verify with a valid TOTP code.
    // Returns the enroll/verify response. Mirrors MfaEnrollmentTests.
    private async Task<HttpResponseMessage> RegisterAndEnrollVerifyAsync(
        WebApplicationFactory<Program> factory, string email)
    {
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await LoginAndGetSessionCookieAsync(factory, client, email);

        var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
        var enrollResp = await client.SendAsync(enrollReq);
        var enrollBody = await enrollResp.Content.ReadFromJsonAsync<JsonElement>();
        var seed = ExtractSecretFromUri(enrollBody.GetProperty("otpAuthUri").GetString()!);

        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var (verifyCookie, verifyHeader) = AuthTestFixture.MintCsrf(factory, user.Id);
        var verifyReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll/verify")
        {
            Content = JsonContent.Create(new { code }),
        };
        verifyReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={verifyCookie}");
        verifyReq.Headers.Add(SessionConstants.CsrfHeaderName, verifyHeader);
        return await client.SendAsync(verifyReq);
    }

    // Drives register → enroll (out-of-band, via base _factory) → full two-step login →
    // returns an authenticated client + session cookie. Mirrors
    // MfaDisableTests.SetupAuthenticatedMfaUserAsync.
    private async Task<(HttpClient client, string sessionCookie, ApplicationUser user)>
        SetupAuthenticatedMfaUserAsync(WebApplicationFactory<Program> factory, string email)
    {
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        // EnrollUserMfaAsync has only the AuthTestWebApplicationFactory overload; both
        // factories back the same DB, so enrolling via _factory persists to this user.
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        using (var scope = factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf1, header1) = AuthTestFixture.MintCsrf(factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf1}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header1);
        var loginResp = await client.SendAsync(loginReq);
        var twoFactorCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
        twoFactorCookie.Should().NotBeNullOrEmpty();

        var loginCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var (csrf2, header2) = AuthTestFixture.MintCsrf(factory);
        var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code = loginCode }),
        };
        totpReq.Headers.Add("Cookie", $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        totpReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var totpResp = await client.SendAsync(totpReq);
        totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var sessionCookie = ExtractSetCookie(totpResp, SessionConstants.SessionCookieName);
        sessionCookie.Should().NotBeNullOrEmpty();

        return (client, sessionCookie!, user);
    }

    private static async Task<HttpResponseMessage> PostNoBodyAsync(
        WebApplicationFactory<Program> factory, HttpClient client, string path, string sessionCookie, Guid userId)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(factory, userId);
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task EnrollVerify_sends_TotpEnrolled_email()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var email = $"enroll-{Guid.NewGuid():N}{EmailDomain}";

        var resp = await RegisterAndEnrollVerifyAsync(factory, email);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var sent = captured.Where(m => m.To.Address == email).ToList();
        sent.Should().ContainSingle("enroll/verify must send exactly one security-event email to the user");
        sent[0].Subject.Should().Be("Two-factor sign-in enabled on your Ceres account");
    }

    [Fact]
    public async Task Disable_sends_TotpDisabled_email()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var email = $"disable-{Guid.NewGuid():N}{EmailDomain}";

        var (client, sessionCookie, user) = await SetupAuthenticatedMfaUserAsync(factory, email);
        captured.Clear(); // ignore any send from the setup path

        var resp = await PostNoBodyAsync(factory, client, "/api/auth/mfa/disable", sessionCookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sent = captured.Where(m => m.To.Address == email).ToList();
        sent.Should().ContainSingle("disable must send exactly one security-event email to the user");
        sent[0].Subject.Should().Be("Two-factor sign-in turned off on your Ceres account");
    }

    [Fact]
    public async Task RegenerateBackupCodes_sends_BackupCodesRegenerated_email()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var email = $"regen-{Guid.NewGuid():N}{EmailDomain}";

        var (client, sessionCookie, user) = await SetupAuthenticatedMfaUserAsync(factory, email);
        captured.Clear();

        var resp = await PostNoBodyAsync(factory, client, "/api/auth/mfa/backup-codes/regenerate", sessionCookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var sent = captured.Where(m => m.To.Address == email).ToList();
        sent.Should().ContainSingle("regenerate must send exactly one security-event email to the user");
        sent[0].Subject.Should().Be("Your Ceres backup codes were regenerated");
    }

    [Fact]
    public async Task EnrollVerify_email_send_failure_does_not_fail_the_action()
    {
        // Throwing email service: the action's try/catch must swallow the failure and
        // still return 200 with the backup codes.
        var throwingMock = new Mock<IEmailService>(MockBehavior.Strict);
        throwingMock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("simulated SMTP failure"));

        await using var factory = _factory.WithReplacedService(throwingMock.Object);
        var email = $"enroll-fail-{Guid.NewGuid():N}{EmailDomain}";

        var resp = await RegisterAndEnrollVerifyAsync(factory, email);

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "email-send failure must not fail the enroll/verify action");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var codes = body.GetProperty("backupCodes").EnumerateArray().Select(e => e.GetString()!).ToList();
        codes.Should().HaveCount(10);
    }

    private static string? ExtractSetCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var first = v.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == cookieName) return first[(eq + 1)..];
        }
        return null;
    }

    private static string ExtractSecretFromUri(string otpAuthUri)
    {
        var uri = new Uri(otpAuthUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["secret"]!;
    }
}
