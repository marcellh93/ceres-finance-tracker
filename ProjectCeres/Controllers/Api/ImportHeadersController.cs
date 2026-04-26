// ProjectCeres/Controllers/Api/ImportHeadersController.cs
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/import/headers")]
public class ImportHeadersController(IHeaderDetectionService headerDetectionService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Detect(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        try
        {
            var result = await headerDetectionService.DetectAsync(file);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
