using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IImportStagedTransactionService
{
    Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task<Result> TryConfirmAsync(Guid id);
    Task<Result> TryConfirmAllAsync();
    Task<Result> TryDisputeAsync(Guid id);
}
