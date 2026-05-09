using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/currencies")]
[Authorize]
public class CurrenciesApiController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() =>
        Ok(await db.Currencies
            .OrderBy(c => c.Code)
            .Select(c => new { id = c.Id, code = c.Code, name = c.Name, symbol = c.Symbol })
            .ToListAsync());
}
