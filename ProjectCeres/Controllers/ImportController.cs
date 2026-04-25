using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class ImportController(
    IImportService importService,
    ICsvImportProfileService profileService,
    AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        await PopulateViewBagAsync();
        return View(new ImportUploadViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ImportUploadViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        // If a profile was selected, override individual column fields with profile mappings.
        CsvColumnMappings mappings;
        if (vm.ProfileId.HasValue)
        {
            var profile = await profileService.GetByIdAsync(vm.ProfileId.Value);
            if (profile is null)
            {
                ModelState.AddModelError("ProfileId", "Selected profile not found.");
                await PopulateViewBagAsync();
                return View(vm);
            }
            mappings = profile.Mappings;
        }
        else
        {
            mappings = new CsvColumnMappings
            {
                DateColumn        = vm.DateColumn ?? "Date",
                AmountColumn      = vm.AmountColumn ?? "Amount",
                DescriptionColumn = vm.DescriptionColumn ?? "Description",
                CategoryColumn    = vm.CategoryColumn,
                FlipDebitSign     = vm.FlipDebitSign
            };
        }

        try
        {
            var result = await importService.ImportAsync(
                vm.File!, vm.AccountId!.Value, vm.CategoryId!.Value, mappings);

            TempData["ImportRowsImported"] = result.RowsImported;
            TempData["ImportRowsFlagged"]  = result.RowsFlagged;
            TempData["ImportRowsFailed"]   = result.RowsFailed;
            TempData["ImportErrors"]       = string.Join("\n", result.Errors);

            return RedirectToAction(nameof(Summary));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }
    }

    public IActionResult Summary()
    {
        var vm = new ImportSummaryViewModel
        {
            RowsImported = (int)(TempData["ImportRowsImported"] ?? 0),
            RowsFlagged  = (int)(TempData["ImportRowsFlagged"]  ?? 0),
            RowsFailed   = (int)(TempData["ImportRowsFailed"]   ?? 0),
            Errors       = ((string?)TempData["ImportErrors"] ?? string.Empty)
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .ToList()
        };
        return View(vm);
    }

    private async Task PopulateViewBagAsync()
    {
        ViewBag.Profiles = new SelectList(
            await profileService.GetAllActiveAsync(),
            "Id", "Name");

        ViewBag.Accounts = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(),
            "Id", "Name");

        ViewBag.Categories = new SelectList(
            await db.Categories
                .Include(c => c.CategoryType)
                .Where(c => c.IsActive && !c.IsSystem && c.CategoryType.Name == "Expense")
                .OrderBy(c => c.Name)
                .ToListAsync(),
            "Id", "Name");
    }
}
