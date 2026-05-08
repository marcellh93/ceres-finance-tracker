import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useApi } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { ACCOUNTS_ACTIVE_URL, type AccountOptionDto } from './movements-api';
import { AccountCombobox } from '../../components/AccountCombobox';
import { TypeFilterCombobox, type TypeFilterValue } from './TypeFilterCombobox';
import { MovementsDateRangePicker } from './MovementsDateRangePicker';

export function buildTypeFilterParams(prev: URLSearchParams, value: string | null): URLSearchParams {
  const next = new URLSearchParams(prev);
  if (value) next.set('type', value);
  else next.delete('type');
  next.delete('page');
  return next;
}

export function MovementsFilterBar() {
  const [params, setParams] = useSearchParams();
  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);

  // The currency tab strip drives the active currency. Narrow the Account
  // dropdown to accounts in that currency so the user can't pick something
  // that the list-fetch would silently filter out.
  const activeCurrency = params.get('currency');
  const filteredAccounts = activeCurrency && accounts
    ? accounts.filter((a) => a.currencyCode === activeCurrency)
    : accounts;

  // Local search input state — debounced before pushing to URL
  const [searchInput, setSearchInput] = useState(params.get('q') ?? '');
  const debouncedSearch = useDebounced(searchInput, 300);

  useEffect(() => {
    const current = params.get('q') ?? '';
    if (debouncedSearch === current) return;
    const next = new URLSearchParams(params);
    if (debouncedSearch) next.set('q', debouncedSearch);
    else next.delete('q');
    next.delete('page'); // reset to first page on filter change
    setParams(next, { replace: true });
  }, [debouncedSearch]); // eslint-disable-line react-hooks/exhaustive-deps

  function setParam(key: string, value: string | null) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    next.delete('page');
    setParams(next, { replace: true });
  }

  const hasFilters = ['q', 'accountId', 'from', 'to', 'type'].some((k) => params.get(k));

  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
      <div className="space-y-1.5 sm:flex-[2] sm:min-w-0">
        <Label htmlFor="mov-search">Search</Label>
        <Input
          id="mov-search"
          placeholder="Search description or category…"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
        />
      </div>
      <div className="space-y-1.5 sm:flex-1 sm:min-w-0">
        <div className="flex items-center gap-2 text-sm leading-none font-medium select-none">Account</div>
        <AccountCombobox
          accounts={filteredAccounts ?? []}
          value={params.get('accountId')}
          onChange={(id) => setParam('accountId', id)}
          onClear={() => setParam('accountId', null)}
          placeholder="All accounts"
        />
      </div>
      <div className="space-y-1.5 sm:flex-1 sm:min-w-0">
        <div className="flex items-center gap-2 text-sm leading-none font-medium select-none">Type</div>
        <TypeFilterCombobox
          id="mov-type"
          value={(params.get('type') as TypeFilterValue | null) ?? 'all'}
          onChange={(v) => {
            setParams(buildTypeFilterParams(params, v === 'all' ? null : v), { replace: true });
          }}
        />
      </div>
      <div className="space-y-1.5 sm:flex-1 sm:min-w-0">
        <div className="flex items-center gap-2 text-sm leading-none font-medium select-none">Date</div>
        <MovementsDateRangePicker />
      </div>
      {hasFilters && (
        <Button
          variant="outline"
          onClick={() => {
            setSearchInput('');
            // Preserve the active currency tab — Clear wipes filters, not the
            // tab the user is on.
            const next = new URLSearchParams();
            if (activeCurrency) next.set('currency', activeCurrency);
            setParams(next, { replace: true });
          }}
        >
          Clear
        </Button>
      )}
    </div>
  );
}
