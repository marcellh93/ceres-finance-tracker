using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ICategoryBudgetService
{
    Task<IEnumerable<CategoryBudget>> GetAllAsync(bool includeInactive = false, string? currency = null);
    Task<CategoryBudget?> GetByIdAsync(Guid id);
    Task<CategoryBudget> CreateAsync(CategoryBudgetCreateViewModel vm);
    Task UpdateAsync(CategoryBudgetEditViewModel vm);
    Task DeactivateAsync(Guid id);
    Task ReactivateAsync(Guid id);
    Task<decimal> GetActualSpendAsync(Guid id, int year, int month);
}
