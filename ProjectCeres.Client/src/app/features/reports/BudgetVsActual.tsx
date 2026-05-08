import { Skeleton } from '@/components/ui/skeleton';
import { Progress } from '@/components/ui/progress';
import { useDocumentTitle } from '../../lib/use-document-title';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { cn } from '@/lib/utils';
import { usePagination } from '@/hooks/usePagination';
import { REPORTS_BUDGET_VS_ACTUAL_URL, type BudgetVsActualRowDto } from './reports-api';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

function BudgetProgressCell({ limit, actual }: { limit: number; actual: number }) {
  const pct = limit === 0 ? 0 : Math.min((actual / limit) * 100, 100);
  const isOver = actual > limit;
  return (
    <div className={cn('flex items-center gap-3', isOver && '[&_[data-slot="progress-indicator"]]:bg-destructive')}>
      <Progress
        value={pct}
        aria-label={`${pct.toFixed(0)}% of budget`}
        className="w-[140px]"
      />
      <Numeric className="text-xs text-muted-foreground w-10 text-right">{pct.toFixed(0)}%</Numeric>
    </div>
  );
}

export function BudgetVsActual() {
  useDocumentTitle('Budget vs. Actual');
  const { toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<BudgetVsActualRowDto[]>(REPORTS_BUDGET_VS_ACTUAL_URL(qs));
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], 6);

  const totalBudget = (data ?? []).reduce((s, r) => s + r.totalLimit, 0);
  const totalSpent  = (data ?? []).reduce((s, r) => s + r.actualSpend, 0);
  const pctUsed     = totalBudget > 0 ? (totalSpent / totalBudget) * 100 : 0;
  const symbol      = data?.[0]?.currencySymbol ?? '€';

  const showSkeleton = useDelayedLoading(loading && !data);
  let state: DataTransitionState;
  if (showSkeleton && !data) state = 'skeleton';
  else if (error && !data) state = 'error';
  else state = 'data';

  const skeleton = <Skeleton className="h-[400px] w-full" />;
  const errorSlot = <CardError section="Budget vs Actual" onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No active budgets for this period.</p>
      )}
      {data && data.length > 0 && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile label="Total Budget" value={<Numeric className="text-xl">{symbol} {totalBudget.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Total Spent" value={<Numeric className={`text-xl ${totalSpent > totalBudget ? 'text-destructive' : totalSpent === totalBudget ? 'text-muted-foreground' : ''}`}>{symbol} {totalSpent.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Overall Used" value={<Numeric className={`text-xl ${pctUsed > 100 ? 'text-destructive' : pctUsed === 100 ? 'text-muted-foreground' : ''}`}>{pctUsed.toFixed(1)}%</Numeric>} />
            </Tile>
          </div>
          <ReportTableCard slug="budget-vs-actual" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
            <Table className="min-w-[680px]">
              <TableHeader>
                <TableRow>
                  <TableHead>Category</TableHead>
                  <TableHead className="text-right">Limit/period</TableHead>
                  <TableHead className="text-right">Total limit</TableHead>
                  <TableHead className="text-right">Actual</TableHead>
                  <TableHead>Progress</TableHead>
                  <TableHead className="text-right">Variance</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((row) => (
                  <TableRow key={row.categoryName}>
                    <TableCell className="font-medium">{row.categoryName}</TableCell>
                    <TableCell className="text-right text-muted-foreground">
                      <Numeric>{row.currencySymbol} {row.limitPerPeriod.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric>{row.currencySymbol} {row.totalLimit.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric>{row.currencySymbol} {row.actualSpend.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell>
                      <BudgetProgressCell limit={row.totalLimit} actual={row.actualSpend} />
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric className={row.variance > 0 ? 'text-success' : row.variance < 0 ? 'text-destructive' : 'text-muted-foreground'}>
                        {row.variance > 0 ? '+' : ''}{row.currencySymbol} {row.variance.toFixed(2)}
                      </Numeric>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
        </div>
      )}
      </DataTransition>
    </div>
  );
}
