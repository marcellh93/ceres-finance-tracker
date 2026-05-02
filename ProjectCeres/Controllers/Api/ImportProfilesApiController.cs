using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/import-profiles")]
public class ImportProfilesApiController(IImportProfileService profileService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ImportProfileListItemDto>>> Get(
        [FromQuery] bool includeDeleted = false)
    {
        var active  = await profileService.GetAllActiveAsync();
        var rows = active.Select(ToDto).ToList();
        if (includeDeleted)
        {
            var deleted = await profileService.GetRecentlyDeletedAsync();
            rows.AddRange(deleted.Select(ToDto));
        }
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ImportProfileListItemDto>> GetById(Guid id)
    {
        var profile = await profileService.GetByIdAsync(id);
        return profile is null ? NotFound() : ToDto(profile);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateImportProfileRequest request)
    {
        if (!Enum.TryParse<ImportFormat>(request.Format, ignoreCase: true, out var format))
            return UnprocessableEntity(Envelope("INVALID_FORMAT", $"Unknown format '{request.Format}'."));

        var result = await profileService.TryCreateAsync(request.Name, format, request.Mappings);
        if (!result.IsSuccess) return ToErrorResponse(result.Error!.Value);

        var fresh = await profileService.GetByIdAsync(result.Value);
        return Created($"/api/import-profiles/{result.Value}", ToDto(fresh!));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateImportProfileRequest request)
    {
        var result = await profileService.TryUpdateAsync(id, request.Name, request.Mappings);
        if (!result.IsSuccess) return ToErrorResponse(result.Error!.Value);
        var fresh = await profileService.GetByIdAsync(id);
        return Ok(ToDto(fresh!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await profileService.TryDeleteAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPost("{id:guid}/recover")]
    public async Task<IActionResult> Recover(Guid id)
    {
        var result = await profileService.TryRecoverAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    private static ImportProfileListItemDto ToDto(ImportProfileViewModel p) => new(
        p.Id, p.Name, p.Format.ToString(), p.SheetName, p.Mappings,
        p.CreatedAt, p.DeletedAt, p.DaysUntilPurge);

    private IActionResult ToErrorResponse(ResultError error) => error.Code switch
    {
        "NOT_FOUND" => NotFound(),
        _           => UnprocessableEntity(Envelope(error.Code, error.Message))
    };

    private static object Envelope(string code, string message) => new
    {
        error = new { code, message, details = Array.Empty<object>() }
    };
}
