using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class CategoryBudgetService(AppDbContext db) : ICategoryBudgetService
{
    public async Task<IEnumerable<CategoryBudget>> GetAllAsync(bool includeInactive = false)
    {
        var query = db.CategoryBudgets
            .Include(cb => cb.Category).ThenInclude(c => c.CategoryType)
            .Include(cb => cb.Currency)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(cb => cb.IsActive);

        return await query.OrderBy(cb => cb.Category.Name).ToListAsync();
    }

    public async Task<CategoryBudget?> GetByIdAsync(Guid id) =>
        await db.CategoryBudgets
            .Include(cb => cb.Category).ThenInclude(c => c.CategoryType)
            .Include(cb => cb.Currency)
            .FirstOrDefaultAsync(cb => cb.Id == id);

    public async Task<CategoryBudget> CreateAsync(CategoryBudgetCreateViewModel vm)
    {
        var category = await db.Categories
            .Include(c => c.CategoryType)
            .FirstOrDefaultAsync(c => c.Id == vm.CategoryId)
            ?? throw new InvalidOperationException("Category not found.");

        if (category.CategoryType.Name != "Expense")
            throw new InvalidOperationException(
                "CategoryBudget can only be applied to Expense categories.");

        var duplicateExists = await db.CategoryBudgets.AnyAsync(cb =>
            cb.CategoryId == vm.CategoryId &&
            cb.CurrencyId == vm.CurrencyId &&
            cb.IsActive);

        if (duplicateExists)
            throw new InvalidOperationException(
                "An active budget already exists for this category and currency.");

        var budget = new CategoryBudget
        {
            Id          = Guid.NewGuid(),
            CategoryId  = vm.CategoryId!.Value,
            CurrencyId  = vm.CurrencyId!.Value,
            LimitAmount = vm.LimitAmount,
            IsActive    = true
        };

        db.CategoryBudgets.Add(budget);
        await db.SaveChangesAsync();
        return budget;
    }

    public async Task UpdateAsync(CategoryBudgetEditViewModel vm)
    {
        var budget = await db.CategoryBudgets.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"CategoryBudget {vm.Id} not found.");

        budget.LimitAmount = vm.LimitAmount;
        await db.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid id)
    {
        var budget = await db.CategoryBudgets.FindAsync(id)
            ?? throw new InvalidOperationException($"CategoryBudget {id} not found.");

        budget.IsActive = false;
        await db.SaveChangesAsync();
    }

    public async Task<decimal> GetActualSpendAsync(Guid id, int year, int month)
    {
        var budget = await db.CategoryBudgets.FindAsync(id)
            ?? throw new InvalidOperationException($"CategoryBudget {id} not found.");

        var settings = await db.Settings.FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Settings row missing.");

        var (periodStart, periodEnd) =
            BudgetPeriod.GetBoundsForMonth(year, month, settings.BudgetPeriodStartDay);

        return await db.Transactions
            .Where(t =>
                t.CategoryId == budget.CategoryId &&
                t.Account.CurrencyId == budget.CurrencyId &&
                t.Date >= periodStart &&
                t.Date <= periodEnd)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
    }
}
