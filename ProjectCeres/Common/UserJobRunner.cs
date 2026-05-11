using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common;

public sealed class UserJobRunner(
    AppDbContext db,
    IUserScope scope,
    ILogger<UserJobRunner> logger) : IUserJobRunner
{
    public async Task ForEachUserAsync(
        Expression<Func<ApplicationUser, bool>> filter,
        Func<Guid, Task> work,
        CancellationToken ct = default)
    {
        // Cross-tenant by design: this is the entry point that enumerates the user list.
        // AspNetUsers carries no query filter; the Stage 7 architecture test grants this
        // file the IgnoreQueryFilters() allow-list exemption.
        var userIds = await db.Users
            .IgnoreQueryFilters()
            .Where(filter)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;
            using (scope.EnterAs(userId))
            {
                try
                {
                    await work(userId);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Cooperative cancellation — propagate so the loop exits.
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Per-user job failed for {UserId}", userId);
                }
            }
        }
    }
}
