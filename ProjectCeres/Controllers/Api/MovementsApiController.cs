using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/movements")]
public class MovementsApiController(
    IMovementService movementService,
    ITransactionService transactionService,
    ITransferService transferService,
    ILiabilityPaymentService liabilityPaymentService) : ControllerBase
{
    public record ClearRequest(string Type, bool Cleared);

    [HttpGet]
    public async Task<MovementsPageDto> GetMovements(
        [FromQuery] string? q = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var offset = (page - 1) * pageSize;

        var items = await movementService.GetRecentAsync(accountId, from, to, pageSize, offset, q);
        var total = await movementService.CountAsync(accountId, from, to, q);

        var dtoItems = items.Select(MapToDto).ToList();

        return new MovementsPageDto(dtoItems, total, page, pageSize);
    }

    [HttpPatch("{id:guid}/cleared")]
    public async Task<IActionResult> PatchCleared(Guid id, [FromBody] ClearRequest request)
    {
        switch (request.Type.ToLowerInvariant())
        {
            case "transaction":
                var tx = await transactionService.GetByIdForEditAsync(id);
                if (tx is null) return NotFound();
                await transactionService.MarkClearedAsync(id, request.Cleared);
                return Ok();

            case "transfer":
                var tr = await transferService.GetByIdAsync(id);
                if (tr is null) return NotFound();
                await transferService.MarkClearedAsync(id, request.Cleared);
                return Ok();

            default:
                return BadRequest(new { error = new { code = "INVALID_TYPE", message = "Type must be 'transaction' or 'transfer'." } });
        }
    }

    private static MovementListItemDto MapToDto(MovementListItemViewModel m)
    {
        return new MovementListItemDto(
            Id: m.Id,
            MovementType: m.MovementType.ToString(),
            Date: m.Date,
            Amount: m.Amount,
            CurrencyCode: "",
            CurrencySymbol: m.CurrencySymbol ?? "",
            Description: m.Description,
            IsCleared: m.IsCleared,
            AccountName: m.AccountName,
            CategoryName: m.CategoryName,
            CategoryTypeName: m.CategoryTypeName,
            SourceAccountName: m.SourceAccountName,
            DestAccountName: m.DestAccountName,
            AssetAccountName: m.AssetAccountName,
            LiabilityAccountName: m.LiabilityAccountName);
    }
}
