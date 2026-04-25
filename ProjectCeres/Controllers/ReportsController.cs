using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
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
            lines.Add($"{Csv(row.CategoryName)},{row.LimitAmount:F2},{row.ActualSpend:F2},{row.Variance:F2},{pct:P1}");
        }

        return CsvFile(lines, $"budget-vs-actual_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
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
            lines.Add($"{row.Date:yyyy-MM-dd},{Csv(row.Description)},{Csv(row.CategoryName)},{Csv(row.AccountName)},{row.Amount:F2}");

        return CsvFile(lines, $"largest-expenses_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
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
            lines.Add($"{new DateTime(row.Year, row.Month, 1):MMM yyyy},{row.TotalIncome:F2},{row.TotalExpenses:F2},{row.Net:F2}");

        return CsvFile(lines, $"monthly-cash-flow_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
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
            lines.Add($"{new DateTime(row.Year, row.Month, 1):MMM yyyy},{row.Assets:F2},{row.Liabilities:F2},{row.NetWorth:F2}");

        return CsvFile(lines, $"net-worth-over-time_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv");
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '@' or '+' or '-') value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private FileContentResult CsvFile(List<string> lines, string fileName)
    {
        var content = string.Join("\n", lines);
        return File(System.Text.Encoding.UTF8.GetBytes(content), "text/csv", fileName);
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
