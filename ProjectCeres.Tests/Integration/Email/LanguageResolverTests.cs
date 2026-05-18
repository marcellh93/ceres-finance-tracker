using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection("IntegrationTests")]
public sealed class LanguageResolverTests
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LanguageResolverTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Falls_back_to_CurrentUICulture_en_when_no_settings_row()
    {
        // Stage 9.6.1 (2026-05-18): the resolver now falls back to the
        // request's CurrentUICulture (set by LanguagePreferenceMiddleware
        // from the `lang` cookie) when Settings.Language is empty. With
        // CurrentUICulture explicitly set to en here, the fallback chooses en.
        var prev = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        try
        {
            using var scope = _factory.Services.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<ILanguageResolver>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = $"lang-{Guid.NewGuid():N}@example.invalid",
                Email = $"lang-{Guid.NewGuid():N}@example.invalid",
            };
            (await users.CreateAsync(user, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();

            var culture = await resolver.ResolveForUserAsync(user.Id, CancellationToken.None);

            culture.TwoLetterISOLanguageName.Should().Be("en");
        }
        finally
        {
            CultureInfo.CurrentUICulture = prev;
        }
    }

    [Fact]
    public async Task Falls_back_to_CurrentUICulture_es_when_no_settings_row_and_request_is_spanish()
    {
        // Stage 9.6.1 (2026-05-18) regression test for the password-reset
        // bug: when the SPA is in Spanish (lang cookie = es →
        // LanguagePreferenceMiddleware sets CurrentUICulture = es) but the
        // user has not saved a Settings.Language preference yet, outbound
        // emails should match the request's language rather than defaulting
        // to en. Pre-fix the resolver hit the `_ => En` branch unconditionally.
        var prev = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("es");
        try
        {
            using var scope = _factory.Services.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<ILanguageResolver>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = $"lang-{Guid.NewGuid():N}@example.invalid",
                Email = $"lang-{Guid.NewGuid():N}@example.invalid",
            };
            (await users.CreateAsync(user, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();

            var culture = await resolver.ResolveForUserAsync(user.Id, CancellationToken.None);

            culture.TwoLetterISOLanguageName.Should().Be("es");
        }
        finally
        {
            CultureInfo.CurrentUICulture = prev;
        }
    }

    [Fact]
    public async Task Settings_language_wins_over_request_CurrentUICulture()
    {
        // Stage 9.6.1: the persistent user preference is the SOURCE OF TRUTH.
        // A user who has explicitly saved Settings.Language = "en" must keep
        // getting English emails even if their current browser session is
        // toggled to Spanish — flipping every transactional email based on a
        // one-off browser-language change would surprise the user. The
        // request-cookie fallback only applies when Settings.Language is empty.
        var prev = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("es");
        try
        {
            using var scope = _factory.Services.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<ILanguageResolver>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new ApplicationUser
            {
                UserName = $"lang-{Guid.NewGuid():N}@example.invalid",
                Email = $"lang-{Guid.NewGuid():N}@example.invalid",
            };
            (await users.CreateAsync(user, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();

            db.Set<Settings>().Add(new Settings
            {
                UserId = user.Id,
                NumberFormat = "1,234.56",
                DateFormat = "MM/dd/yyyy",
                DefaultCurrencyId = 1,
                Language = "en",
            });
            await db.SaveChangesAsync();

            var culture = await resolver.ResolveForUserAsync(user.Id, CancellationToken.None);

            culture.TwoLetterISOLanguageName.Should().Be("en");
        }
        finally
        {
            CultureInfo.CurrentUICulture = prev;
        }
    }

    [Fact]
    public async Task Resolves_es_when_settings_language_is_es()
    {
        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ILanguageResolver>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new ApplicationUser
        {
            UserName = $"lang-{Guid.NewGuid():N}@example.invalid",
            Email = $"lang-{Guid.NewGuid():N}@example.invalid",
        };
        (await users.CreateAsync(user, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();

        db.Set<Settings>().Add(new Settings
        {
            UserId = user.Id,
            NumberFormat = "1.234,56",
            DateFormat = "dd/MM/yyyy",
            DefaultCurrencyId = 1,
            Language = "es",
        });
        await db.SaveChangesAsync();

        var culture = await resolver.ResolveForUserAsync(user.Id, CancellationToken.None);

        culture.TwoLetterISOLanguageName.Should().Be("es");
    }
}
