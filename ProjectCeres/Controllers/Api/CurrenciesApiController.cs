using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/currencies")]
public class CurrenciesApiController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() =>
        Ok(await db.Currencies
            .OrderBy(c => c.Code)
            .Select(c => new { id = c.Id, code = c.Code, symbol = c.Symbol })
            .ToListAsync());
}
