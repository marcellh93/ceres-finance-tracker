using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Test double for <see cref="ICurrentUserAccessor"/>. Stage 7.6.7 / ADR-0073: takes a
/// <see cref="UserContext"/> directly. Convenience overload accepts a <see cref="Guid"/>
/// for the 80+ existing call sites:
/// <list type="bullet">
///   <item><see cref="Guid.Empty"/> → <see cref="UserContext.Uninitialized"/>.</item>
///   <item>Any other value → <see cref="UserContext.Resolved"/>.</item>
/// </list>
/// </summary>
public sealed class FakeCurrentUserAccessor : ICurrentUserAccessor
{
    public UserContext Context { get; }

    public FakeCurrentUserAccessor(UserContext context) => Context = context;

    public FakeCurrentUserAccessor(Guid userId)
        : this(userId == Guid.Empty
            ? UserContext.Uninitialized.Instance
            : new UserContext.Resolved(userId))
    {
    }
}
