import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useSettings } from '../../lib/use-settings';
import { getCurrentPeriodMonth, getBoundsForMonth } from '../../lib/period';

export type ReportsFilters = {
  from: string | null;
  to: string | null;
  currencyId: number | null;
  accountId: string | null;
  categoryId: string | null;
  limit: number | null;
  page: number | null;
};

function read(params: URLSearchParams, periodStartDay: number): ReportsFilters {
  const today = new Date();
  const { year, month } = getCurrentPeriodMonth(today, periodStartDay);
  const defaults = getBoundsForMonth(year, month, periodStartDay);
  return {
    from: params.get('from') ?? defaults.from,
    to: params.get('to') ?? defaults.to,
    currencyId: params.has('currencyId') ? Number(params.get('currencyId')) : null,
    accountId: params.get('accountId'),
    categoryId: params.get('categoryId'),
    limit: params.has('limit') ? Number(params.get('limit')) : null,
    page: params.has('page') ? Number(params.get('page')) : null,
  };
}

export function useReportsFilters() {
  const [params, setParams] = useSearchParams();
  const { data: settings } = useSettings();
  const periodStartDay = settings?.periodStartDay ?? 1;
  const filters = read(params, periodStartDay);

  const setFilter = useCallback(
    <K extends keyof ReportsFilters>(key: K, value: ReportsFilters[K]) => {
      const next = new URLSearchParams(params);
      if (value === null || value === undefined) {
        next.delete(key);
      } else {
        next.set(key, String(value));
      }
      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const reset = useCallback(() => {
    const next = new URLSearchParams(params);
    ['currencyId', 'accountId', 'categoryId', 'limit', 'page'].forEach((k) => next.delete(k));
    setParams(next, { replace: true });
  }, [params, setParams]);

  const toQueryString = useCallback((): string => {
    const p = new URLSearchParams();
    if (filters.from) p.set('from', filters.from);
    if (filters.to) p.set('to', filters.to);
    if (filters.currencyId !== null) p.set('currencyId', String(filters.currencyId));
    if (filters.accountId) p.set('accountId', filters.accountId);
    if (filters.categoryId) p.set('categoryId', filters.categoryId);
    if (filters.limit !== null) p.set('limit', String(filters.limit));
    if (filters.page !== null) p.set('page', String(filters.page));
    return p.toString();
  }, [filters]);

  return { filters, setFilter, reset, toQueryString };
}
