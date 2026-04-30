using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/liability-payments")]
public class LiabilityPaymentsApiController(ILiabilityPaymentService liabilityPaymentService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLiabilityPaymentRequest request)
    {
        // [ApiController] auto-runs ModelState validation via InvalidModelStateResponseFactory (returns 422).

        var vm = new TransactionCreateViewModel
        {
            TransactionType    = "LiabilityPayment",
            Date               = request.Date,
            Amount             = request.Amount,
            AccountId          = request.AssetAccountId,
            LiabilityAccountId = request.LiabilityAccountId,
            Description        = request.Description
        };

        var payment = await liabilityPaymentService.CreateAsync(vm);
        return Created($"/api/liability-payments/{payment.Id}", new { id = payment.Id });
    }
}
