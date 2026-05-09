using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
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

        // Bypass the email-confirmation flow per spec § 8 transitional rule.
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmed = await userManager.ConfirmEmailAsync(user, token);
        confirmed.Succeeded.Should().BeTrue();
        return user;
    }
}
