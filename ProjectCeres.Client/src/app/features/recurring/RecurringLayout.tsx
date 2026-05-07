import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Outlet, useMatch, useOutletContext, useSearchParams } from 'react-router-dom';
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
import { useReminderCount } from '../../layout/ReminderCountProvider';
import { RecurringTable } from './RecurringTable';
import { buildListUrl, type RecurringTransactionListItemDto } from './recurring-api';

const TODAY = new Date().toISOString().slice(0, 10);

export type RecurringLayoutCtx = { refetch: () => void; refreshBell: () => void };

export function useRecurringLayoutCtx() {
  return useOutletContext<RecurringLayoutCtx>();
}

export function RecurringLayout() {
  const [params, setParams] = useSearchParams();
  const onNew  = !!useMatch('/recurring/new');
  const onEdit = !!useMatch('/recurring/:id/edit');
  const childActive = onNew || onEdit;

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
  }, [debouncedSearch]); // eslint-disable-line react-hooks/exhaustive-deps

  const list = useApi<RecurringTransactionListItemDto[]>(buildListUrl(includeInactive));
  const allList = useApi<RecurringTransactionListItemDto[]>('/api/recurring-transactions?includeInactive=true');

  const { refresh: refreshBell } = useReminderCount();
  const refetch = () => { list.refetch(); allList.refetch(); refreshBell(); };
  const ctx: RecurringLayoutCtx = { refetch, refreshBell };

  if (childActive) {
    return (
      <div className="mx-auto max-w-3xl space-y-6">
        <Outlet context={ctx} />
      </div>
    );
  }

  function setIncludeInactive(checked: boolean) {
    const p = new URLSearchParams(params);
    if (checked) p.set('includeInactive', 'true');
    else p.delete('includeInactive');
    setParams(p, { replace: true });
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <header>
        <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold outline-none">
          Recurring transactions
        </h1>
        <p className="mt-2 text-muted-foreground">
          Templates that confirm into real transactions on a schedule. Confirm a reminder when it
          actually happens; dismiss it when you want to skip.
        </p>
      </header>

      <Card>
        <CardContent className="space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="recurring-search" className="sr-only">Filter reminders</Label>
              <Input
                id="recurring-search"
                placeholder="Filter reminders…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
              />
            </div>
            <Button nativeButton={false} render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New reminder</Link>} />
          </div>
          <div className="flex items-center gap-2">
            <Switch id="include-archived" checked={includeInactive} onCheckedChange={setIncludeInactive} />
            <Label htmlFor="include-archived" className="text-sm font-normal">Include archived</Label>
          </div>

          <RecurringBody
            list={list}
            allList={allList}
            query={debouncedSearch}
            onClearSearch={() => setSearchInput('')}
            onChanged={refetch}
          />
        </CardContent>
      </Card>
    </div>
  );
}

type BodyProps = {
  list: UseApiResult<RecurringTransactionListItemDto[]>;
  allList: UseApiResult<RecurringTransactionListItemDto[]>;
  query: string;
  onClearSearch: () => void;
  onChanged: () => void;
};

function RecurringBody({ list, allList, query, onClearSearch, onChanged }: BodyProps) {
  const lower = query.toLowerCase();

  const sorted = useMemo(() => {
    if (!list.data) return [];
    return [...list.data]
      .filter((r) => r.name.toLowerCase().includes(lower))
      .sort((a, b) => {
        if (a.isActive !== b.isActive) return a.isActive ? -1 : 1;
        return a.nextDueDate.localeCompare(b.nextDueDate);
      });
  }, [list.data, lower]);

  const showSkeleton = useDelayedLoading(list.loading && !list.data);
  let state: DataTransitionState;
  if (showSkeleton && !list.data) state = 'skeleton';
  else if (list.error && !list.data) state = 'error';
  else state = 'data';

  const skeleton = (
    <div data-testid="recurring-skeleton" className="space-y-2 py-2">
      {Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
    </div>
  );

  const errorSlot = <CardError section="Recurring transactions" onRetry={list.refetch} />;

  return (
    <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
      <RecurringDataView
        sorted={sorted}
        allList={allList}
        query={query}
        onClearSearch={onClearSearch}
        onChanged={onChanged}
      />
    </DataTransition>
  );
}

function RecurringDataView({
  sorted,
  allList,
  query,
  onClearSearch,
  onChanged,
}: {
  sorted: RecurringTransactionListItemDto[];
  allList: UseApiResult<RecurringTransactionListItemDto[]>;
  query: string;
  onClearSearch: () => void;
  onChanged: () => void;
}) {
  if (sorted.length === 0 && query === '') {
    const anyExist = (allList.data?.length ?? 0) > 0;
    if (!anyExist) {
      return (
        <div className="px-3 py-12 text-center space-y-3">
          <h2 className="text-lg font-semibold">No recurring reminders yet</h2>
          <p className="text-sm text-muted-foreground">
            Set up reminders for bills, subscriptions, salary — anything that recurs.
          </p>
          <Button render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New reminder</Link>} />
        </div>
      );
    }
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No active reminders.
      </div>
    );
  }

  if (sorted.length === 0 && query) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic space-y-3">
        <p>No reminders match '{query}'.</p>
        <Button type="button" variant="link" onClick={onClearSearch}>Clear search</Button>
      </div>
    );
  }

  return <RecurringTable rows={sorted} today={TODAY} onChanged={onChanged} />;
}
