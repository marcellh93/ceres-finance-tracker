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

[Collection("IntegrationParallel2")]
public class UserJobRunnerTests : IntegrationTestBase<Bucket2AuthFactory>, IAsyncLifetime
{
    private readonly Bucket2AuthFactory _factory;
    private const string TestEmailSuffix = "@user-job-runner-test.local";

    public UserJobRunnerTests(Bucket2AuthFactory factory, Bucket2Database bucketDb)
        : base(factory, bucketDb) => _factory = factory;

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
        var bgScope = new BackgroundJobScope(new FakeCurrentUserAccessor(UserContext.Uninitialized.Instance), userScope, NullLogger<BackgroundJobScope>.Instance);
        var runner = new UserJobRunner(db, bgScope, NullLogger<UserJobRunner>.Instance);

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
        var bgScope = new BackgroundJobScope(new FakeCurrentUserAccessor(UserContext.Uninitialized.Instance), new UserScope(), NullLogger<BackgroundJobScope>.Instance);
        var runner = new UserJobRunner(db, bgScope, NullLogger<UserJobRunner>.Instance);
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
        var bgScope = new BackgroundJobScope(new FakeCurrentUserAccessor(UserContext.Uninitialized.Instance), userScope, NullLogger<BackgroundJobScope>.Instance);
        var runner = new UserJobRunner(db, bgScope, NullLogger<UserJobRunner>.Instance);

        await runner.ForEachUserAsync(
            u => u.Id == userA.Id,
            _ => throw new InvalidOperationException("boom"));

        userScope.Current.Should().BeNull("scope must unwind even when per-user work throws");
    }

    [Fact]
    public async Task ForEachUserAsync_exits_loop_early_when_token_cancels_between_users()
    {
        // Pins the pre-iteration cancellation check (`if (ct.IsCancellationRequested) break;`).
        // Two users pass the filter. The FIRST user's work cancels the token; the runner must
        // NOT invoke work on the second — even though both would otherwise iterate.
        //
        // ForEachUserAsync does not ORDER BY (the enumeration order is Postgres's choice), so the
        // test must not assume which user comes first: it cancels on whichever the runner hits
        // first and asserts the other is skipped. The earlier version pinned a specific user and
        // passed only when Postgres happened to return that row first — a latent order-flake that
        // failed once the row order flipped (2026-09-07). The contract under test is order-
        // independent, so the test now is too.
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, $"cancel-a-{Guid.NewGuid():N}{TestEmailSuffix}");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, $"cancel-b-{Guid.NewGuid():N}{TestEmailSuffix}");

        using var diScope = _factory.Services.CreateScope();
        var db = diScope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var bgScope = new BackgroundJobScope(new FakeCurrentUserAccessor(UserContext.Uninitialized.Instance), new UserScope(), NullLogger<BackgroundJobScope>.Instance);
        var runner = new UserJobRunner(db, bgScope, NullLogger<UserJobRunner>.Instance);
        using var cts = new CancellationTokenSource();
        var invoked = new List<Guid>();

        await runner.ForEachUserAsync(
            u => u.Id == userA.Id || u.Id == userB.Id,
            id =>
            {
                invoked.Add(id);
                // Cancel on the first user the runner reaches, regardless of DB order.
                if (invoked.Count == 1) cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token);

        // Exactly one user was invoked (the second was skipped by the cancellation check), and it
        // was one of the two that passed the filter — without assuming which the DB returned first.
        invoked.Should().HaveCount(1, "the pre-iteration cancellation check must skip the second "
            + "user's invocation once the first user's work cancelled the token");
        invoked.Should().BeSubsetOf(new[] { userA.Id, userB.Id });
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
        var bgScope = new BackgroundJobScope(new FakeCurrentUserAccessor(UserContext.Uninitialized.Instance), new UserScope(), NullLogger<BackgroundJobScope>.Instance);
        var runner = new UserJobRunner(db, bgScope, NullLogger<UserJobRunner>.Instance);
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
