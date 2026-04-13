using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class CategoriesController(ICategoryService categoryService, AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var categories = await categoryService.GetAllAsync(includeInactive: true);
        return View(categories);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateViewBagAsync();
        return View(new CategoryCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await categoryService.CreateAsync(vm);
            TempData["SuccessMessage"] = $"Category \"{vm.Name}\" created.";
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
        var category = await categoryService.GetByIdAsync(id);
        if (category is null) return NotFound();
        if (category.IsSystem)
        {
            TempData["ErrorMessage"] = "System categories cannot be edited.";
            return RedirectToAction(nameof(Index));
        }

        var vm = new CategoryEditViewModel
        {
            Id           = category.Id,
            Name         = category.Name,
            LifestyleTag = category.LifestyleTag
        };

        await PopulateViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        try
        {
            await categoryService.UpdateAsync(vm);
            TempData["SuccessMessage"] = $"Category \"{vm.Name}\" updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }
    }

    public async Task<IActionResult> Deactivate(Guid id)
    {
        var category = await categoryService.GetByIdAsync(id);
        if (category is null) return NotFound();
        if (category.IsSystem)
        {
            TempData["ErrorMessage"] = "System categories cannot be deactivated.";
            return RedirectToAction(nameof(Index));
        }
        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, string _ = "")
    {
        try
        {
            var category = await categoryService.GetByIdAsync(id);
            if (category is null) return NotFound();

            await categoryService.DeactivateAsync(id);
            TempData["SuccessMessage"] = $"Category \"{category.Name}\" deactivated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateViewBagAsync()
    {
        ViewBag.CategoryTypes = new SelectList(
            await db.CategoryTypes.OrderBy(t => t.Name).ToListAsync(), "Id", "Name");

        ViewBag.LifestyleTags = new SelectList(new[]
        {
            new { Value = "",       Text = "— None —" },
            new { Value = "Needs",  Text = "Needs" },
            new { Value = "Wants",  Text = "Wants" },
            new { Value = "Savings", Text = "Savings" }
        }, "Value", "Text");
    }
}
