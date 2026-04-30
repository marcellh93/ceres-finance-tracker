using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Helpers;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;
using static ProjectCeres.ViewModels.TransactionTypes;

namespace ProjectCeres.Controllers;

public class TransactionsController(ITransactionService transactionService, IFileAttachmentService attachmentService, ITransactionExportService exportService, AppDbContext db) : Controller
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(Guid? accountId, DateOnly? from, DateOnly? to, int page = 1)
    {
        var offset = (page - 1) * PageSize;
        var items  = await transactionService.GetRecentAsync(accountId, from, to, PageSize, offset);
        var total  = await transactionService.CountAsync(accountId, from, to);

        ViewBag.AccountId  = accountId;
        ViewBag.From       = from;
        ViewBag.To         = to;
        ViewBag.Page       = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)PageSize);
        ViewBag.Accounts   = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");

        return View(items);
    }

    public async Task<IActionResult> Export(Guid? accountId, DateOnly? from, DateOnly? to)
    {
        var rows = await exportService.ExportAsync(accountId, from, to);

        var lines = new List<string> { "Date,Account,Category,Type,Description,Amount" };
        foreach (var r in rows)
            lines.Add($"{r.Date:yyyy-MM-dd},{CsvFormattingHelper.Csv(r.Account)},{CsvFormattingHelper.Csv(r.Category)},{CsvFormattingHelper.Csv(r.CategoryType)},{CsvFormattingHelper.Csv(r.Description)},{r.Amount.ToString("F2", CultureInfo.InvariantCulture)}");

        var fileName = from.HasValue && to.HasValue
            ? $"transactions_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.csv"
            : $"transactions_{DateTime.Today:yyyy-MM-dd}.csv";

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", fileName);
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
        // Conditional server-side validation — required fields differ by type
        if (vm.TransactionType == LiabilityPayment)
        {
            ModelState.Remove("CategoryId");
            if (vm.LiabilityAccountId is null)
                ModelState.AddModelError("LiabilityAccountId", "Please select a liability account.");
        }
        else
        {
            ModelState.Remove("LiabilityAccountId");
            if (vm.CategoryId is null)
                ModelState.AddModelError("CategoryId", "Please select a category.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        // Validate attachments before saving anything
        if (vm.Attachments is { Count: > 0 })
        {
            foreach (var file in vm.Attachments)
            {
                try { await attachmentService.ValidateAsync(file); }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(nameof(vm.Attachments), ex.Message);
                    await PopulateViewBagAsync();
                    return View(vm);
                }
            }
        }

        Guid newId;
        try
        {
            newId = await transactionService.CreateAsync(vm);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }

        if (vm.Attachments is { Count: > 0 })
        {
            foreach (var file in vm.Attachments)
            {
                try { await attachmentService.UploadAsync(newId, file); }
                catch (InvalidOperationException ex)
                {
                    TempData["ErrorMessage"] = $"Transaction saved, but '{file.FileName}' could not be uploaded: {ex.Message}";
                }
            }
        }

        TempData["SuccessMessage"] = vm.TransactionType == LiabilityPayment
            ? "Liability payment recorded."
            : "Transaction recorded.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id, string? returnUrl = null)
    {
        var vm = await transactionService.GetByIdForEditAsync(id);
        if (vm is null) return NotFound();

        // Attachments only apply to regular transactions
        if (vm.TransactionType == Regular)
        {
            var t = await db.Transactions.Include(t => t.Attachments).FirstOrDefaultAsync(t => t.Id == id);
            ViewBag.Attachments = t?.Attachments;
        }

        ViewBag.ReturnUrl = returnUrl;
        await PopulateViewBagAsync(currencyFilterAccountId: vm.AccountId);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TransactionEditViewModel vm, string? returnUrl = null)
    {
        if (vm.TransactionType == LiabilityPayment)
        {
            ModelState.Remove("CategoryId");
            if (vm.LiabilityAccountId is null)
                ModelState.AddModelError("LiabilityAccountId", "Please select a liability account.");
        }
        else
        {
            ModelState.Remove("LiabilityAccountId");
            if (vm.CategoryId is null)
                ModelState.AddModelError("CategoryId", "Please select a category.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.ReturnUrl = returnUrl;
            await PopulateViewBagAsync(currencyFilterAccountId: vm.AccountId);
            return View(vm);
        }

        if (vm.Attachments is { Count: > 0 })
        {
            foreach (var file in vm.Attachments)
            {
                try { await attachmentService.ValidateAsync(file); }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(nameof(vm.Attachments), ex.Message);
                    ViewBag.ReturnUrl = returnUrl;
                    await PopulateViewBagAsync(currencyFilterAccountId: vm.AccountId);
                    return View(vm);
                }
            }
        }

        try
        {
            await transactionService.UpdateAsync(vm);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.ReturnUrl = returnUrl;
            await PopulateViewBagAsync(currencyFilterAccountId: vm.AccountId);
            return View(vm);
        }

        if (vm.Attachments is { Count: > 0 })
        {
            foreach (var file in vm.Attachments)
            {
                try { await attachmentService.UploadAsync(vm.Id, file); }
                catch (InvalidOperationException ex)
                {
                    TempData["ErrorMessage"] = $"Transaction saved, but '{file.FileName}' could not be uploaded: {ex.Message}";
                }
            }
        }

        TempData["SuccessMessage"] = vm.TransactionType == LiabilityPayment
            ? "Liability payment updated."
            : "Transaction updated.";

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(Guid id, string? returnUrl = null)
    {
        var vm = await transactionService.GetByIdForEditAsync(id);
        if (vm is null) return NotFound();
        ViewBag.ReturnUrl = returnUrl;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? returnUrl = null, string _ = "")
    {
        try
        {
            await transactionService.DeleteAsync(id);
            TempData["SuccessMessage"] = "Deleted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCleared(Guid id, bool cleared, Guid? accountId, DateOnly? from, DateOnly? to, int page = 1)
    {
        try { await transactionService.MarkClearedAsync(id, cleared); }
        catch (InvalidOperationException ex) { TempData["ErrorMessage"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { accountId, from, to, page });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkMarkCleared(DateOnly from, DateOnly to, Guid? accountId)
    {
        await transactionService.BulkMarkClearedAsync(from, to, accountId);
        TempData["SuccessMessage"] = $"All transactions between {from:dd/MM/yyyy} and {to:dd/MM/yyyy} marked as cleared.";
        return RedirectToAction(nameof(Index), new { accountId, from, to });
    }

    private async Task PopulateViewBagAsync(Guid? currencyFilterAccountId = null)
    {
        var accounts = await db.Accounts
            .Where(a => a.IsActive)
            .Include(a => a.AccountType)
            .OrderBy(a => a.Name)
            .ToListAsync();

        ViewBag.Accounts          = new SelectList(accounts, "Id", "Name");
        ViewBag.AssetAccounts     = new SelectList(accounts.Where(a => a.AccountType.Name == "Asset"),     "Id", "Name");
        ViewBag.LiabilityAccounts = new SelectList(accounts.Where(a => a.AccountType.Name == "Liability"), "Id", "Name");

        ViewBag.Categories = new SelectList(
            await db.Categories
                .Where(c => c.IsActive && !c.IsSystem)
                .Include(c => c.CategoryType)
                .OrderBy(c => c.CategoryType.Name).ThenBy(c => c.Name)
                .ToListAsync(), "Id", "Name");

        var budgetQuery = db.Budgets
            .Where(b => b.IsActive && b.GoalType == "Spending");

        if (currencyFilterAccountId.HasValue)
        {
            var account = accounts.FirstOrDefault(a => a.Id == currencyFilterAccountId.Value);
            if (account is not null)
                budgetQuery = budgetQuery.Where(b => b.CurrencyId == account.CurrencyId);
        }

        var budgets = await budgetQuery.OrderBy(b => b.Name).ToListAsync();
        ViewBag.Budgets = budgets.Any() ? new SelectList(budgets, "Id", "Name") : null;
    }
}
