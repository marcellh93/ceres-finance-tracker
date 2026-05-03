export type { RecurringTransactionListItemDto } from './reminder-status';

export const RECURRING_URL = '/api/recurring-transactions';
export const RECURRING_BY_ID_URL = (id: string) => `/api/recurring-transactions/${id}`;
export const RECURRING_ARCHIVE_URL = (id: string) => `/api/recurring-transactions/${id}/archive`;
export const RECURRING_REACTIVATE_URL = (id: string) => `/api/recurring-transactions/${id}/reactivate`;
export const RECURRING_CONFIRM_URL = (id: string) => `/api/recurring-transactions/${id}/confirm`;
export const RECURRING_DISMISS_URL = (id: string) => `/api/recurring-transactions/${id}/dismiss`;
export const RECURRING_UPCOMING_URL = (days: number) =>
  `/api/recurring-transactions/upcoming?days=${days}`;

export function buildListUrl(includeInactive: boolean): string {
  return includeInactive ? `${RECURRING_URL}?includeInactive=true` : RECURRING_URL;
}

export type RecurringTransactionDetailDto = {
  id: string;
  name: string;
  estimatedAmount: number | null;
  accountId: string;
  categoryId: string;
  frequency: string;
  dayOfPeriod: number | null;
  nextDueDate: string;
  isActive: boolean;
  reminderBehaviour: string;
};

export type CreateRecurringTransactionRequest = {
  name: string;
  estimatedAmount: number | null;
  accountId: string;
  categoryId: string;
  frequency: string;
  dayOfPeriod: number | null;
  nextDueDate: string;
  reminderBehaviour: string;
};

export type UpdateRecurringTransactionRequest = CreateRecurringTransactionRequest;

export type ConfirmRecurringTransactionRequest = {
  date: string;
  amount: number;
  description: string | null;
  nextDueDate: string | null;
};

export type DismissRecurringTransactionRequest = {
  nextDueDate: string | null;
};
