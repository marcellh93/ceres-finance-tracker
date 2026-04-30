using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/transactions")]
public class TransactionsApiController(
    ITransactionService transactionService,
    AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionRequest request)
    {
        var vm = new TransactionCreateViewModel
        {
            TransactionType = TransactionTypes.Regular,
            Date = request.Date,
            Amount = request.Amount,
            AccountId = request.AccountId,
            CategoryId = request.CategoryId,
            Description = request.Description
        };

        var id = await transactionService.CreateAsync(vm);

        return Created($"/api/transactions/{id}", new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransactionEditDto>> Get(Guid id)
    {
        var dto = await db.Transactions
            .Where(t => t.Id == id)
            .Select(t => new TransactionEditDto(
                t.Id,
                t.Date,
                t.Amount,
                t.AccountId,
                t.CategoryId,
                t.Description,
                t.IsCleared,
                t.Attachments
                    .OrderBy(a => a.UploadedAt)
                    .ThenBy(a => a.Id)
                    .Select(a => new AttachmentDto(a.Id, a.FileName, a.FileSizeBytes, a.ContentType, a.UploadedAt))
                    .ToList()))
            .SingleOrDefaultAsync();

        return dto is null ? NotFound() : dto;
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTransactionRequest request)
    {
        // [ApiController] auto-runs ModelState → 422 via InvalidModelStateResponseFactory.

        var existing = await transactionService.GetByIdForEditAsync(id);
        if (existing is null) return NotFound();

        var vm = new TransactionEditViewModel
        {
            Id              = id,
            TransactionType = TransactionTypes.Regular,
            Date            = request.Date,
            Amount          = request.Amount,
            AccountId       = request.AccountId,
            CategoryId      = request.CategoryId,
            Description     = request.Description,
            IsCleared       = request.IsCleared,
            BudgetId        = request.BudgetId,
            NeedsReview     = request.NeedsReview
        };

        try
        {
            await transactionService.UpdateAsync(vm);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = ex.Message,
                    details = Array.Empty<object>()
                }
            });
        }
    }
}
