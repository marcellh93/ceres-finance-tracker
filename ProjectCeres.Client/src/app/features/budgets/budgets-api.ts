// ---------- URL builders ----------

export const CATEGORY_BUDGETS_URL = '/api/category-budgets';
export const CATEGORY_BUDGET_BY_ID_URL = (id: string) => `/api/category-budgets/${id}`;
export const CATEGORY_BUDGET_ARCHIVE_URL = (id: string) => `/api/category-budgets/${id}/archive`;
export const CATEGORY_BUDGET_REACTIVATE_URL = (id: string) => `/api/category-budgets/${id}/reactivate`;
export const CATEGORY_BUDGET_SPEND_URL = (id: string, year: number, month: number) =>
  `/api/category-budgets/${id}/spend?year=${year}&month=${month}`;

export const GOAL_BUDGETS_URL = '/api/goal-budgets';
export const GOAL_BUDGET_BY_ID_URL = (id: string) => `/api/goal-budgets/${id}`;
export const GOAL_BUDGET_ARCHIVE_URL = (id: string) => `/api/goal-budgets/${id}/archive`;
export const GOAL_BUDGET_REACTIVATE_URL = (id: string) => `/api/goal-budgets/${id}/reactivate`;
export const GOAL_BUDGET_PROGRESS_URL = (id: string) => `/api/goal-budgets/${id}/progress`;

export const BUDGET_DISCRIMINATOR_URL = (id: string) => `/api/budgets/${id}`;

// ---------- DTOs ----------

export type CategoryBudgetListItemDto = {
  id: string;
  categoryId: string;
  categoryName: string;
  currencyCode: string;
  currencySymbol: string;
  limitAmount: number;
  isActive: boolean;
  currentPeriodSpend: number;
  currentPeriodEnd: string; // yyyy-MM-dd
};

export type CategoryBudgetEditDto = {
  id: string;
  categoryId: string;
  currencyId: number;
  currencyCode: string;
  limitAmount: number;
  isActive: boolean;
};

export type CreateCategoryBudgetRequest = {
  categoryId: string;
  currencyId: number;
  limitAmount: number;
};

export type UpdateCategoryBudgetRequest = CreateCategoryBudgetRequest & { isActive: boolean };

export type GoalType = 'Spending' | 'Savings';

export type GoalBudgetListItemDto = {
  id: string;
  name: string;
  goalType: GoalType;
  currencyCode: string;
  currencySymbol: string;
  targetAmount: number;
  startDate: string;
  endDate: string | null;
  description: string | null;
  isActive: boolean;
  linkedAccountId: string | null;
  linkedAccountName: string | null;
  progress: number;
};

export type GoalBudgetEditDto = {
  id: string;
  name: string;
  goalType: GoalType;
  currencyId: number;
  currencyCode: string;
  targetAmount: number;
  startDate: string;
  endDate: string | null;
  description: string | null;
  isActive: boolean;
  linkedAccountId: string | null;
};

export type CreateGoalBudgetRequest = {
  name: string;
  goalType: GoalType;
  currencyId: number | null;       // null when goalType=Savings
  targetAmount: number;
  startDate: string;
  endDate: string | null;
  description: string | null;
  linkedAccountId: string | null;  // required when goalType=Savings
};

export type UpdateGoalBudgetRequest = CreateGoalBudgetRequest & { isActive: boolean };

export type BudgetKind = 'CategoryBudget' | 'GoalBudget';
export type BudgetDiscriminatorDto = { id: string; kind: BudgetKind };

// ---------- DUPLICATE_BUDGET 409 envelope shape ----------

export type DuplicateBudgetEnvelope = {
  error: {
    code: 'DUPLICATE_BUDGET';
    message: string;
    existingBudgetId: string;
    existingIsActive: boolean;
  };
};
