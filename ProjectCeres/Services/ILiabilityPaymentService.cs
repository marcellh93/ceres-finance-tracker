using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ILiabilityPaymentService
{
    Task<LiabilityPayment?> GetByIdAsync(Guid id);
    /// <summary>
    /// Creates a liability payment. Throws if asset/liability types are wrong,
    /// currencies differ, or date is before either account's opening balance.
    /// </summary>
    Task<LiabilityPayment> CreateAsync(TransactionCreateViewModel vm);
    Task UpdateAsync(TransactionEditViewModel vm);
    /// <summary>Hard delete with no soft-delete fallback.</summary>
    Task DeleteAsync(Guid id);
    Task MarkClearedAsync(Guid id, bool cleared);
    Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null, string? currency = null);
}
