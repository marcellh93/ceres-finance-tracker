import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_INCOME_EXPENSE_URL, type IncomeExpenseSummaryDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

export function IncomeExpense() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<IncomeExpenseSummaryDto>(REPORTS_INCOME_EXPENSE_URL(qs));

  return (
    <div className="space-y-6">
      <ReportHeader title="Income vs Expense" filters={filters} />
      {loading && <Skeleton className="h-[80px] w-full" />}
      {error && <CardError section="Income vs Expense" onRetry={refetch} />}
      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3">
            <Tile>
              <StatTile
                label="Income"
                value={<Numeric className="text-xl text-success">{data.currencySymbol} {data.totalIncome.toFixed(2)}</Numeric>}
              />
            </Tile>
            <Tile>
              <StatTile
                label="Expenses"
                value={<Numeric className="text-xl text-destructive">{data.currencySymbol} {data.totalExpenses.toFixed(2)}</Numeric>}
              />
            </Tile>
            <Tile>
              <StatTile
                label="Savings Rate"
                value={<Numeric className="text-xl">{(data.savingsRate * 100).toFixed(1)}%</Numeric>}
              />
            </Tile>
          </div>
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
                  <TableCell className="text-right"><Numeric className="text-success">{data.currencySymbol} {data.totalIncome.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Total Expenses</TableCell>
                  <TableCell className="text-right"><Numeric className="text-destructive">{data.currencySymbol} {data.totalExpenses.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Net</TableCell>
                  <TableCell className="text-right">
                    <Numeric className={(data.totalIncome - data.totalExpenses) >= 0 ? 'text-success' : 'text-destructive'}>
                      {data.currencySymbol} {(data.totalIncome - data.totalExpenses).toFixed(2)}
                    </Numeric>
                  </TableCell>
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
