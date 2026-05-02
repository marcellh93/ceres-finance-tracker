// Typed response shapes for the Dashboard endpoints.
//
// All numeric fields on HealthDto are nullable — the server returns null
// when a particular metric is unavailable (e.g., insufficient history).
//
// Use with the useApi hook:
//
//   const { data, error, loading, refetch } = useApi<HealthDto>(HEALTH_URL);

export const HEALTH_URL = '/api/dashboard/health';
export const SUMMARY_URL = '/api/dashboard/summary';
export const CATEGORY_BUDGETS_URL = '/api/dashboard/category-budgets';
export const GOAL_BUDGETS_URL = '/api/dashboard/goal-budgets';

export type HealthDto = {
  availableToday: number | null;
  safeToSpend: number | null;
  imminentBills: number | null;
  laterBills: number | null;
  budgetReserve: number | null;
  runwayMonths: number | null;
  avgMonthlyExpense: number | null;
  currentMonthIncome: number | null;
  rollingAverageIncome: number | null;
  incomeDeltaPercent: number | null;
  budgetBurnRate: number | null;
  budgetSpentMtd: number | null;
  budgetTotalLimit: number | null;
  currencyCode: string;
  currencySymbol: string;
};

export type NetWorthEntry = {
  currencyCode: string;
  currencySymbol: string;
  assets: number;
  liabilities: number;
  netWorth: number;
};

export type MtdSummary = {
  currencyCode: string;
  currencySymbol: string;
  income: number;
  expenses: number;
  /** Fraction (0–1), not a percentage. */
  savingsRate: number;
  /** FULL prior-period totals — null when no prior-period data exists yet. */
  priorPeriodIncome: number | null;
  priorPeriodExpenses: number | null;
  priorPeriodSavingsRate: number | null;
};

export type SummaryDto = {
  netWorth: NetWorthEntry[];
  mtd: MtdSummary;
  remindersDueCount: number;
};
