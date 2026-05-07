import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { ChartContainer } from '@/components/ui/chart';
import { Bar, BarChart, CartesianGrid, XAxis, YAxis, Tooltip, Legend } from 'recharts';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { usePagination } from '@/hooks/usePagination';
import { REPORTS_MONTHLY_CASH_FLOW_URL, type MonthlyCashFlowRowDto } from './reports-api';
import { formatK } from './chart-format';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

export function MonthlyCashFlow() {
  const { toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<MonthlyCashFlowRowDto[]>(REPORTS_MONTHLY_CASH_FLOW_URL(qs));
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], 6);

  const totalIncome   = (data ?? []).reduce((s, r) => s + r.totalIncome, 0);
  const totalExpenses = (data ?? []).reduce((s, r) => s + r.totalExpenses, 0);
  const netFlow       = totalIncome - totalExpenses;
  const symbol        = data?.[0]?.currencySymbol ?? '€';

  const chartData = (data ?? []).map((row) => ({
    name: `${MONTHS[row.month - 1]} ${row.year}`,
    income: row.totalIncome,
    expenses: row.totalExpenses,
  }));

  const showSkeleton = useDelayedLoading(loading && !data);
  let state: DataTransitionState;
  if (showSkeleton && !data) state = 'skeleton';
  else if (error && !data) state = 'error';
  else state = 'data';

  const skeleton = <Skeleton className="h-[400px] w-full" />;
  const errorSlot = <CardError section="Monthly Cash Flow" onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
      {data && data.length === 0 && <p className="text-sm text-muted-foreground">No data for this period.</p>}
      {data && data.length > 0 && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile label="Total Income" value={<Numeric className="text-xl text-success">{symbol} {totalIncome.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Total Expenses" value={<Numeric className="text-xl text-destructive">{symbol} {totalExpenses.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Net Flow" value={<Numeric className={`text-xl ${netFlow >= 0 ? 'text-success' : 'text-destructive'}`}>{symbol} {netFlow.toFixed(2)}</Numeric>} />
            </Tile>
          </div>

          <ChartContainer
            config={{
              income: { label: 'Income', color: 'var(--success)' },
              expenses: { label: 'Expenses', color: 'var(--destructive)' },
            }}
            className="h-[220px] w-full"
          >
            <BarChart data={chartData} margin={{ top: 8, right: 0, left: 0, bottom: 0 }}>
              <CartesianGrid vertical={false} stroke="var(--border)" />
              <XAxis dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} />
              <YAxis tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={formatK} />
              <Tooltip contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }} formatter={(v: unknown) => `${symbol} ${(v as number).toFixed(2)}`} />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="income" fill="var(--success)" radius={[4, 4, 0, 0]} />
              <Bar dataKey="expenses" fill="var(--destructive)" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ChartContainer>

          <ReportTableCard slug="monthly-cash-flow" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Month</TableHead>
                  <TableHead className="text-right">Income</TableHead>
                  <TableHead className="text-right">Expenses</TableHead>
                  <TableHead className="text-right">Net</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((row) => (
                  <TableRow key={`${row.year}-${row.month}`}>
                    <TableCell>{MONTHS[row.month - 1]} {row.year}</TableCell>
                    <TableCell className="text-right"><Numeric className="text-success">{row.currencySymbol} {row.totalIncome.toFixed(2)}</Numeric></TableCell>
                    <TableCell className="text-right"><Numeric className="text-destructive">{row.currencySymbol} {row.totalExpenses.toFixed(2)}</Numeric></TableCell>
                    <TableCell className="text-right"><Numeric className={row.net >= 0 ? 'text-success' : 'text-destructive'}>{row.currencySymbol} {row.net.toFixed(2)}</Numeric></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
      </DataTransition>
    </div>
  );
}
