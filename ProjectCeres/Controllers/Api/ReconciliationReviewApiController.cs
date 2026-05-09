using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Common;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/reconciliation-review")]
[Authorize]
public class ReconciliationReviewApiController(IImportStagedTransactionService stagedService) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<StagedTransactionDto>>> GetPending()
    {
        var pending = await stagedService.GetPendingAsync();
        return Ok(pending.Select(s => new StagedTransactionDto(
            s.Id, s.ImportedAt,
            s.AccountId, s.Account?.Name ?? s.AccountId.ToString(),
            s.Account?.Currency?.Code   ?? string.Empty,
            s.Account?.Currency?.Symbol ?? string.Empty,
            s.RawDate, s.RawAmount, s.RawDescription,
            s.MatchedTransactionId,
            s.MatchedTransaction?.Description,
            s.MatchedTransaction?.Date ?? s.RawDate,
            s.MatchedTransaction?.Amount ?? s.RawAmount)));
    }

    [HttpGet("pending/count")]
    public async Task<ActionResult<int>> GetPendingCount() =>
        Ok(await stagedService.GetPendingCountAsync());

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id)
    {
        var result = await stagedService.TryConfirmAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPost("confirm-all")]
    public async Task<IActionResult> ConfirmAll()
    {
        var result = await stagedService.TryConfirmAllAsync();
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPost("{id:guid}/dispute")]
    public async Task<IActionResult> Dispute(Guid id)
    {
        var result = await stagedService.TryDisputeAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    private IActionResult ToErrorResponse(ResultError error) => error.Code switch
    {
        "NOT_FOUND" => NotFound(),
        _           => UnprocessableEntity(new { error = new { code = error.Code, message = error.Message, details = Array.Empty<object>() } })
    };
}
