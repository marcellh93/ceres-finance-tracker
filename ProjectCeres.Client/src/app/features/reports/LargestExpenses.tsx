import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { useSettings } from '../../lib/use-settings';
import { formatDate } from '../../lib/date-format';
import { REPORTS_LARGEST_EXPENSES_URL, type LargestExpenseRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

export function LargestExpenses() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<LargestExpenseRowDto[]>(REPORTS_LARGEST_EXPENSES_URL(qs));
  const { data: settings } = useSettings();

  return (
    <div className="space-y-6">
      <ReportHeader title="Largest Expenses" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Largest Expenses" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No expenses for this period.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="largest-expenses" queryString={qs}>
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
              {data.map((row, i) => (
                <TableRow key={i}>
                  <TableCell className="whitespace-nowrap">
                    <Numeric>{formatDate(row.date, settings?.dateFormat)}</Numeric>
                  </TableCell>
                  <TableCell>{row.description}</TableCell>
                  <TableCell className="text-muted-foreground">{row.categoryName}</TableCell>
                  <TableCell className="text-muted-foreground">{row.accountName}</TableCell>
                  <TableCell className="text-right">
                    <Numeric className="text-destructive">{row.currencySymbol} {row.amount.toFixed(2)}</Numeric>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ReportTableCard>
      )}
    </div>
  );
}
