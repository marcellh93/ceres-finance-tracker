import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { useDocumentTitle } from '../../lib/use-document-title';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { ChartContainer } from '@/components/ui/chart';
import { Bar, BarChart, CartesianGrid, XAxis, YAxis, Tooltip } from 'recharts';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { useSettings } from '../../lib/use-settings';
import { formatDate } from '../../lib/date-format';
import { usePagination } from '@/hooks/usePagination';
import { REPORTS_LARGEST_EXPENSES_URL, type LargestExpenseRowDto } from './reports-api';
import { formatK, truncateLabel } from './chart-format';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

export function LargestExpenses() {
  useDocumentTitle('Largest Expenses');
  const { toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<LargestExpenseRowDto[]>(REPORTS_LARGEST_EXPENSES_URL(qs));
  const { data: settings } = useSettings();
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], 6);

  const top    = data?.[0];
  const total  = (data ?? []).reduce((s, r) => s + r.amount, 0);
  const avg    = data && data.length > 0 ? total / data.length : 0;
  const symbol = top?.currencySymbol ?? '€';

  const chartData = (data ?? []).slice(0, 10).map((r) => ({
    name: r.description ?? r.categoryName,
    amount: r.amount,
  }));

  const showSkeleton = useDelayedLoading(loading && !data);
  let state: DataTransitionState;
  if (showSkeleton && !data) state = 'skeleton';
  else if (error && !data) state = 'error';
  else state = 'data';

  const skeleton = <Skeleton className="h-[400px] w-full" />;
  const errorSlot = <CardError section="Largest Expenses" onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
      {data && data.length === 0 && <p className="text-sm text-muted-foreground">No expenses for this period.</p>}
      {data && data.length > 0 && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile
                label="Top Expense"
                value={<span className="text-base font-medium">{top?.description || top?.categoryName || '—'}</span>}
              />
              {top && <p className="mt-1 text-xs text-destructive"><Numeric>{symbol} {top.amount.toFixed(2)}</Numeric></p>}
            </Tile>
            <Tile>
              <StatTile label="Total (Top N)" value={<Numeric className="text-xl text-destructive">{symbol} {total.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Avg per Transaction" value={<Numeric className="text-xl">{symbol} {avg.toFixed(2)}</Numeric>} />
            </Tile>
          </div>

          <ChartContainer
            config={{ amount: { label: 'Amount', color: 'var(--destructive)' } }}
            className="h-[240px] w-full"
          >
            <BarChart layout="vertical" data={chartData} margin={{ top: 0, right: 16, left: 0, bottom: 0 }}>
              <CartesianGrid horizontal={false} stroke="var(--border)" />
              <XAxis type="number" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={formatK} />
              <YAxis type="category" dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} width={120} tickFormatter={(v: string) => truncateLabel(v)} />
              <Tooltip contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }} formatter={(v: unknown) => `${symbol} ${(v as number).toFixed(2)}`} />
              <Bar dataKey="amount" fill="var(--destructive)" radius={[0, 4, 4, 0]} />
            </BarChart>
          </ChartContainer>

          <ReportTableCard slug="largest-expenses" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Date</TableHead>
                  <TableHead>Description</TableHead>
                  <TableHead>Category</TableHead>
                  <TableHead>Account</TableHead>
                  <TableHead className="text-right">Amount</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((row, i) => (
                  <TableRow key={i}>
                    <TableCell className="whitespace-nowrap"><Numeric>{formatDate(row.date, settings?.dateFormat)}</Numeric></TableCell>
                    <TableCell>{row.description}</TableCell>
                    <TableCell className="text-muted-foreground">{row.categoryName}</TableCell>
                    <TableCell className="text-muted-foreground">{row.accountName}</TableCell>
                    <TableCell className="text-right"><Numeric className="text-destructive">{row.currencySymbol} {row.amount.toFixed(2)}</Numeric></TableCell>
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
