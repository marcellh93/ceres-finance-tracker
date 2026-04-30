using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/accounts")]
public class AccountsApiController(AppDbContext db) : ControllerBase
{
    [HttpGet("active")]
    public async Task<IReadOnlyList<AccountOptionDto>> GetActive()
    {
        return await db.Accounts
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new AccountOptionDto(
                a.Id,
                a.Name,
                a.Currency.Code,
                a.Currency.Symbol,
                a.AccountType.Name))
            .ToListAsync();
    }
}
