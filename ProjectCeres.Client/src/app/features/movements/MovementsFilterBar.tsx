import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useApi } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { ACCOUNTS_ACTIVE_URL, type AccountOptionDto } from './movements-api';
import { AccountCombobox } from '../../components/AccountCombobox';

export function MovementsFilterBar() {
  const [params, setParams] = useSearchParams();
  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);

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

  const hasFilters = ['q', 'accountId', 'from', 'to'].some((k) => params.get(k));

  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
      <div className="flex-1 space-y-1.5">
        <Label htmlFor="mov-search">Search</Label>
        <Input
          id="mov-search"
          placeholder="Search description or category…"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
        />
      </div>
      <div className="space-y-1.5 sm:w-56">
        <Label>Account</Label>
        <AccountCombobox
          accounts={accounts ?? []}
          value={params.get('accountId')}
          onChange={(id) => setParam('accountId', id)}
          placeholder="All accounts"
        />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="mov-from">From</Label>
        <Input
          id="mov-from"
          type="date"
          value={params.get('from') ?? ''}
          onChange={(e) => setParam('from', e.target.value || null)}
        />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="mov-to">To</Label>
        <Input
          id="mov-to"
          type="date"
          value={params.get('to') ?? ''}
          onChange={(e) => setParam('to', e.target.value || null)}
        />
      </div>
      {hasFilters && (
        <Button
          variant="outline"
          onClick={() => {
            setSearchInput('');
            setParams(new URLSearchParams(), { replace: true });
          }}
        >
          Clear
        </Button>
      )}
    </div>
  );
}
