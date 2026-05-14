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
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
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
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
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
        var db = diScope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userScope = new UserScope();
        var runner = new UserJobRunner(db, userScope, NullLogger<UserJobRunner>.Instance);

        await runner.ForEachUserAsync(
            u => u.Id == userA.Id,
            _ => throw new InvalidOperationException("boom"));

        userScope.Current.Should().BeNull("scope must unwind even when per-user work throws");
    }

    [Fact]
    public async Task ForEachUserAsync_exits_loop_early_when_token_cancels_between_users()
    {
        // Pins the pre-iteration cancellation check (`if (ct.IsCancellationRequested) break;`).
        // Two users, A and B. Work on A cancels the token. The runner must NOT invoke
        // work on B — even though both passed the filter and would otherwise iterate.
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, $"cancel-a-{Guid.NewGuid():N}{TestEmailSuffix}");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, $"cancel-b-{Guid.NewGuid():N}{TestEmailSuffix}");

        using var diScope = _factory.Services.CreateScope();
        var db = diScope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var runner = new UserJobRunner(db, new UserScope(), NullLogger<UserJobRunner>.Instance);
        using var cts = new CancellationTokenSource();
        var invoked = new List<Guid>();

        await runner.ForEachUserAsync(
            u => u.Id == userA.Id || u.Id == userB.Id,
            id =>
            {
                invoked.Add(id);
                if (id == userA.Id) cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token);

        invoked.Should().ContainSingle()
            .Which.Should().Be(userA.Id,
                "the pre-iteration cancellation check must skip user B's invocation once A's work cancelled the token");
    }

    [Fact]
    public async Task ForEachUserAsync_rethrows_OperationCanceledException_when_runners_own_token_fires()
    {
        // Pins the `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }`
        // branch. If a user's work throws OperationCanceledException AFTER the runner's own token
        // has fired, the runner must propagate the exception (it's cooperative cancellation, not a
        // per-user failure). The general `catch (Exception)` branch must NOT swallow this one.
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, $"rethrow-{Guid.NewGuid():N}{TestEmailSuffix}");

        using var diScope = _factory.Services.CreateScope();
        var db = diScope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var runner = new UserJobRunner(db, new UserScope(), NullLogger<UserJobRunner>.Instance);
        using var cts = new CancellationTokenSource();

        var act = async () => await runner.ForEachUserAsync(
            u => u.Id == userA.Id,
            _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "the runner's own cancellation must propagate, not be swallowed by the per-user exception isolation");
    }
}
