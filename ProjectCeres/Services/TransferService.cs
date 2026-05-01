using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class TransferService(AppDbContext db, IAccountService accountService) : ITransferService
{
    public async Task<IEnumerable<Transfer>> GetAllAsync() =>
        await db.Transfers
            .Include(t => t.SourceAccount).ThenInclude(a => a.Currency)
            .Include(t => t.DestAccount).ThenInclude(a => a.Currency)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

    public async Task<Transfer?> GetByIdAsync(Guid id) =>
        await db.Transfers
            .Include(t => t.SourceAccount).ThenInclude(a => a.Currency)
            .Include(t => t.DestAccount).ThenInclude(a => a.Currency)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<Transfer> CreateAsync(TransferCreateViewModel vm)
    {
        await ValidateSameCurrencyAsync(vm.SourceAccountId!.Value, vm.DestAccountId!.Value);
        await ValidateNotBeforeOpeningBalanceAsync(vm.SourceAccountId!.Value, vm.DestAccountId!.Value, vm.Date);

        var transfer = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = vm.Date,
            Amount          = vm.Amount,
            SourceAccountId = vm.SourceAccountId!.Value,
            DestAccountId   = vm.DestAccountId!.Value,
            Description     = vm.Description,
            CreatedAt       = DateTime.UtcNow
        };

        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();
        return transfer;
    }

    public async Task UpdateAsync(TransferEditViewModel vm)
    {
        var transfer = await db.Transfers.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Transfer {vm.Id} not found.");

        await ValidateSameCurrencyAsync(vm.SourceAccountId!.Value, vm.DestAccountId!.Value);
        await ValidateNotBeforeOpeningBalanceAsync(vm.SourceAccountId!.Value, vm.DestAccountId!.Value, vm.Date);

        transfer.Date            = vm.Date;
        transfer.Amount          = vm.Amount;
        transfer.SourceAccountId = vm.SourceAccountId!.Value;
        transfer.DestAccountId   = vm.DestAccountId!.Value;
        transfer.Description     = vm.Description;
        transfer.IsCleared       = vm.IsCleared;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var transfer = await db.Transfers.FindAsync(id)
            ?? throw new InvalidOperationException($"Transfer {id} not found.");

        db.Transfers.Remove(transfer);
        await db.SaveChangesAsync();
    }

    public async Task MarkClearedAsync(Guid id, bool cleared)
    {
        var transfer = await db.Transfers.FindAsync(id)
            ?? throw new InvalidOperationException($"Transfer {id} not found.");
        transfer.IsCleared = cleared;
        await db.SaveChangesAsync();
    }

    public async Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null, string? currency = null)
    {
        var query = db.Transfers.Where(t => !t.IsCleared && t.Date >= from && t.Date <= to);
        if (accountId.HasValue)
            query = query.Where(t => t.SourceAccountId == accountId.Value || t.DestAccountId == accountId.Value);
        if (!string.IsNullOrWhiteSpace(currency))
            query = query.Where(t => t.SourceAccount.Currency.Code == currency);
        var rowsAffected = await query.ExecuteUpdateAsync(s => s.SetProperty(t => t.IsCleared, true));
        return rowsAffected;
    }

    private async Task ValidateNotBeforeOpeningBalanceAsync(Guid sourceId, Guid destId, DateOnly date)
    {
        foreach (var (accountId, label) in new[] { (sourceId, "source"), (destId, "destination") })
        {
            var openingDate = await accountService.GetOpeningBalanceDateAsync(accountId);
            if (openingDate.HasValue && date < openingDate.Value)
                throw new InvalidOperationException(
                    $"This transfer cannot be dated before the opening balance date of the {label} account ({openingDate.Value:dd/MM/yyyy}). " +
                    $"To allow earlier dates, edit the account and move the opening balance date to {date:dd/MM/yyyy} or earlier.");
        }
    }

    private async Task ValidateSameCurrencyAsync(Guid sourceId, Guid destId)
    {
        var accounts = await db.Accounts
            .Where(a => a.Id == sourceId || a.Id == destId)
            .Select(a => new { a.Id, a.CurrencyId })
            .ToListAsync();

        var source = accounts.FirstOrDefault(a => a.Id == sourceId)
            ?? throw new InvalidOperationException("Source account not found.");
        var dest = accounts.FirstOrDefault(a => a.Id == destId)
            ?? throw new InvalidOperationException("Destination account not found.");

        if (source.CurrencyId != dest.CurrencyId)
            throw new InvalidOperationException("Transfer source and destination accounts must share the same currency.");
    }
}
