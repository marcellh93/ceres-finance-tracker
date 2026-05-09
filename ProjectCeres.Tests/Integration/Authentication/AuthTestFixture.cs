using System.Net.Http.Json;
using System.Security.Claims;
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
    /// Mints an antiforgery token pair from the running factory. If userId is non-null,
    /// the synthetic HttpContext used for minting carries that user's NameIdentifier
    /// claim, so the resulting token validates against authenticated requests for that
    /// user. If userId is null, the pair is anonymous-bound.
    /// </summary>
    public static (string CookieValue, string HeaderValue) MintCsrf(
        TestWebApplicationFactory factory, Guid? userId = null)
    {
        using var scope = factory.Services.CreateScope();
        var antiforgery = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (userId is { } id)
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                    new Claim(ClaimTypes.Name, id.ToString()),
                },
                authenticationType: "Test");
            ctx.User = new ClaimsPrincipal(identity);
        }
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        return (tokens.CookieToken!, tokens.RequestToken!);
    }

    /// <summary>
    /// POSTs JSON with a valid anonymous CSRF token attached. For the first POST
    /// in a test where the user is not yet authenticated (login, register).
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
