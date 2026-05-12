using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.MultiTenancy;

[Collection("IntegrationTests")]
public class GlobalQueryFilterTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    private const string EmailSuffix = "@query-filter-test.local";

    public GlobalQueryFilterTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            await db.Accounts.IgnoreQueryFilters().Where(a => a.UserId == u.Id).ExecuteDeleteAsync();
            await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Account_query_filtered_to_current_user_via_IUserScope()
    {
        var userA = await ProjectCeres.Tests.Integration.Authentication.AuthTestFixture.RegisterUserAsync(_factory, $"a-{Guid.NewGuid():N}{EmailSuffix}");
        var userB = await ProjectCeres.Tests.Integration.Authentication.AuthTestFixture.RegisterUserAsync(_factory, $"b-{Guid.NewGuid():N}{EmailSuffix}");

        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedDb.Accounts.AddRange(
                new Account { Id = Guid.NewGuid(), Name = "A-acct", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = userA.Id },
                new Account { Id = Guid.NewGuid(), Name = "B-acct", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = userB.Id });
            await seedDb.SaveChangesAsync();
        }

        // Enter scope as User A and confirm the global query filter hides B's row WITHOUT any explicit .Where().
        using var queryScope = _factory.Services.CreateScope();
        var userScope = queryScope.ServiceProvider.GetRequiredService<IUserScope>();
        using (userScope.EnterAs(userA.Id))
        {
            var db = queryScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accounts = await db.Accounts.ToListAsync();
            accounts.Should().OnlyContain(a => a.UserId == userA.Id,
                "global query filter must hide cross-tenant rows from User A");
            accounts.Should().NotContain(a => a.Name == "B-acct");
        }
    }
}
