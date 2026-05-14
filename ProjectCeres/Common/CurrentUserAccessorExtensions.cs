using ProjectCeres.Common.Exceptions;

namespace ProjectCeres.Common;

/// <summary>
/// Stage 7.6.7 / ADR-0073 — service-layer convenience for code paths that require a
/// <see cref="UserContext.Resolved"/> context.
/// </summary>
public static class CurrentUserAccessorExtensions
{
    /// <summary>
    /// Returns the current <see cref="UserContext.Resolved"/> case, or throws
    /// <see cref="UserContextRequiredException"/> naming the actual case if anything
    /// else is current. Use at service-layer entry points where "no user resolved"
    /// is a bug, not a recoverable state.
    /// </summary>
    public static UserContext.Resolved Require(this ICurrentUserAccessor accessor)
    {
        if (accessor.Context is UserContext.Resolved resolved)
            return resolved;

        throw new UserContextRequiredException(
            expected: nameof(UserContext.Resolved),
            actual: accessor.Context.GetType().Name);
    }
}
