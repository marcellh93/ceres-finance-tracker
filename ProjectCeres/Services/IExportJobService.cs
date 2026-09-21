using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IExportJobService
{
    /// <summary>Returns the caller's existing Pending/Processing job, else inserts a new one.</summary>
    Task<ExportJob> CreateOrGetPendingAsync(CancellationToken ct);

    /// <summary>The caller's own job matching the token fingerprint. RLS hides other users' rows.</summary>
    Task<ExportJob?> FindOwnByTokenAsync(byte[] tokenLookup, CancellationToken ct);
}
