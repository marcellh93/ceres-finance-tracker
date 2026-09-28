using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Services.Reports;

public class NetWorthGenerator(AppDbContext db, ICurrentUserAccessor user) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        // !ExcludeFromReports, not IsActive: an archived account still counts toward
        // net worth unless the user explicitly opted out at archive time (matches
        // ReportService.GetNetWorthAsync's own filter).
        var accounts = await db.Accounts
            .Owned(user)
            .Where(a => !a.ExcludeFromReports)
            .Include(a => a.Currency)
            .Include(a => a.AccountType)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var liabilityPayments = await db.LiabilityPayments
            .Owned(user)
            .AsNoTracking()
            .Where(p => accountIds.Contains(p.AssetAccountId) || accountIds.Contains(p.LiabilityAccountId))
            .ToListAsync();

        var paymentsByAsset = liabilityPayments
            .GroupBy(p => p.AssetAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var paymentsByLiability = liabilityPayments
            .GroupBy(p => p.LiabilityAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        // A Transfer moves money between two of the user's own accounts and never
        // touches Transactions — an account funded entirely by a transfer (e.g. an
        // opening-balance transfer into a new liability) has no Transactions to sum
        // above and would otherwise contribute nothing here, matching
        // AccountService.GetBalanceAsync's own transfersIn/transfersOut terms.
        var transfers = await db.Transfers
            .Owned(user)
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.SourceAccountId) || accountIds.Contains(t.DestAccountId))
            .ToListAsync();

        var transfersOutByAccount = transfers
            .GroupBy(t => t.SourceAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var transfersInByAccount = transfers
            .GroupBy(t => t.DestAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var grouped = accounts
            .GroupBy(a => new { a.Currency.Code, a.Currency.Symbol, a.CurrencyId })
            .Select(g =>
            {
                decimal assets      = 0;
                decimal liabilities = 0;

                foreach (var account in g)
                {
                    bool isLiability = account.AccountType.Name == "Liability";

                    var balance = account.Transactions.Sum(t =>
                    {
                        if (t.Category.IsSystem) return t.Amount;
                        bool isIncome = t.Category.CategoryType.Name == "Income";
                        bool addsToBalance = isLiability ? !isIncome : isIncome;
                        return addsToBalance ? t.Amount : -t.Amount;
                    });

                    balance -= paymentsByAsset.GetValueOrDefault(account.Id);
                    balance -= paymentsByLiability.GetValueOrDefault(account.Id);

                    balance += transfersInByAccount.GetValueOrDefault(account.Id);
                    balance -= transfersOutByAccount.GetValueOrDefault(account.Id);

                    if (!isLiability)
                        assets += balance;
                    else
                        liabilities += balance;
                }

                return new NetWorthEntry(g.Key.Code, g.Key.Symbol, assets, liabilities, assets - liabilities);
            })
            .ToList();

        return grouped;
    }
}
