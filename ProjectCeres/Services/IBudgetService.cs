using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IBudgetService
{
    Task<IEnumerable<Budget>> GetAllAsync(bool includeInactive = false);
    Task<Budget?> GetByIdAsync(Guid id);
    Task<Budget> CreateAsync(BudgetCreateViewModel vm);
    Task UpdateAsync(BudgetEditViewModel vm);
    Task DeactivateAsync(Guid id);
    /// <summary>Derived actual spend: SUM of transaction amounts linked to this budget.</summary>
    Task<decimal> GetActualSpendAsync(Guid id);
}
