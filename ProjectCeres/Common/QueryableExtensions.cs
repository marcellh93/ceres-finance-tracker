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

    /// <summary>
    /// For entities where shared rows (UserId IS NULL) coexist with user-owned rows.
    /// Returns rows whose UserId matches the current user OR is null (shared).
    /// </summary>
    public static IQueryable<T> OwnedOrShared<T>(this IQueryable<T> source, ICurrentUserAccessor user)
        where T : class, IOptionallyUserOwned
        => source.Where(BuildOwnedOrSharedPredicate<T>(user.UserId));

    private static Expression<Func<T, bool>> BuildOwnedPredicate<T>(Guid userId) where T : IUserOwned
    {
        var p = Expression.Parameter(typeof(T), "x");
        var body = Expression.Equal(
            Expression.Property(p, nameof(IUserOwned.UserId)),
            Expression.Constant(userId));
        return Expression.Lambda<Func<T, bool>>(body, p);
    }

    private static Expression<Func<T, bool>> BuildOwnedOrSharedPredicate<T>(Guid userId) where T : IOptionallyUserOwned
    {
        var p = Expression.Parameter(typeof(T), "x");
        var prop = Expression.Property(p, nameof(IOptionallyUserOwned.UserId));
        var equalsUser = Expression.Equal(prop, Expression.Convert(Expression.Constant(userId), typeof(Guid?)));
        var isNull = Expression.Equal(prop, Expression.Constant(null, typeof(Guid?)));
        var body = Expression.OrElse(equalsUser, isNull);
        return Expression.Lambda<Func<T, bool>>(body, p);
    }
}
