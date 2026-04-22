using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ICategoryBudgetService
{
    Task<IEnumerable<CategoryBudget>> GetAllAsync(bool includeInactive = false);
    Task<CategoryBudget?> GetByIdAsync(Guid id);
    Task<CategoryBudget> CreateAsync(CategoryBudgetCreateViewModel vm);
    Task UpdateAsync(CategoryBudgetEditViewModel vm);
    Task DeactivateAsync(Guid id);
    /// <summary>Derived actual spend: SUM of Expense transactions matching this budget's category
    /// and currency account for the specified calendar month.</summary>
    Task<decimal> GetActualSpendAsync(Guid id, int year, int month);
}
