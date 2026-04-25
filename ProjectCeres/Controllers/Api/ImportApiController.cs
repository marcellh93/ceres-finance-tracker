using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/import")]
public class ImportApiController(IImportService importService) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import([FromForm] ImportRequestViewModel vm)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var mappings = new CsvColumnMappings
        {
            DateColumn        = vm.DateColumn!,
            AmountColumn      = vm.AmountColumn!,
            DescriptionColumn = vm.DescriptionColumn!,
            CategoryColumn    = vm.CategoryColumn,
            FlipDebitSign     = vm.FlipDebitSign
        };

        try
        {
            var result = await importService.ImportAsync(
                vm.File!, vm.AccountId!.Value, vm.CategoryId!.Value, mappings);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
