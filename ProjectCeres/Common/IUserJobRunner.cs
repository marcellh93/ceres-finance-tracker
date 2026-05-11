using System.Linq.Expressions;
using ProjectCeres.Models;

namespace ProjectCeres.Common;

public interface IUserJobRunner
{
    Task ForEachUserAsync(
        Expression<Func<ApplicationUser, bool>> filter,
        Func<Guid, Task> work,
        CancellationToken ct = default);
}
