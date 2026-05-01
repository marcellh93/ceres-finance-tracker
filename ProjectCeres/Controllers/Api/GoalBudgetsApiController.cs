using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/goal-budgets")]
public class GoalBudgetsApiController(IBudgetService budgetService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GoalBudgetListItemDto>>> Get(
        [FromQuery] string? currency = null,
        [FromQuery] string? type = null,
        [FromQuery] bool includeArchived = false)
    {
        try
        {
            var budgets = await budgetService.GetAllAsync(includeInactive: includeArchived, currency: currency, type: type);
            var dtos = new List<GoalBudgetListItemDto>();
            foreach (var b in budgets)
            {
                var progress = await budgetService.GetProgressAsync(b.Id);
                dtos.Add(new GoalBudgetListItemDto(
                    Id:                 b.Id,
                    Name:               b.Name,
                    GoalType:           b.GoalType,
                    CurrencyCode:       b.Currency.Code,
                    CurrencySymbol:     b.Currency.Symbol,
                    TargetAmount:       b.TargetAmount,
                    StartDate:          b.StartDate,
                    EndDate:            b.EndDate,
                    Description:        b.Description,
                    IsActive:           b.IsActive,
                    LinkedAccountId:    b.LinkedAccountId,
                    LinkedAccountName:  b.LinkedAccount?.Name,
                    Progress:           progress.AmountProgress));
            }
            return Ok(dtos);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = new { code = "INVALID_TYPE", message = ex.Message } });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoalBudgetEditDto>> GetById(Guid id)
    {
        var b = await budgetService.GetByIdAsync(id);
        if (b is null) return NotFound();
        return new GoalBudgetEditDto(
            Id:              b.Id,
            Name:            b.Name,
            GoalType:        b.GoalType,
            CurrencyId:      b.CurrencyId,
            CurrencyCode:    b.Currency.Code,
            TargetAmount:    b.TargetAmount,
            StartDate:       b.StartDate,
            EndDate:         b.EndDate,
            Description:     b.Description,
            IsActive:        b.IsActive,
            LinkedAccountId: b.LinkedAccountId);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGoalBudgetRequest request)
    {
        if (!ModelState.IsValid) return UnprocessableEntity(ToValidationEnvelope());

        var vm = new BudgetCreateViewModel
        {
            Name            = request.Name,
            TargetAmount    = request.TargetAmount,
            CurrencyId      = request.CurrencyId,
            StartDate       = request.StartDate,
            EndDate         = request.EndDate,
            Description     = request.Description,
            GoalType        = request.GoalType,
            LinkedAccountId = request.LinkedAccountId
        };

        try
        {
            var budget = await budgetService.CreateAsync(vm);
            return Created($"/api/goal-budgets/{budget.Id}", new { id = budget.Id });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = new { code = "VALIDATION_ERROR", message = ex.Message, details = Array.Empty<object>() } });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGoalBudgetRequest request)
    {
        if (!ModelState.IsValid) return UnprocessableEntity(ToValidationEnvelope());

        var existing = await budgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        var vm = new BudgetEditViewModel
        {
            Id              = id,
            Name            = request.Name,
            TargetAmount    = request.TargetAmount,
            CurrencyId      = request.CurrencyId,
            StartDate       = request.StartDate,
            EndDate         = request.EndDate,
            Description     = request.Description,
            GoalType        = request.GoalType,
            LinkedAccountId = request.LinkedAccountId
        };

        try
        {
            await budgetService.UpdateAsync(vm);
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
        var existing = await budgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();
        await budgetService.DeactivateAsync(id);
        return NoContent();
    }

    [HttpPatch("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var existing = await budgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();
        await budgetService.ReactivateAsync(id);
        return NoContent();
    }

    [HttpGet("{id:guid}/progress")]
    public async Task<ActionResult<GoalBudgetProgressDto>> GetProgress(Guid id)
    {
        var existing = await budgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();
        var p = await budgetService.GetProgressAsync(id);
        var pct = p.TargetAmount == 0m ? 0 : (int)Math.Round(p.AmountProgress / p.TargetAmount * 100m);
        return new GoalBudgetProgressDto(p.AmountProgress, p.TargetAmount, pct);
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
