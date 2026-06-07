using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class RegisterWritesUnderRlsTests : AppRoleTestBase
{
    private readonly List<Guid> _seededUsers = new();

    public RegisterWritesUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Register_under_ceres_app_seeds_categories_visible_only_to_the_new_user()
    {
        var email = $"reg-{Marker}@approle-test.local";
        var client = Factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/register", new { email, password = AuthTestFixture.ValidPassword });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        Guid newUserId;
        await using (var admin = Factory.NewAdminContext())
        {
            var user = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            newUserId = user.Id;
            _seededUsers.Add(newUserId);
        }

        await AssertRlsVisibility<Category>(
            owner: newUserId, otherUser: Guid.NewGuid(),
            predicate: c => c.UserId == newUserId, expectedOwnerCount: 26);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        foreach (var uid in _seededUsers)
        {
            await admin.Context.Categories.IgnoreQueryFilters().Where(c => c.UserId == uid).ExecuteDeleteAsync();
            await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == uid).ExecuteDeleteAsync();
            await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == uid).ExecuteDeleteAsync();
        }
    }
}
