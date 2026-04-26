using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class CategoryService(AppDbContext db) : ICategoryService
{
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

    private static bool IsReserved(Guid id) => id == UncategorizedIncomeId || id == UncategorizedExpenseId;

    public async Task<IEnumerable<Category>> GetAllAsync(bool includeInactive = false)
    {
        var query = db.Categories
            .Include(c => c.CategoryType)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        return await query.OrderBy(c => c.CategoryType.Name).ThenBy(c => c.Name).ToListAsync();
    }

    public async Task<Category?> GetByIdAsync(Guid id) =>
        await db.Categories
            .Include(c => c.CategoryType)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<Category> CreateAsync(CategoryCreateViewModel vm)
    {
        var category = new Category
        {
            Id             = Guid.NewGuid(),
            Name           = vm.Name,
            CategoryTypeId = vm.CategoryTypeId!.Value,
            IsActive       = true,
            IsSystem       = false,
            LifestyleTag   = vm.LifestyleTag
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    public async Task UpdateAsync(CategoryEditViewModel vm)
    {
        var category = await db.Categories.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Category {vm.Id} not found.");

        if (category.IsSystem || IsReserved(category.Id))
            throw new InvalidOperationException("System categories cannot be modified.");

        category.Name         = vm.Name;
        category.LifestyleTag = vm.LifestyleTag;
        await db.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid id)
    {
        var category = await db.Categories.FindAsync(id)
            ?? throw new InvalidOperationException($"Category {id} not found.");

        if (category.IsSystem || IsReserved(category.Id))
            throw new InvalidOperationException("System categories cannot be modified.");

        category.IsActive = false;
        await db.SaveChangesAsync();
    }
}
