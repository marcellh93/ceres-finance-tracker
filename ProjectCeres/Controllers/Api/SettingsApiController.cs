using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/settings")]
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
            BudgetPeriodStartDay:   settings.BudgetPeriodStartDay);
        return Ok(dto);
    }
}
