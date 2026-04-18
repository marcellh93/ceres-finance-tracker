using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class AccountsController(IAccountService accountService, AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var accounts = (await accountService.GetAllAsync(includeInactive: true)).ToList();

        var balances = new Dictionary<Guid, decimal>();
        foreach (var a in accounts)
            balances[a.Id] = await accountService.GetBalanceAsync(a.Id);

        ViewBag.Balances = balances;
        return View(accounts);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateViewBagAsync();
        return View(new AccountCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AccountCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await accountService.CreateAsync(vm);

            TempData["SuccessMessage"] = $"Account \"{vm.Name}\" created.";
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
        var account = await accountService.GetByIdAsync(id);
        if (account is null) return NotFound();

        var openingDate = await accountService.GetOpeningBalanceDateAsync(account.Id);
        var vm = new AccountEditViewModel
        {
            Id                  = account.Id,
            Name                = account.Name,
            Description         = account.Description,
            OpeningBalance      = await accountService.GetOpeningBalanceAsync(account.Id),
            OpeningBalanceDate  = openingDate ?? DateOnly.FromDateTime(DateTime.Today)
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AccountEditViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            await accountService.UpdateAsync(vm);
            TempData["SuccessMessage"] = $"Account \"{vm.Name}\" updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    public async Task<IActionResult> Deactivate(Guid id)
    {
        var account = await accountService.GetByIdAsync(id);
        if (account is null) return NotFound();
        return View(account);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, string _ = "")
    {
        try
        {
            var account = await accountService.GetByIdAsync(id);
            if (account is null) return NotFound();

            await accountService.DeactivateAsync(id);
            TempData["SuccessMessage"] = $"Account \"{account.Name}\" deactivated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Ledger(Guid id)
    {
        var vm = await accountService.GetLedgerAsync(id);
        if (vm is null) return NotFound();
        return View(vm);
    }

    private async Task PopulateViewBagAsync()
    {
        ViewBag.AccountTypes = new SelectList(
            await db.AccountTypes.OrderBy(t => t.Name).ToListAsync(), "Id", "Name");
        ViewBag.Currencies = new SelectList(
            await db.Currencies.OrderBy(c => c.Code).ToListAsync(), "Id", "Code");
    }
}
