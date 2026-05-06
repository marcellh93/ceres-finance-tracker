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
import { useApi, type UseApiResult } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { ProfilesTable } from './ProfilesTable';
import {
  buildProfilesListUrl,
  type ImportProfileListItemDto,
} from './import-api';

export function ProfilesLayout() {
  const [params, setParams] = useSearchParams();

  const onCreate = !!useMatch('/import/profiles/new');
  const onEdit   = !!useMatch('/import/profiles/:id/edit');
  const childActive = onCreate || onEdit;

  const includeDeleted = params.get('includeDeleted') === 'true';
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

  const list = useApi<ImportProfileListItemDto[]>(buildProfilesListUrl(includeDeleted));

  if (childActive) {
    return (
      <div className="mx-auto max-w-3xl space-y-6">
        <Outlet context={{ refetch: list.refetch }} />
      </div>
    );
  }

  function setIncludeDeleted(checked: boolean) {
    const p = new URLSearchParams(params);
    if (checked) p.set('includeDeleted', 'true');
    else p.delete('includeDeleted');
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
          Import profiles
        </h1>
        <p className="mt-2 text-muted-foreground">
          Saved column mappings reused on every import. Archived profiles are
          purged 90 days after deletion.
        </p>
      </header>

      <Card>
        <CardContent className="space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="profiles-search" className="sr-only">Filter profiles</Label>
              <Input
                id="profiles-search"
                placeholder="Filter profiles…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
              />
            </div>
            <Button
              nativeButton={false}
              render={
                <Link to="new">
                  <Plus className="h-4 w-4 mr-1" />
                  New profile
                </Link>
              }
            />
          </div>
          <div className="flex items-center gap-2">
            <Switch
              id="include-archived"
              checked={includeDeleted}
              onCheckedChange={setIncludeDeleted}
            />
            <Label htmlFor="include-archived" className="text-sm font-normal">
              Include archived
            </Label>
          </div>

          <ProfilesBody
            list={list}
            includeDeleted={includeDeleted}
            query={debouncedSearch}
            onClearSearch={clearSearch}
          />
        </CardContent>
      </Card>
    </div>
  );
}

function ProfilesBody({
  list,
  includeDeleted,
  query,
  onClearSearch,
}: {
  list: UseApiResult<ImportProfileListItemDto[]>;
  includeDeleted: boolean;
  query: string;
  onClearSearch: () => void;
}) {
  const lower = query.toLowerCase();

  const sorted = useMemo(() => {
    if (!list.data) return [];
    const filtered = list.data.filter((p) => p.name.toLowerCase().includes(lower));
    // Active profiles A-Z first, then archived A-Z.
    const active   = filtered.filter((p) => p.deletedAt === null)
                             .sort((a, b) => a.name.localeCompare(b.name));
    const archived = filtered.filter((p) => p.deletedAt !== null)
                             .sort((a, b) => a.name.localeCompare(b.name));
    return [...active, ...archived];
  }, [list.data, lower]);

  if (list.loading && !list.data) {
    return (
      <div data-testid="profiles-skeleton" className="space-y-2 py-2">
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
      </div>
    );
  }

  if (list.error || !list.data) {
    return <CardError section="Import profiles" onRetry={list.refetch} />;
  }

  if (sorted.length === 0 && query) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic space-y-3">
        <p>No profiles match '{query}'.</p>
        <Button type="button" variant="link" onClick={onClearSearch}>
          Clear search
        </Button>
      </div>
    );
  }

  if (sorted.length === 0) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No saved profiles yet. Create one after your first import, or click
        New profile above.
      </div>
    );
  }

  const archivedToggleNoArchivedRows =
    includeDeleted && !sorted.some((r) => r.deletedAt !== null);

  return (
    <div className="space-y-3">
      <ProfilesTable rows={sorted} onChanged={list.refetch} />
      {archivedToggleNoArchivedRows ? (
        <p className="text-xs italic text-muted-foreground">
          No archived profiles.
        </p>
      ) : null}
    </div>
  );
}
