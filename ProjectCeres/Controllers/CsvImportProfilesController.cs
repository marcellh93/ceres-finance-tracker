// ProjectCeres/Controllers/CsvImportProfilesController.cs
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class ImportProfilesController(IImportProfileService profileService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var active  = await profileService.GetAllActiveAsync();
        var deleted = await profileService.GetRecentlyDeletedAsync();
        ViewBag.DeletedProfiles = deleted;
        return View(active);
    }

    public IActionResult Create() => View(new ImportProfileCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ImportProfileCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            await profileService.CreateAsync(vm.Name, vm.Format, vm.Mappings);
            TempData["SuccessMessage"] = $"Import profile \"{vm.Name}\" created.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var profile = await profileService.GetByIdAsync(id);
        if (profile is null) return NotFound();

        var vm = new ImportProfileEditViewModel
        {
            Id       = profile.Id,
            Name     = profile.Name,
            Format   = profile.Format,
            Mappings = profile.Mappings
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ImportProfileEditViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            await profileService.UpdateAsync(vm.Id, vm.Name, vm.Mappings);
            TempData["SuccessMessage"] = $"Import profile \"{vm.Name}\" updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    public async Task<IActionResult> Delete(Guid id)
    {
        var profile = await profileService.GetByIdAsync(id);
        if (profile is null) return NotFound();
        return View(profile);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        try
        {
            var profile = await profileService.GetByIdAsync(id);
            if (profile is null) return NotFound();
            await profileService.DeleteAsync(id);
            TempData["SuccessMessage"] = $"Import profile \"{profile.Name}\" deleted. Recoverable for 90 days.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recover(Guid id)
    {
        try
        {
            var profile = await profileService.GetByIdAsync(id);
            if (profile is null) return NotFound();
            await profileService.RecoverAsync(id);
            TempData["SuccessMessage"] = $"Import profile \"{profile.Name}\" restored.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
