import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_EXPENSE_BREAKDOWN_URL, type ExpenseBreakdownDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

export function ExpenseBreakdown() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<ExpenseBreakdownDto>(REPORTS_EXPENSE_BREAKDOWN_URL(qs));

  const total = data?.categories.reduce((sum, c) => sum + c.total, 0) ?? 0;

  return (
    <div className="space-y-6">
      <ReportHeader title="Expense Breakdown" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Expense Breakdown" onRetry={refetch} />}
      {data && data.categories.length === 0 && (
        <p className="text-sm text-muted-foreground">No expense transactions for this period.</p>
      )}
      {data && data.categories.length > 0 && (
        <ReportTableCard slug="expense-breakdown" queryString={qs}>
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
              {data.categories.map((cat) => {
                const pct = total > 0 ? (cat.total / total) * 100 : 0;
                return (
                  <TableRow key={cat.categoryName}>
                    <TableCell className="font-medium">{cat.categoryName}</TableCell>
                    <TableCell className="text-muted-foreground">{cat.lifestyleTag ?? '—'}</TableCell>
                    <TableCell className="text-right">
                      <Numeric>{data.currencySymbol} {cat.total.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric className="text-muted-foreground">{pct.toFixed(1)}%</Numeric>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </ReportTableCard>
      )}
    </div>
  );
}
