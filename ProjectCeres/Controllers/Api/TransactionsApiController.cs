using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/transactions")]
public class TransactionsApiController(ITransactionService transactionService) : ControllerBase
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
        var vm = await transactionService.GetByIdForEditAsync(id);
        if (vm is null) return NotFound();

        using var scope = HttpContext.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attachments = await db.TransactionAttachments
            .Where(a => a.TransactionId == id)
            .Select(a => new AttachmentDto(a.Id, a.FileName, a.FileSizeBytes, a.ContentType, a.UploadedAt))
            .ToListAsync();

        return new TransactionEditDto(
            Id:          vm.Id,
            Date:        vm.Date,
            Amount:      vm.Amount,
            AccountId:   vm.AccountId!.Value,
            CategoryId:  vm.CategoryId!.Value,
            Description: vm.Description,
            IsCleared:   vm.IsCleared,
            Attachments: attachments);
    }
}
