import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';

export type ReportsFilters = {
  from: string | null;
  to: string | null;
  currencyId: number | null;
  accountId: string | null;
  categoryId: string | null;
  limit: number | null;
  page: number | null;
};

function currentMonthRange(): { from: string; to: string } {
  const now = new Date();
  const y = now.getFullYear();
  const m = now.getMonth();
  const lastDay = new Date(y, m + 1, 0).getDate();
  const pad = (n: number) => String(n).padStart(2, '0');
  return { from: `${y}-${pad(m + 1)}-01`, to: `${y}-${pad(m + 1)}-${pad(lastDay)}` };
}

function read(params: URLSearchParams): ReportsFilters {
  const defaults = currentMonthRange();
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
  const filters = read(params);

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
