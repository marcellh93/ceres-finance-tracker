using Microsoft.AspNetCore.Mvc;
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
}
