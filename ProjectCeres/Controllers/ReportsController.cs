using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

[Authorize]
public class ReportsController : Controller
{
    [HttpGet] public IActionResult Index()             => Redirect("/app/reports");
    [HttpGet] public IActionResult NetWorth()          => Redirect("/app/reports/net-worth");
    [HttpGet] public IActionResult IncomeExpense(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/income-expense", currencyId, from, to));
    [HttpGet] public IActionResult ExpenseBreakdown(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/expense-breakdown", currencyId, from, to));
    [HttpGet] public IActionResult TransactionHistory(int? currencyId, DateOnly? from, DateOnly? to, Guid? accountId, Guid? categoryId, int page = 1)
        => Redirect(BuildRedirect("/app/reports/transaction-history", currencyId, from, to, accountId, categoryId, page));
    [HttpGet] public IActionResult BudgetVsActual(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/budget-vs-actual", currencyId, from, to));
    [HttpGet] public IActionResult LargestExpenses(int? currencyId, DateOnly? from, DateOnly? to, int limit = 25)
        => Redirect(BuildRedirect("/app/reports/largest-expenses", currencyId, from, to, limit: limit));
    [HttpGet] public IActionResult MonthlyCashFlow(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/monthly-cash-flow", currencyId, from, to));
    [HttpGet] public IActionResult NetWorthOverTime(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/net-worth-over-time", currencyId, from, to));

    [HttpGet] public IActionResult ExportNetWorth()          => Redirect("/app/reports/net-worth");
    [HttpGet] public IActionResult ExportIncomeExpense(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/income-expense", currencyId, from, to));
    [HttpGet] public IActionResult ExportExpenseBreakdown(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/expense-breakdown", currencyId, from, to));
    [HttpGet] public IActionResult ExportTransactionHistory(int? currencyId, DateOnly? from, DateOnly? to, Guid? accountId, Guid? categoryId)
        => Redirect(BuildRedirect("/app/reports/transaction-history", currencyId, from, to, accountId, categoryId));
    [HttpGet] public IActionResult ExportBudgetVsActual(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/budget-vs-actual", currencyId, from, to));
    [HttpGet] public IActionResult ExportLargestExpenses(int? currencyId, DateOnly? from, DateOnly? to, int limit = 25)
        => Redirect(BuildRedirect("/app/reports/largest-expenses", currencyId, from, to, limit: limit));
    [HttpGet] public IActionResult ExportMonthlyCashFlow(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/monthly-cash-flow", currencyId, from, to));
    [HttpGet] public IActionResult ExportNetWorthOverTime(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/net-worth-over-time", currencyId, from, to));

    private static string BuildRedirect(string basePath, int? currencyId = null, DateOnly? from = null, DateOnly? to = null, Guid? accountId = null, Guid? categoryId = null, int? page = null, int? limit = null)
    {
        var qs = new System.Text.StringBuilder();
        void Append(string key, string value)
        {
            qs.Append(qs.Length == 0 ? '?' : '&');
            qs.Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }
        if (currencyId.HasValue) Append("currencyId", currencyId.Value.ToString());
        if (from.HasValue)       Append("from", from.Value.ToString("yyyy-MM-dd"));
        if (to.HasValue)         Append("to",   to.Value.ToString("yyyy-MM-dd"));
        if (accountId.HasValue)  Append("accountId", accountId.Value.ToString());
        if (categoryId.HasValue) Append("categoryId", categoryId.Value.ToString());
        if (page is > 1)         Append("page", page.Value.ToString());
        if (limit.HasValue)      Append("limit", limit.Value.ToString());
        return basePath + qs;
    }
}
