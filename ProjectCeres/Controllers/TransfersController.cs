using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class TransfersController(ITransferService transferService, AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var transfers = await transferService.GetAllAsync();
        return View(transfers);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateViewBagAsync();
        return View(new TransferCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransferCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await transferService.CreateAsync(vm);

            TempData["SuccessMessage"] = "Transfer recorded.";
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
        var transfer = await transferService.GetByIdAsync(id);
        if (transfer is null) return NotFound();

        var vm = new TransferEditViewModel
        {
            Id              = transfer.Id,
            Date            = transfer.Date,
            Amount          = transfer.Amount,
            SourceAccountId = transfer.SourceAccountId,
            DestAccountId   = transfer.DestAccountId,
            Description     = transfer.Description
        };

        await PopulateViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TransferEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await transferService.UpdateAsync(vm);

            TempData["SuccessMessage"] = "Transfer updated.";
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
        var transfer = await transferService.GetByIdAsync(id);
        if (transfer is null) return NotFound();
        return View(transfer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string _ = "")
    {
        try
        {
            await transferService.DeleteAsync(id);
            TempData["SuccessMessage"] = "Transfer deleted.";
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
            .Include(a => a.Currency)
            .OrderBy(a => a.Name)
            .ToListAsync();

        ViewBag.SourceAccounts = new SelectList(accounts, "Id", "Name");
        ViewBag.DestAccounts   = new SelectList(accounts, "Id", "Name");
    }
}
