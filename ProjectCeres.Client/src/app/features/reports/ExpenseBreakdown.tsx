import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { ChartContainer } from '@/components/ui/chart';
import { Bar, BarChart, CartesianGrid, XAxis, YAxis, Tooltip } from 'recharts';
import { useApi } from '../../lib/use-api';
import { usePagination } from '@/hooks/usePagination';
import { REPORTS_EXPENSE_BREAKDOWN_URL, reportMetaBySlug, type ExpenseBreakdownDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { useReportsFilters } from './useReportsFilters';

export function ExpenseBreakdown() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<ExpenseBreakdownDto>(REPORTS_EXPENSE_BREAKDOWN_URL(qs));
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data?.categories ?? [], 6);

  const total   = data?.categories.reduce((s, c) => s + c.total, 0) ?? 0;
  const largest = data?.categories[0]; // API returns sorted by total desc
  const symbol  = data?.currencySymbol ?? '€';

  const chartData = (data?.categories ?? []).map((c) => ({
    name: c.categoryName,
    amount: c.total,
  }));

  return (
    <div className="space-y-6">
      <ReportHeader title="Expense Breakdown" description={reportMetaBySlug('expense-breakdown')?.description} filters={filters} />
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Expense Breakdown" onRetry={refetch} />}
      {data && data.categories.length === 0 && <p className="text-sm text-muted-foreground">No expense transactions for this period.</p>}
      {data && data.categories.length > 0 && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile
                label="Largest Category"
                value={<span className="text-base font-medium">{largest?.categoryName ?? '—'}</span>}
              />
              {largest && <p className="mt-1 text-xs text-muted-foreground"><Numeric>{symbol} {largest.total.toFixed(2)}</Numeric></p>}
            </Tile>
            <Tile>
              <StatTile label="Total Expenses" value={<Numeric className="text-xl text-destructive">{symbol} {total.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Categories" value={<span className="text-xl font-medium">{data.categories.length}</span>} />
            </Tile>
          </div>

          <ChartContainer
            config={{ amount: { label: 'Amount', color: 'var(--primary)' } }}
            className="h-[240px] w-full"
          >
            <BarChart layout="vertical" data={chartData} margin={{ top: 0, right: 16, left: 0, bottom: 0 }}>
              <CartesianGrid horizontal={false} stroke="var(--border)" />
              <XAxis type="number" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
              <YAxis type="category" dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} width={90} />
              <Tooltip contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }} formatter={(v: unknown) => `${symbol} ${(v as number).toFixed(2)}`} />
              <Bar dataKey="amount" fill="var(--primary)" radius={[0, 4, 4, 0]} />
            </BarChart>
          </ChartContainer>

          <ReportTableCard slug="expense-breakdown" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Category</TableHead>
                  <TableHead>Tag</TableHead>
                  <TableHead className="text-right">Amount</TableHead>
                  <TableHead className="text-right">% of Total</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((cat) => {
                  const pct = total > 0 ? (cat.total / total) * 100 : 0;
                  return (
                    <TableRow key={cat.categoryName}>
                      <TableCell className="font-medium">{cat.categoryName}</TableCell>
                      <TableCell className="text-muted-foreground">{cat.lifestyleTag ?? '—'}</TableCell>
                      <TableCell className="text-right"><Numeric>{symbol} {cat.total.toFixed(2)}</Numeric></TableCell>
                      <TableCell className="text-right"><Numeric className="text-muted-foreground">{pct.toFixed(1)}%</Numeric></TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
