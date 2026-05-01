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
import { MovementsBulkActions } from './MovementsBulkActions';
import { MovementsCurrencyTabs } from './MovementsCurrencyTabs';
import { MovementsFilterBar } from './MovementsFilterBar';
import { MovementsPagination } from './MovementsPagination';
import { MovementsTable } from './MovementsTable';
import { MOVEMENTS_URL, type MovementsPageDto } from './movements-api';
import { useApi } from '../../lib/use-api';
import { useActiveCurrency } from './use-active-currency';

function buildUrl(params: URLSearchParams, activeCurrency: string | null): string {
  const next = new URLSearchParams(params);
  if (activeCurrency) next.set('currency', activeCurrency);
  const search = next.toString();
  return search ? `${MOVEMENTS_URL}?${search}` : MOVEMENTS_URL;
}

function buildNewMovementUrl(type: string, activeCurrency: string | null): string {
  return activeCurrency
    ? `new?type=${type}&currency=${activeCurrency}`
    : `new?type=${type}`;
}

export function MovementsLayout() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const [params] = useSearchParams();
  const {
    availableCurrencies,
    activeCurrency,
    loading: currencyLoading,
    accounts,
  } = useActiveCurrency();

  // Defer the list fetch until the currency has been resolved AND we know
  // whether the user has any accounts at all — otherwise we'd flash a list
  // of mixed-currency rows for one render before the filter snaps in.
  const hasAccounts = accounts.length > 0;
  const currencyReady = !currencyLoading && (!hasAccounts || activeCurrency !== null);
  const url = buildUrl(params, hasAccounts ? activeCurrency : null);
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
        <div className="flex items-center gap-2">
          <MovementsBulkActions totalCount={data?.totalCount ?? 0} onAfterBulk={refetch} />
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
                onClick={() => navigate(buildNewMovementUrl('transaction', activeCurrency))}
                className="gap-2.5 px-3 py-2 transition-colors"
              >
                <Receipt className="h-4 w-4 text-muted-foreground" />
                <span>Transaction</span>
              </DropdownMenuItem>
              <DropdownMenuItem
                onClick={() => navigate(buildNewMovementUrl('transfer', activeCurrency))}
                className="gap-2.5 px-3 py-2 transition-colors"
              >
                <ArrowLeftRight className="h-4 w-4 text-muted-foreground" />
                <span>Transfer</span>
              </DropdownMenuItem>
              <DropdownMenuItem
                onClick={() => navigate(buildNewMovementUrl('liabilitypayment', activeCurrency))}
                className="gap-2.5 px-3 py-2 transition-colors"
              >
                <CreditCard className="h-4 w-4 text-muted-foreground" />
                <span>Liability Payment</span>
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>

      <MovementsCurrencyTabs
        availableCurrencies={availableCurrencies}
        activeCurrency={activeCurrency}
      />

      <MovementsFilterBar />

      {(!currencyReady || loading) && <Skeleton className="h-[400px] w-full" />}
      {currencyReady && error && <CardError section="Movements" onRetry={refetch} />}
      {currencyReady && data && data.items.length === 0 && (
        <p className="text-sm text-muted-foreground">No movements found.</p>
      )}
      {currencyReady && data && data.items.length > 0 && (
        <>
          <MovementsTable items={data.items} onRefetch={refetch} />
          <MovementsPagination totalCount={data.totalCount} pageSize={data.pageSize} />
        </>
      )}
    </div>
  );
}
