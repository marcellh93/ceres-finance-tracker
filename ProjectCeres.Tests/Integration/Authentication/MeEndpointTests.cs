using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel4")]
public class MeEndpointTests : IntegrationTestBase<AuthTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MeEndpointTests(AuthTestWebApplicationFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@me-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Me_returns_401_when_anonymous()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_user_shape_when_authenticated()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "shape@me-test.local");
        var client = await AuthTestFixture.AuthenticatedClientAsync(_factory, user.Id);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponseShape>();
        body!.UserId.Should().Be(user.Id);
        body.Email.Should().Be("shape@me-test.local");
        body.TwoFactorEnabled.Should().BeFalse();
        body.BackupCodesRemaining.Should().Be(0);
        body.UsedBackupCodeAtLastLogin.Should().BeFalse();
        body.LastReauthAt.Should().BeNull();
    }

    [Fact]
    public async Task Me_sets_no_store_cache_control()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "cache@me-test.local");
        var client = await AuthTestFixture.AuthenticatedClientAsync(_factory, user.Id);

        var response = await client.GetAsync("/api/auth/me");

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private sealed record MeResponseShape(
        Guid UserId,
        string Email,
        bool TwoFactorEnabled,
        long? LastReauthAt,
        int BackupCodesRemaining,
        bool UsedBackupCodeAtLastLogin);
}
