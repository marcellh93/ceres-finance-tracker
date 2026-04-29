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
/// Month-to-date income, expenses, and savings rate.
/// SavingsRate is a fraction (0–1), not a percentage. The client formats for display.
/// </summary>
public record MtdSummary(
    string CurrencyCode,
    string CurrencySymbol,
    decimal Income,
    decimal Expenses,
    decimal SavingsRate);
