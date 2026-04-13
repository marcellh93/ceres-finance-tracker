using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ICategoryService
{
    Task<IEnumerable<Category>> GetAllAsync(bool includeInactive = false);
    Task<Category?> GetByIdAsync(Guid id);
    Task<Category> CreateAsync(CategoryCreateViewModel vm);
    Task UpdateAsync(CategoryEditViewModel vm);
    /// <summary>Sets IsActive = false. Throws if category IsSystem.</summary>
    Task DeactivateAsync(Guid id);
}
