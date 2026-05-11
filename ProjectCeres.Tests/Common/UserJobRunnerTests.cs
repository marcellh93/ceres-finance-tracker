using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Common;

[Collection("IntegrationTests")]
public class UserJobRunnerTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    private const string TestEmailSuffix = "@user-job-runner-test.local";

    public UserJobRunnerTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Email!.EndsWith(TestEmailSuffix)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ForEachUserAsync_enters_scope_per_user_in_turn()
    {
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, $"a-{Guid.NewGuid():N}{TestEmailSuffix}");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, $"b-{Guid.NewGuid():N}{TestEmailSuffix}");

        var observed = new List<Guid>();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userScope = new UserScope();
        var runner = new UserJobRunner(db, userScope, NullLogger<UserJobRunner>.Instance);

        await runner.ForEachUserAsync(
            u => u.Id == userA.Id || u.Id == userB.Id,
            _ =>
            {
                observed.Add(userScope.Current!.Value);
                return Task.CompletedTask;
            });

        observed.Should().BeEquivalentTo(new[] { userA.Id, userB.Id });
    }

    [Fact]
    public async Task ForEachUserAsync_continues_after_one_user_throws()
    {
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, $"fail-{Guid.NewGuid():N}{TestEmailSuffix}");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, $"ok-{Guid.NewGuid():N}{TestEmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runner = new UserJobRunner(db, new UserScope(), NullLogger<UserJobRunner>.Instance);
        var succeeded = new List<Guid>();

        await runner.ForEachUserAsync(
            u => u.Id == userA.Id || u.Id == userB.Id,
            id =>
            {
                if (id == userA.Id) throw new InvalidOperationException("boom");
                succeeded.Add(id);
                return Task.CompletedTask;
            });

        succeeded.Should().ContainSingle().Which.Should().Be(userB.Id);
    }

    [Fact]
    public async Task Scope_unwinds_after_per_user_work_throws()
    {
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, $"u-{Guid.NewGuid():N}{TestEmailSuffix}");
        using var diScope = _factory.Services.CreateScope();
        var db = diScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userScope = new UserScope();
        var runner = new UserJobRunner(db, userScope, NullLogger<UserJobRunner>.Instance);

        await runner.ForEachUserAsync(
            u => u.Id == userA.Id,
            _ => throw new InvalidOperationException("boom"));

        userScope.Current.Should().BeNull("scope must unwind even when per-user work throws");
    }
}
