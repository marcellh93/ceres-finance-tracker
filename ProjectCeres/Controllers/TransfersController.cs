using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;
using ProjectCeres.Models;

namespace ProjectCeres.Controllers;

public class TransfersController(ITransferService transferService, AppDbContext db, IFileAttachmentService attachmentService) : Controller
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

    public async Task<IActionResult> Edit(Guid id, string? returnUrl = null)
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
            Description     = transfer.Description,
            IsCleared       = transfer.IsCleared
        };

        ViewBag.ReturnUrl   = returnUrl;
        ViewBag.Attachments = await db.TransferAttachments
            .Where(a => a.TransferId == id)
            .OrderBy(a => a.UploadedAt)
            .ToListAsync();
        await PopulateViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TransferEditViewModel vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.ReturnUrl   = returnUrl;
            ViewBag.Attachments = await db.TransferAttachments
                .Where(a => a.TransferId == vm.Id)
                .OrderBy(a => a.UploadedAt)
                .ToListAsync();
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await transferService.UpdateAsync(vm);

            if (vm.Attachment is { Length: > 0 })
                await attachmentService.UploadForTransferAsync(vm.Id, vm.Attachment);

            TempData["SuccessMessage"] = "Transfer updated.";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.ReturnUrl   = returnUrl;
            ViewBag.Attachments = await db.TransferAttachments
                .Where(a => a.TransferId == vm.Id)
                .OrderBy(a => a.UploadedAt)
                .ToListAsync();
            await PopulateViewBagAsync();
            return View(vm);
        }
    }

    public async Task<IActionResult> Delete(Guid id, string? returnUrl = null)
    {
        var transfer = await transferService.GetByIdAsync(id);
        if (transfer is null) return NotFound();
        ViewBag.ReturnUrl = returnUrl;
        return View(transfer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? returnUrl = null, string _ = "")
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

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCleared(Guid id, bool cleared)
    {
        try { await transferService.MarkClearedAsync(id, cleared); }
        catch (InvalidOperationException ex) { TempData["ErrorMessage"] = ex.Message; }
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
