using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;
using static ProjectCeres.ViewModels.TransactionTypes;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/liability-payments")]
public class LiabilityPaymentsApiController(
    ILiabilityPaymentService liabilityPaymentService,
    AppDbContext db,
    ICurrentUserAccessor user) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLiabilityPaymentRequest request)
    {
        // [ApiController] auto-runs ModelState validation via InvalidModelStateResponseFactory (returns 422).

        var vm = new TransactionCreateViewModel
        {
            TransactionType    = LiabilityPayment,
            Date               = request.Date,
            Amount             = request.Amount,
            AccountId          = request.AssetAccountId,
            LiabilityAccountId = request.LiabilityAccountId,
            Description        = request.Description
        };

        var payment = await liabilityPaymentService.CreateAsync(vm);
        return Created($"/api/liability-payments/{payment.Id}", new { id = payment.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LiabilityPaymentEditDto>> Get(Guid id)
    {
        var dto = await db.LiabilityPayments
            .Owned(user)
            .Where(p => p.Id == id)
            .Select(p => new LiabilityPaymentEditDto(
                p.Id,
                p.Date,
                p.Amount,
                p.AssetAccountId,
                p.LiabilityAccountId,
                p.Description,
                p.IsCleared))
            .SingleOrDefaultAsync();

        return dto is null ? NotFound() : dto;
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLiabilityPaymentRequest request)
    {
        var existing = await liabilityPaymentService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        var vm = new TransactionEditViewModel
        {
            Id                  = id,
            TransactionType     = LiabilityPayment,
            Date                = request.Date,
            Amount              = request.Amount,
            AccountId           = request.AssetAccountId,
            LiabilityAccountId  = request.LiabilityAccountId,
            Description         = request.Description,
            IsCleared           = request.IsCleared
        };

        try
        {
            await liabilityPaymentService.UpdateAsync(vm);
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var existing = await liabilityPaymentService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        try
        {
            await liabilityPaymentService.DeleteAsync(id);
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
