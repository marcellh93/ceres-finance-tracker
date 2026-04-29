namespace ProjectCeres.ViewModels;

public record NetWorthTrendPoint(string Month, decimal Assets, decimal Liabilities, decimal NetWorth);
public record NetWorthTrendDto(string CurrencyCode, string CurrencySymbol, IReadOnlyList<NetWorthTrendPoint> Points);

public record IncomeExpensePoint(string Month, decimal Income, decimal Expenses);
public record IncomeExpenseDto(string CurrencyCode, string CurrencySymbol, IReadOnlyList<IncomeExpensePoint> Points);

public record SpendingByCategorySlice(string CategoryName, decimal Amount);
public record SpendingByCategoryDto(string CurrencyCode, string CurrencySymbol, decimal Total, IReadOnlyList<SpendingByCategorySlice> Slices);

public record AccountBalanceRow(string AccountName, decimal Balance);
public record AccountBalancesDto(string CurrencyCode, string CurrencySymbol, IReadOnlyList<AccountBalanceRow> Rows);

public record CashFlowPoint(string Month, decimal NetFlow);
public record CashFlowDto(string CurrencyCode, string CurrencySymbol, IReadOnlyList<CashFlowPoint> Points);
