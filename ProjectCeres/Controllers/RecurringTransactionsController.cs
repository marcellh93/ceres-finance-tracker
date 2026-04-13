using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class RecurringTransactionsController(
    IRecurringTransactionService reminderService,
    AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var reminders = await reminderService.GetAllAsync(includeInactive: true);
        return View(reminders);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateViewBagAsync();
        return View(new RecurringTransactionCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RecurringTransactionCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await reminderService.CreateAsync(vm);

            TempData["SuccessMessage"] = $"Reminder \"{vm.Name}\" created.";
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
        var reminder = await reminderService.GetByIdAsync(id);
        if (reminder is null) return NotFound();

        var vm = new RecurringTransactionEditViewModel
        {
            Id              = reminder.Id,
            Name            = reminder.Name,
            EstimatedAmount = reminder.EstimatedAmount,
            AccountId       = reminder.AccountId,
            CategoryId      = reminder.CategoryId,
            Frequency       = reminder.Frequency,
            DayOfPeriod     = reminder.DayOfPeriod,
            NextDueDate     = reminder.NextDueDate
        };

        await PopulateViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RecurringTransactionEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await reminderService.UpdateAsync(vm);

            TempData["SuccessMessage"] = $"Reminder \"{vm.Name}\" updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }
    }

    // GET: pre-fills the transaction form from the reminder template
    public async Task<IActionResult> Confirm(Guid id)
    {
        var reminder = await reminderService.GetByIdAsync(id);
        if (reminder is null) return NotFound();

        ViewBag.Reminder = reminder;
        var vm = new TransactionCreateViewModel
        {
            Date       = reminder.NextDueDate,
            Amount     = reminder.EstimatedAmount,
            AccountId  = reminder.AccountId,
            CategoryId = reminder.CategoryId
        };

        await PopulateTransactionViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, TransactionCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            var reminder = await reminderService.GetByIdAsync(id);
            ViewBag.Reminder = reminder;
            await PopulateTransactionViewBagAsync();
            return View(vm);
        }

        try
        {
            await reminderService.ConfirmAsync(id, vm.Date, vm.Amount, vm.Description);
            TempData["SuccessMessage"] = "Reminder confirmed and transaction recorded.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Dismiss(Guid id)
    {
        var reminder = await reminderService.GetByIdAsync(id);
        if (reminder is null) return NotFound();
        return View(reminder);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(Guid id, string _ = "")
    {
        try
        {
            await reminderService.DismissAsync(id);
            TempData["SuccessMessage"] = "Reminder dismissed. Next due date advanced.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Deactivate(Guid id)
    {
        var reminder = await reminderService.GetByIdAsync(id);
        if (reminder is null) return NotFound();
        return View(reminder);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, string _ = "")
    {
        try
        {
            var reminder = await reminderService.GetByIdAsync(id);
            if (reminder is null) return NotFound();
            await reminderService.DeactivateAsync(id);
            TempData["SuccessMessage"] = $"Reminder \"{reminder.Name}\" deactivated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateViewBagAsync()
    {
        ViewBag.Accounts = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");
        ViewBag.Categories = new SelectList(
            await db.Categories.Where(c => c.IsActive)
                .Include(c => c.CategoryType)
                .OrderBy(c => c.CategoryType.Name).ThenBy(c => c.Name)
                .ToListAsync(), "Id", "Name");
        ViewBag.Frequencies = new SelectList(Enum.GetValues<Frequency>()
            .Select(f => new { Value = f.ToString(), Text = f.ToString() }), "Value", "Text");
    }

    private async Task PopulateTransactionViewBagAsync()
    {
        ViewBag.Accounts = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");
        ViewBag.Categories = new SelectList(
            await db.Categories.Where(c => c.IsActive)
                .Include(c => c.CategoryType)
                .OrderBy(c => c.CategoryType.Name).ThenBy(c => c.Name)
                .ToListAsync(), "Id", "Name");
    }
}
