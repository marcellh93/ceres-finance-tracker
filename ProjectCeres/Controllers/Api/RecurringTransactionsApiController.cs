using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/recurring-transactions")]
[Authorize]
public class RecurringTransactionsApiController(
    AppDbContext db,
    IRecurringTransactionService reminderService,
    ICurrentUserAccessor user) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RecurringTransactionListItemDto>>> Get(
        [FromQuery] bool includeInactive = false)
    {
        var query = db.RecurringTransactions
            .Owned(user)
            .Include(r => r.Account).ThenInclude(a => a.Currency)
            .Include(r => r.Category).ThenInclude(c => c.CategoryType)
            .AsQueryable();
        if (!includeInactive) query = query.Where(r => r.IsActive);

        var rows = await query
            .OrderBy(r => r.NextDueDate)
            .ToListAsync();

        return Ok(rows.Select(r => new RecurringTransactionListItemDto(
            r.Id, r.Name, r.EstimatedAmount,
            r.AccountId, r.Account.Name, r.Account.Currency.Symbol,
            r.CategoryId, r.Category.Name, r.Category.CategoryType.Name,
            r.Frequency.ToString(), r.DayOfPeriod, r.NextDueDate, r.IsActive, r.ReminderBehaviour.ToString())));
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<IEnumerable<RecurringTransactionListItemDto>>> GetUpcoming(
        [FromQuery] int days = 7)
    {
        if (days < 0) days = 0;
        var reminders = await reminderService.GetUpcomingAsync(days);
        var rows = reminders.Select(r => new RecurringTransactionListItemDto(
            r.Id, r.Name, r.EstimatedAmount,
            r.AccountId, r.Account.Name, r.Account.Currency?.Symbol ?? "",
            r.CategoryId, r.Category.Name, r.Category.CategoryType?.Name ?? "",
            r.Frequency.ToString(), r.DayOfPeriod, r.NextDueDate, r.IsActive, r.ReminderBehaviour.ToString()));
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RecurringTransactionDetailDto>> GetById(Guid id)
    {
        var r = await db.RecurringTransactions.Owned(user).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound();
        return ToDetail(r);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecurringTransactionRequest request)
    {
        var result = await reminderService.TryCreateAsync(request);
        if (!result.IsSuccess) return ToErrorResponse(result.Error!.Value);
        return Created($"/api/recurring-transactions/{result.Value!.Id}", ToDetail(result.Value!));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRecurringTransactionRequest request)
    {
        var result = await reminderService.TryUpdateAsync(id, request);
        if (!result.IsSuccess) return ToErrorResponse(result.Error!.Value);
        return Ok(ToDetail(result.Value!));
    }

    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var result = await reminderService.TryDeactivateAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPatch("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var result = await reminderService.TryReactivateAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] ConfirmRecurringTransactionRequest request)
    {
        var result = await reminderService.TryConfirmAsync(id, request);
        if (!result.IsSuccess) return ToErrorResponse(result.Error!.Value);
        return Created($"/api/transactions/{result.Value!.Id}", new { transactionId = result.Value!.Id });
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, [FromBody] DismissRecurringTransactionRequest? body)
    {
        var result = await reminderService.TryDismissAsync(id, body?.NextDueDate);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    private static RecurringTransactionDetailDto ToDetail(Models.RecurringTransaction r) => new(
        r.Id, r.Name, r.EstimatedAmount,
        r.AccountId, r.CategoryId,
        r.Frequency.ToString(), r.DayOfPeriod, r.NextDueDate, r.IsActive, r.ReminderBehaviour.ToString());

    private IActionResult ToErrorResponse(ResultError error) => error.Code switch
    {
        "NOT_FOUND" => NotFound(),
        _           => UnprocessableEntity(new { error = new { code = error.Code, message = error.Message, details = Array.Empty<object>() } })
    };
}
