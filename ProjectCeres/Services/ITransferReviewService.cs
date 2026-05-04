using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface ITransferReviewService
{
    Task<IReadOnlyList<ImportStagedTransfer>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task<Result> TryLinkToExistingAsync(Guid stagedId, Guid otherAccountId);
    Task<Result> TryCreateAsTransferAsync(Guid stagedId, Guid otherAccountId);
    Task<Result> TryDismissAsTransactionAsync(Guid stagedId);
}
