using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class TransferReviewController(
    ITransferReviewService reviewService,
    AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var pending = await reviewService.GetPendingAsync();

        var vms = pending.Select(s => new StagedTransferViewModel
        {
            Id                              = s.Id,
            ImportedAt                      = s.ImportedAt,
            AccountName                     = s.Account?.Name ?? s.AccountId.ToString(),
            RawDate                         = s.RawDate,
            RawAmount                       = s.RawAmount,
            RawDescription                  = s.RawDescription,
            CandidateTransactionId          = s.CandidateTransactionId,
            CandidateTransactionDescription = s.CandidateTransaction?.Description,
            CandidateTransactionDate        = s.CandidateTransaction?.Date,
            CandidateTransactionAmount      = s.CandidateTransaction?.Amount
        }).ToList();

        ViewBag.Accounts = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(),
            "Id", "Name");

        return View(vms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkToExisting(Guid stagedId, Guid otherAccountId)
    {
        try
        {
            await reviewService.LinkToExistingAsync(stagedId, otherAccountId);
            TempData["SuccessMessage"] = "Transfer linked successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAsTransfer(Guid stagedId, Guid otherAccountId)
    {
        try
        {
            await reviewService.CreateAsTransferAsync(stagedId, otherAccountId);
            TempData["SuccessMessage"] = "Transfer created successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissAsTransaction(Guid stagedId)
    {
        try
        {
            await reviewService.DismissAsTransactionAsync(stagedId);
            TempData["SuccessMessage"] = "Row imported as a plain transaction.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
