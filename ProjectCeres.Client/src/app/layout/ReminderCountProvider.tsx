import { createContext, useContext, useMemo } from 'react';
import { useApi } from '../lib/use-api';
import type { RecurringTransactionListItemDto } from '../features/recurring/reminder-status';

type Ctx = {
  count: number;
  reminders: RecurringTransactionListItemDto[];
  loading: boolean;
  refresh: () => void;
};

const ReminderCountContext = createContext<Ctx>({
  count: 0, reminders: [], loading: false, refresh: () => {},
});

export function ReminderCountProvider({ children }: { children: React.ReactNode }) {
  const api = useApi<RecurringTransactionListItemDto[]>('/api/recurring-transactions/upcoming?days=0');
  const value = useMemo<Ctx>(() => ({
    count: api.data?.length ?? 0,
    reminders: api.data ?? [],
    loading: api.loading,
    refresh: api.refetch,
  }), [api.data, api.loading]);

  return <ReminderCountContext.Provider value={value}>{children}</ReminderCountContext.Provider>;
}

export function useReminderCount(): Ctx {
  return useContext(ReminderCountContext);
}
