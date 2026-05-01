import { useEffect, useRef } from 'react';
import { Outlet, useMatch, useNavigate, useSearchParams } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
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

  const navigate = useNavigate();
  const onCreateRoute = !!useMatch('/movements/new');
  const onEditRoute = !!useMatch('/movements/:id/edit');
  const childActive = onCreateRoute || onEditRoute;

  if (childActive) {
    return (
      <div className="space-y-6">
        <Outlet context={{ refetch }} />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          Movements
        </h1>
        <DropdownMenu>
          <DropdownMenuTrigger
            render={
              <Button>
                <Plus className="h-4 w-4" />
                New
              </Button>
            }
          />
          <DropdownMenuContent align="end">
            <DropdownMenuItem onClick={() => navigate('new?type=transaction')}>
              Transaction
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => navigate('new?type=transfer')}>
              Transfer
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => navigate('new?type=liabilitypayment')}>
              Liability Payment
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
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
    </div>
  );
}
