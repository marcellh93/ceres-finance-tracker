using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 9.5d Task 6. MFA enroll/verify is fully authenticated — the request carries the
/// session cookie, so the RLS interceptor resolves app.current_user_ref to the real user
/// before MfaBackupCodeService.GenerateAndPersistAsync inserts the 10 backup-code rows.
/// That insert path is NOT [RlsBypassJustified] (only verify/regenerate/purge are), so the
/// write goes through user_isolation as the owner — expected to PASS with no production change.
/// </summary>
[Collection("AppRoleTests")]
public class MfaWritesUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public MfaWritesUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task MfaEnroll_under_ceres_app_persists_10_backup_codes_visible_only_to_owner()
    {
        // Seed via the production HTTP register endpoint (owns its PreAuthUserScope so the
        // category seed passes RLS under ceres_app), then confirm the email via the BYPASSRLS
        // admin context — login requires a confirmed account.
        var email = $"mfa-{Marker}@approle-test.local";
        var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });
        var register = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/register", new { email, password = AuthTestFixture.ValidPassword });
        register.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var admin = Factory.NewAdminContext())
        {
            var seeded = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            _userId = seeded.Id;
            seeded.EmailConfirmed = true;
            await admin.Context.SaveChangesAsync();
        }

        // Fresh login stamps LastReauthAt on the session cookie, satisfying the [RequireRecentAuth]
        // gate on both enroll endpoints.
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(Factory, client, email);

        // Enroll → read the authenticator seed out of the otpauth URI.
        var enrollResp = await SendAuthedAsync(client, HttpMethod.Post, "/api/auth/mfa/enroll", sessionCookie, body: null);
        enrollResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var enrollBody = await enrollResp.Content.ReadFromJsonAsync<JsonElement>();
        var otpAuthUri = enrollBody.GetProperty("otpAuthUri").GetString()!;
        var seed = ExtractSecretFromUri(otpAuthUri);

        // Compute a valid TOTP from the seed (reuse AuthTestFixture's RFC 6238 helper — do not reimplement).
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);

        var verifyResp = await SendAuthedAsync(
            client, HttpMethod.Post, "/api/auth/mfa/enroll/verify", sessionCookie, body: new { code });
        verifyResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "MFA enroll/verify is authenticated — the backup-code insert under ceres_app must resolve the owner's " +
            "RLS GUC; a 500 here would mean the UserMfaBackupCodes insert hit 42501");
        var verifyBody = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        verifyBody.GetProperty("backupCodes").EnumerateArray().Should().HaveCount(10);

        // The 10 persisted, unused backup-code rows are visible to the owner under ceres_app and to no one else.
        await AssertRlsVisibility<UserMfaBackupCode>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: c => c.UserId == _userId && c.UsedAt == null, expectedOwnerCount: 10);
    }

    /// <summary>
    /// Sends an authenticated request with the session cookie + a user-bound CSRF pair, mirroring
    /// the cookie/header wiring MfaEnrollmentTests uses for the same endpoints.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthedAsync(
        HttpClient client, HttpMethod method, string url, string sessionCookie, object? body)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(Factory, _userId);
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return await client.SendAsync(req);
    }

    private static string ExtractSecretFromUri(string otpAuthUri)
    {
        var uri = new Uri(otpAuthUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["secret"]!;
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
