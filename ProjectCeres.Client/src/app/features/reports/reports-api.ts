// URL constants
export const REPORTS_NET_WORTH_URL           = '/api/reports/net-worth';
export const REPORTS_INCOME_EXPENSE_URL      = (qs: string) => `/api/reports/income-expense?${qs}`;
export const REPORTS_EXPENSE_BREAKDOWN_URL   = (qs: string) => `/api/reports/expense-breakdown?${qs}`;
export const REPORTS_TRANSACTION_HISTORY_URL = (qs: string) => `/api/reports/transaction-history?${qs}`;
export const REPORTS_BUDGET_VS_ACTUAL_URL    = (qs: string) => `/api/reports/budget-vs-actual?${qs}`;
export const REPORTS_LARGEST_EXPENSES_URL    = (qs: string) => `/api/reports/largest-expenses?${qs}`;
export const REPORTS_MONTHLY_CASH_FLOW_URL   = (qs: string) => `/api/reports/monthly-cash-flow?${qs}`;
export const REPORTS_NET_WORTH_OVER_TIME_URL = (qs: string) => `/api/reports/net-worth-over-time?${qs}`;

// DTOs — shaped to match what ReportsApiController returns

export type NetWorthEntryDto = {
  currencyCode: string;
  currencySymbol: string;
  assets: number;
  liabilities: number;
  netWorth: number;
};

export type IncomeExpenseSummaryDto = {
  currencyCode: string;
  currencySymbol: string;
  totalIncome: number;
  totalExpenses: number;
  savingsRate: number;
};

export type CategoryExpenseDto = {
  categoryName: string;
  lifestyleTag: string | null;
  total: number;
};

export type ExpenseBreakdownDto = {
  currencyCode: string;
  currencySymbol: string;
  categories: CategoryExpenseDto[];
};

export type TransactionHistoryRowDto = {
  id: string;
  date: string;
  accountName: string;
  categoryName: string;
  categoryTypeName: string;
  description: string | null;
  amount: number;
  currencySymbol: string;
};

export type BudgetVsActualRowDto = {
  categoryName: string;
  currencyCode: string;
  currencySymbol: string;
  limitPerPeriod: number;
  totalLimit: number;
  actualSpend: number;
  variance: number;
};

export type LargestExpenseRowDto = {
  date: string;
  description: string;
  categoryName: string;
  accountName: string;
  currencySymbol: string;
  amount: number;
};

export type MonthlyCashFlowRowDto = {
  year: number;
  month: number;
  currencyCode: string;
  currencySymbol: string;
  totalIncome: number;
  totalExpenses: number;
  net: number;
};

export type NetWorthSnapshotRowDto = {
  year: number;
  month: number;
  currencyCode: string;
  currencySymbol: string;
  assets: number;
  liabilities: number;
  netWorth: number;
};

import { Wallet, LineChart, Scale, CalendarClock, PieChart, Target, TrendingUp, Receipt, type LucideIcon } from 'lucide-react';

// Slug → label map (used by ReportsIndex and ReportHeader)
export const REPORT_META: Array<{
  slug: string;
  label: string;
  description: string;
  icon: LucideIcon;
}> = [
  { slug: 'net-worth',           label: 'Net Worth',            description: 'Current assets, liabilities, and net worth across all accounts.',  icon: Wallet },
  { slug: 'net-worth-over-time', label: 'Net Worth Over Time',  description: 'How your net worth has evolved month by month.',                   icon: LineChart },
  { slug: 'income-expense',      label: 'Income vs Expense',    description: 'Total income, expenses, and savings rate for the period.',          icon: Scale },
  { slug: 'monthly-cash-flow',   label: 'Monthly Cash Flow',    description: 'Income and expenses broken down by calendar month.',                icon: CalendarClock },
  { slug: 'expense-breakdown',   label: 'Expense Breakdown',    description: 'Spending ranked by category for the period.',                       icon: PieChart },
  { slug: 'budget-vs-actual',    label: 'Budget vs Actual',     description: 'How actual spending compares to your category budgets.',            icon: Target },
  { slug: 'largest-expenses',    label: 'Largest Expenses',     description: 'Your highest individual expenses, ranked.',                         icon: TrendingUp },
  { slug: 'transaction-history', label: 'Transaction History',  description: 'Paginated ledger of all transactions for the period.',              icon: Receipt },
];
