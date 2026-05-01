import { useEffect, useRef } from 'react';
import { Outlet, useMatch, useNavigate, useSearchParams } from 'react-router-dom';
import { ArrowLeftRight, ChevronDown, CreditCard, Plus, Receipt } from 'lucide-react';
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
              <Button
                className="gap-2 transition-colors hover:bg-primary/90 data-[popup-open]:bg-primary/90"
              >
                <Plus className="h-4 w-4" />
                New
                <ChevronDown className="h-4 w-4 opacity-70 transition-transform duration-200 group-data-[popup-open]/button:rotate-180" />
              </Button>
            }
          />
          <DropdownMenuContent align="end" sideOffset={8} className="min-w-[12rem] p-1.5">
            <DropdownMenuItem
              onClick={() => navigate('new?type=transaction')}
              className="gap-2.5 px-3 py-2 transition-colors"
            >
              <Receipt className="h-4 w-4 text-muted-foreground" />
              <span>Transaction</span>
            </DropdownMenuItem>
            <DropdownMenuItem
              onClick={() => navigate('new?type=transfer')}
              className="gap-2.5 px-3 py-2 transition-colors"
            >
              <ArrowLeftRight className="h-4 w-4 text-muted-foreground" />
              <span>Transfer</span>
            </DropdownMenuItem>
            <DropdownMenuItem
              onClick={() => navigate('new?type=liabilitypayment')}
              className="gap-2.5 px-3 py-2 transition-colors"
            >
              <CreditCard className="h-4 w-4 text-muted-foreground" />
              <span>Liability Payment</span>
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
