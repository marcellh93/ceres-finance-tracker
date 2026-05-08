import { useEffect, useRef } from 'react';
import { Outlet, useMatch, useNavigate, useSearchParams } from 'react-router-dom';
import { ChevronDown, CreditCard, PiggyBank, Plus, Trophy } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Switch } from '@/components/ui/switch';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { CategoryBudgetsTable } from './CategoryBudgetsTable';
import { GoalBudgetsTable } from './GoalBudgetsTable';
import {
  CATEGORY_BUDGETS_URL,
  GOAL_BUDGETS_URL,
  type CategoryBudgetListItemDto,
  type GoalBudgetListItemDto,
} from './budgets-api';
import { useApi, type UseApiResult } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';

type Tab = 'category' | 'goal';

function buildListUrl(base: string, includeArchived: boolean): string {
  return includeArchived ? `${base}?includeArchived=true` : base;
}

export function BudgetsLayout() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();

  const onCreate = !!useMatch('/budgets/new');
  const onEdit   = !!useMatch('/budgets/:id/edit');
  const childActive = onCreate || onEdit;

  // Always fire both queries so they're warm when the user switches tabs.
  // (Acceptable cost for an internal beta — two small JSON fetches.)
  const includeArchived = params.get('includeArchived') === 'true';
  const tab = (params.get('type') ?? 'category') as Tab;

  const categoryQuery = useApi<CategoryBudgetListItemDto[]>(buildListUrl(CATEGORY_BUDGETS_URL, includeArchived));
  const goalQuery     = useApi<GoalBudgetListItemDto[]>(buildListUrl(GOAL_BUDGETS_URL, includeArchived));

  if (childActive) {
    return (
      <div className="mx-auto max-w-7xl space-y-6">
        <Outlet context={{ refetch: () => { categoryQuery.refetch(); goalQuery.refetch(); } }} />
      </div>
    );
  }

  function setTab(next: Tab) {
    const p = new URLSearchParams(params);
    p.set('type', next);
    setParams(p, { replace: true });
  }

  function setIncludeArchived(checked: boolean) {
    const p = new URLSearchParams(params);
    if (checked) p.set('includeArchived', 'true');
    else p.delete('includeArchived');
    setParams(p, { replace: true });
  }

  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          Budgets
        </h1>
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
                      onClick={() => navigate('new?type=category')}
                      className="gap-2.5 px-3 py-2 transition-colors"
                    >
                      <CreditCard className="h-4 w-4 text-muted-foreground" />
                      <span>Category Budget</span>
                    </DropdownMenuItem>
                  }
                />
                <TooltipContent side="left" sideOffset={12}>
                  A monthly cap on spending in a specific category (e.g., €600/month on groceries).
                </TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger
                  render={
                    <DropdownMenuItem
                      onClick={() => navigate('new?type=spending')}
                      className="gap-2.5 px-3 py-2 transition-colors"
                    >
                      <Trophy className="h-4 w-4 text-muted-foreground" />
                      <span>Spending Goal</span>
                    </DropdownMenuItem>
                  }
                />
                <TooltipContent side="left" sideOffset={12}>
                  Track money you're spending toward a target (e.g., a trip, a renovation). Tagged transactions count toward progress.
                </TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger
                  render={
                    <DropdownMenuItem
                      onClick={() => navigate('new?type=savings')}
                      className="gap-2.5 px-3 py-2 transition-colors"
                    >
                      <PiggyBank className="h-4 w-4 text-muted-foreground" />
                      <span>Savings Goal</span>
                    </DropdownMenuItem>
                  }
                />
                <TooltipContent side="left" sideOffset={12}>
                  Track money accumulated in a designated account (e.g., emergency fund). Progress = current account balance.
                </TooltipContent>
              </Tooltip>
            </TooltipProvider>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <Tabs value={tab} onValueChange={(v) => setTab(v as Tab)}>
        <TabsList>
          <TabsTrigger value="category">Category Budgets</TabsTrigger>
          <TabsTrigger value="goal">Goal Budgets</TabsTrigger>
        </TabsList>
      </Tabs>

      <div className="flex items-center gap-2">
        <Switch checked={includeArchived} onCheckedChange={setIncludeArchived} id="show-archived" />
        <label htmlFor="show-archived" className="text-sm select-none">Show archived</label>
      </div>

      {tab === 'category' && (
        <CategoryTabBody query={categoryQuery} />
      )}

      {tab === 'goal' && (
        <GoalTabBody query={goalQuery} />
      )}
    </div>
  );
}

function CategoryTabBody({ query }: { query: UseApiResult<CategoryBudgetListItemDto[]> }) {
  const showSkeleton = useDelayedLoading(query.loading && !query.data);
  let state: DataTransitionState;
  if (showSkeleton && !query.data) state = 'skeleton';
  else if (query.error && !query.data) state = 'error';
  else state = 'data';

  return (
    <DataTransition
      state={state}
      skeleton={<Skeleton className="h-[300px] w-full" />}
      error={<CardError section="Category Budgets" onRetry={query.refetch} />}
    >
      {query.data && query.data.length === 0 && (
        <p className="text-sm text-muted-foreground">No category budgets.</p>
      )}
      {query.data && query.data.length > 0 && (
        <CategoryBudgetsTable items={query.data} onChanged={query.refetch} />
      )}
    </DataTransition>
  );
}

function GoalTabBody({ query }: { query: UseApiResult<GoalBudgetListItemDto[]> }) {
  const showSkeleton = useDelayedLoading(query.loading && !query.data);
  let state: DataTransitionState;
  if (showSkeleton && !query.data) state = 'skeleton';
  else if (query.error && !query.data) state = 'error';
  else state = 'data';

  return (
    <DataTransition
      state={state}
      skeleton={<Skeleton className="h-[300px] w-full" />}
      error={<CardError section="Goal Budgets" onRetry={query.refetch} />}
    >
      {query.data && query.data.length === 0 && (
        <p className="text-sm text-muted-foreground">No goal budgets.</p>
      )}
      {query.data && query.data.length > 0 && (
        <GoalBudgetsTable items={query.data} onChanged={query.refetch} />
      )}
    </DataTransition>
  );
}
