using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IImportStagedTransactionService
{
    // Razor-era methods (throwing).
    Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task ConfirmAsync(Guid id);
    Task ConfirmAllAsync();
    Task DisputeAsync(Guid id);

    // API surface (Result-returning).
    Task<Result> TryConfirmAsync(Guid id);
    Task<Result> TryConfirmAllAsync();
    Task<Result> TryDisputeAsync(Guid id);
}
