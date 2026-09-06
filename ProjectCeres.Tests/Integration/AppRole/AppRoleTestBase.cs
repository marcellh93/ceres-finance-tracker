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
    ///
    /// Stage 12.8.2: both queries call <c>IgnoreQueryFilters()</c> so the ONLY isolation
    /// mechanism left is the Postgres RLS policy. Without it, EF's global query filter
    /// (HasQueryFilter, application-layer) already restricts each context to its own UserId,
    /// so the negative assertion passed even with RLS disabled — the test proved the EF filter
    /// worked, not that the database enforces isolation. RlsOffMakesOtherUserSeeOwnerRowTests
    /// pins that this stripping is load-bearing.
    /// </summary>
    protected Task AssertRlsVisibility<TEntity>(
        Guid owner, Guid otherUser, Expression<Func<TEntity, bool>> predicate, int expectedOwnerCount)
        where TEntity : class =>
        AssertRlsVisibilityCore(owner, otherUser, predicate, expectedOwnerCount, Factory);

    /// <summary>
    /// Static core so a meta-test can drive the same assertion shape against a factory it
    /// controls (and toggle RLS around it). Both reads strip the EF query filter — see the
    /// instance method's remarks.
    /// </summary>
    internal static async Task AssertRlsVisibilityCore<TEntity>(
        Guid owner, Guid otherUser, Expression<Func<TEntity, bool>> predicate, int expectedOwnerCount,
        DualContextWebApplicationFactory factory)
        where TEntity : class
    {
        await using (var appOwner = factory.NewAppContext(owner))
        {
            (await appOwner.Context.Set<TEntity>().IgnoreQueryFilters().CountAsync(predicate))
                .Should().Be(expectedOwnerCount, "the owner's ceres_app context must see its own row(s)");
        }
        await using (var appOther = factory.NewAppContext(otherUser))
        {
            (await appOther.Context.Set<TEntity>().IgnoreQueryFilters().CountAsync(predicate))
                .Should().Be(0, "a different user's ceres_app context must NOT see the owner's row(s) — "
                    + "with the EF filter stripped, only the Postgres RLS policy can enforce this");
        }
    }
}
