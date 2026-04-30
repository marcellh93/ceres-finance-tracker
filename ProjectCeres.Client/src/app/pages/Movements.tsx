import { Plus } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '../features/dashboard/CardError';
import { MovementsFilterBar } from '../features/movements/MovementsFilterBar';
import { MovementsPagination } from '../features/movements/MovementsPagination';
import { MovementsTable } from '../features/movements/MovementsTable';
import { MOVEMENTS_URL, type MovementsPageDto } from '../features/movements/movements-api';
import { QuickAddModal } from '../components/QuickAddModal';
import { useApi } from '../lib/use-api';

function buildUrl(params: URLSearchParams): string {
  const search = params.toString();
  return search ? `${MOVEMENTS_URL}?${search}` : MOVEMENTS_URL;
}

export function Movements() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const [params] = useSearchParams();
  const url = buildUrl(params);
  const { data, error, loading, refetch } = useApi<MovementsPageDto>(url);

  const [quickAddOpen, setQuickAddOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          Movements
        </h1>
        <Button onClick={() => setQuickAddOpen(true)}>
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
          <MovementsTable items={data.items} />
          <MovementsPagination totalCount={data.totalCount} pageSize={data.pageSize} />
        </>
      )}

      <QuickAddModal
        open={quickAddOpen}
        onOpenChange={setQuickAddOpen}
        onSaved={refetch}
      />
    </div>
  );
}
