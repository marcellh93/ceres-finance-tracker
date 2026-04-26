using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class ImportController(
    IImportService importService,
    IImportProfileService profileService,
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

        const long MaxImportFileBytes = 10 * 1024 * 1024;
        if (vm.File!.Length > MaxImportFileBytes)
        {
            ModelState.AddModelError("File", "The import file exceeds the 10 MB size limit. Please split the file and try again.");
            await PopulateViewBagAsync();
            return View(vm);
        }

        ImportColumnMappings mappings;
        bool usedProfile = false;

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
            usedProfile = true;
        }
        else
        {
            mappings = new ImportColumnMappings
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
            var result = await importService.ImportAsync(vm.File!, vm.AccountId!.Value, mappings);

            TempData["ImportRowsImported"]   = result.RowsImported;
            TempData["ImportRowsReconciled"] = result.RowsReconciled;
            TempData["ImportRowsFlagged"]    = result.RowsFlagged;
            TempData["ImportRowsFailed"]     = result.RowsFailed;
            TempData["ImportErrors"]         = string.Join("\n", result.Errors);

            if (!usedProfile)
            {
                TempData["SaveMappingsDate"]        = mappings.DateColumn;
                TempData["SaveMappingsAmount"]      = mappings.AmountColumn;
                TempData["SaveMappingsDescription"] = mappings.DescriptionColumn;
                TempData["SaveMappingsCategory"]    = mappings.CategoryColumn;
                TempData["SaveMappingsFlipDebit"]   = mappings.FlipDebitSign;
            }

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
            RowsImported   = (int)(TempData["ImportRowsImported"]   ?? 0),
            RowsReconciled = (int)(TempData["ImportRowsReconciled"] ?? 0),
            RowsFlagged    = (int)(TempData["ImportRowsFlagged"]    ?? 0),
            RowsFailed     = (int)(TempData["ImportRowsFailed"]     ?? 0),
            Errors         = ((string?)TempData["ImportErrors"] ?? string.Empty)
                              .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                              .ToList()
        };

        if (TempData["SaveMappingsDate"] is string dateCol)
        {
            vm.MappingsToSave = new ImportColumnMappings
            {
                DateColumn        = dateCol,
                AmountColumn      = (string?)TempData["SaveMappingsAmount"]      ?? "Amount",
                DescriptionColumn = (string?)TempData["SaveMappingsDescription"] ?? "Description",
                CategoryColumn    = (string?)TempData["SaveMappingsCategory"],
                FlipDebitSign     = (bool?)TempData["SaveMappingsFlipDebit"]     ?? false
            };
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProfile(string profileName,
        string dateColumn, string amountColumn, string descriptionColumn,
        string? categoryColumn, bool flipDebitSign)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            TempData["ErrorMessage"] = "Profile name is required.";
            return RedirectToAction(nameof(Summary));
        }

        var mappings = new ImportColumnMappings
        {
            DateColumn        = dateColumn,
            AmountColumn      = amountColumn,
            DescriptionColumn = descriptionColumn,
            CategoryColumn    = categoryColumn,
            FlipDebitSign     = flipDebitSign
        };

        await profileService.CreateAsync(profileName, Models.ImportFormat.Csv, mappings);
        TempData["SuccessMessage"] = $"Profile \"{profileName}\" saved.";
        return RedirectToAction(nameof(Summary));
    }

    private async Task PopulateViewBagAsync()
    {
        ViewBag.Profiles = new SelectList(
            await profileService.GetAllActiveAsync(),
            "Id", "Name");

        ViewBag.Accounts = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(),
            "Id", "Name");
    }
}
