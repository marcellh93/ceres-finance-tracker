using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class SettingsController(ISettingsService settingsService, AppDbContext db) : Controller
{
    public async Task<IActionResult> Edit()
    {
        var settings = await settingsService.GetAsync();

        var vm = new SettingsEditViewModel
        {
            NumberFormat         = settings.NumberFormat,
            DateFormat           = settings.DateFormat,
            DefaultCurrencyId    = settings.DefaultCurrencyId,
            BudgetPeriodStartDay = settings.BudgetPeriodStartDay
        };

        await PopulateViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SettingsEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        await settingsService.UpdateAsync(vm);

        TempData["SuccessMessage"] = "Settings saved.";
        return RedirectToAction(nameof(Edit));
    }

    private async Task PopulateViewBagAsync()
    {
        var currencies = await db.Currencies.OrderBy(c => c.Code).ToListAsync();
        ViewBag.Currencies = new SelectList(currencies, "Id", "Code");

        ViewBag.NumberFormats = new SelectList(new[]
        {
            new { Value = "comma_decimal",  Text = "1.234,56  (comma decimal — EU)" },
            new { Value = "period_decimal", Text = "1,234.56  (period decimal — US)" }
        }, "Value", "Text");

        ViewBag.DateFormats = new SelectList(new[]
        {
            new { Value = "DD/MM/YYYY", Text = "DD/MM/YYYY" },
            new { Value = "MM/DD/YYYY", Text = "MM/DD/YYYY" },
            new { Value = "YYYY-MM-DD", Text = "YYYY-MM-DD" }
        }, "Value", "Text");
    }
}
