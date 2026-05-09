// ProjectCeres/Controllers/Api/ImportApiController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/import")]
[Authorize]
public class ImportApiController(IImportService importService) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import([FromForm] ImportRequestViewModel vm)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        const long MaxImportFileBytes = 10 * 1024 * 1024; // 10 MB
        if (vm.File!.Length > MaxImportFileBytes)
            return BadRequest(new { message = "The import file exceeds the 10 MB size limit. Please split the file and try again." });

        var mappings = new ImportColumnMappings
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
                vm.File!, vm.AccountId!.Value, mappings);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
