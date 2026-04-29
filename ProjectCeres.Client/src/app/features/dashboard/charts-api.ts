// Typed response shapes for the Dashboard chart endpoints.
//
// Use with the useApi hook:
//
//   const { data, error, loading, refetch } = useApi<NetWorthTrendDto>(NET_WORTH_TREND_URL);

export const NET_WORTH_TREND_URL = '/api/dashboard/net-worth-trend';
export const INCOME_EXPENSE_URL = '/api/dashboard/income-expense';
export const SPENDING_BY_CATEGORY_URL = '/api/dashboard/spending-by-category';
export const ACCOUNT_BALANCES_URL = '/api/dashboard/account-balances';
export const CASH_FLOW_URL = '/api/dashboard/cash-flow';

export type NetWorthTrendPoint = {
  month: string;
  assets: number;
  liabilities: number;
  netWorth: number;
};
export type NetWorthTrendDto = {
  currencyCode: string;
  currencySymbol: string;
  points: NetWorthTrendPoint[];
};

export type IncomeExpensePoint = {
  month: string;
  income: number;
  expenses: number;
};
export type IncomeExpenseDto = {
  currencyCode: string;
  currencySymbol: string;
  points: IncomeExpensePoint[];
};

export type SpendingByCategorySlice = {
  categoryName: string;
  amount: number;
};
export type SpendingByCategoryDto = {
  currencyCode: string;
  currencySymbol: string;
  total: number;
  slices: SpendingByCategorySlice[];
};

export type AccountBalanceRow = {
  accountName: string;
  balance: number;
};
export type AccountBalancesDto = {
  currencyCode: string;
  currencySymbol: string;
  rows: AccountBalanceRow[];
};

export type CashFlowPoint = {
  month: string;
  netFlow: number;
};
export type CashFlowDto = {
  currencyCode: string;
  currencySymbol: string;
  points: CashFlowPoint[];
};
