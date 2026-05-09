using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Shared helpers for Stage 6a auth integration tests. RegisterUserAsync bypasses
/// the email-confirmation flow per spec § 8 transitional rule (production flow
/// is register → email-confirm → login; in 6a there is no email service).
/// </summary>
public static class AuthTestFixture
{
    public const string ValidPassword = "correct horse battery staple";

    public static async Task<ApplicationUser> RegisterUserAsync(
        TestWebApplicationFactory factory, string email, string password = ValidPassword)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue("expected user to be created: {0}",
            string.Join(", ", result.Errors.Select(e => e.Description)));

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmed = await userManager.ConfirmEmailAsync(user, token);
        confirmed.Succeeded.Should().BeTrue();
        return user;
    }

    /// <summary>
    /// Mints a fresh antiforgery token pair from the running factory and returns
    /// (cookieValue, headerValue). The test then forwards the cookie + the X-XSRF-TOKEN
    /// header on the POST/PUT/PATCH/DELETE request — same shape the React client uses.
    /// </summary>
    public static (string CookieValue, string HeaderValue) MintCsrf(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var antiforgery = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var ctx = new DefaultHttpContext();
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        return (tokens.CookieToken!, tokens.RequestToken!);
    }

    /// <summary>
    /// POSTs JSON with a valid CSRF token attached. Mirrors what the React client does:
    /// read the __Host-XSRF cookie value and forward it as X-XSRF-TOKEN.
    /// </summary>
    public static Task<HttpResponseMessage> PostJsonWithCsrfAsync<T>(
        TestWebApplicationFactory factory, HttpClient client, string url, T body)
    {
        var (cookie, header) = MintCsrf(factory);
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={cookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return client.SendAsync(req);
    }
}
