using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers;

public class MovementsController(IMovementService movementService, AppDbContext db) : Controller
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(Guid? accountId, DateOnly? from, DateOnly? to, int page = 1)
    {
        var offset = (page - 1) * PageSize;
        var items  = await movementService.GetRecentAsync(accountId, from, to, PageSize, offset);
        var total  = await movementService.CountAsync(accountId, from, to);

        ViewBag.AccountId  = accountId;
        ViewBag.From       = from;
        ViewBag.To         = to;
        ViewBag.Page       = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)PageSize);
        ViewBag.Accounts   = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");

        return View(items);
    }
}
