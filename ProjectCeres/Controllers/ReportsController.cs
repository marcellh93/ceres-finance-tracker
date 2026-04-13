using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers;

public class ReportsController(IReportService reportService, ISettingsService settingsService, AppDbContext db) : Controller
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
        var cid  = currencyId ?? settings.DefaultCurrencyId;
        var end  = to   ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        var data = await reportService.GetIncomeExpenseSummaryAsync(cid, start, end);

        await PopulateViewBagAsync(cid, start, end);
        return View(data);
    }

    public async Task<IActionResult> ExpenseBreakdown(int? currencyId, DateOnly? from, DateOnly? to)
    {
        var settings = await settingsService.GetAsync();
        var cid  = currencyId ?? settings.DefaultCurrencyId;
        var end  = to   ?? DateOnly.FromDateTime(DateTime.Today);
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
        ViewBag.Page      = page;
        ViewBag.AccountId  = accountId;
        ViewBag.CategoryId = categoryId;
        ViewBag.Accounts   = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");
        ViewBag.Categories = new SelectList(
            await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");

        return View(data);
    }

    private async Task PopulateViewBagAsync(int selectedCurrencyId, DateOnly from, DateOnly to)
    {
        ViewBag.Currencies       = new SelectList(
            await db.Currencies.OrderBy(c => c.Code).ToListAsync(), "Id", "Code", selectedCurrencyId);
        ViewBag.SelectedCurrencyId = selectedCurrencyId;
        ViewBag.From             = from;
        ViewBag.To               = to;
    }
}
