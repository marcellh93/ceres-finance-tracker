using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class TransactionsController(ITransactionService transactionService, AppDbContext db) : Controller
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(Guid? accountId, DateOnly? from, DateOnly? to, int page = 1)
    {
        var offset = (page - 1) * PageSize;
        var transactions = await transactionService.GetRecentAsync(accountId, from, to, PageSize, offset);
        var total = await transactionService.CountAsync(accountId, from, to);

        ViewBag.AccountId   = accountId;
        ViewBag.From        = from;
        ViewBag.To          = to;
        ViewBag.Page        = page;
        ViewBag.TotalPages  = (int)Math.Ceiling(total / (double)PageSize);
        ViewBag.Accounts    = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");

        return View(transactions);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateViewBagAsync();
        return View(new TransactionCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransactionCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await transactionService.CreateAsync(vm);

            TempData["SuccessMessage"] = "Transaction recorded.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var t = await transactionService.GetByIdAsync(id);
        if (t is null) return NotFound();

        var vm = new TransactionEditViewModel
        {
            Id          = t.Id,
            Date        = t.Date,
            Amount      = t.Amount,
            Description = t.Description,
            AccountId   = t.AccountId,
            CategoryId  = t.CategoryId,
            BudgetId    = t.BudgetId
        };

        ViewBag.Attachments = t.Attachments;
        await PopulateViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TransactionEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await transactionService.UpdateAsync(vm);

            TempData["SuccessMessage"] = "Transaction updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }
    }

    public async Task<IActionResult> Delete(Guid id)
    {
        var t = await transactionService.GetByIdAsync(id);
        if (t is null) return NotFound();
        return View(t);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string _ = "")
    {
        try
        {
            await transactionService.DeleteAsync(id);
            TempData["SuccessMessage"] = "Transaction deleted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateViewBagAsync()
    {
        ViewBag.Accounts = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");
        ViewBag.Categories = new SelectList(
            await db.Categories.Where(c => c.IsActive)
                .Include(c => c.CategoryType)
                .OrderBy(c => c.CategoryType.Name).ThenBy(c => c.Name)
                .ToListAsync(), "Id", "Name");
        ViewBag.Budgets = new SelectList(
            await db.Budgets.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(), "Id", "Name");
    }
}
