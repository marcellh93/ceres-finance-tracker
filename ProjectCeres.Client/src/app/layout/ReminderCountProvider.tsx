/* eslint-disable react-refresh/only-export-components -- Why: this file is a provider+hook pair; splitting into provider.tsx+context.ts would require updating 5 consumer files including test fixtures that import both from the same path. */
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
  // eslint-disable-next-line react-hooks/exhaustive-deps -- Why: api.refetch is intentionally excluded; it is a stable callback from useApi that does not change across renders and including it would cause memo thrash via the useApi object identity changing.
  }), [api.data, api.loading]);

  return <ReminderCountContext.Provider value={value}>{children}</ReminderCountContext.Provider>;
}

export function useReminderCount(): Ctx {
  return useContext(ReminderCountContext);
}
