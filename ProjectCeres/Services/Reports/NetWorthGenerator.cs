using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Services.Reports;

public class NetWorthGenerator(AppDbContext db, ICurrentUserAccessor user) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var accounts = await db.Accounts
            .Owned(user)
            .Where(a => a.IsActive)
            .Include(a => a.Currency)
            .Include(a => a.AccountType)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
            .ToListAsync();

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
