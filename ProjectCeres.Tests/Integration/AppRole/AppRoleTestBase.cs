using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 9.5d. Shared shape for AppRole tests: a unique per-test marker, the admin-context
/// cleanup discipline (cross-user deletes must run BYPASSRLS), and the positive+negative RLS
/// visibility assertion. Subclasses override DisposeAsync to delete their seeded rows.
/// </summary>
public abstract class AppRoleTestBase : IAsyncLifetime
{
    protected readonly AppRoleFixture Fixture;
    protected DualContextWebApplicationFactory Factory => Fixture.Factory;
    protected readonly string Marker = $"approle-{Guid.NewGuid():N}";

    protected AppRoleTestBase(AppRoleFixture fixture) => Fixture = fixture;

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public abstract Task DisposeAsync();

    /// <summary>
    /// Positive + negative RLS control: the owner's ceres_app context sees exactly
    /// <paramref name="expectedOwnerCount"/> matching rows; a different user's context sees zero.
    /// </summary>
    protected async Task AssertRlsVisibility<TEntity>(
        Guid owner, Guid otherUser, Expression<Func<TEntity, bool>> predicate, int expectedOwnerCount)
        where TEntity : class
    {
        await using (var appOwner = Factory.NewAppContext(owner))
        {
            (await appOwner.Context.Set<TEntity>().CountAsync(predicate))
                .Should().Be(expectedOwnerCount, "the owner's ceres_app context must see its own row(s)");
        }
        await using (var appOther = Factory.NewAppContext(otherUser))
        {
            (await appOther.Context.Set<TEntity>().CountAsync(predicate))
                .Should().Be(0, "a different user's ceres_app context must NOT see the owner's row(s)");
        }
    }
}
