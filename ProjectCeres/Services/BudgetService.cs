using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class BudgetService(AppDbContext db) : IBudgetService
{
    public async Task<IEnumerable<Budget>> GetAllAsync(bool includeInactive = false)
    {
        var query = db.Budgets
            .Include(b => b.Currency)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(b => b.IsActive);

        return await query.OrderByDescending(b => b.StartDate).ToListAsync();
    }

    public async Task<Budget?> GetByIdAsync(Guid id) =>
        await db.Budgets
            .Include(b => b.Currency)
            .Include(b => b.Transactions)
            .FirstOrDefaultAsync(b => b.Id == id);

    public async Task<Budget> CreateAsync(BudgetCreateViewModel vm)
    {
        var budget = new Budget
        {
            Id           = Guid.NewGuid(),
            Name         = vm.Name,
            TargetAmount = vm.TargetAmount,
            CurrencyId   = vm.CurrencyId!.Value,
            StartDate    = vm.StartDate,
            EndDate      = vm.EndDate,
            Description  = vm.Description,
            IsActive     = true
        };

        db.Budgets.Add(budget);
        await db.SaveChangesAsync();
        return budget;
    }

    public async Task UpdateAsync(BudgetEditViewModel vm)
    {
        var budget = await db.Budgets.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Budget {vm.Id} not found.");

        budget.Name         = vm.Name;
        budget.TargetAmount = vm.TargetAmount;
        budget.CurrencyId   = vm.CurrencyId!.Value;
        budget.StartDate    = vm.StartDate;
        budget.EndDate      = vm.EndDate;
        budget.Description  = vm.Description;
        await db.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid id)
    {
        var budget = await db.Budgets.FindAsync(id)
            ?? throw new InvalidOperationException($"Budget {id} not found.");

        budget.IsActive = false;
        await db.SaveChangesAsync();
    }

    public async Task<decimal> GetActualSpendAsync(Guid id) =>
        await db.Transactions
            .Where(t => t.BudgetId == id)
            .SumAsync(t => t.Amount);
}
