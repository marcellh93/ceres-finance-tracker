using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tools;

/// <summary>
/// Cron entry: executes Sealed ErasureRequest rows whose 72-hour cancel window has
/// elapsed. Cross-tenant by design (acts for no single user) — same AdminDbContext +
/// IgnoreQueryFilters pattern as ExportJobWorker/SweepSessions. Stage 13.9 Task 8/10.
/// Invoked via `dotnet run --project ProjectCeres -- --run-erasure-jobs`.
/// </summary>
[RequiresAdminContext]
public static class ErasureWorker
{
    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var logger = sp.GetRequiredService<ILogger<Program>>();

        var touched = await ProcessEligibleAsync(
            sp.GetRequiredService<AdminDbContext>(),
            sp.GetRequiredService<ErasureExecutor>(),
            sp.GetRequiredService<TimeProvider>(),
            logger,
            CancellationToken.None);

        return touched >= 0 ? 0 : 1;
    }

    /// <summary>
    /// Executes every Sealed request past its ExecuteAfter time. Re-checks Status ==
    /// Sealed immediately before invoking the executor — a defense-in-depth guard on
    /// top of ErasureExecutor's own internal claim-check, since a user can cancel
    /// between this query and the execution attempt. Returns the count actually executed.
    /// </summary>
    public static async Task<int> ProcessEligibleAsync(
        AdminDbContext db,
        ErasureExecutor executor,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var touched = 0;

        // Cross-tenant by design: the worker acts for no single user. Allow-listed in
        // ArchitectureTests.IgnoreQueryFilters_only_appears_in_documented_exception_paths.
        var eligible = await db.ErasureRequests
            .IgnoreQueryFilters()
            .Where(r => r.Status == ErasureStatus.Sealed && r.ExecuteAfter <= now)
            .ToListAsync(ct);

        foreach (var request in eligible)
        {
            // Re-read: a fresh read, not the stale list item — the user may have
            // cancelled between the query above and this iteration.
            var current = await db.ErasureRequests
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.Id == request.Id)
                .Select(r => r.Status)
                .FirstOrDefaultAsync(ct);
            if (current != ErasureStatus.Sealed) continue;

            await executor.ExecuteAsync(request.UserId, ct);
            touched++;
        }

        logger.LogInformation("Erasure sweep processed {Count} requests.", touched);
        return touched;
    }
}
