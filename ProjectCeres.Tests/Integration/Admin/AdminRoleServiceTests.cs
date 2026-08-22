using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Admin;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.Admin;

public class AppRolesTests
{
    [Fact]
    public void Admin_role_name_is_the_exact_string_used_by_authorize_attributes()
    {
        AppRoles.Admin.Should().Be("Admin",
            "the value is duplicated in [Authorize(Roles = \"Admin\")] attributes, " +
            "which take a literal string and cannot reference the constant");
    }
}

[Collection("IntegrationTests")]
public class AdminRoleServiceTests : IAsyncLifetime
{
    private const string EmailSuffix = "@admin-role-test.local";
    private readonly AuthTestWebApplicationFactory _factory;

    public AdminRoleServiceTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task EnsureRoleExistsAsync_is_safe_to_call_twice()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
        var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await svc.EnsureRoleExistsAsync();
        await svc.EnsureRoleExistsAsync();

        (await rm.RoleExistsAsync(AppRoles.Admin)).Should().BeTrue();
    }

    [Fact]
    public async Task GrantAsync_makes_the_user_an_admin()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"grant{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        (await svc.IsAdminAsync(user.Id)).Should().BeFalse("a freshly registered user is not an admin");

        var granted = await svc.GrantAsync(user.Id);

        granted.Should().BeTrue();
        (await svc.IsAdminAsync(user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task GrantAsync_is_idempotent()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"twice{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        await svc.GrantAsync(user.Id);
        var second = await svc.GrantAsync(user.Id);

        second.Should().BeTrue("granting an existing admin is a no-op, not a failure");
        (await svc.IsAdminAsync(user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task GrantAsync_returns_false_for_an_unknown_user()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        (await svc.GrantAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_removes_the_role()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"revoke{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        await svc.GrantAsync(user.Id);
        var revoked = await svc.RevokeAsync(user.Id);

        revoked.Should().BeTrue();
        (await svc.IsAdminAsync(user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AnyAdminExistsAsync_is_true_once_a_user_is_granted()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"exists{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        await svc.GrantAsync(user.Id);

        (await svc.AnyAdminExistsAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task AnyAdminExistsAsync_is_false_after_the_only_admin_is_revoked()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"existsfalse{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        await svc.GrantAsync(user.Id);
        (await svc.AnyAdminExistsAsync()).Should().BeTrue();

        await svc.RevokeAsync(user.Id);

        (await svc.AnyAdminExistsAsync()).Should().BeFalse(
            "the role row still exists but has no members, so this must read membership, not role existence");
    }

    [Fact]
    public async Task RevokeAsync_returns_false_for_an_unknown_user()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        (await svc.RevokeAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_returns_false_for_a_user_who_was_never_granted_admin()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"neveradmin{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        (await svc.RevokeAsync(user.Id)).Should().BeFalse();
    }
}

