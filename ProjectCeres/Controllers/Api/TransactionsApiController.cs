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
            TransactionType = "Regular",
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
                    .Select(a => new AttachmentDto(a.Id, a.FileName, a.FileSizeBytes, a.ContentType, a.UploadedAt))
                    .ToList()))
            .SingleOrDefaultAsync();

        return dto is null ? NotFound() : dto;
    }
}
