using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;

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

        var grouped = accounts
            .GroupBy(a => new { a.Currency.Code, a.Currency.Symbol, a.CurrencyId })
            .Select(g =>
            {
                decimal assets      = 0;
                decimal liabilities = 0;

                foreach (var account in g)
                {
                    var accountTransfers = transfers.Where(t => t.SourceAccountId == account.Id || t.DestAccountId == account.Id);
                    var accountPayments  = liabilityPayments.Where(p => p.AssetAccountId == account.Id || p.LiabilityAccountId == account.Id);
                    var balance = AccountBalanceCalculator.ComputeBalance(account, account.Transactions, accountTransfers, accountPayments);

                    if (account.AccountType.Name != "Liability")
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
