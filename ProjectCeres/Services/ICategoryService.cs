using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ICategoryService
{
    Task<IEnumerable<Category>> GetAllAsync(bool includeInactive = false);
    Task<Category?> GetByIdAsync(Guid id);

    // API methods (Result-returning). Used by CategoriesApiController.
    Task<Result<Category>> TryCreateAsync(CreateCategoryRequest request);
    Task<Result<Category>> TryUpdateAsync(Guid id, UpdateCategoryRequest request);
    Task<Result> TryDeactivateAsync(Guid id);
    Task<Result> TryReactivateAsync(Guid id);
}
