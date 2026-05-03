import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Outlet, useMatch, useSearchParams } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { CardError } from '../../components/CardError';
import { useApi, type UseApiResult } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { CategoriesTable } from './CategoriesTable';
import {
  buildListUrl,
  isLockedCategory,
  type CategoryListItemDto,
} from './categories-api';

type Tab = 'income' | 'expense';

function asTab(raw: string | null): Tab {
  return raw === 'income' ? 'income' : 'expense';
}

function tabToTypeName(tab: Tab): 'Income' | 'Expense' {
  return tab === 'income' ? 'Income' : 'Expense';
}

export function CategoriesLayout() {
  const [params, setParams] = useSearchParams();

  const onCreate = !!useMatch('/categories/new');
  const onEdit   = !!useMatch('/categories/:id/edit');
  const childActive = onCreate || onEdit;

  const tab = asTab(params.get('type'));
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

  const list = useApi<CategoryListItemDto[]>(buildListUrl(includeInactive));

  if (childActive) {
    return (
      <div className="mx-auto max-w-3xl space-y-6">
        <Outlet context={{ refetch: list.refetch }} />
      </div>
    );
  }

  function setTab(next: Tab) {
    const p = new URLSearchParams(params);
    p.set('type', next);
    setParams(p, { replace: true });
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
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-2xl font-semibold outline-none"
        >
          Categories
        </h1>
        <p className="mt-2 text-muted-foreground">
          Manage how transactions are classified. System categories used by
          imports and accounting cannot be edited.
        </p>
      </header>

      <Card>
        <CardContent className="space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <Tabs value={tab} onValueChange={(v) => setTab(asTab(v))}>
              <TabsList>
                <TabsTrigger value="income">Income</TabsTrigger>
                <TabsTrigger value="expense">Expense</TabsTrigger>
              </TabsList>
            </Tabs>
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="cat-search" className="sr-only">Filter categories</Label>
              <Input
                id="cat-search"
                placeholder="Filter categories…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
              />
            </div>
            <Button
              nativeButton={false}
              render={
                <Link to="new">
                  <Plus className="h-4 w-4 mr-1" />
                  New category
                </Link>
              }
            />

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

          <CategoriesBody
            list={list}
            tab={tab}
            includeInactive={includeInactive}
            query={debouncedSearch}
            onClearSearch={clearSearch}
          />
        </CardContent>
      </Card>
    </div>
  );
}

function CategoriesBody({
  list,
  tab,
  includeInactive,
  query,
  onClearSearch,
}: {
  list: UseApiResult<CategoryListItemDto[]>;
  tab: Tab;
  includeInactive: boolean;
  query: string;
  onClearSearch: () => void;
}) {
  const typeName = tabToTypeName(tab);
  const lower = query.toLowerCase();

  const sorted = useMemo(() => {
    if (!list.data) return [];
    const filtered = list.data
      .filter((row) => row.categoryTypeName === typeName)
      .filter((row) => row.name.toLowerCase().includes(lower));
    // 1) Active user rows A-Z, 2) Archived user rows A-Z, 3) System rows A-Z.
    const userActive   = filtered.filter((r) => !isLockedCategory(r) && r.isActive)
                                 .sort((a, b) => a.name.localeCompare(b.name));
    const userArchived = filtered.filter((r) => !isLockedCategory(r) && !r.isActive)
                                 .sort((a, b) => a.name.localeCompare(b.name));
    const system       = filtered.filter((r) => isLockedCategory(r))
                                 .sort((a, b) => a.name.localeCompare(b.name));
    return [...userActive, ...userArchived, ...system];
  }, [list.data, typeName, lower]);

  if (list.loading && !list.data) {
    return (
      <div data-testid="categories-skeleton" className="space-y-2 py-2">
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
      </div>
    );
  }

  if (list.error || !list.data) {
    return <CardError section="Categories" onRetry={list.refetch} />;
  }

  if (sorted.length === 0 && query) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic space-y-3">
        <p>No categories match '{query}'.</p>
        <Button type="button" variant="link" onClick={onClearSearch}>
          Clear search
        </Button>
      </div>
    );
  }

  const hasArchivedToggleNoArchivedRows =
    includeInactive && !sorted.some((r) => !r.isActive && !isLockedCategory(r));

  return (
    <div className="space-y-3">
      <CategoriesTable rows={sorted} onChanged={list.refetch} />
      {hasArchivedToggleNoArchivedRows ? (
        <p className="text-xs italic text-muted-foreground">
          No archived categories in this tab.
        </p>
      ) : null}
    </div>
  );
}
