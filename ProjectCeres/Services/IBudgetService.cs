using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IBudgetService
{
    Task<IEnumerable<Budget>> GetAllAsync(
        bool includeInactive = false,
        string? currency = null,
        string? type = null);

    Task<Budget?> GetByIdAsync(Guid id);
    Task<Budget> CreateAsync(BudgetCreateViewModel vm);
    Task UpdateAsync(BudgetEditViewModel vm);
    Task DeactivateAsync(Guid id);
    Task ReactivateAsync(Guid id);
    /// <summary>Derived actual spend: SUM of transaction amounts linked to this budget.</summary>
    Task<decimal> GetActualSpendAsync(Guid id);
    /// <summary>Progress toward goal target. Spending goals sum tagged transactions;
    /// Savings goals read the linked account balance.</summary>
    Task<BudgetProgressResult> GetProgressAsync(Guid id);
}
