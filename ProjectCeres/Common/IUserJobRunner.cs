using System.Linq.Expressions;
using ProjectCeres.Models;

namespace ProjectCeres.Common;

public interface IUserJobRunner
{
    /// <summary>
    /// Enumerates users matching <paramref name="filter"/>, enters a per-user
    /// <see cref="IUserScope"/>, and invokes <paramref name="work"/> in turn. One user's
    /// failure does not abort the batch; the runner's own <paramref name="ct"/> does.
    /// </summary>
    Task ForEachUserAsync(
        Expression<Func<ApplicationUser, bool>> filter,
        Func<Guid, Task> work,
        CancellationToken ct = default);
}
