using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ITransactionService
{
    Task<IEnumerable<TransactionListItemViewModel>> GetRecentAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 50,
        int offset = 0);
    Task<int> CountAsync(Guid? accountId = null, DateOnly? from = null, DateOnly? to = null);
    Task<TransactionEditViewModel?> GetByIdForEditAsync(Guid id);
    Task<Guid> CreateAsync(TransactionCreateViewModel vm);
    Task UpdateAsync(TransactionEditViewModel vm);
    /// <summary>Hard delete with no soft-delete fallback.</summary>
    Task DeleteAsync(Guid id);
    Task MarkClearedAsync(Guid id, bool cleared);
    Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null);
    Task MarkNeedsReviewAsync(Guid id, bool needsReview);
}
