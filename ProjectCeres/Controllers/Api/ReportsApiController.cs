using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Helpers;
using ProjectCeres.Services;
using ProjectCeres.Services.Reports;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/reports")]
public class ReportsApiController(
    IReportService reportService,
    ISettingsService settingsService,
    ReportGeneratorFactory generatorFactory) : ControllerBase
{
    private async Task<(int CurrencyId, DateOnly From, DateOnly To)> ResolveRangeAsync(
        int? currencyId, DateOnly? from, DateOnly? to, RangeDefault rangeDefault)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? rangeDefault switch
        {
            RangeDefault.MonthStart      => new DateOnly(end.Year, end.Month, 1),
            RangeDefault.YearStart       => new DateOnly(end.Year, 1, 1),
            RangeDefault.SixMonthsAgo    => DateOnly.FromDateTime(DateTime.Today.AddMonths(-6)),
            RangeDefault.TwelveMonthsAgo => DateOnly.FromDateTime(DateTime.Today.AddMonths(-12)),
            _                            => new DateOnly(end.Year, end.Month, 1),
        };
        return (cid, start, end);
    }

    private enum RangeDefault { MonthStart, YearStart, SixMonthsAgo, TwelveMonthsAgo }

    [HttpGet("net-worth")]
    public async Task<IActionResult> NetWorth([FromQuery] string? format = null)
    {
        var data = await reportService.GetNetWorthAsync();
        if (IsCsv(format))
        {
            var lines = new List<string> { "Currency,Assets,Liabilities,Net Worth" };
            foreach (var entry in data)
                lines.Add($"{CsvFormattingHelper.Csv(entry.CurrencyCode)},{F(entry.Assets)},{F(entry.Liabilities)},{F(entry.NetWorth)}");
            return Csv(lines, $"net-worth_{DateTime.Today:yyyy-MM-dd}.csv");
        }
        return Ok(data);
    }

    [HttpGet("income-expense")]
    public async Task<IActionResult> IncomeExpense(
        [FromQuery] int? currencyId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? format = null)
    {
        var (cid, start, end) = await ResolveRangeAsync(currencyId, from, to, RangeDefault.MonthStart);
        var data = await reportService.GetIncomeExpenseSummaryAsync(cid, start, end);
        if (IsCsv(format))
        {
            var lines = new List<string>
            {
                "Metric,Value",
                $"Total Income,{F(data.TotalIncome)}",
                $"Total Expenses,{F(data.TotalExpenses)}",
                $"Net,{F(data.TotalIncome - data.TotalExpenses)}",
                $"Savings Rate,{data.SavingsRate.ToString("P1", CultureInfo.InvariantCulture)}",
            };
            return Csv(lines, $"income-expense_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
        }
        return Ok(data);
    }

    [HttpGet("expense-breakdown")]
    public async Task<IActionResult> ExpenseBreakdown(
        [FromQuery] int? currencyId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? format = null)
    {
        var (cid, start, end) = await ResolveRangeAsync(currencyId, from, to, RangeDefault.MonthStart);
        var data = await reportService.GetExpenseBreakdownAsync(cid, start, end);
        if (IsCsv(format))
        {
            var total = data.Categories.Sum(c => c.Total);
            var lines = new List<string> { "Category,Lifestyle Tag,Amount,% of Total" };
            foreach (var cat in data.Categories)
            {
                var pct = total > 0 ? cat.Total / total : 0;
                lines.Add($"{CsvFormattingHelper.Csv(cat.CategoryName)},{CsvFormattingHelper.Csv(cat.LifestyleTag ?? "")},{F(cat.Total)},{pct.ToString("P1", CultureInfo.InvariantCulture)}");
            }
            return Csv(lines, $"expense-breakdown_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
        }
        return Ok(data);
    }

    [HttpGet("transaction-history")]
    public async Task<IActionResult> TransactionHistory(
        [FromQuery] int? currencyId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? accountId, [FromQuery] Guid? categoryId,
        [FromQuery] int page = 1, [FromQuery] string? format = null)
    {
        var (cid, start, end) = await ResolveRangeAsync(currencyId, from, to, RangeDefault.YearStart);
        if (page < 1) page = 1;
        const int pageSize = 50;

        if (IsCsv(format))
        {
            var allRows = await reportService.GetTransactionHistoryAsync(cid, start, end, accountId, categoryId, limit: 10_000, offset: 0);
            var lines = new List<string> { "Date,Account,Category,Description,Type,Amount" };
            foreach (var t in allRows)
                lines.Add($"{t.Date:yyyy-MM-dd},{CsvFormattingHelper.Csv(t.Account.Name)},{CsvFormattingHelper.Csv(t.Category.Name)},{CsvFormattingHelper.Csv(t.Description ?? "")},{CsvFormattingHelper.Csv(t.Category.CategoryType.Name)},{F(t.Amount)}");
            return Csv(lines, $"transaction-history_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
        }

        var rows = await reportService.GetTransactionHistoryAsync(cid, start, end, accountId, categoryId, pageSize, (page - 1) * pageSize);
        var dtos = rows.Select(t => new TransactionHistoryRowDto(
            t.Id, t.Date, t.Account.Name, t.Category.Name,
            t.Category.CategoryType.Name, t.Description, t.Amount, t.Account.Currency?.Symbol ?? ""));
        return Ok(dtos);
    }

    [HttpGet("budget-vs-actual")]
    public async Task<IActionResult> BudgetVsActual(
        [FromQuery] int? currencyId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? format = null)
    {
        var (cid, start, end) = await ResolveRangeAsync(currencyId, from, to, RangeDefault.MonthStart);
        var rows = (List<BudgetVsActualRow>)await generatorFactory.GetGenerator(ReportTypeKey.BudgetVsActual)
            .GenerateAsync(new ReportParameters(CurrencyId: cid, From: start, To: end));
        if (IsCsv(format))
        {
            var lines = new List<string> { "Category,Limit/Period,Total Limit,Actual Spend,Variance,% Used" };
            foreach (var r in rows)
            {
                var pct = r.TotalLimit > 0 ? r.ActualSpend / r.TotalLimit : 0;
                lines.Add($"{CsvFormattingHelper.Csv(r.CategoryName)},{F(r.LimitPerPeriod)},{F(r.TotalLimit)},{F(r.ActualSpend)},{F(r.Variance)},{pct.ToString("P1", CultureInfo.InvariantCulture)}");
            }
            return Csv(lines, $"budget-vs-actual_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
        }
        return Ok(rows);
    }

    [HttpGet("largest-expenses")]
    public async Task<IActionResult> LargestExpenses(
        [FromQuery] int? currencyId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int limit = 25, [FromQuery] string? format = null)
    {
        var (cid, start, end) = await ResolveRangeAsync(currencyId, from, to, RangeDefault.YearStart);
        if (limit < 1) limit = 1;
        if (limit > 1000) limit = 1000;

        var rows = (List<LargestExpenseRow>)await generatorFactory.GetGenerator(ReportTypeKey.LargestExpenses)
            .GenerateAsync(new ReportParameters(CurrencyId: cid, From: start, To: end, Limit: limit));
        if (IsCsv(format))
        {
            var lines = new List<string> { "Date,Description,Category,Account,Amount" };
            foreach (var r in rows)
                lines.Add($"{r.Date:yyyy-MM-dd},{CsvFormattingHelper.Csv(r.Description)},{CsvFormattingHelper.Csv(r.CategoryName)},{CsvFormattingHelper.Csv(r.AccountName)},{F(r.Amount)}");
            return Csv(lines, $"largest-expenses_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
        }
        return Ok(rows);
    }

    [HttpGet("monthly-cash-flow")]
    public async Task<IActionResult> MonthlyCashFlow(
        [FromQuery] int? currencyId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? format = null)
    {
        var (cid, start, end) = await ResolveRangeAsync(currencyId, from, to, RangeDefault.SixMonthsAgo);
        var rows = (List<MonthlyCashFlowRow>)await generatorFactory.GetGenerator(ReportTypeKey.MonthlyCashFlow)
            .GenerateAsync(new ReportParameters(CurrencyId: cid, From: start, To: end));
        if (IsCsv(format))
        {
            var lines = new List<string> { "Month,Income,Expenses,Net" };
            foreach (var r in rows)
                lines.Add($"{new DateTime(r.Year, r.Month, 1):MMM yyyy},{F(r.TotalIncome)},{F(r.TotalExpenses)},{F(r.Net)}");
            return Csv(lines, $"monthly-cash-flow_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
        }
        return Ok(rows);
    }

    [HttpGet("net-worth-over-time")]
    public async Task<IActionResult> NetWorthOverTime(
        [FromQuery] int? currencyId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? format = null)
    {
        var (cid, start, end) = await ResolveRangeAsync(currencyId, from, to, RangeDefault.TwelveMonthsAgo);
        var rows = (List<NetWorthSnapshotRow>)await generatorFactory.GetGenerator(ReportTypeKey.NetWorthOverTime)
            .GenerateAsync(new ReportParameters(CurrencyId: cid, From: start, To: end));
        if (IsCsv(format))
        {
            var lines = new List<string> { "Month,Assets,Liabilities,Net Worth" };
            foreach (var r in rows)
                lines.Add($"{new DateTime(r.Year, r.Month, 1):MMM yyyy},{F(r.Assets)},{F(r.Liabilities)},{F(r.NetWorth)}");
            return Csv(lines, $"net-worth-over-time_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
        }
        return Ok(rows);
    }

    private static bool IsCsv(string? format) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    private FileContentResult Csv(List<string> lines, string fileName) =>
        File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", fileName);

    private static string F(decimal value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);
}
