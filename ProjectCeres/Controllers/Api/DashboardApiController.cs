using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/dashboard")]
public class DashboardApiController(
    ICategoryBudgetService categoryBudgetService,
    IBudgetService budgetService,
    AppDbContext db) : ControllerBase
{
    [HttpGet("category-budgets")]
    public async Task<IActionResult> GetCategoryBudgets()
    {
        var budgets = await categoryBudgetService.GetAllAsync(includeInactive: false);
        var now = DateTime.Today;

        var result = new List<object>();
        foreach (var budget in budgets)
        {
            var spent = await categoryBudgetService.GetActualSpendAsync(budget.Id, now.Year, now.Month);
            var percentUsed = budget.LimitAmount == 0m
                ? 0m
                : Math.Round(spent / budget.LimitAmount * 100m, 2);

            result.Add(new
            {
                id           = budget.Id,
                categoryName = budget.Category.Name,
                currencyCode = budget.Currency.Code,
                spent        = spent,
                limit        = budget.LimitAmount,
                percentUsed  = percentUsed
            });
        }

        return Ok(result);
    }

    [HttpGet("goal-budgets")]
    public async Task<IActionResult> GetGoalBudgets()
    {
        var goals = await budgetService.GetAllAsync(includeInactive: false);

        var result = new List<object>();
        foreach (var goal in goals)
        {
            var progress = await budgetService.GetProgressAsync(goal.Id);

            result.Add(new
            {
                id             = goal.Id,
                name           = goal.Name,
                goalType       = goal.GoalType,
                amountProgress = progress.AmountProgress,
                targetAmount   = progress.TargetAmount,
                remaining      = progress.Remaining,
                percentUsed    = progress.PercentUsed,
                currencyCode   = goal.Currency.Code
            });
        }

        return Ok(result);
    }
}
