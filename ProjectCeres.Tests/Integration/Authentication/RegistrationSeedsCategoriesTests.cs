using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel2")]
public class RegistrationSeedsCategoriesTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    private const string EmailSuffix = "@reg-seed-cat-test.local";

    public RegistrationSeedsCategoriesTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Registering_a_new_user_creates_26_per_user_categories()
    {
        var email = $"u-{Guid.NewGuid():N}{EmailSuffix}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.Categories
            .IgnoreQueryFilters()
            .CountAsync(c => c.UserId == user.Id);

        count.Should().Be(26, "every registration seeds the canonical default list");
    }

    [Fact]
    public async Task Two_users_get_independent_category_copies()
    {
        var a = await AuthTestFixture.RegisterUserAsync(_factory, $"a-{Guid.NewGuid():N}{EmailSuffix}");
        var b = await AuthTestFixture.RegisterUserAsync(_factory, $"b-{Guid.NewGuid():N}{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var aIds = await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == a.Id).Select(c => c.Id).ToListAsync();
        var bIds = await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == b.Id).Select(c => c.Id).ToListAsync();

        aIds.Should().HaveCount(26);
        bIds.Should().HaveCount(26);
        aIds.Intersect(bIds).Should().BeEmpty("categories are per-user copies with their own GUIDs");
    }

    [Fact]
    public async Task Re_registering_an_already_seeded_user_is_idempotent()
    {
        // Direct service call without going through registration. Confirms the
        // idempotency check works (no duplicate rows on a second call).
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"idem-{Guid.NewGuid():N}{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<Services.CategorySeedService>();
        await seed.CopyDefaultsForUserAsync(user.Id);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == user.Id);
        count.Should().Be(26, "second seed call must not duplicate rows");
    }
}
