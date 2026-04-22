using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class BudgetsController(
    ICategoryBudgetService categoryBudgetService,
    IBudgetService budgetService,
    AppDbContext db) : Controller
{
    // =========================================================================
    // Category Budgets
    // =========================================================================

    public async Task<IActionResult> Index()
    {
        var categoryBudgets = await categoryBudgetService.GetAllAsync(includeInactive: true);
        return View(categoryBudgets);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateCategoryBudgetViewBagAsync();
        return View(new CategoryBudgetCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryBudgetCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCategoryBudgetViewBagAsync();
            return View(vm);
        }

        try
        {
            await categoryBudgetService.CreateAsync(vm);
            TempData["SuccessMessage"] = "Category budget created.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateCategoryBudgetViewBagAsync();
            return View(vm);
        }
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var budget = await categoryBudgetService.GetByIdAsync(id);
        if (budget is null) return NotFound();

        var vm = new CategoryBudgetEditViewModel
        {
            Id          = budget.Id,
            LimitAmount = budget.LimitAmount
        };

        ViewBag.CategoryName = budget.Category.Name;
        ViewBag.CurrencyCode = budget.Currency.Code;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryBudgetEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            var budget = await categoryBudgetService.GetByIdAsync(vm.Id);
            ViewBag.CategoryName = budget?.Category.Name;
            ViewBag.CurrencyCode = budget?.Currency.Code;
            return View(vm);
        }

        try
        {
            await categoryBudgetService.UpdateAsync(vm);
            TempData["SuccessMessage"] = "Category budget updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    public async Task<IActionResult> Deactivate(Guid id)
    {
        var budget = await categoryBudgetService.GetByIdAsync(id);
        if (budget is null) return NotFound();
        return View(budget);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, string _ = "")
    {
        try
        {
            var budget = await categoryBudgetService.GetByIdAsync(id);
            if (budget is null) return NotFound();

            await categoryBudgetService.DeactivateAsync(id);
            TempData["SuccessMessage"] = $"Category budget for \"{budget.Category.Name}\" deactivated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // =========================================================================
    // Goal Budgets
    // =========================================================================

    public async Task<IActionResult> Goals()
    {
        var goals = await budgetService.GetAllAsync(includeInactive: true);
        return View(goals);
    }

    public async Task<IActionResult> CreateGoal()
    {
        await PopulateGoalBudgetViewBagAsync();
        return View(new BudgetCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGoal(BudgetCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateGoalBudgetViewBagAsync();
            return View(vm);
        }

        try
        {
            await budgetService.CreateAsync(vm);
            TempData["SuccessMessage"] = $"Goal budget \"{vm.Name}\" created.";
            return RedirectToAction(nameof(Goals));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateGoalBudgetViewBagAsync();
            return View(vm);
        }
    }

    public async Task<IActionResult> EditGoal(Guid id)
    {
        var budget = await budgetService.GetByIdAsync(id);
        if (budget is null) return NotFound();

        var vm = new BudgetEditViewModel
        {
            Id           = budget.Id,
            Name         = budget.Name,
            TargetAmount = budget.TargetAmount,
            CurrencyId   = budget.CurrencyId,
            StartDate    = budget.StartDate,
            EndDate      = budget.EndDate,
            Description  = budget.Description,
            GoalType        = budget.GoalType,
            LinkedAccountId = budget.LinkedAccountId
        };

        await PopulateGoalBudgetViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditGoal(BudgetEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateGoalBudgetViewBagAsync();
            return View(vm);
        }

        try
        {
            await budgetService.UpdateAsync(vm);
            TempData["SuccessMessage"] = $"Goal budget \"{vm.Name}\" updated.";
            return RedirectToAction(nameof(Goals));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateGoalBudgetViewBagAsync();
            return View(vm);
        }
    }

    public async Task<IActionResult> DeactivateGoal(Guid id)
    {
        var budget = await budgetService.GetByIdAsync(id);
        if (budget is null) return NotFound();
        return View(budget);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateGoal(Guid id, string _ = "")
    {
        try
        {
            var budget = await budgetService.GetByIdAsync(id);
            if (budget is null) return NotFound();

            await budgetService.DeactivateAsync(id);
            TempData["SuccessMessage"] = $"Goal budget \"{budget.Name}\" deactivated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Goals));
    }

    // =========================================================================
    // ViewBag helpers
    // =========================================================================

    private async Task PopulateCategoryBudgetViewBagAsync()
    {
        var expenseCategories = await db.Categories
            .Include(c => c.CategoryType)
            .Where(c => c.IsActive && c.CategoryType.Name == "Expense")
            .OrderBy(c => c.Name)
            .ToListAsync();

        ViewBag.Categories = new SelectList(expenseCategories, "Id", "Name");
        ViewBag.Currencies = new SelectList(
            await db.Currencies.OrderBy(c => c.Code).ToListAsync(), "Id", "Code");
    }

    private async Task PopulateGoalBudgetViewBagAsync()
    {
        ViewBag.Currencies = new SelectList(
            await db.Currencies.OrderBy(c => c.Code).ToListAsync(), "Id", "Code");

        ViewBag.AssetAccounts = new SelectList(
            await db.Accounts
                .Include(a => a.AccountType)
                .Where(a => a.IsActive && a.AccountType.Name == "Asset")
                .OrderBy(a => a.Name)
                .ToListAsync(),
            "Id", "Name");

        ViewBag.GoalTypes = new SelectList(new[]
        {
            new { Value = "Spending", Text = "Spending — track tagged expenses toward a target" },
            new { Value = "Savings",  Text = "Savings — track an account balance toward a target" }
        }, "Value", "Text");
    }
}
