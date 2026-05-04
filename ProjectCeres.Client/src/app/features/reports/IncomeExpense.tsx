import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { ChartContainer } from '@/components/ui/chart';
import { Bar, BarChart, CartesianGrid, XAxis, YAxis, Tooltip, Legend } from 'recharts';
import { useApi } from '../../lib/use-api';
import { REPORTS_INCOME_EXPENSE_URL, type IncomeExpenseSummaryDto } from './reports-api';
import { ReportTableCard } from './ReportTableCard';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { useReportsFilters } from './useReportsFilters';

export function IncomeExpense() {
  const { toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<IncomeExpenseSummaryDto>(REPORTS_INCOME_EXPENSE_URL(qs));

  const net    = data ? data.totalIncome - data.totalExpenses : 0;
  const symbol = data?.currencySymbol ?? '€';

  const chartData = data
    ? [{ name: 'Period', income: data.totalIncome, expenses: data.totalExpenses }]
    : [];

  return (
    <div className="space-y-6">
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[80px] w-full" />}
      {error && <CardError section="Income vs Expense" onRetry={refetch} />}
      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-4">
            <Tile>
              <StatTile label="Income" value={<Numeric className="text-xl text-success">{symbol} {data.totalIncome.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Expenses" value={<Numeric className="text-xl text-destructive">{symbol} {data.totalExpenses.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Net" value={<Numeric className={`text-xl ${net > 0 ? 'text-success' : net < 0 ? 'text-destructive' : 'text-muted-foreground'}`}>{symbol} {net.toFixed(2)}</Numeric>} />
            </Tile>
            <Tile>
              <StatTile label="Savings Rate" value={<Numeric className="text-xl">{(data.savingsRate * 100).toFixed(1)}%</Numeric>} />
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
              <YAxis tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
              <Tooltip contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }} formatter={(v: unknown) => `${symbol} ${(v as number).toFixed(2)}`} />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="income" fill="var(--success)" radius={[4, 4, 0, 0]} />
              <Bar dataKey="expenses" fill="var(--destructive)" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ChartContainer>

          <ReportTableCard slug="income-expense" queryString={qs}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Metric</TableHead>
                  <TableHead className="text-right">Value</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow>
                  <TableCell>Total Income</TableCell>
                  <TableCell className="text-right"><Numeric className="text-success">{symbol} {data.totalIncome.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Total Expenses</TableCell>
                  <TableCell className="text-right"><Numeric className="text-destructive">{symbol} {data.totalExpenses.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Net</TableCell>
                  <TableCell className="text-right"><Numeric className={net > 0 ? 'text-success' : net < 0 ? 'text-destructive' : 'text-muted-foreground'}>{symbol} {net.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Savings Rate</TableCell>
                  <TableCell className="text-right"><Numeric>{(data.savingsRate * 100).toFixed(1)}%</Numeric></TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
