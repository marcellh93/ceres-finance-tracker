using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Shared account-balance arithmetic. Pure functions over pre-loaded data —
/// no DbContext, no account-selection logic. Callers query and filter
/// however their own purpose requires (single account, all active accounts,
/// Asset-only, per-currency-grouped, etc.) and pass the resulting
/// collections in. See docs/superpowers/specs/2026-10-01-stage-16-5-accountbalancecalculator-design.md.
/// </summary>
public static class AccountBalanceCalculator
{
    /// <summary>
    /// The signed contribution of one transaction to its account's balance.
    /// System categories (e.g. Opening Balance) are a neutral starting point
    /// and always add. For regular transactions: on an asset account, income
    /// adds and expense subtracts; on a liability account, expense adds
    /// (increases what you owe) and income subtracts (e.g. a refund).
    /// </summary>
    public static decimal SignedAmount(Transaction transaction, bool isLiability)
    {
        if (transaction.Category.IsSystem) return transaction.Amount;
        bool isIncome = transaction.Category.CategoryType.Name == "Income";
        bool addsToBalance = isLiability ? !isIncome : isIncome;
        return addsToBalance ? transaction.Amount : -transaction.Amount;
    }

    /// <summary>
    /// An account's full balance: summed Transactions (via <see cref="SignedAmount"/>),
    /// minus LiabilityPayments on either leg, plus/minus Transfers in either direction.
    /// <paramref name="transfers"/> and <paramref name="liabilityPayments"/> must contain
    /// every row touching <paramref name="account"/> in EITHER direction — this method
    /// splits them internally by SourceAccountId/DestAccountId and
    /// AssetAccountId/LiabilityAccountId.
    /// </summary>
    public static decimal ComputeBalance(
        Account account,
        IEnumerable<Transaction> transactions,
        IEnumerable<Transfer> transfers,
        IEnumerable<LiabilityPayment> liabilityPayments)
    {
        bool isLiability = account.AccountType.Name == "Liability";

        decimal balance = transactions.Sum(t => SignedAmount(t, isLiability));

        foreach (var payment in liabilityPayments)
        {
            if (payment.AssetAccountId == account.Id) balance -= payment.Amount;
            if (payment.LiabilityAccountId == account.Id) balance -= payment.Amount;
        }

        foreach (var transfer in transfers)
        {
            if (transfer.DestAccountId == account.Id) balance += transfer.Amount;
            if (transfer.SourceAccountId == account.Id) balance -= transfer.Amount;
        }

        return balance;
    }
}
