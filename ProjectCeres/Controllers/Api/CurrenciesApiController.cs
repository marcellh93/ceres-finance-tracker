using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers.Api;

public record CurrencyDto(int Id, string Code, string Name, string Symbol);

[ApiController]
[Route("api/currencies")]
[Authorize]
public class CurrenciesApiController(
    AppDbContext db,
    ICurrentUserAccessor user,
    ISettingsService settingsService) : ControllerBase
{
    /// <summary>
    /// Every currency, or with <paramref name="inUse"/> only those the user has an account in
    /// (archived accounts count) plus their default currency, which reports fall back to.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool inUse = false)
    {
        var query = db.Currencies.AsQueryable();

        if (inUse)
        {
            var defaultCurrencyId = (await settingsService.GetAsync()).DefaultCurrencyId;
            var accountCurrencyIds = db.Accounts.Owned(user).Select(a => a.CurrencyId);
            query = query.Where(c => c.Id == defaultCurrencyId || accountCurrencyIds.Contains(c.Id));
        }

        return Ok(await query
            .OrderBy(c => c.Code)
            .Select(c => new CurrencyDto(c.Id, c.Code, c.Name, c.Symbol))
            .ToListAsync());
    }
}
