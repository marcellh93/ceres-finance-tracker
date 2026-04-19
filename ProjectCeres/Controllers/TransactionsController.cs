using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class TransactionsController(ITransactionService transactionService, IFileAttachmentService attachmentService, AppDbContext db) : Controller
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
        if (vm.TransactionType == "LiabilityPayment")
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

        // Validate attachment before saving anything
        if (vm.Attachment is not null)
        {
            try { await attachmentService.ValidateAsync(vm.Attachment); }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(vm.Attachment), ex.Message);
                await PopulateViewBagAsync();
                return View(vm);
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

        if (vm.Attachment is not null)
        {
            try { await attachmentService.UploadAsync(newId, vm.Attachment); }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = $"Transaction saved, but the attachment could not be uploaded: {ex.Message}";
                return RedirectToAction(nameof(Edit), new { id = newId });
            }
        }

        TempData["SuccessMessage"] = vm.TransactionType == "LiabilityPayment"
            ? "Liability payment recorded."
            : "Transaction recorded.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var vm = await transactionService.GetByIdForEditAsync(id);
        if (vm is null) return NotFound();

        // Attachments only apply to regular transactions
        if (vm.TransactionType == "Regular")
        {
            var t = await db.Transactions.Include(t => t.Attachments).FirstOrDefaultAsync(t => t.Id == id);
            ViewBag.Attachments = t?.Attachments;
        }

        await PopulateViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TransactionEditViewModel vm)
    {
        if (vm.TransactionType == "LiabilityPayment")
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

        if (vm.Attachment is not null)
        {
            try { await attachmentService.ValidateAsync(vm.Attachment); }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(vm.Attachment), ex.Message);
                await PopulateViewBagAsync();
                return View(vm);
            }
        }

        try
        {
            await transactionService.UpdateAsync(vm);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }

        if (vm.Attachment is not null)
        {
            try { await attachmentService.UploadAsync(vm.Id, vm.Attachment); }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = $"Transaction saved, but the attachment could not be uploaded: {ex.Message}";
                return RedirectToAction(nameof(Edit), new { id = vm.Id });
            }
        }

        TempData["SuccessMessage"] = vm.TransactionType == "LiabilityPayment"
            ? "Liability payment updated."
            : "Transaction updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(Guid id)
    {
        var vm = await transactionService.GetByIdForEditAsync(id);
        if (vm is null) return NotFound();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string _ = "")
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

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateViewBagAsync()
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

        ViewBag.Budgets = new SelectList(
            await db.Budgets.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(), "Id", "Name");
    }
}
