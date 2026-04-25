using Microsoft.EntityFrameworkCore;
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

public class NetWorthOverTimeReportGenerator(AppDbContext db) : IReportGenerator
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
            .Where(a => a.CurrencyId == currencyId && a.IsActive)
            .Include(a => a.AccountType)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
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
