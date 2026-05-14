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
    public async Task Falls_back_to_en_when_no_settings_row()
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
