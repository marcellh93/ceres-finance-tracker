using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class ReconciliationReviewController(
    IImportStagedTransactionService stagedTransactionService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var pending = await stagedTransactionService.GetPendingAsync();

        var vms = pending.Select(s => new StagedTransactionViewModel
        {
            Id                              = s.Id,
            ImportedAt                      = s.ImportedAt,
            AccountName                     = s.Account?.Name ?? s.AccountId.ToString(),
            RawDate                         = s.RawDate,
            RawAmount                       = s.RawAmount,
            RawDescription                  = s.RawDescription,
            MatchedTransactionDescription   = s.MatchedTransaction?.Description,
            MatchedTransactionDate          = s.MatchedTransaction?.Date ?? s.RawDate,
            MatchedTransactionAmount        = s.MatchedTransaction?.Amount ?? s.RawAmount
        }).ToList();

        return View(vms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id)
    {
        try
        {
            await stagedTransactionService.ConfirmAsync(id);
            TempData["SuccessMessage"] = "Reconciliation confirmed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmAll()
    {
        try
        {
            await stagedTransactionService.ConfirmAllAsync();
            TempData["SuccessMessage"] = "All reconciliations confirmed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispute(Guid id)
    {
        try
        {
            await stagedTransactionService.DisputeAsync(id);
            TempData["SuccessMessage"] = "Match disputed — original transaction un-cleared and CSV row inserted as a new transaction.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
