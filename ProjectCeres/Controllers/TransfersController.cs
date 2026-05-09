using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers;

[Authorize]
public class TransfersController(ITransferService transferService) : Controller
{
    [HttpGet]
    public IActionResult Index() => Redirect("/app/movements");

    [HttpGet]
    public IActionResult Create() => Redirect("/app/movements/new?type=transfer");

    [HttpGet]
    public IActionResult Edit(Guid id) => Redirect($"/app/movements/{id}/edit");

    [HttpGet]
    public IActionResult Delete(Guid id) => Redirect($"/app/movements/{id}/edit");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Obsolete("Use PATCH /api/movements/{id}/cleared")]
    public async Task<IActionResult> ToggleCleared(Guid id, bool cleared)
    {
        try { await transferService.MarkClearedAsync(id, cleared); }
        catch (InvalidOperationException ex) { TempData["ErrorMessage"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
