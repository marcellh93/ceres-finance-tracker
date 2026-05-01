using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/category-budgets")]
public class CategoryBudgetsApiController(
    ICategoryBudgetService categoryBudgetService,
    ISettingsService settingsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryBudgetListItemDto>>> Get(
        [FromQuery] string? currency = null,
        [FromQuery] bool includeArchived = false)
    {
        var budgets  = await categoryBudgetService.GetAllAsync(includeInactive: includeArchived, currency: currency);
        var settings = await settingsService.GetAsync();
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var (year, month) = BudgetPeriod.GetCurrentPeriodMonth(today, settings.PeriodStartDay);
        var (_, periodEnd) = BudgetPeriod.GetBoundsForMonth(year, month, settings.PeriodStartDay);

        var dtos = new List<CategoryBudgetListItemDto>();
        foreach (var b in budgets)
        {
            var spent = await categoryBudgetService.GetActualSpendAsync(b.Id, year, month);
            dtos.Add(new CategoryBudgetListItemDto(
                Id:                 b.Id,
                CategoryId:         b.CategoryId,
                CategoryName:       b.Category.Name,
                CurrencyCode:       b.Currency.Code,
                CurrencySymbol:     b.Currency.Symbol,
                LimitAmount:        b.LimitAmount,
                IsActive:           b.IsActive,
                CurrentPeriodSpend: spent,
                CurrentPeriodEnd:   periodEnd));
        }
        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryBudgetEditDto>> GetById(Guid id)
    {
        var b = await categoryBudgetService.GetByIdAsync(id);
        if (b is null) return NotFound();
        return new CategoryBudgetEditDto(
            Id:             b.Id,
            CategoryId:     b.CategoryId,
            CurrencyId:     b.CurrencyId,
            CurrencyCode:   b.Currency.Code,
            LimitAmount:    b.LimitAmount,
            IsActive:       b.IsActive);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryBudgetRequest request)
    {
        if (!ModelState.IsValid) return UnprocessableEntity(ToValidationEnvelope());

        var vm = new CategoryBudgetCreateViewModel
        {
            CategoryId  = request.CategoryId,
            CurrencyId  = request.CurrencyId,
            LimitAmount = request.LimitAmount
        };

        try
        {
            var budget = await categoryBudgetService.CreateAsync(vm);
            return Created($"/api/category-budgets/{budget.Id}", new { id = budget.Id });
        }
        catch (DuplicateBudgetException ex)
        {
            return Conflict(new
            {
                error = new
                {
                    code             = "DUPLICATE_BUDGET",
                    message          = ex.Message,
                    existingBudgetId = ex.ExistingBudgetId,
                    existingIsActive = ex.ExistingIsActive
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = new { code = "VALIDATION_ERROR", message = ex.Message, details = Array.Empty<object>() } });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryBudgetRequest request)
    {
        if (!ModelState.IsValid) return UnprocessableEntity(ToValidationEnvelope());

        var existing = await categoryBudgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        // Note: CategoryBudgetEditViewModel only carries Id + LimitAmount;
        // CategoryId/CurrencyId/IsActive on the request are accepted for SPA symmetry
        // but are not editable here — use the archive/reactivate endpoints for IsActive,
        // and a separate (Category, Currency) means a different budget row entirely.
        var vm = new CategoryBudgetEditViewModel
        {
            Id          = id,
            LimitAmount = request.LimitAmount
        };

        try
        {
            await categoryBudgetService.UpdateAsync(vm);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = new { code = "VALIDATION_ERROR", message = ex.Message, details = Array.Empty<object>() } });
        }
    }

    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var existing = await categoryBudgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();
        await categoryBudgetService.DeactivateAsync(id);
        return NoContent();
    }

    [HttpPatch("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var existing = await categoryBudgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        try
        {
            await categoryBudgetService.ReactivateAsync(id);
            return NoContent();
        }
        catch (DuplicateBudgetException ex)
        {
            return Conflict(new
            {
                error = new
                {
                    code             = "DUPLICATE_BUDGET",
                    message          = ex.Message,
                    existingBudgetId = ex.ExistingBudgetId,
                    existingIsActive = ex.ExistingIsActive
                }
            });
        }
    }

    [HttpGet("{id:guid}/spend")]
    public async Task<ActionResult<CategoryBudgetSpendDto>> GetSpend(Guid id, [FromQuery] int year, [FromQuery] int month)
    {
        var existing = await categoryBudgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        var settings = await settingsService.GetAsync();
        var (start, end) = BudgetPeriod.GetBoundsForMonth(year, month, settings.PeriodStartDay);
        var spent = await categoryBudgetService.GetActualSpendAsync(id, year, month);

        return new CategoryBudgetSpendDto(spent, start, end);
    }

    private object ToValidationEnvelope() => new
    {
        error = new
        {
            code    = "VALIDATION_ERROR",
            message = "Validation failed.",
            details = ModelState
                .Where(kv => kv.Value!.Errors.Count > 0)
                .SelectMany(kv => kv.Value!.Errors.Select(e => new { field = kv.Key, message = e.ErrorMessage }))
                .ToArray()
        }
    };
}
