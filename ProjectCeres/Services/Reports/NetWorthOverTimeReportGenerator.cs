using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Services.Reports;

public record NetWorthSnapshotRow(
    int Year,
    int Month,
    string CurrencyCode,
    string CurrencySymbol,
    decimal Assets,
    decimal Liabilities,
    decimal NetWorth);

public class NetWorthOverTimeReportGenerator(AppDbContext db, ICurrentUserAccessor user) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var currencyId = parameters.CurrencyId ?? throw new ArgumentException("CurrencyId is required.");
        var from       = parameters.From ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(-12));
        var to         = parameters.To   ?? DateOnly.FromDateTime(DateTime.Today);

        var currency = await db.Currencies.FindAsync(currencyId)
            ?? throw new InvalidOperationException($"Currency {currencyId} not found.");

        // Load all transactions up to end of range for accounts in this currency.
        // We need all history (not just within range) to compute cumulative balances.
        var accounts = await db.Accounts
            .Owned(user)
            .Where(a => a.CurrencyId == currencyId && !a.ExcludeFromReports)
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
        // opening-balance transfer into a new liability) would otherwise contribute
        // nothing here, matching AccountService.GetBalanceAsync's own
        // transfersIn/transfersOut terms.
        var transfers = await db.Transfers
            .Owned(user)
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.SourceAccountId) || accountIds.Contains(t.DestAccountId))
            .ToListAsync();

        // Build list of months to snapshot.
        var months = new List<(int Year, int Month)>();
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var endMonth = new DateOnly(to.Year, to.Month, 1);
        while (cursor <= endMonth)
        {
            months.Add((cursor.Year, cursor.Month));
            cursor = cursor.AddMonths(1);
        }

        var rows = months.Select(m =>
        {
            var snapshotEnd = new DateOnly(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));

            decimal assets      = 0;
            decimal liabilities = 0;

            var paymentsUpToMonth = liabilityPayments.Where(p => p.Date <= snapshotEnd).ToList();
            var paymentsByAsset = paymentsUpToMonth
                .GroupBy(p => p.AssetAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
            var paymentsByLiability = paymentsUpToMonth
                .GroupBy(p => p.LiabilityAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            var transfersUpToMonth = transfers.Where(t => t.Date <= snapshotEnd).ToList();
            var transfersOutByAccount = transfersUpToMonth
                .GroupBy(t => t.SourceAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
            var transfersInByAccount = transfersUpToMonth
                .GroupBy(t => t.DestAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

            foreach (var account in accounts)
            {
                bool isLiability = account.AccountType.Name == "Liability";

                var balance = account.Transactions
                    .Where(t => t.Date <= snapshotEnd)
                    .Sum(t =>
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

            return new NetWorthSnapshotRow(m.Year, m.Month, currency.Code, currency.Symbol, assets, liabilities, assets - liabilities);
        }).ToList();

        return rows;
    }
}
