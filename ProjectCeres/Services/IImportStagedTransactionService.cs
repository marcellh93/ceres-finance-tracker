using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IImportStagedTransactionService
{
    Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task ConfirmAsync(Guid id);
    Task ConfirmAllAsync();
    Task DisputeAsync(Guid id);
}
