using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ITransactionService
{
    Task<IEnumerable<Transaction>> GetRecentAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 50,
        int offset = 0);
    Task<int> CountAsync(Guid? accountId = null, DateOnly? from = null, DateOnly? to = null);
    Task<Transaction?> GetByIdAsync(Guid id);
    Task<Transaction> CreateAsync(TransactionCreateViewModel vm);
    Task UpdateAsync(TransactionEditViewModel vm);
    /// <summary>Hard delete with no soft-delete fallback.</summary>
    Task DeleteAsync(Guid id);
}
