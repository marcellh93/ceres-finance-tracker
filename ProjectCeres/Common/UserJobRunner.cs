using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common;

public sealed class UserJobRunner(
    AdminDbContext db,
    IBackgroundJobScope backgroundScope,
    ILogger<UserJobRunner> logger) : IUserJobRunner
{
    public async Task ForEachUserAsync(
        Expression<Func<ApplicationUser, bool>> filter,
        Func<Guid, Task> work,
        CancellationToken ct = default)
    {
        // Cross-tenant by design: this is the entry point that enumerates the user list.
        // Backed by AdminDbContext (Postgres role ceres_admin with BYPASSRLS, Stage 7.5);
        // IgnoreQueryFilters() remains for defence-in-depth on the EF filter side, with
        // the Stage 7 architecture test granting this file the allow-list exemption.
        var userIds = await db.Users
            .IgnoreQueryFilters()
            .Where(filter)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                // Stage 7.5 Commit 6: routes through BackgroundJobScope which holds the
                // Guid.Empty doorway refusal. Direct IUserScope.EnterAs() calls outside
                // BackgroundJobScope are forbidden by an architecture test.
                await backgroundScope.RunAsync(userId, nameof(ForEachUserAsync), () => work(userId));
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
