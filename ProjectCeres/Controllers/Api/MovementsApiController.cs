using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/movements")]
public class MovementsApiController(
    IMovementService movementService,
    ITransactionService transactionService,
    ITransferService transferService,
    ILiabilityPaymentService liabilityPaymentService,
    IMovementExportService exportService,
    ICurrentUserAccessor user,
    AppDbContext db) : ControllerBase
{
    public record ClearRequest(string Type, bool Cleared);

    [HttpGet]
    public async Task<ActionResult<MovementsPageDto>> GetMovements(
        [FromQuery] string? q = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? type = null,
        [FromQuery] string? currency = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var (typedFilter, error) = ParseType(type);
        if (error is not null) return error;

        var offset = (page - 1) * pageSize;

        var items = await movementService.GetRecentAsync(accountId, from, to, pageSize, offset, q, typedFilter, currency);
        var total = await movementService.CountAsync(accountId, from, to, q, typedFilter, currency);

        var dtoItems = items.Select(MapToDto).ToList();

        return Ok(new MovementsPageDto(dtoItems, total, page, pageSize));
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

            case "liabilitypayment":
                var lp = await liabilityPaymentService.GetByIdAsync(id);
                if (lp is null) return NotFound();
                await liabilityPaymentService.MarkClearedAsync(id, request.Cleared);
                return Ok();

            default:
                return BadRequest(new { error = new { code = "INVALID_TYPE", message = "Type must be 'transaction', 'transfer', or 'liabilitypayment'." } });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MovementTypeDto>> GetType(Guid id)
    {
        var type = await movementService.GetTypeAsync(id);
        if (type is null) return NotFound();
        return new MovementTypeDto(id, MovementTypeFormatting.Format(type.Value));
    }

    [HttpPost("bulk-cleared")]
    public async Task<ActionResult<object>> BulkCleared([FromBody] BulkClearedRequest request)
    {
        var (typedFilter, error) = ParseType(request.Type);
        if (error is not null) return error;

        var total = 0;
        if (typedFilter is null or MovementType.Transaction)
            total += await transactionService.BulkMarkClearedAsync(request.From, request.To, request.AccountId, request.Currency);
        if (typedFilter is null or MovementType.Transfer)
            total += await transferService.BulkMarkClearedAsync(request.From, request.To, request.AccountId, request.Currency);
        if (typedFilter is null or MovementType.LiabilityPayment)
            total += await liabilityPaymentService.BulkMarkClearedAsync(request.From, request.To, request.AccountId, request.Currency);

        return Ok(new { cleared = total });
    }

    private (MovementType? typed, BadRequestObjectResult? error) ParseType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return (null, null);

        MovementType? typed = type.ToLowerInvariant() switch
        {
            "transaction"      => MovementType.Transaction,
            "transfer"         => MovementType.Transfer,
            "liabilitypayment" => MovementType.LiabilityPayment,
            _ => null
        };

        if (typed is null)
            return (null, BadRequest(new { error = new { code = "INVALID_TYPE", message = "type must be 'transaction', 'transfer', or 'liabilitypayment'." } }));

        return (typed, null);
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> Export(
        [FromQuery] string? q = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? type = null,
        [FromQuery] string? currency = null)
    {
        var (typedFilter, error) = ParseType(type);
        if (error is not null) return error;

        var csv = await exportService.BuildCsvAsync(accountId, from, to, q, typedFilter, currency);

        // UTF-8 BOM so Excel/Numbers on macOS render the € symbol (and other
        // multibyte glyphs) correctly instead of misinterpreting the file as MacRoman.
        var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = utf8.GetPreamble().Concat(utf8.GetBytes(csv)).ToArray();

        var accountName = accountId is { } id
            ? await db.Accounts.Owned(user).Where(a => a.Id == id).Select(a => a.Name).FirstOrDefaultAsync()
            : null;
        var fileName = BuildExportFileName(accountName, type, from, to, currency);

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static string BuildExportFileName(string? accountName, string? type, DateOnly? from, DateOnly? to, string? currency)
    {
        var parts = new List<string> { "movements" };

        if (!string.IsNullOrWhiteSpace(currency))
            parts.Add(currency.ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(type))
        {
            var typeLabel = type.ToLowerInvariant() switch
            {
                "transaction"      => "transactions",
                "transfer"         => "transfers",
                "liabilitypayment" => "liability-payments",
                _                  => null
            };
            if (typeLabel is not null) parts.Add(typeLabel);
        }

        if (!string.IsNullOrWhiteSpace(accountName))
            parts.Add(Slugify(accountName));

        if (from is { } f && to is { } t)
            parts.Add($"{f:yyyy-MM-dd}_{t:yyyy-MM-dd}");
        else if (from is { } fOnly)
            parts.Add($"from-{fOnly:yyyy-MM-dd}");
        else if (to is { } tOnly)
            parts.Add($"to-{tOnly:yyyy-MM-dd}");
        else
            parts.Add(DateTime.Today.ToString("yyyy-MM-dd"));

        return string.Join("_", parts) + ".csv";
    }

    private static string Slugify(string input)
    {
        var lower = input.ToLowerInvariant();
        var chars = lower.Select(c =>
            char.IsLetterOrDigit(c) ? c :
            (c == ' ' || c == '-' || c == '_') ? '-' :
            '\0').Where(c => c != '\0').ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return string.IsNullOrEmpty(slug) ? "account" : slug;
    }

    private static MovementListItemDto MapToDto(MovementListItemViewModel m)
    {
        return new MovementListItemDto(
            Id: m.Id,
            MovementType: MovementTypeFormatting.Format(m.MovementType),
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
