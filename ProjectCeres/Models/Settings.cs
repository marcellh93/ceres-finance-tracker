namespace ProjectCeres.Models;

public class Settings
{
    public int Id { get; set; }
    public string NumberFormat { get; set; } = string.Empty;
    public string DateFormat { get; set; } = string.Empty;
    public int DefaultCurrencyId { get; set; }
    /// <summary>
    /// Day of month (1–31) when budget periods start. Default 1 = calendar months.
    /// For months shorter than the configured day (e.g., 31 in April), the period
    /// starts on that month's last day. See BudgetPeriod helper.
    /// </summary>
    public int BudgetPeriodStartDay { get; set; } = 1;

    public Currency DefaultCurrency { get; set; } = null!;
}
