using System.Linq.Expressions;

namespace ProjectCeres.Common;

public static class QueryableExtensions
{
    /// <summary>
    /// Restricts a query to rows owned by the current user. Apply this on every read of
    /// a user-owned DbSet — it is the single chokepoint that prevents IDOR by ensuring no
    /// query returns another user's data.
    /// </summary>
    public static IQueryable<T> Owned<T>(this IQueryable<T> source, ICurrentUserAccessor user)
        where T : class, IUserOwned
        => source.Where(BuildOwnedPredicate<T>(user.UserId));

    private static Expression<Func<T, bool>> BuildOwnedPredicate<T>(Guid userId) where T : IUserOwned
    {
        var p    = Expression.Parameter(typeof(T), "x");
        var body = Expression.Equal(
            Expression.Property(p, nameof(IUserOwned.UserId)),
            Expression.Constant(userId));
        return Expression.Lambda<Func<T, bool>>(body, p);
    }
}
