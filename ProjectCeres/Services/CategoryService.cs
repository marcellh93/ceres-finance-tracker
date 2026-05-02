using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class CategoryService(AppDbContext db, ICurrentUserAccessor user) : ICategoryService
{
    private static bool IsReserved(Guid id) => CategoryPolicies.IsReserved(id);

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

    // -------------------------------------------------------------------------
    // API surface (Result-returning).
    // -------------------------------------------------------------------------

    public async Task<Result<Category>> TryCreateAsync(CreateCategoryRequest request)
    {
        var typeExists = await db.CategoryTypes.AnyAsync(t => t.Id == request.CategoryTypeId!.Value);
        if (!typeExists)
            return Result<Category>.Fail("INVALID_CATEGORY_TYPE", "The selected category type does not exist.");

        var category = new Category
        {
            Id             = Guid.NewGuid(),
            Name           = request.Name.Trim(),
            CategoryTypeId = request.CategoryTypeId!.Value,
            IsActive       = true,
            IsSystem       = false,
            LifestyleTag   = string.IsNullOrWhiteSpace(request.LifestyleTag) ? null : request.LifestyleTag,
            UserId         = user.UserId
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var fresh = await db.Categories
            .Include(c => c.CategoryType)
            .FirstAsync(c => c.Id == category.Id);
        return Result<Category>.Ok(fresh);
    }

    public async Task<Result<Category>> TryUpdateAsync(Guid id, UpdateCategoryRequest request)
    {
        var category = await db.Categories
            .Include(c => c.CategoryType)
            .OwnedOrShared(user)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
            return Result<Category>.Fail("NOT_FOUND", "Category not found.");

        var policy = CategoryPolicies.CanEdit(category);
        if (!policy.IsSuccess)
            return Result<Category>.Fail(policy.Error!.Value.Code, policy.Error!.Value.Message);

        category.Name         = request.Name.Trim();
        category.LifestyleTag = string.IsNullOrWhiteSpace(request.LifestyleTag) ? null : request.LifestyleTag;
        await db.SaveChangesAsync();
        return Result<Category>.Ok(category);
    }

    public async Task<Result> TryDeactivateAsync(Guid id)
    {
        var category = await db.Categories
            .OwnedOrShared(user)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
            return Result.Fail("NOT_FOUND", "Category not found.");

        var hasTransactions = await db.Transactions.AnyAsync(t => t.CategoryId == id);
        var policy = CategoryPolicies.CanDeactivate(category, hasTransactions);
        if (!policy.IsSuccess) return policy;

        category.IsActive = false;
        await db.SaveChangesAsync();
        return Result.Ok();
    }
}
