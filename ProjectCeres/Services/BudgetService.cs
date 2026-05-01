using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class BudgetService(AppDbContext db, IAccountService accountService) : IBudgetService
{
    public async Task<IEnumerable<Budget>> GetAllAsync(
        bool includeInactive = false,
        string? currency = null,
        string? type = null)
    {
        var query = db.Budgets
            .Include(b => b.Currency)
            .Include(b => b.LinkedAccount)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(b => b.IsActive);

        if (!string.IsNullOrWhiteSpace(currency))
            query = query.Where(b => b.Currency.Code == currency);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var goalType = type.ToLowerInvariant() switch
            {
                "spending" => "Spending",
                "savings"  => "Savings",
                _          => null
            };
            if (goalType is null)
                throw new ArgumentException("type must be 'spending' or 'savings'.", nameof(type));
            query = query.Where(b => b.GoalType == goalType);
        }

        return await query
            .OrderByDescending(b => b.IsActive)
            .ThenBy(b => b.EndDate ?? DateOnly.MaxValue)
            .ToListAsync();
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
        ValidateDateRange(vm.StartDate, vm.EndDate);

        int currencyId = vm.CurrencyId ?? 0;
        if (vm.GoalType == "Savings" && vm.LinkedAccountId.HasValue)
        {
            var account = await db.Accounts.FindAsync(vm.LinkedAccountId.Value)
                ?? throw new InvalidOperationException("Linked account not found.");
            currencyId = account.CurrencyId;
        }
        if (currencyId == 0)
            throw new InvalidOperationException("CurrencyId is required for Spending goals.");

        var budget = new Budget
        {
            Id              = Guid.NewGuid(),
            Name            = vm.Name,
            TargetAmount    = vm.TargetAmount,
            CurrencyId      = currencyId,
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
        ValidateDateRange(vm.StartDate, vm.EndDate);

        int currencyId = vm.CurrencyId ?? budget.CurrencyId;
        if (vm.GoalType == "Savings" && vm.LinkedAccountId.HasValue)
        {
            var account = await db.Accounts.FindAsync(vm.LinkedAccountId.Value)
                ?? throw new InvalidOperationException("Linked account not found.");
            currencyId = account.CurrencyId;
        }

        budget.Name            = vm.Name;
        budget.TargetAmount    = vm.TargetAmount;
        budget.CurrencyId      = currencyId;
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

    public async Task ReactivateAsync(Guid id)
    {
        var budget = await db.Budgets.FindAsync(id)
            ?? throw new InvalidOperationException($"Budget {id} not found.");

        if (budget.IsActive) return;
        budget.IsActive = true;
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

    private Task<decimal> GetAccountBalanceAsync(Guid accountId) =>
        accountService.GetBalanceAsync(accountId);

    private static void ValidateGoalTypeRules(string goalType, Guid? linkedAccountId)
    {
        if (goalType == "Savings" && linkedAccountId is null)
            throw new InvalidOperationException(
                "LinkedAccountId is required for Savings goal budgets.");

        if (goalType == "Spending" && linkedAccountId is not null)
            throw new InvalidOperationException(
                "LinkedAccountId must be null for Spending goal budgets.");
    }

    private static void ValidateDateRange(DateOnly startDate, DateOnly? endDate)
    {
        if (endDate.HasValue && endDate.Value < startDate)
            throw new InvalidOperationException("End date must be on or after start date.");
    }
}
