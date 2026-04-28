using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Helpers;
using ProjectCeres.Services;
using ProjectCeres.Services.Reports;

namespace ProjectCeres.Controllers;

public class ReportsController(
    IReportService reportService,
    ISettingsService settingsService,
    AppDbContext db,
    BudgetVsActualReportGenerator budgetVsActualGenerator,
    LargestExpensesReportGenerator largestExpensesGenerator,
    MonthlyCashFlowReportGenerator monthlyCashFlowGenerator,
    NetWorthOverTimeReportGenerator netWorthOverTimeGenerator) : Controller
{
    public IActionResult Index() => View();

    public async Task<IActionResult> NetWorth()
    {
        var data = await reportService.GetNetWorthAsync();
        return View(data);
    }

    public async Task<IActionResult> IncomeExpense(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        var data = await reportService.GetIncomeExpenseSummaryAsync(cid, start, end);

        await PopulateViewBagAsync(cid, start, end);
        return View(data);
    }

    public async Task<IActionResult> ExpenseBreakdown(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        var data = await reportService.GetExpenseBreakdownAsync(cid, start, end);

        await PopulateViewBagAsync(cid, start, end);
        return View(data);
    }

    public async Task<IActionResult> TransactionHistory(
        int? currencyId, DateOnly? from, DateOnly? to,
        Guid? accountId, Guid? categoryId, int page = 1)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, 1, 1);
        const int pageSize = 50;

        var data = await reportService.GetTransactionHistoryAsync(
            cid, start, end, accountId, categoryId, pageSize, (page - 1) * pageSize);

        await PopulateViewBagAsync(cid, start, end);
        ViewBag.Page       = page;
        ViewBag.AccountId  = accountId;
        ViewBag.CategoryId = categoryId;
        ViewBag.Accounts   = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");
        ViewBag.Categories = new SelectList(
            await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");

        return View(data);
    }

    public async Task<IActionResult> BudgetVsActual(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        var data = (List<BudgetVsActualRow>)await budgetVsActualGenerator.GenerateAsync(
            new ReportParameters(CurrencyId: cid, From: start, To: end));

        await PopulateViewBagAsync(cid, start, end);
        return View(data);
    }

    public async Task<IActionResult> LargestExpenses(int? currencyId, DateOnly? from, DateOnly? to, int limit = 25)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, 1, 1);

        var data = (List<LargestExpenseRow>)await largestExpensesGenerator.GenerateAsync(
            new ReportParameters(CurrencyId: cid, From: start, To: end, Limit: limit));

        await PopulateViewBagAsync(cid, start, end);
        ViewBag.Limit = limit;
        return View(data);
    }

    public async Task<IActionResult> MonthlyCashFlow(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(-6));

        var data = (List<MonthlyCashFlowRow>)await monthlyCashFlowGenerator.GenerateAsync(
            new ReportParameters(CurrencyId: cid, From: start, To: end));

        await PopulateViewBagAsync(cid, start, end);
        return View(data);
    }

    public async Task<IActionResult> NetWorthOverTime(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(-12));

        var data = (List<NetWorthSnapshotRow>)await netWorthOverTimeGenerator.GenerateAsync(
            new ReportParameters(CurrencyId: cid, From: start, To: end));

        await PopulateViewBagAsync(cid, start, end);
        return View(data);
    }

    public async Task<IActionResult> ExportNetWorth()
    {
        var data = await reportService.GetNetWorthAsync();

        var lines = new List<string> { "Currency,Assets,Liabilities,Net Worth" };
        foreach (var entry in data)
            lines.Add($"{CsvFormattingHelper.Csv(entry.CurrencyCode)},{entry.Assets.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{entry.Liabilities.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{entry.NetWorth.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", $"net-worth_{DateTime.Today:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> ExportIncomeExpense(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        var data = await reportService.GetIncomeExpenseSummaryAsync(cid, start, end);

        var lines = new List<string> { "Metric,Value" };
        lines.Add($"Total Income,{data.TotalIncome.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
        lines.Add($"Total Expenses,{data.TotalExpenses.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
        lines.Add($"Net,{(data.TotalIncome - data.TotalExpenses).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
        lines.Add($"Savings Rate,{data.SavingsRate.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)}");

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", $"income-expense_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> ExportExpenseBreakdown(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        var data = await reportService.GetExpenseBreakdownAsync(cid, start, end);
        var total = data.Categories.Sum(c => c.Total);

        var lines = new List<string> { "Category,Lifestyle Tag,Amount,% of Total" };
        foreach (var cat in data.Categories)
        {
            var pct = total > 0 ? cat.Total / total : 0;
            lines.Add($"{CsvFormattingHelper.Csv(cat.CategoryName)},{CsvFormattingHelper.Csv(cat.LifestyleTag ?? "")},{cat.Total.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{pct.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", $"expense-breakdown_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> ExportTransactionHistory(int? currencyId, DateOnly? from, DateOnly? to, Guid? accountId, Guid? categoryId)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, 1, 1);

        var data = await reportService.GetTransactionHistoryAsync(cid, start, end, accountId, categoryId, limit: 10_000, offset: 0);

        var lines = new List<string> { "Date,Account,Category,Description,Type,Amount" };
        foreach (var t in data)
            lines.Add($"{t.Date:yyyy-MM-dd},{CsvFormattingHelper.Csv(t.Account.Name)},{CsvFormattingHelper.Csv(t.Category.Name)},{CsvFormattingHelper.Csv(t.Description ?? "")},{CsvFormattingHelper.Csv(t.Category.CategoryType.Name)},{t.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", $"transaction-history_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> ExportBudgetVsActual(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        var data = (List<BudgetVsActualRow>)await budgetVsActualGenerator.GenerateAsync(
            new ReportParameters(CurrencyId: cid, From: start, To: end));

        var lines = new List<string> { "Category,Budget Limit,Actual Spend,Variance,% Used" };
        foreach (var row in data)
        {
            var pct = row.LimitAmount > 0 ? row.ActualSpend / row.LimitAmount : 0;
            lines.Add($"{CsvFormattingHelper.Csv(row.CategoryName)},{row.LimitAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{row.ActualSpend.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{row.Variance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{pct.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", $"budget-vs-actual_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> ExportLargestExpenses(int? currencyId, DateOnly? from, DateOnly? to, int limit = 25)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, 1, 1);

        var data = (List<LargestExpenseRow>)await largestExpensesGenerator.GenerateAsync(
            new ReportParameters(CurrencyId: cid, From: start, To: end, Limit: limit));

        var lines = new List<string> { "Date,Description,Category,Account,Amount" };
        foreach (var row in data)
            lines.Add($"{row.Date:yyyy-MM-dd},{CsvFormattingHelper.Csv(row.Description)},{CsvFormattingHelper.Csv(row.CategoryName)},{CsvFormattingHelper.Csv(row.AccountName)},{row.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", $"largest-expenses_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> ExportMonthlyCashFlow(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(-6));

        var data = (List<MonthlyCashFlowRow>)await monthlyCashFlowGenerator.GenerateAsync(
            new ReportParameters(CurrencyId: cid, From: start, To: end));

        var lines = new List<string> { "Month,Income,Expenses,Net" };
        foreach (var row in data)
            lines.Add($"{new DateTime(row.Year, row.Month, 1):MMM yyyy},{row.TotalIncome.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{row.TotalExpenses.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{row.Net.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", $"monthly-cash-flow_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> ExportNetWorthOverTime(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid   = currencyId ?? settings.DefaultCurrencyId;
        var end   = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(-12));

        var data = (List<NetWorthSnapshotRow>)await netWorthOverTimeGenerator.GenerateAsync(
            new ReportParameters(CurrencyId: cid, From: start, To: end));

        var lines = new List<string> { "Month,Assets,Liabilities,Net Worth" };
        foreach (var row in data)
            lines.Add($"{new DateTime(row.Year, row.Month, 1):MMM yyyy},{row.Assets.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{row.Liabilities.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{row.NetWorth.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");

        return File(CsvFormattingHelper.CsvBytes(lines), "text/csv; charset=utf-8", $"net-worth-over-time_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
    }

    private async Task PopulateViewBagAsync(int selectedCurrencyId, DateOnly from, DateOnly to)
    {
        ViewBag.Currencies         = new SelectList(
            await db.Currencies.OrderBy(c => c.Code).ToListAsync(), "Id", "Code", selectedCurrencyId);
        ViewBag.SelectedCurrencyId = selectedCurrencyId;
        ViewBag.From               = from;
        ViewBag.To                 = to;
    }
}
