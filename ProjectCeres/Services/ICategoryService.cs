using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ICategoryService
{
    // Razor-era methods (throwing). Used by CategoriesController.
    Task<IEnumerable<Category>> GetAllAsync(bool includeInactive = false);
    Task<Category?> GetByIdAsync(Guid id);
    Task<Category> CreateAsync(CategoryCreateViewModel vm);
    Task UpdateAsync(CategoryEditViewModel vm);
    /// <summary>Sets IsActive = false. Throws if category IsSystem.</summary>
    Task DeactivateAsync(Guid id);

    // API methods (Result-returning). Used by CategoriesApiController.
    Task<Result<Category>> TryCreateAsync(CreateCategoryRequest request);
    Task<Result<Category>> TryUpdateAsync(Guid id, UpdateCategoryRequest request);
    Task<Result> TryDeactivateAsync(Guid id);
}
