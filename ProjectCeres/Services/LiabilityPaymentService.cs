using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class LiabilityPaymentService(AppDbContext db, IAccountService accountService) : ILiabilityPaymentService
{
    public async Task<LiabilityPayment?> GetByIdAsync(Guid id) =>
        await db.LiabilityPayments
            .Include(p => p.AssetAccount).ThenInclude(a => a.Currency)
            .Include(p => p.LiabilityAccount).ThenInclude(a => a.Currency)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<LiabilityPayment> CreateAsync(TransactionCreateViewModel vm)
    {
        await ValidateAsync(vm.AccountId!.Value, vm.LiabilityAccountId!.Value, vm.Date);

        var payment = new LiabilityPayment
        {
            Id                 = Guid.NewGuid(),
            Date               = vm.Date,
            Amount             = vm.Amount,
            AssetAccountId     = vm.AccountId!.Value,
            LiabilityAccountId = vm.LiabilityAccountId!.Value,
            Description        = vm.Description,
            CreatedAt          = DateTime.UtcNow
        };

        db.LiabilityPayments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    public async Task UpdateAsync(TransactionEditViewModel vm)
    {
        var payment = await db.LiabilityPayments.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Liability payment {vm.Id} not found.");

        await ValidateAsync(vm.AccountId!.Value, vm.LiabilityAccountId!.Value, vm.Date);

        payment.Date               = vm.Date;
        payment.Amount             = vm.Amount;
        payment.AssetAccountId     = vm.AccountId!.Value;
        payment.LiabilityAccountId = vm.LiabilityAccountId!.Value;
        payment.Description        = vm.Description;
        payment.IsCleared          = vm.IsCleared;
        // LiabilityPayment has no NeedsReview column (unlike Transaction); this is intentional.
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var payment = await db.LiabilityPayments.FindAsync(id)
            ?? throw new InvalidOperationException($"Liability payment {id} not found.");

        db.LiabilityPayments.Remove(payment);
        await db.SaveChangesAsync();
    }

    public async Task MarkClearedAsync(Guid id, bool cleared)
    {
        var payment = await db.LiabilityPayments.FindAsync(id)
            ?? throw new InvalidOperationException($"Liability payment {id} not found.");

        payment.IsCleared = cleared;
        await db.SaveChangesAsync();
    }

    public async Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null, string? currency = null)
    {
        var query = db.LiabilityPayments.Where(p => !p.IsCleared && p.Date >= from && p.Date <= to);
        if (accountId.HasValue)
            query = query.Where(p => p.AssetAccountId == accountId.Value || p.LiabilityAccountId == accountId.Value);
        if (!string.IsNullOrWhiteSpace(currency))
            query = query.Where(p => p.AssetAccount.Currency.Code == currency);
        var rowsAffected = await query.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCleared, true));
        return rowsAffected;
    }

    private async Task ValidateAsync(Guid assetAccountId, Guid liabilityAccountId, DateOnly date)
    {
        var accounts = await db.Accounts
            .Where(a => a.Id == assetAccountId || a.Id == liabilityAccountId)
            .Include(a => a.AccountType)
            .ToListAsync();

        var asset = accounts.FirstOrDefault(a => a.Id == assetAccountId)
            ?? throw new InvalidOperationException("Paying account not found.");
        var liability = accounts.FirstOrDefault(a => a.Id == liabilityAccountId)
            ?? throw new InvalidOperationException("Liability account not found.");

        if (asset.AccountType.Name != "Asset")
            throw new InvalidOperationException("The paying account must be an Asset account (e.g. Checking, Savings).");

        if (liability.AccountType.Name != "Liability")
            throw new InvalidOperationException("The account being paid must be a Liability account (e.g. Credit Card, Loan).");

        if (asset.CurrencyId != liability.CurrencyId)
            throw new InvalidOperationException("Both accounts must share the same currency.");

        foreach (var (accountId, label) in new[] { (assetAccountId, "paying"), (liabilityAccountId, "liability") })
        {
            var openingDate = await accountService.GetOpeningBalanceDateAsync(accountId);
            if (openingDate.HasValue && date < openingDate.Value)
                throw new InvalidOperationException(
                    $"This payment cannot be dated before the opening balance date of the {label} account ({openingDate.Value:dd/MM/yyyy}).");
        }
    }
}
