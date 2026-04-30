using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/transfers")]
public class TransfersApiController(ITransferService transferService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransferRequest request)
    {
        // [ApiController] auto-runs ModelState validation via InvalidModelStateResponseFactory (returns 422).
        // We only need explicit ValidationProblem for cross-field checks below.

        if (request.SourceAccountId == request.DestAccountId)
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "One or more fields are invalid.",
                    details = new[]
                    {
                        new { field = nameof(request.DestAccountId), message = "Source and destination accounts must be different." }
                    }
                }
            });
        }

        var vm = new TransferCreateViewModel
        {
            Date            = request.Date,
            Amount          = request.Amount,
            SourceAccountId = request.SourceAccountId,
            DestAccountId   = request.DestAccountId,
            Description     = request.Description
        };

        try
        {
            var transfer = await transferService.CreateAsync(vm);
            return Created($"/api/transfers/{transfer.Id}", new { id = transfer.Id });
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
