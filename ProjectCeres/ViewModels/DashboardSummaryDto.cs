using ProjectCeres.Services;

namespace ProjectCeres.ViewModels;

/// <summary>
/// Shape returned by GET /api/dashboard/summary. Bundles the three small KPIs
/// (net worth per currency, current-month income/expense/savings, pending reminders)
/// into a single response.
/// </summary>
public record DashboardSummaryDto(
    IReadOnlyList<NetWorthEntry> NetWorth,
    MtdSummary Mtd,
    int RemindersDueCount);

/// <summary>
/// Cycle-to-date income, expenses, and savings rate, plus the FULL prior-period
/// totals for the comparison line on the Cycle to Date card.
/// SavingsRate is a fraction (0–1), not a percentage — the client formats for display.
/// Prior values are null when no prior-period data exists yet (first period of usage).
/// </summary>
public record MtdSummary(
    string CurrencyCode,
    string CurrencySymbol,
    decimal Income,
    decimal Expenses,
    decimal SavingsRate,
    decimal? PriorPeriodIncome,
    decimal? PriorPeriodExpenses,
    decimal? PriorPeriodSavingsRate);
