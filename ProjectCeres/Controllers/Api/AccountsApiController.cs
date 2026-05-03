using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/accounts")]
public class AccountsApiController(
    AppDbContext db,
    IAccountService accountService,
    ICurrentUserAccessor user) : ControllerBase
{
    [HttpGet("active")]
    public async Task<IReadOnlyList<AccountOptionDto>> GetActive()
    {
        return await db.Accounts
            .Owned(user)
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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccountListItemDto>>> Get([FromQuery] bool includeInactive = false)
    {
        var query = db.Accounts
            .Include(a => a.AccountType)
            .Include(a => a.Currency)
            .Owned(user)
            .AsQueryable();
        if (!includeInactive) query = query.Where(a => a.IsActive);

        var accounts = await query.OrderBy(a => a.Name).ToListAsync();

        var dtos = new List<AccountListItemDto>(accounts.Count);
        foreach (var a in accounts)
        {
            var balance = await accountService.GetBalanceAsync(a.Id);
            var hasTransactions =
                await db.Transactions.Owned(user).AnyAsync(t => t.AccountId == a.Id) ||
                await db.Transfers.Owned(user).AnyAsync(t => t.SourceAccountId == a.Id || t.DestAccountId == a.Id) ||
                await db.LiabilityPayments.Owned(user).AnyAsync(p => p.AssetAccountId == a.Id || p.LiabilityAccountId == a.Id);
            dtos.Add(new AccountListItemDto(
                a.Id, a.Name, a.AccountTypeId, a.AccountType.Name,
                a.CurrencyId, a.Currency.Code, a.Currency.Symbol,
                a.Description, a.IsActive, a.ExcludeFromSpendable,
                a.LiabilityRepaymentType, a.InterestRate, balance, hasTransactions));
        }
        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountDetailDto>> GetById(Guid id)
    {
        var a = await db.Accounts
            .Include(x => x.AccountType)
            .Include(x => x.Currency)
            .Owned(user)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        var openingBalance     = await accountService.GetOpeningBalanceAsync(id);
        var openingBalanceDate = await accountService.GetOpeningBalanceDateAsync(id);

        return new AccountDetailDto(
            a.Id, a.Name, a.AccountTypeId, a.AccountType.Name,
            a.CurrencyId, a.Currency.Code, a.Currency.Symbol,
            a.Description, a.IsActive, a.ExcludeFromSpendable,
            a.LiabilityRepaymentType, a.InterestRate,
            openingBalance, openingBalanceDate);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request)
    {
        var result = await accountService.TryCreateAsync(request);
        if (!result.IsSuccess) return ToErrorResponse(result.Error!.Value);
        var a = result.Value!;
        var dto = new AccountListItemDto(
            a.Id, a.Name, a.AccountTypeId, a.AccountType.Name,
            a.CurrencyId, a.Currency.Code, a.Currency.Symbol,
            a.Description, a.IsActive, a.ExcludeFromSpendable,
            a.LiabilityRepaymentType, a.InterestRate, request.OpeningBalance,
            request.OpeningBalance != 0);
        return Created($"/api/accounts/{a.Id}", dto);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAccountRequest request)
    {
        var result = await accountService.TryUpdateAsync(id, request);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var result = await accountService.TryDeactivateAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPatch("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var result = await accountService.TryReactivateAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpGet("{id:guid}/ledger")]
    public async Task<ActionResult<AccountLedgerDto>> GetLedger(Guid id)
    {
        var owned = await db.Accounts.Owned(user).AnyAsync(a => a.Id == id);
        if (!owned) return NotFound();

        var ledger = await accountService.GetLedgerAsync(id);
        if (ledger is null) return NotFound();

        var entries = ledger.Entries
            .Select(e => new LedgerEntryDto(
                e.Date, e.CreatedAt, e.Description, e.EntryType,
                e.CategoryName, e.SignedAmount, e.RunningBalance))
            .ToList();
        return new AccountLedgerDto(ledger.AccountId, ledger.AccountName, ledger.CurrencySymbol, entries);
    }

    private IActionResult ToErrorResponse(ResultError error) => error.Code switch
    {
        "NOT_FOUND" => NotFound(),
        _           => UnprocessableEntity(new { error = new { code = error.Code, message = error.Message, details = Array.Empty<object>() } })
    };
}

[ApiController]
[Route("api/account-types")]
public class AccountTypesApiController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AccountTypeDto>> Get() =>
        await db.AccountTypes
            .OrderBy(t => t.Id)
            .Select(t => new AccountTypeDto(t.Id, t.Name))
            .ToListAsync();
}
