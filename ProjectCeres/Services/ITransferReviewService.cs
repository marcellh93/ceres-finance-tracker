using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface ITransferReviewService
{
    Task<IReadOnlyList<ImportStagedTransfer>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    /// <summary>Link staged row to an existing transaction on the other side, creating a Transfer record.</summary>
    Task LinkToExistingAsync(Guid stagedId, Guid otherAccountId);
    /// <summary>User specifies which account the other side belongs to. Creates Transfer record.</summary>
    Task CreateAsTransferAsync(Guid stagedId, Guid otherAccountId);
    /// <summary>Import staged row as a plain transaction; save description to exclusion store.</summary>
    Task DismissAsTransactionAsync(Guid stagedId);
}
