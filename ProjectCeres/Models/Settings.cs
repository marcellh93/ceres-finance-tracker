using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class Settings : IUserOwned
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string NumberFormat { get; set; } = string.Empty;
    public string DateFormat { get; set; } = string.Empty;
    public int DefaultCurrencyId { get; set; }
    /// <summary>
    /// Day of month (1–31) when monthly cycles start. Drives every monthly view in
    /// the app (Cycle to Date, Spending by Category, Income vs. Avg, Budget periods).
    /// Default 1 = calendar months. For months shorter than the configured day
    /// (e.g., 31 in April), the cycle starts on that month's last day. See BudgetPeriod helper.
    /// </summary>
    public int PeriodStartDay { get; set; } = 1;
    /// <summary>
    /// User's preferred UI language. "en" | "es". Default "en". Drives the culture
    /// used by IStringLocalizer for server-rendered emails and reports. Wired by
    /// Stage 8b; the SPA language toggle PATCHes this column in Stage 9.
    /// </summary>
    public string Language { get; set; } = "en";

    public Currency DefaultCurrency { get; set; } = null!;
}
