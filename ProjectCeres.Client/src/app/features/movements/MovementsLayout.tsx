import { useEffect, useRef } from 'react';
import { Link, Outlet, useSearchParams } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '../../components/CardError';
import { MovementsFilterBar } from './MovementsFilterBar';
import { MovementsPagination } from './MovementsPagination';
import { MovementsTable } from './MovementsTable';
import { MOVEMENTS_URL, type MovementsPageDto } from './movements-api';
import { useApi } from '../../lib/use-api';

function buildUrl(params: URLSearchParams): string {
  const search = params.toString();
  return search ? `${MOVEMENTS_URL}?${search}` : MOVEMENTS_URL;
}

export function MovementsLayout() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const [params] = useSearchParams();
  const url = buildUrl(params);
  const { data, error, loading, refetch } = useApi<MovementsPageDto>(url);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          Movements
        </h1>
        <Button render={<Link to="new" />} nativeButton={false}>
          <Plus className="h-4 w-4" />
          New
        </Button>
      </div>

      <MovementsFilterBar />

      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Movements" onRetry={refetch} />}
      {data && data.items.length === 0 && (
        <p className="text-sm text-muted-foreground">No movements found.</p>
      )}
      {data && data.items.length > 0 && (
        <>
          <MovementsTable items={data.items} onRefetch={refetch} />
          <MovementsPagination totalCount={data.totalCount} pageSize={data.pageSize} />
        </>
      )}

      <Outlet context={{ refetch }} />
    </div>
  );
}
