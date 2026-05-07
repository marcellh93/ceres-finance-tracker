import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Outlet, useMatch, useSearchParams } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useApi, type UseApiResult } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { AccountCurrencySubtotals } from './AccountCurrencySubtotals';
import { AccountsTable } from './AccountsTable';
import { ACCOUNTS_URL, buildListUrl, type AccountListItemDto } from './accounts-api';

export function AccountsLayout() {
  const [params, setParams] = useSearchParams();

  const onCreate = !!useMatch('/accounts/new');
  const onEdit   = !!useMatch('/accounts/:id/edit');
  const childActive = onCreate || onEdit;

  const includeInactive = params.get('includeInactive') === 'true';
  const queryParam = params.get('q') ?? '';

  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const [searchInput, setSearchInput] = useState(queryParam);
  const debouncedSearch = useDebounced(searchInput, 200);

  useEffect(() => {
    const next = new URLSearchParams(params);
    if (debouncedSearch) next.set('q', debouncedSearch);
    else next.delete('q');
    setParams(next, { replace: true });
  }, [debouncedSearch]);  // eslint-disable-line react-hooks/exhaustive-deps

  const list = useApi<AccountListItemDto[]>(buildListUrl(includeInactive));
  const allList = useApi<AccountListItemDto[]>(`${ACCOUNTS_URL}?includeInactive=true`);

  if (childActive) {
    return (
      <div className="mx-auto max-w-3xl space-y-6">
        <Outlet context={{ refetch: list.refetch }} />
      </div>
    );
  }

  function setIncludeInactive(checked: boolean) {
    const p = new URLSearchParams(params);
    if (checked) p.set('includeInactive', 'true');
    else p.delete('includeInactive');
    setParams(p, { replace: true });
  }

  function clearSearch() {
    setSearchInput('');
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header>
        <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold outline-none">
          Accounts
        </h1>
        <p className="mt-2 text-muted-foreground">
          Manage the accounts you own and the debts you owe. Balances are derived from your
          transactions, transfers, and liability payments.
        </p>
      </header>

      <SubtotalsArea list={list} />

      <Card>
        <CardContent className="space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="acc-search" className="sr-only">Filter accounts</Label>
              <Input
                id="acc-search"
                placeholder="Filter accounts…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
              />
            </div>
            <Button nativeButton={false} render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New account</Link>} />
          </div>
          <div className="flex items-center gap-2">
            <Switch
              id="include-archived"
              checked={includeInactive}
              onCheckedChange={setIncludeInactive}
            />
            <Label htmlFor="include-archived" className="text-sm font-normal">
              Include archived
            </Label>
          </div>

          <AccountsBody
            list={list}
            allList={allList}
            query={debouncedSearch}
            onClearSearch={clearSearch}
          />
        </CardContent>
      </Card>
    </div>
  );
}

function SubtotalsArea({ list }: { list: UseApiResult<AccountListItemDto[]> }) {
  const showSkeleton = useDelayedLoading(list.loading && !list.data);
  const state: DataTransitionState =
    showSkeleton && !list.data ? 'skeleton' : 'data';

  const skeleton = (
    <Card>
      <CardContent
        data-testid="subtotals-skeleton"
        className="py-3"
      >
        <Skeleton className="h-5 w-48" />
      </CardContent>
    </Card>
  );

  return (
    <DataTransition state={state} skeleton={skeleton} error={null}>
      {list.data ? <AccountCurrencySubtotals rows={list.data} /> : null}
    </DataTransition>
  );
}

function AccountsBody({
  list, allList, query, onClearSearch,
}: {
  list: UseApiResult<AccountListItemDto[]>;
  allList: UseApiResult<AccountListItemDto[]>;
  query: string;
  onClearSearch: () => void;
}) {
  const lower = query.toLowerCase();

  const sorted = useMemo(() => {
    if (!list.data) return [];
    return [...list.data]
      .filter((row) => row.name.toLowerCase().includes(lower))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [list.data, lower]);

  const showSkeleton = useDelayedLoading(list.loading && !list.data);

  let state: DataTransitionState;
  if (showSkeleton && !list.data) {
    state = 'skeleton';
  } else if (list.error && !list.data) {
    state = 'error';
  } else {
    state = 'data';
  }

  const skeleton = (
    <div data-testid="accounts-skeleton" className="space-y-2 py-2">
      <Skeleton className="h-9 w-full" />
      <Skeleton className="h-9 w-full" />
      <Skeleton className="h-9 w-full" />
      <Skeleton className="h-9 w-full" />
    </div>
  );

  const errorSlot = <CardError section="Accounts" onRetry={list.refetch} />;

  return (
    <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
      <AccountsDataView
        list={list}
        allList={allList}
        sorted={sorted}
        query={query}
        onClearSearch={onClearSearch}
      />
    </DataTransition>
  );
}

function AccountsDataView({
  list, allList, sorted, query, onClearSearch,
}: {
  list: UseApiResult<AccountListItemDto[]>;
  allList: UseApiResult<AccountListItemDto[]>;
  sorted: AccountListItemDto[];
  query: string;
  onClearSearch: () => void;
}) {
  if (!list.data) return null;

  if (sorted.length === 0 && query === '') {
    const totalAccountsExist = (allList.data?.length ?? 0) > 0;
    if (!totalAccountsExist) {
      return (
        <div className="px-3 py-12 text-center space-y-3">
          <h2 className="text-lg font-semibold">No accounts yet</h2>
          <p className="text-sm text-muted-foreground">
            Create your first account to start tracking.
          </p>
          <Button render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New account</Link>} />
        </div>
      );
    }
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No accounts.
      </div>
    );
  }

  if (sorted.length === 0 && query) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic space-y-3">
        <p>No accounts match '{query}'.</p>
        <Button type="button" variant="link" onClick={onClearSearch}>
          Clear search
        </Button>
      </div>
    );
  }

  return <AccountsTable rows={sorted} onChanged={() => { list.refetch(); allList.refetch(); }} />;
}
