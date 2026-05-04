using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Common;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/transfer-review")]
public class TransferReviewApiController(ITransferReviewService reviewService) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<StagedTransferDto>>> GetPending()
    {
        var pending = await reviewService.GetPendingAsync();
        return Ok(pending.Select(s => new StagedTransferDto(
            s.Id, s.ImportedAt,
            s.AccountId, s.Account?.Name ?? s.AccountId.ToString(),
            s.Account?.Currency?.Code   ?? string.Empty,
            s.Account?.Currency?.Symbol ?? string.Empty,
            s.RawDate, s.RawAmount, s.RawDescription,
            s.CandidateTransactionId,
            s.CandidateTransaction?.Description,
            s.CandidateTransaction?.Date,
            s.CandidateTransaction?.Amount)));
    }

    [HttpGet("pending/count")]
    public async Task<ActionResult<int>> GetPendingCount() =>
        Ok(await reviewService.GetPendingCountAsync());

    [HttpPost("{stagedId:guid}/link-to-existing")]
    public async Task<IActionResult> LinkToExisting(Guid stagedId, [FromBody] TransferReviewActionRequest request)
    {
        var result = await reviewService.TryLinkToExistingAsync(stagedId, request.OtherAccountId!.Value);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPost("{stagedId:guid}/create-as-transfer")]
    public async Task<IActionResult> CreateAsTransfer(Guid stagedId, [FromBody] TransferReviewActionRequest request)
    {
        var result = await reviewService.TryCreateAsTransferAsync(stagedId, request.OtherAccountId!.Value);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPost("{stagedId:guid}/dismiss-as-transaction")]
    public async Task<IActionResult> DismissAsTransaction(Guid stagedId)
    {
        var result = await reviewService.TryDismissAsTransactionAsync(stagedId);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    private IActionResult ToErrorResponse(ResultError error) => error.Code switch
    {
        "NOT_FOUND" => NotFound(),
        _           => UnprocessableEntity(new { error = new { code = error.Code, message = error.Message, details = Array.Empty<object>() } })
    };
}
