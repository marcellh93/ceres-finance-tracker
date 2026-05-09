using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Common;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsApiController(ISettingsService settingsService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var settings = await settingsService.GetAsync();
        var dto = new SettingsDto(
            NumberFormat:           settings.NumberFormat,
            DateFormat:             settings.DateFormat,
            DefaultCurrencyCode:    settings.DefaultCurrency.Code,
            DefaultCurrencySymbol:  settings.DefaultCurrency.Symbol,
            PeriodStartDay:         settings.PeriodStartDay);
        return Ok(dto);
    }

    [HttpPatch]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request)
    {
        var result = await settingsService.TryUpdateAsync(request);
        if (result.IsSuccess) return NoContent();
        var error = result.Error!.Value;
        return error.Code switch
        {
            "INVALID_CURRENCY" => UnprocessableEntity(Envelope(error)),
            _                  => UnprocessableEntity(Envelope(error)),
        };
    }

    private static object Envelope(ResultError error) => new
    {
        error = new { code = error.Code, message = error.Message, details = Array.Empty<object>() }
    };
}
