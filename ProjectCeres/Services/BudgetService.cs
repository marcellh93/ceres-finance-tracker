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
            .Include(b => b.LinkedAccount)
            .Include(b => b.Transactions)
            .FirstOrDefaultAsync(b => b.Id == id);

    public async Task<Budget> CreateAsync(BudgetCreateViewModel vm)
    {
        ValidateGoalTypeRules(vm.GoalType, vm.LinkedAccountId);

        var budget = new Budget
        {
            Id              = Guid.NewGuid(),
            Name            = vm.Name,
            TargetAmount    = vm.TargetAmount,
            CurrencyId      = vm.CurrencyId!.Value,
            StartDate       = vm.StartDate,
            EndDate         = vm.EndDate,
            Description     = vm.Description,
            GoalType        = vm.GoalType,
            LinkedAccountId = vm.LinkedAccountId,
            IsActive        = true
        };

        db.Budgets.Add(budget);
        await db.SaveChangesAsync();
        return budget;
    }

    public async Task UpdateAsync(BudgetEditViewModel vm)
    {
        var budget = await db.Budgets.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Budget {vm.Id} not found.");

        ValidateGoalTypeRules(vm.GoalType, vm.LinkedAccountId);

        budget.Name            = vm.Name;
        budget.TargetAmount    = vm.TargetAmount;
        budget.CurrencyId      = vm.CurrencyId!.Value;
        budget.StartDate       = vm.StartDate;
        budget.EndDate         = vm.EndDate;
        budget.Description     = vm.Description;
        budget.GoalType        = vm.GoalType;
        budget.LinkedAccountId = vm.LinkedAccountId;
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

    public async Task<BudgetProgressResult> GetProgressAsync(Guid id)
    {
        var budget = await db.Budgets.FindAsync(id)
            ?? throw new InvalidOperationException($"Budget {id} not found.");

        decimal progress = budget.GoalType == "Savings"
            ? await GetAccountBalanceAsync(budget.LinkedAccountId!.Value)
            : await db.Transactions
                .Where(t => t.BudgetId == id)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return new BudgetProgressResult
        {
            AmountProgress = progress,
            TargetAmount   = budget.TargetAmount
        };
    }

    private async Task<decimal> GetAccountBalanceAsync(Guid accountId) =>
        await db.Transactions
            .Where(t => t.AccountId == accountId)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

    private static void ValidateGoalTypeRules(string goalType, Guid? linkedAccountId)
    {
        if (goalType == "Savings" && linkedAccountId is null)
            throw new InvalidOperationException(
                "LinkedAccountId is required for Savings goal budgets.");

        if (goalType == "Spending" && linkedAccountId is not null)
            throw new InvalidOperationException(
                "LinkedAccountId must be null for Spending goal budgets.");
    }
}
