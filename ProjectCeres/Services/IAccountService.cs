using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IAccountService
{
    Task<IEnumerable<Account>> GetAllAsync(bool includeInactive = false);
    Task<Account?> GetByIdAsync(Guid id);
    /// <summary>Creates account and auto-creates an Opening Balance transaction when openingBalance != 0.</summary>
    Task<Account> CreateAsync(AccountCreateViewModel vm);
    Task UpdateAsync(AccountEditViewModel vm);
    Task DeactivateAsync(Guid id);
    /// <summary>Derived balance: SUM of income transactions − SUM of expense transactions.</summary>
    Task<decimal> GetBalanceAsync(Guid id);
    /// <summary>Returns the current opening balance amount, or 0 if none has been set.</summary>
    Task<decimal> GetOpeningBalanceAsync(Guid id);
    /// <summary>Returns the opening balance transaction date, or null if no opening balance exists.</summary>
    Task<DateOnly?> GetOpeningBalanceDateAsync(Guid id);
    /// <summary>Returns all ledger entries for an account in chronological order with a running balance.</summary>
    Task<AccountLedgerViewModel?> GetLedgerAsync(Guid id);

    // API surface (Result-returning).
    Task<Result<Account>> TryCreateAsync(CreateAccountRequest request);
    Task<Result<Account>> TryUpdateAsync(Guid id, UpdateAccountRequest request);
    Task<Result> TryDeactivateAsync(Guid id);
}
