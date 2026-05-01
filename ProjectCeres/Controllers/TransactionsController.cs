using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers;

public class TransactionsController(ITransactionService transactionService) : Controller
{
    [HttpGet]
    public IActionResult Index() => Redirect("/app/movements");

    [HttpGet]
    public IActionResult Export() => Redirect("/app/movements");

    [HttpGet]
    public IActionResult Create() => Redirect("/app/movements/new?type=transaction");

    [HttpGet]
    public IActionResult Edit(Guid id) => Redirect($"/app/movements/{id}/edit");

    [HttpGet]
    public IActionResult Delete(Guid id) => Redirect($"/app/movements/{id}/edit");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Obsolete("Use PATCH /api/movements/{id}/cleared")]
    public async Task<IActionResult> ToggleCleared(Guid id, bool cleared, Guid? accountId, DateOnly? from, DateOnly? to, int page = 1)
    {
        try { await transactionService.MarkClearedAsync(id, cleared); }
        catch (InvalidOperationException ex) { TempData["ErrorMessage"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { accountId, from, to, page });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Obsolete("Use POST /api/movements/bulk-cleared")]
    public async Task<IActionResult> BulkMarkCleared(DateOnly from, DateOnly to, Guid? accountId)
    {
        await transactionService.BulkMarkClearedAsync(from, to, accountId);
        TempData["SuccessMessage"] = $"All transactions between {from:dd/MM/yyyy} and {to:dd/MM/yyyy} marked as cleared.";
        return RedirectToAction(nameof(Index), new { accountId, from, to });
    }
}
