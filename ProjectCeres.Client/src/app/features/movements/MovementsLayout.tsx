import { useEffect, useRef } from 'react';
import { Outlet, useMatch, useNavigate, useSearchParams } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { ArrowLeftRight, ChevronDown, CreditCard, Plus, Receipt } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { MovementsBulkActions } from './MovementsBulkActions';
import { MovementsCurrencyTabs } from './MovementsCurrencyTabs';
import { MovementsFilterBar } from './MovementsFilterBar';
import { MovementsPagination } from './MovementsPagination';
import { MovementsCardList } from './MovementsCardList';
import { MovementsTable } from './MovementsTable';
import { useMediaQuery } from '../../lib/use-media-query';
import { MOVEMENTS_URL, type MovementsPageDto } from './movements-api';
import { MOVEMENT_TYPE_HINT, MOVEMENT_TYPE_LABEL } from './movement-type-display';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
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
  useDocumentTitle('Movements');
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
  const isDesktop = useMediaQuery('(min-width: 768px)');

  // Hooks before any early return.
  const isLoadingForTransition = !currencyReady || (loading && !data);
  const showSkeleton = useDelayedLoading(isLoadingForTransition);

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

  let state: DataTransitionState;
  if (showSkeleton && !data) state = 'skeleton';
  else if (currencyReady && error && !data) state = 'error';
  else state = 'data';

  const skeleton = (
    <div data-testid="movements-skeleton">
      <Skeleton className="h-[400px] w-full" />
    </div>
  );

  const errorSlot = <CardError section="Movements" onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-y-3">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          Movements
        </h1>
        <div className="ml-auto flex items-center gap-2">
          <MovementsBulkActions totalCount={data?.totalCount ?? 0} onAfterBulk={refetch} />
          <DropdownMenu>
            <DropdownMenuTrigger
              render={
                <Button
                  className="gap-2 transition-colors hover:bg-primary/90 data-[popup-open]:bg-primary/90"
                >
                  <Plus className="h-4 w-4" />
                  New
                  <ChevronDown className="h-4 w-4 opacity-70 transition-transform [transition-duration:var(--motion-duration-base)] group-data-[popup-open]/button:rotate-180" />
                </Button>
              }
            />
            <DropdownMenuContent align="end" sideOffset={8} className="min-w-[14rem] p-1.5">
              <TooltipProvider delay={200}>
                <Tooltip>
                  <TooltipTrigger
                    render={
                      <DropdownMenuItem
                        onClick={() => navigate(buildNewMovementUrl('transaction', activeCurrency))}
                        className="gap-2.5 px-3 py-2 transition-colors"
                      >
                        <Receipt className="h-4 w-4 text-muted-foreground" />
                        <span>{MOVEMENT_TYPE_LABEL.Transaction}</span>
                      </DropdownMenuItem>
                    }
                  />
                  <TooltipContent side="left" sideOffset={12}>
                    {MOVEMENT_TYPE_HINT.Transaction}
                  </TooltipContent>
                </Tooltip>
                <Tooltip>
                  <TooltipTrigger
                    render={
                      <DropdownMenuItem
                        onClick={() => navigate(buildNewMovementUrl('transfer', activeCurrency))}
                        className="gap-2.5 px-3 py-2 transition-colors"
                      >
                        <ArrowLeftRight className="h-4 w-4 text-muted-foreground" />
                        <span>{MOVEMENT_TYPE_LABEL.Transfer}</span>
                      </DropdownMenuItem>
                    }
                  />
                  <TooltipContent side="left" sideOffset={12}>
                    {MOVEMENT_TYPE_HINT.Transfer}
                  </TooltipContent>
                </Tooltip>
                <Tooltip>
                  <TooltipTrigger
                    render={
                      <DropdownMenuItem
                        onClick={() => navigate(buildNewMovementUrl('liabilitypayment', activeCurrency))}
                        className="gap-2.5 px-3 py-2 transition-colors"
                      >
                        <CreditCard className="h-4 w-4 text-muted-foreground" />
                        <span>{MOVEMENT_TYPE_LABEL.LiabilityPayment}</span>
                      </DropdownMenuItem>
                    }
                  />
                  <TooltipContent side="left" sideOffset={12}>
                    {MOVEMENT_TYPE_HINT.LiabilityPayment}
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>

      <MovementsCurrencyTabs
        availableCurrencies={availableCurrencies}
        activeCurrency={activeCurrency}
        loading={currencyLoading}
      />

      <MovementsFilterBar />

      <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
        {data && data.items.length === 0 && (
          <p className="text-sm text-muted-foreground">No movements found.</p>
        )}
        {data && data.items.length > 0 && (
          <div className="space-y-4">
            {isDesktop
              ? <MovementsTable items={data.items} onRefetch={refetch} />
              : <MovementsCardList items={data.items} onRefetch={refetch} />}
            <MovementsPagination totalCount={data.totalCount} pageSize={data.pageSize} />
          </div>
        )}
      </DataTransition>
    </div>
  );
}
